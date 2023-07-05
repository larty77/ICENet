using System.Net;

using ICENet.Core.Data;
using ICENet.Traffic;
using ICENet.Rudp;

namespace ICENet
{
    public sealed class IceConnection
    {
        public int Id { get; internal set; }

        public IPEndPoint RemoteEndPoint { get; }

        private RudpConnection _connection;

        public int Ping => (_connection != null && _connection.IsConnected) ? _connection.Ping : 0;

        public bool IsConnected => !(_connection is null) && _connection.IsConnected;

        internal IceConnection(RudpConnection connection, IPEndPoint remoteEp)
        {
            _connection = connection;
            RemoteEndPoint = remoteEp;
        }

        public void Send(Packet packet)
        {
            if (IsConnected is false) throw new System.Exception("The connection does not exist. Cannot send a packet!");

            Data data = new Data(0);

            packet.Serialize(ref data);

            if (packet.IsReliable is true) _connection.SendReliable(ref data);

            else _connection.SendUnreliable(ref data);
        }

        public void Disconnect(string reason = "No reason") => _connection.Disconnect(reason);  
    }
}
