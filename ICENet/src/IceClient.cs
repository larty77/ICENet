using System;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using ICENet.Rudp;
using ICENet.Traffic;
using ICENet.Core.Data;
using ICENet.Core.Transport;
using ICENet.Core.Helpers;
using ICENet.Rudp.Transport;

namespace ICENet
{
    public sealed class IceClient
    {
        private readonly RudpClient _client;

        public IPEndPoint? LocalEndPoint => _client?.LocalEndPoint;

        public IPEndPoint? RemoteEndPoint => _client?.RemoteEndPoint;

        public int Ping => _client.Ping;

        public bool IsConnected => _client.IsConnected;

        public class TrafficHelper
        {
            private readonly PacketFactory _packetFactory;

            private readonly ConcurrentDictionary<Type, Action<Packet>> _handlers = new ConcurrentDictionary<Type, Action<Packet>>();

            private readonly ConcurrentDictionary<Type, MethodInfo> _methodCache = new ConcurrentDictionary<Type, MethodInfo>();

            public TrafficHelper()
            {
                _packetFactory = new PacketFactory();
            }

            public void AddOrUpdateHandler<T>(Action<T> handleAction) where T : Packet, new()
            {
                if (_packetFactory.AddOrUpdate<T>() is true)
                    _handlers[typeof(T)] = (packet) => handleAction((T)packet);
            }

            internal Packet GetPacketInstance(ref Data data) => _packetFactory.GetInstance(ref data);

            internal void InvokeHandler(Packet packet)
            {
                try
                {
                    Type packetType = packet.GetType();

                    if (_methodCache.TryGetValue(packetType, out MethodInfo method) is false)
                        _methodCache[packetType] = GetType().GetMethod(nameof(InvokeHandlerGeneric), BindingFlags.Instance |
                            BindingFlags.NonPublic).MakeGenericMethod(packetType);

                    method = _methodCache[packetType];

                    method.Invoke(this, new object[] { packet });
                }
                catch (Exception exc) { Logger.Log(LogType.Error, "PACKET HANDLER ERROR", $"Error invoking handler for packet type {packet.GetType()}: {exc.Message}"); }
            }

            private void InvokeHandlerGeneric<T>(T packet) where T : Packet, new()
            {
                try { _handlers[packet!.GetType()].Invoke(packet); } catch { }
            }
        }

        public TrafficHelper Traffic { get; private set; }

        public delegate void ConnectionStatusChangedHandler();

        public ConnectionStatusChangedHandler? Disconnected;

        public IceClient(IClient socket = null!)
        {
            Traffic = new TrafficHelper();

            socket ??= new UdpClient();

            _client = new RudpClient(socket)
            {
                ExternalDataHandled = Handle,

                Disconnected = () =>
                {
                    try { Disconnected?.Invoke(); }

                    catch (Exception exc) { Logger.Log(LogType.Error, "DISCONNECTED EVENT ERROR", exc.Message); }
                }
            };
        }

        public bool TryConnect(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint)
        {
            try
            {
                var taskCompletionSource = new TaskCompletionSource<bool>();

                Task.Run(async () =>
                {
                    try
                    {
                        bool result = await TryConnectAsync(remoteEndPoint, localEndPoint);
                        taskCompletionSource.SetResult(result);
                    }
                    catch (Exception ex) { taskCompletionSource.SetException(ex); }
                });

                bool result = taskCompletionSource.Task.Wait(TimeSpan.FromMilliseconds(2500));

                return result;
            }
            catch (Exception exc)
            {
                Logger.Log(LogType.Error, "TRY CONNECT ERROR", exc.Message);
                return false;
            }
        }

        private async Task<bool> TryConnectAsync(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint) => await _client.TryConnect(remoteEndPoint, localEndPoint);
        
        private void Handle(ref Data data)
        {
            try
            {
                Packet packet = Traffic.GetPacketInstance(ref data);

                packet.Deserialize(ref data);

                Traffic.InvokeHandler(packet);
            }
            catch (Exception exc) { Logger.Log(LogType.Error, "HANDLE ERROR", exc.Message); }
        }

        public void Send(Packet packet)
        {
            if (IsConnected is false) throw new Exception("The connection does not exist. Cannot send a packet!");

            Data data = new Data(0);

            packet.Serialize(ref data);

            if (packet.IsReliable is true) _client.SendReliable(data);

            else _client.SendUnreliable(ref data);
        }

        public void Disconnect(string reason = "No reason") => _client?.Disconnect(reason); 
    }
}
