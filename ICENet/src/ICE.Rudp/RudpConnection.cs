using System;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;

using ICENet.Core.Data;
using ICENet.Rudp.Common;

using static ICENet.Rudp.Common.RudpPeer;
using ICENet.Core.Helpers;

namespace ICENet.Rudp
{
    internal sealed class RudpConnection : RudpPeer
    {
        private IPEndPoint _remoteEndPoint;

        public IPEndPoint RemoteEndPoint
        {
            get
            {
                lock (_lock)
                {
                    return _remoteEndPoint;
                }
            }
            private set
            {
                lock (_lock)
                {
                    _remoteEndPoint = value;
                }
            }
        }

        public bool IsConnected { get; private set; }

        private delegate void DataHandler(ref Data data);

        private readonly Dictionary<byte, DataHandler> _internalDataHandlers = new Dictionary<byte, DataHandler>()
        {
            [(byte)RudpHeaders.ConnectRequest] = { },
            [(byte)RudpHeaders.HearbeatRequest] = { },
            [(byte)RudpHeaders.HeartbeatResponse] = { },
            [(byte)RudpHeaders.Unreliable] = { },
            [(byte)RudpHeaders.Reliable] = { },
            [(byte)RudpHeaders.Ack] = { }
        };

        #region Server Fields

        private Action<byte[], IPEndPoint> _send;

        private Action<string> _disconnect;

        private RudpServer.DataHandler _externalDataHandled;

        #endregion

        #region Connection Fields

        public int Ping => CalculateSmoothRtt() / 2;

        #endregion

        #region Connection Support

        private readonly Timer _disconnectTimer;

        private readonly Timer _heartbeatTimer;

        private readonly Stopwatch _heartbeatWatch;

        #endregion

        #region Reability Fields

        private RudpPool<RudpPacket> _rudpTimersPool;

        private ConcurrentDictionary<ushort, RudpPacket> _pendingTimers;

        private ushort _lastPacketId;

        private ushort NextPacketId { get { lock (_lock) { return _lastPacketId = (_lastPacketId == ushort.MaxValue) ? (ushort)1 : (ushort)(_lastPacketId + 1); } } }

        private int NextResendTime => CalculateResendTime();

        #endregion

        private readonly object _lock = new object();

        public RudpConnection(IPEndPoint remoteEp, Action<byte[], IPEndPoint> send, Action<string> disconnect, RudpServer.DataHandler externalDataHandler, RudpPool<RudpPacket> rudpTimersPool)
        {
            _remoteEndPoint = remoteEp;

            _heartbeatWatch = new Stopwatch();
            _heartbeatWatch.Restart();

            _send = send;
            _disconnect = disconnect;

            _externalDataHandled = externalDataHandler;

            _disconnectTimer = new Timer(_ => Disconnect("The connection has been lost"), null, Timeout.Infinite, Timeout.Infinite);
            _heartbeatTimer = new Timer(_ => SendHearbeatRequest(), null, Timeout.Infinite, Timeout.Infinite);

            _disconnectTimer.Change(DisconnectTimeout, Timeout.Infinite);
            _heartbeatTimer.Change(HeartbeatInterval, HeartbeatInterval);

            _rudpTimersPool = rudpTimersPool;
            _pendingTimers = new ConcurrentDictionary<ushort, RudpPacket>();

            _internalDataHandlers[(byte)RudpHeaders.ConnectRequest] = HandleConnectRequest;
            _internalDataHandlers[(byte)RudpHeaders.HearbeatRequest] = HandleHeartbeatRequest;
            _internalDataHandlers[(byte)RudpHeaders.HeartbeatResponse] = HandleHeartbeatResponse;
            _internalDataHandlers[(byte)RudpHeaders.Unreliable] = HandleUnreliable;
            _internalDataHandlers[(byte)RudpHeaders.Reliable] = HandleReliable;
            _internalDataHandlers[(byte)RudpHeaders.Ack] = HandleAck;

            IsConnected = true;
        }

        private void HandleConnectRequest(ref Data data)
        {
            SendConnectResponse();
        }

        private void HandleHeartbeatRequest(ref Data data)
        {
            RestartDisconnectTimer();

            SendHeartbeatResponse();
        }

        private void HandleHeartbeatResponse(ref Data data)
        {
            _heartbeatWatch.Stop();
            _rtt = (int)Math.Min(_heartbeatWatch.ElapsedMilliseconds, int.MaxValue);

            RestartDisconnectTimer();
        }

        private void HandleUnreliable(ref Data data)
        {
            _externalDataHandled?.Invoke(ref data, this);
        }

        private void HandleReliable(ref Data data)
        {
            ushort packetId = data.ReadUInt16();
            SendAck(packetId);
            _externalDataHandled?.Invoke(ref data, this);
        }
            
        private void HandleAck(ref Data data)
        {
            ushort packetId = data.ReadUInt16();
            if (
                _pendingTimers == null ||
                !_pendingTimers.TryRemove(packetId, out var timer))
                return;
            _rudpTimersPool.Release(timer);
        }
     
        public void Handle(ref Data data)
        {
            byte packetId = data.ReadByte();

            if (_internalDataHandlers.TryGetValue(packetId, out var handler) is false) return;

            handler.Invoke(ref data);

            if (packetId is (byte)RudpHeaders.ConnectRequest) return;

            RestartDisconnectTimer();
        }

        private void SendConnectResponse()
        {
            Send(GetHeaderBytes(RudpHeaders.ConnectResponse));
        }

        private void SendHearbeatRequest()
        {
            Send(GetHeaderBytes(RudpHeaders.HearbeatRequest));

            _heartbeatWatch.Reset();
            _heartbeatWatch.Start();
        }

        private void SendHeartbeatResponse()
        {
            Send(GetHeaderBytes(RudpHeaders.HeartbeatResponse));
        }

        public void SendUnreliable(ref Data data)
        {
            Data result = new Data(0);
            result.Write(GetHeaderBytes(RudpHeaders.Unreliable));
            result.Write(data.ToArray());

            Send(result.ToArray());
        }

        public void SendReliable(Data data)
        {
            ushort packetId = NextPacketId;

            Data send_data = new Data(0);
            send_data.Write(GetHeaderBytes(RudpHeaders.Reliable));
            send_data.Write(packetId);
            send_data.Write(data.ToArray());

            RudpPacket rudpPacket = _rudpTimersPool.Get();
            rudpPacket.Interval = NextResendTime;
            rudpPacket.MaxElaspedCount = MaxResendCount;
            rudpPacket.PendingData = send_data;

            rudpPacket.Elapsed = (Data data) =>
            {
                try
                {
                    Send(data.ToArray());
                }
                catch (NullReferenceException ex)
                {
                    Logger.Log(LogType.Error, "NULL-REFERENCE RELIABLE ERROR", ex.Message);
                }
                catch
                {
                }
            };

            rudpPacket.Ended = () =>
            {
                try
                {
                    Logger.Log(LogType.Error, "SERVER SENDER REABILITY", "reliable packet was not received by remote client!");

                    if (
                    _pendingTimers == null ||
                    !_pendingTimers.TryRemove(packetId, out var failPacket))
                        return;

                    _rudpTimersPool?.Release(failPacket);
                }
                catch (NullReferenceException ex)
                {
                    Logger.Log(LogType.Error, "NULL-REFERENCE RELIABLE ERROR", ex.Message);
                }
                catch
                {
                }
            };

            if (_pendingTimers.TryAdd(packetId, rudpPacket) is false) 
            { _rudpTimersPool.Release(rudpPacket); return; }

            Send(data.ToArray());
            rudpPacket.Start();
        }

        private void SendAck(ushort packetId)
        {
            Data data = new Data(0);
            data.Write(GetHeaderBytes(RudpHeaders.Ack));
            data.Write(packetId);

            Send(data.ToArray());
        }

        private byte[] GetHeaderBytes(RudpHeaders header) => new byte[1] { (byte)header };

        private void Send(byte[] bytes) => _send.Invoke(bytes, RemoteEndPoint);

        #region Timers

        private void RestartDisconnectTimer()
        {
            lock (_lock)
            {
                _disconnectTimer.Change(DisconnectTimeout, Timeout.Infinite);
            }
        }

        #endregion

        public void Disconnect(string reason)
        {
            try
            {
                lock (_lock)
                {
                    RemoteEndPoint = null!;

                    _heartbeatWatch.Restart();
                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _heartbeatTimer.Dispose();

                    _send = null!;
                    _externalDataHandled = null!;

                    _rtt = 0;

                    foreach (var t in _pendingTimers.Values) _rudpTimersPool.Release(t);
                    _pendingTimers.Clear();
                    _pendingTimers = null!;
                    _rudpTimersPool = null!;

                    _lastPacketId = 0;
                }
            }
            catch { }

            IsConnected = false;

            _disconnect.Invoke(reason);
        }
    }
}
