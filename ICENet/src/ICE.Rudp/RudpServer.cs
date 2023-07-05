using System;
using System.Net;
using System.Collections.Concurrent;

using ICENet.Core.Data;
using ICENet.Rudp.Common;
using ICENet.Core.Transport;
using ICENet.Rudp.Transport;
using ICENet.Core.Helpers;

namespace ICENet.Rudp
{
    internal sealed class RudpServer : RudpPeer
    {
        private readonly IListener _socket;

        public IPEndPoint? LocalEndPoint => _socket.LocalEndPoint;

        public delegate void DataHandler(ref Data data, RudpConnection connection);

        public DataHandler? ExternalDataHandled;

        #region Connections Management

        private readonly ConcurrentDictionary<IPEndPoint, RudpConnection> _connections;

        private readonly int _maxConnections;

        public int MaxConnections => _maxConnections;

        public int ConnectionsCount => _connections.Count;

        #endregion

        #region Connections Events

        public Action<RudpConnection>? ConnectionAdded;

        public Action<IPEndPoint>? ConnectionRemoved;

        #endregion

        #region Reability

        private RudpPool<RudpTimer> _rudpTimersPool;

        #endregion

        private readonly object _lock = new object();

        public RudpServer(IListener socket, int maxConnections)
        {
            _socket = socket ?? new UdpListener();
            _socket.AddReceiveListener(Handle);

            _connections = new ConcurrentDictionary<IPEndPoint, RudpConnection>();

            _maxConnections = (maxConnections < 3 || maxConnections > 128) ? 4 : maxConnections;

            _rudpTimersPool = new RudpPool<RudpTimer>();
        }

        public bool TryStart(IPEndPoint localEp) => _socket.TryStart(localEp);

        private void Handle(byte[] bytes, IPEndPoint remoteEp)
        {
            try
            {
                byte packetId = bytes[0];

                if (TryGetConnection(out var connection, remoteEp) is false && packetId is (byte)RudpHeaders.ConnectRequest)
                {
                    if (TryAddConnection(out connection, remoteEp) is false)
                        return;
                }

                Data data = new Data(0);
                data.LoadBytes(bytes);

                connection.Handle(ref data);
            }
            catch {  }
        }

        internal void Send(byte[] bytes, IPEndPoint remoteEp) => _socket.Send(bytes, remoteEp);

        #region Connections

        private bool TryGetConnection(out RudpConnection connection, IPEndPoint remoteEp)
        {
            return _connections.TryGetValue(remoteEp, out connection);
        }

        private bool TryAddConnection(out RudpConnection connection, IPEndPoint remoteEp)
        {
            if (_connections.Count >= _maxConnections)
            {
                Logger.Log(LogType.Info, "ADD NEW CONNECTION", $"The client with address [{remoteEp}] has NOT been added!");
                connection = default!;
                return false;
            }

            try
            {
                connection = new RudpConnection(remoteEp, Send, (reason) => TryRemoveConnection(remoteEp, reason), ExternalDataHandled!, _rudpTimersPool);

                if (_connections.TryAdd(remoteEp, connection))
                {
                    Logger.Log(LogType.Info, "ADD NEW CONNECTION", $"A new client with [{remoteEp}] has been added!");
                    ConnectionAdded?.Invoke(connection);
                    return true;
                }

                return false;
            }
            catch (Exception exc)
            {
                Logger.Log(LogType.Error, "ADD CONNECTION ERROR", exc.Message);
                
                connection = default!;
                return false;
            }
        }

        private void TryRemoveConnection(IPEndPoint remoteEp, string reason)
        {
            try
            {
                if (_connections.TryRemove(remoteEp, out _))
                {
                    Logger.Log(LogType.Info, "REMOVE CONNECTION", $"Connection[{remoteEp}] has been disconnected! REASON: [{reason}]!");
                    ConnectionRemoved?.Invoke(remoteEp);
                }
            }
            catch (Exception exc) { Logger.Log(LogType.Error, "REMOVE CONNECTION ERROR", exc.Message); }
        }

        #endregion

        public void Stop(string reason)
        {
            try
            {
                lock (_lock)
                {
                    _socket.Stop();

                    _connections.Clear();

                    _rudpTimersPool.Dispose();
                }
            }
            catch { }

            Logger.Log(LogType.Info, "STOPED", $"Stoped! REASON: [{reason}]!");
        }
    }
}
