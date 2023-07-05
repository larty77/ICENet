using System;
using System.Net;
using System.Threading.Tasks;

namespace ICENet.Rudp.Transport.Protocol
{
    public sealed class Client
    {
        private Socket _socket;

        public EndPoint LocalEp => _socket.LocalEp;

        public EndPoint RemoteEp => _socket.RemoteEp;

        public Client() => _socket = new Socket();

        public void Bind(IPEndPoint localEP) => _socket.Bind(localEP);

        public void Connect(IPEndPoint remoteEP) => _socket.Connect(remoteEP);

        public Task<UdpReceiveResult> ReceiveAsync() => _socket.ReceiveAsync();

        public Task<Exception> SendAsync(byte[] data) => _socket.SendAsync(data);

        public void Stop()
        {
            _socket.Stop();
            _socket = null!;
        }
    }
}
