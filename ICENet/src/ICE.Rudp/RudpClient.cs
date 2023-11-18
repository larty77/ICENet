using System;
using System.Net;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

using ICENet.Core.Data;
using ICENet.Rudp.Common;
using ICENet.Core.Transport;
using ICENet.Rudp.Transport;
using ICENet.Core.Helpers;

using Timer = System.Threading.Timer;

namespace ICENet.Rudp
{
    internal sealed class RudpClient : RudpPeer
    {
        private readonly IClient _socket;

        public IPEndPoint? LocalEndPoint => _socket.LocalEndPoint;

        public IPEndPoint? RemoteEndPoint => _socket.RemoteEndPoint;

        public delegate void DataHandler(ref Data data);

        private readonly Dictionary<byte, DataHandler> _internalDataHandlers = new Dictionary<byte, DataHandler>()
        {
            [(byte)RudpHeaders.ConnectResponse] = { },
            [(byte)RudpHeaders.HearbeatRequest] = { },
            [(byte)RudpHeaders.HeartbeatResponse] = { },
            [(byte)RudpHeaders.Unreliable] = { },
            [(byte)RudpHeaders.Reliable] = { },
            [(byte)RudpHeaders.Ack] = { }
        };

        public DataHandler? ExternalDataHandled;

        public Action? Disconnected;

        #region Trying to connect

        private const int _maxConnectionAttempts = 3;

        private const int _connectionTimeout = 1500;

        private int _connectionAttempts = 0;

        private TaskCompletionSource<bool>? _connectionResponseHandled;

        private CancellationTokenSource? _connectionCancellationTokenSource;

        private CancellationToken _connectionCancellationToken => _connectionCancellationTokenSource!.Token;

        #endregion

        #region Connection Status

        private ClientState _connectionState;

        public bool IsConnected => _connectionState is ClientState.Connected;

        #endregion

        #region Connection Fields

        public int Ping => CalculateSmoothRtt() / 2;

        private int NextResendTime => CalculateResendTime();

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

        #endregion

        private readonly object _lock = new object();

        public RudpClient(IClient socket)
        {
            _socket = socket ?? new UdpClient();
            _socket.AddReceiveListener(Handle);

            _disconnectTimer = new Timer(_ => Disconnect("The connection has been lost"), null, Timeout.Infinite, Timeout.Infinite);
            _heartbeatTimer = new Timer(_ => SendHeartbeatRequest(), null, Timeout.Infinite, Timeout.Infinite);

            _heartbeatWatch = new Stopwatch();
            _heartbeatWatch.Restart();

            _rudpTimersPool = new RudpPool<RudpPacket>();
            _pendingTimers = new ConcurrentDictionary<ushort, RudpPacket>();

            _internalDataHandlers[(byte)RudpHeaders.ConnectResponse] = HandleConnectResponse;
            _internalDataHandlers[(byte)RudpHeaders.HearbeatRequest] = HandleHeartbeatRequest;
            _internalDataHandlers[(byte)RudpHeaders.HeartbeatResponse] = HandleHeartbeatResponse;
            _internalDataHandlers[(byte)RudpHeaders.Unreliable] = HandleUnreliable;
            _internalDataHandlers[(byte)RudpHeaders.Reliable] = HandleReliable;
            _internalDataHandlers[(byte)RudpHeaders.Ack] = HandleAck;
        }

        public async Task<bool> TryConnect(IPEndPoint remoteEp, IPEndPoint localEp)
        {
            try
            {
                lock (_lock)
                {
                    _connectionState = ClientState.Connecting;

                    if (_socket.TryConnect(remoteEp, localEp) is false)
                        return false;

                    _connectionResponseHandled = new TaskCompletionSource<bool>(false);

                    _connectionCancellationTokenSource = new CancellationTokenSource();
                }

                async Task<bool> Attempt()
                {
                    int connectionAttempts;
                    lock (_lock)
                    {
                        connectionAttempts = _connectionAttempts;
                    }

                    if (connectionAttempts >= _maxConnectionAttempts)
                        return false;

                    Logger.Log(LogType.Info, "TRY-CONNECT", $"Trying to connect... Attempt[{connectionAttempts}]!");

                    Interlocked.Increment(ref _connectionAttempts);

                    SendConnectRequest();

                    var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(_connectionTimeout), _connectionCancellationToken) ?? null;

                    var completedTask = await Task.WhenAny(_connectionResponseHandled?.Task, timeoutTask);

                    if (completedTask.Equals(_connectionResponseHandled?.Task ?? null) is true) return true;

                    return await Attempt();
                }

                bool result = await Attempt();

                string log = result ? "The connection was successful!" : "The connection failed!";

                Logger.Log(LogType.Info, "TRY-CONNECT", log);

                lock (_lock)
                {
                    _connectionState = result is true ? ClientState.Connected : ClientState.Disconnected;

                    if (_connectionState is ClientState.Connected)
                    {
                        _disconnectTimer.Change(DisconnectTimeout, Timeout.Infinite);
                        _heartbeatTimer.Change(HeartbeatInterval, HeartbeatInterval);
                    }
                }

                return _connectionState is ClientState.Connected;
            }

            catch { return false; }
        }

        private void HandleConnectResponse(ref Data data)
        {
            _connectionResponseHandled?.TrySetResult(true);
        }

        private void HandleHeartbeatRequest(ref Data data)
        {
            SendHeartbeatResponse();
        }

        private void HandleHeartbeatResponse(ref Data data)
        {
            _heartbeatWatch.Stop();
            _rtt = (int)Math.Min(_heartbeatWatch.ElapsedMilliseconds, int.MaxValue);
        }

        private void HandleUnreliable(ref Data data)
        {
            ExternalDataHandled?.Invoke(ref data);
        }

        private void HandleReliable(ref Data data)
        {
            ushort packetId = data.ReadUInt16();
            SendAck(packetId);
            ExternalDataHandled?.Invoke(ref data);
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

        private void Handle(byte[] bytes)
        {
            try
            {
                byte packetId = bytes[0];

                if (packetId < 1) return;

                if (packetId != (byte)RudpHeaders.ConnectResponse && _connectionState != ClientState.Connected) return;

                if (_internalDataHandlers.TryGetValue(packetId, out var handler) is false) return;

                RestartDisconnectTimer();

                Data data = new Data(0);

                data.LoadBytes(bytes);

                data.ReadByte();

                handler.Invoke(ref data);
            }
            catch { }
        }

        private void SendConnectRequest()
        {
            Send(GetHeaderBytes(RudpHeaders.ConnectRequest));
        }

        private void SendHeartbeatRequest()
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

            rudpPacket.Elapsed = (Core.Data.Data data) =>
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
                    Logger.Log(LogType.Info, "CLIENT SENDER REABILITY", "reliable packet was not received by remote client!");

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

            if (_pendingTimers.TryAdd(packetId, rudpPacket) is false) { _rudpTimersPool.Release(rudpPacket); return; }

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

        private void Send(byte[] bytes) => _socket.Send(bytes);

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
                    _socket.Stop();

                    _connectionAttempts = 0;

                    _connectionCancellationTokenSource?.Cancel();

                    _connectionCancellationTokenSource?.Dispose();

                    _connectionCancellationTokenSource = null;

                    _disconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

                    _heartbeatWatch.Stop();

                    _rtt = 0;

                    _rudpTimersPool.Dispose();
                    _pendingTimers.Clear();

                    _lastPacketId = 0;
                }

                Logger.Log(LogType.Info, "DISCONNECTED", $"Disconnected! REASON: [{reason}]!");
            }
            catch { }

            _connectionState = ClientState.Disconnected;

            Disconnected?.Invoke();
        }
    }
}