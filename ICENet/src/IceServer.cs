using System;
using System.Net;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Collections.Concurrent;

using ICENet.Rudp;
using ICENet.Traffic;
using ICENet.Core.Data;
using ICENet.Core.Transport;
using ICENet.Core.Helpers;
using ICENet.Rudp.Transport;
using ICENet;

namespace ICENet
{
    public sealed class IceServer
    {
        private readonly RudpServer? _server;

        public IPEndPoint? LocalEndPoint => _server?.LocalEndPoint;

        public class ConnectionsHelper
        {
            private readonly ConcurrentDictionary<int, IceConnection> _connections;

            public IEnumerable<KeyValuePair<int, IceConnection>> Connections => _connections;

            public int Count => _connections.Count;

            public delegate void ConnectionAddedHandler(IceConnection connection);

            public delegate void ConnectionRemovedHandler(IPEndPoint remoteEp);

            public ConnectionAddedHandler? Added;

            public ConnectionRemovedHandler? Removed;

            private object _lock = new object();

            public IceConnection this[int index]
            {
                get => _connections[index];
            }

            internal ConnectionsHelper()
            {
                _connections = new ConcurrentDictionary<int, IceConnection>();
            }

            internal void AddClient(RudpConnection connection)
            {
                IceConnection addedConnection = new IceConnection(connection, new IPEndPoint(connection.RemoteEndPoint.Address, connection.RemoteEndPoint.Port));

                int newIndex = 0;
                lock (_lock) { while (_connections.ContainsKey(newIndex)) newIndex++; }

                addedConnection.Id = newIndex;
                if (_connections.TryAdd(newIndex, addedConnection) is false) return;
                
                try { Added?.Invoke(addedConnection); Logger.Log(LogType.Info, "ICE-CONNECTED", $"Client added with index {newIndex}!"); }
                catch (Exception exc) { Logger.Log(LogType.Error, "CONNECTED EVENT ERROR", exc.Message); }
            }

            internal void RemoveClient(IPEndPoint remoteEp)
            {
                if(TryGetClient(out var connection, remoteEp) is false) return;

                if (_connections.TryRemove(connection.Id, out var removedConnection) is false) return;

                try { Removed?.Invoke(remoteEp); Logger.Log(LogType.Info, "ICE-DISCONNECTED", $"Client with index {connection.Id} removed!"); }
                catch (Exception exc) { Logger.Log(LogType.Error, "DISCONNECTED EVENT ERROR", exc.Message); }
            }

            internal bool TryGetClient(out IceConnection connection, IPEndPoint remoteEp)
            {
                connection = _connections.SingleOrDefault(x => (x.Value.RemoteEndPoint.Address == remoteEp.Address && x.Value.RemoteEndPoint.Port == remoteEp.Port)).Value;
                return connection != null;
            }

            internal void Clear()
            {
                _connections?.Clear();
            }
        }

        public ConnectionsHelper Connections { get; private set; }

        public class TrafficHelper
        {
            private readonly PacketFactory _packetFactory;

            private readonly ConcurrentDictionary<Type, Action<Packet, IceConnection>> _handlers = new ConcurrentDictionary<Type, Action<Packet, IceConnection>>();

            private readonly ConcurrentDictionary<Type, MethodInfo> _methodCache = new ConcurrentDictionary<Type, MethodInfo>();

            public TrafficHelper()
            {
                _packetFactory = new PacketFactory();
            }

            public void AddOrUpdateHandler<T>(Action<T, IceConnection> handleAction) where T : Packet, new()
            {
                if (_packetFactory.AddOrUpdate<T>() is true)
                    _handlers[typeof(T)] = (packet, connection) => handleAction((T)packet, connection);
            }

            internal Packet GetPacketInstance(ref Data data) => _packetFactory.GetInstance(ref data);

            internal void InvokeHandler(Packet packet, IceConnection connection)
            {
                try
                {
                    Type packetType = packet.GetType();

                    if (_methodCache.TryGetValue(packetType, out MethodInfo method) is false)
                        _methodCache[packetType] = GetType().GetMethod(nameof(InvokeHandlerGeneric), BindingFlags.Instance |
                            BindingFlags.NonPublic).MakeGenericMethod(packetType);

                    method = _methodCache[packetType];

                    method.Invoke(this, new object[] { packet, connection });
                }
                catch (Exception exc) { Logger.Log(LogType.Error, "PACKET HANDLER ERROR", $"Error invoking handler for packet type {packet.GetType()}: {exc.Message}"); }
            }

            private void InvokeHandlerGeneric<T>(T packet, IceConnection connection) where T : Packet, new()
            {
                try { _handlers[packet!.GetType()].Invoke(packet, connection); } catch { }
            }
        }

        public TrafficHelper Traffic { get; private set; }

        public IceServer(IListener socket = null!, int maxConnections = 8)
        {
            Connections = new ConnectionsHelper();

            Traffic = new TrafficHelper();

            socket ??= new UdpListener();

            _server = new RudpServer(socket, maxConnections)
            {
                ExternalDataHandled = Handle,

                ConnectionAdded = Connections.AddClient,

                ConnectionRemoved = Connections.RemoveClient
            };
        }

        public bool TryStart(IPEndPoint localEndPoint) => _server!.TryStart(localEndPoint);
        
        private void Handle(ref Data data, RudpConnection connection)
        {
            try
            {
                IceConnection iceConnection = null!;

                if (Connections?.TryGetClient(out iceConnection, connection.RemoteEndPoint) is false) return;

                Packet packet = Traffic.GetPacketInstance(ref data);

                packet.Deserialize(ref data);

                Traffic.InvokeHandler(packet, iceConnection);
            }
            catch(Exception exc) { Logger.Log(LogType.Error, "HANDLE ERROR", exc.Message); }
        }

        public void Stop(string reason = "No reason") => _server?.Stop(reason);
    }
}
