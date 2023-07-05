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
using System.Data.Common;

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

        private readonly Handlers _handlers;

        private readonly Senders _senders;

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

        public RudpClient(IClient socket)
        {
            _socket = socket ?? new UdpClient();
            _socket.AddReceiveListener(Handle);

            _handlers = new Handlers(this);
            _senders = new Senders(this);

            _disconnectTimer = new Timer(_ => Disconnect("The connection has been lost"), null, Timeout.Infinite, Timeout.Infinite);
            _heartbeatTimer = new Timer(_ => _senders.SendHeartbeatRequest(), null, Timeout.Infinite, Timeout.Infinite);

            _heartbeatWatch = new Stopwatch();
            _heartbeatWatch.Restart();

            _rudpTimersPool = new RudpPool<RudpTimer>();
            _pendingTimers = new ConcurrentDictionary<ushort, RudpTimer>();

            _internalDataHandlers[(byte)RudpHeaders.ConnectResponse] = _handlers.HandleConnectResponse;
            _internalDataHandlers[(byte)RudpHeaders.HearbeatRequest] = _handlers.HandleHeartbeatRequest;
            _internalDataHandlers[(byte)RudpHeaders.HeartbeatResponse] = _handlers.HandleHeartbeatResponse;
            _internalDataHandlers[(byte)RudpHeaders.Unreliable] = _handlers.HandleUnreliable;
            _internalDataHandlers[(byte)RudpHeaders.Reliable] = _handlers.HandleReliable;
            _internalDataHandlers[(byte)RudpHeaders.Ack] = _handlers.HandleAck;
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

                    _senders.SendConnectRequest();

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

        private class Handlers
        {
            private readonly RudpClient _client;

            public Handlers(RudpClient client)
            {
                _client = client;
            }

            public void HandleConnectResponse(ref Data data)
            {
                _client._connectionResponseHandled?.TrySetResult(true);
            }

            public void HandleHeartbeatRequest(ref Data data)
            {
                _client._senders.SendHeartbeatResponse();
            }

            public void HandleHeartbeatResponse(ref Data data)
            {
                _client._heartbeatWatch.Stop();
                _client._rtt = (int)Math.Min(_client._heartbeatWatch.ElapsedMilliseconds, int.MaxValue);
            }

            public void HandleUnreliable(ref Data data)
            {
                _client.ExternalDataHandled?.Invoke(ref data);
            }

            public void HandleReliable(ref Data data)
            {
                ushort packetId = data.ReadUInt16();
                _client._senders.SendAck(packetId);
                _client.ExternalDataHandled?.Invoke(ref data);
            }

            public void HandleAck(ref Data data)
            {
                ushort packetId = data.ReadUInt16();
                _client._pendingTimers.TryRemove(packetId, out var timer);
                _client._rudpTimersPool.Release(timer);
            }
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
            catch {  }
        }

        private class Senders
        {
            private readonly RudpClient _client;

            public Senders(RudpClient client)
            {
                _client = client;
            }

            public void SendConnectRequest()
            {
                _client.Send(GetHeaderBytes(RudpHeaders.ConnectRequest));
            }       

            public void SendHeartbeatRequest()
            {
                _client.Send(GetHeaderBytes(RudpHeaders.HearbeatRequest));

                _client._heartbeatWatch.Reset();
                _client._heartbeatWatch.Start();
            }

            public void SendHeartbeatResponse()
            {
                _client.Send(GetHeaderBytes(RudpHeaders.HeartbeatResponse));
            }

            public void SendUnreliable(ref Data data)
            {
                Data result = new Data(0);
                result.Write(GetHeaderBytes(RudpHeaders.Unreliable));
                result.Write(data.ToArray());

                _client.Send(result.ToArray());
            }

            public void SendReliable(Data data)
            {
                ushort packetId = _client.NextPacketId;

                void Resend()
                {
                    Data reliableData = new Data(0);
                    reliableData.Write(GetHeaderBytes(RudpHeaders.Reliable));
                    reliableData.Write(packetId);
                    reliableData.Write(data.ToArray());

                    _client?.Send(reliableData.ToArray());
                }

                void Failure()
                {
                    Logger.Log(LogType.Error, "RELIABLE SEND ERROR", $"Reliable packet[Id: {packetId}] was not received by server!");

                    _client._pendingTimers.TryRemove(packetId, out var timer);
                    _client._rudpTimersPool.Release(timer);
                }

                RudpTimer timer = _client._rudpTimersPool.Get();

                timer.Interval = _client.NextResendTime;
                timer.MaxElaspedCount = MaxResendCount;

                timer.Elapsed = Resend;
                timer.Ended = Failure;

                if (_client._pendingTimers.TryAdd(packetId, timer) is false) { _client._rudpTimersPool.Release(timer); return; }

                Resend();

                timer.Start();
            }

            public void SendAck(ushort packetId)
            {
                Data data = new Data(0);
                data.Write(GetHeaderBytes(RudpHeaders.Ack));
                data.Write(packetId);

                _client.Send(data.ToArray());
            }

            private byte[] GetHeaderBytes(RudpHeaders header) => new byte[1] { (byte)header };
        }

        public void SendUnreliable(ref Data data) => _senders.SendUnreliable(ref data);

        public void SendReliable(ref Data data) => _senders.SendReliable(data);

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

                    _rtt = 0;

                    _connectionCancellationTokenSource?.Cancel();

                    _connectionCancellationTokenSource?.Dispose();

                    _connectionCancellationTokenSource = null;

                    _disconnectTimer.Change(Timeout.Infinite, Timeout.Infinite);
                    _heartbeatTimer.Change(Timeout.Infinite, Timeout.Infinite);

                    _heartbeatWatch.Stop();

                    _rudpTimersPool.Dispose();
                    _pendingTimers.Clear();

                    _lastPacketId = 0;
                }

                Logger.Log(LogType.Info, "DISCONNECTED", $"Disconnected! REASON: [{reason}]!");
            }
            catch {  }

            _connectionState = ClientState.Disconnected;

            Disconnected?.Invoke();
        }
    }
}