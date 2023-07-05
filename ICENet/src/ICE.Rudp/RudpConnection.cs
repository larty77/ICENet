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
    internal sealed class RudpConnection
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

        private readonly Handlers _handlers;

        private readonly Senders _senders;

        #region Server Fields

        private Action<byte[], IPEndPoint> _send;

        private Action<string> _disconnect;

        private RudpServer.DataHandler _externalDataHandled;

        #endregion

        #region Connection Fields

        private int _rtt;

        private int _smoothRtt;

        public int Ping => SmoothRtt / 2;

        public int SmoothRtt => CalculateSmoothRtt(_rtt, ref _smoothRtt);

        #endregion

        #region Connection Support

        private readonly Timer _disconnectTimer;

        private readonly Timer _heartbeatTimer;

        private readonly Stopwatch _heartbeatWatch;

        #endregion

        #region Reability Fields

        private RudpPool<RudpTimer> _rudpTimersPool;

        private ConcurrentDictionary<ushort, RudpTimer> _pendingTimers;

        private ushort _lastPacketId;

        private ushort NextPacketId { get { lock (_lock) { return _lastPacketId = (_lastPacketId == ushort.MaxValue) ? (ushort)1 : (ushort)(_lastPacketId + 1); } } }

        private int NextResendTime => CalculateResendTime(SmoothRtt);

        #endregion

        private readonly object _lock = new object();

        public RudpConnection(IPEndPoint remoteEp, Action<byte[], IPEndPoint> send, Action<string> disconnect, RudpServer.DataHandler externalDataHandler, RudpPool<RudpTimer> rudpTimersPool)
        {
            _remoteEndPoint = remoteEp;

            _handlers = new Handlers(this);
            _senders = new Senders(this);

            _heartbeatWatch = new Stopwatch();
            _heartbeatWatch.Restart();

            _send = send;
            _disconnect = disconnect;

            _externalDataHandled = externalDataHandler;

            _disconnectTimer = new Timer(_ => Disconnect("The connection has been lost"), null, Timeout.Infinite, Timeout.Infinite);
            _heartbeatTimer = new Timer(_ => _senders.SendHearbeatRequest(), null, Timeout.Infinite, Timeout.Infinite);

            _disconnectTimer.Change(DisconnectTimeout, Timeout.Infinite);
            _heartbeatTimer.Change(HeartbeatInterval, HeartbeatInterval);

            _rudpTimersPool = rudpTimersPool;
            _pendingTimers = new ConcurrentDictionary<ushort, RudpTimer>();

            _internalDataHandlers[(byte)RudpHeaders.ConnectRequest] = _handlers.HandleConnectRequest;
            _internalDataHandlers[(byte)RudpHeaders.HearbeatRequest] = _handlers.HandleHeartbeatRequest;
            _internalDataHandlers[(byte)RudpHeaders.HeartbeatResponse] = _handlers.HandleHeartbeatResponse;
            _internalDataHandlers[(byte)RudpHeaders.Unreliable] = _handlers.HandleUnreliable;
            _internalDataHandlers[(byte)RudpHeaders.Reliable] = _handlers.HandleReliable;
            _internalDataHandlers[(byte)RudpHeaders.Ack] = _handlers.HandleAck;

            IsConnected = true;
        }

        private class Handlers
        {
            private readonly RudpConnection _connection;

            public Handlers(RudpConnection connection)
            {
                _connection = connection;
            }

            public void HandleConnectRequest(ref Data data)
            {
                _connection._senders.SendConnectResponse();
            }

            public void HandleHeartbeatRequest(ref Data data)
            {
                _connection.RestartDisconnectTimer();

                _connection._senders.SendHeartbeatResponse();
            }

            public void HandleHeartbeatResponse(ref Data data)
            {
                _connection._heartbeatWatch.Stop();
                _connection._rtt = (int)Math.Min(_connection._heartbeatWatch.ElapsedMilliseconds, int.MaxValue);

                _connection.RestartDisconnectTimer();
            }

            public void HandleUnreliable(ref Data data)
            {
                _connection._externalDataHandled?.Invoke(ref data, _connection);
            }

            public void HandleReliable(ref Data data)
            {
                ushort packetId = data.ReadUInt16();
                _connection._senders.SendAck(packetId);
                _connection._externalDataHandled?.Invoke(ref data, _connection);
            }

            public void HandleAck(ref Data data)
            {
                ushort packetId = data.ReadUInt16();
                _connection._pendingTimers.TryRemove(packetId, out var timer);
                _connection._rudpTimersPool.Release(timer);
            }

        }

        public void Handle(ref Data data)
        {
            byte packetId = data.ReadByte();

            if (_internalDataHandlers.TryGetValue(packetId, out var handler) is false) return;

            handler.Invoke(ref data);

            if (packetId is (byte)RudpHeaders.ConnectRequest) return;

            RestartDisconnectTimer();
        }

        private class Senders
        {
            private readonly RudpConnection _connection;

            public Senders(RudpConnection connection)
            {
                _connection = connection;
            }

            public void SendConnectResponse()
            {
                _connection.Send(GetHeaderBytes(RudpHeaders.ConnectResponse));
            }

            public void SendHearbeatRequest()
            {
                _connection.Send(GetHeaderBytes(RudpHeaders.HearbeatRequest));

                _connection._heartbeatWatch.Reset();
                _connection._heartbeatWatch.Start();
            }

            public void SendHeartbeatResponse()
            {
                _connection.Send(GetHeaderBytes(RudpHeaders.HeartbeatResponse));
            }

            public void SendUnreliable(ref Data data)
            {
                Data result = new Data(0);
                result.Write(GetHeaderBytes(RudpHeaders.Unreliable));
                result.Write(data.ToArray());

                _connection.Send(result.ToArray());
            }

            public void SendReliable(Data data)
            {
                ushort packetId = _connection.NextPacketId;

                void Resend()
                {
                    Data reliableData = new Data(0);
                    reliableData.Write(GetHeaderBytes(RudpHeaders.Reliable));
                    reliableData.Write(packetId);
                    reliableData.Write(data.ToArray());

                    _connection?.Send(reliableData.ToArray());
                }

                void Failure()
                {
                    Logger.Log(LogType.Error, "RELIABLE SEND ERROR", $"Reliable packet[Id: {packetId}] cannot not received by {_connection.RemoteEndPoint}!");

                    _connection._pendingTimers.TryRemove(packetId, out var timer);
                    _connection._rudpTimersPool.Release(timer);
                }

                RudpTimer timer = _connection._rudpTimersPool.Get();

                timer.Interval = _connection.NextResendTime;
                timer.MaxElaspedCount = MaxResendCount;

                timer.Elapsed = Resend;
                timer.Ended = Failure;

                if (_connection._pendingTimers.TryAdd(packetId, timer) is false) { _connection._rudpTimersPool.Release(timer); return; }

                Resend();

                timer.Start();
            }

            public void SendAck(ushort packetId)
            {
                Data data = new Data(0);
                data.Write(GetHeaderBytes(RudpHeaders.Ack));
                data.Write(packetId);

                _connection.Send(data.ToArray());
            }

            private byte[] GetHeaderBytes(RudpHeaders header) => new byte[1] { (byte)header };
        }

        public void SendUnreliable(ref Data data) => _senders.SendUnreliable(ref data);

        public void SendReliable(ref Data data) => _senders.SendReliable(data);

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
                    _smoothRtt = 0;

                    foreach (var t in _pendingTimers.Values) _rudpTimersPool.Release(t); 
                    _pendingTimers.Clear();
                    _pendingTimers = null!;
                    _rudpTimersPool = null!;

                    _lastPacketId = 0;
                }
            }
            catch {  }

            IsConnected = false;

            _disconnect.Invoke(reason);
        }
    }
}
