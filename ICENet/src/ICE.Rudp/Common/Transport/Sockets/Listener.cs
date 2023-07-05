using System;
using System.Net;
using System.Threading.Tasks;

namespace ICENet.Rudp.Transport.Protocol
{
    public sealed class Listener
    {
        private Socket _socket;

        public EndPoint LocalEp => _socket.LocalEp;

        public Listener() => _socket = new Socket();

        public void Bind(IPEndPoint localEP) => _socket.Bind(localEP);

        public void Start() => _socket.Start();

        public Task<UdpReceiveResult> ReceiveAsync() => _socket.ReceiveAsync();

        public Task<Exception> SendAsync(byte[] data, IPEndPoint remoteEP) => _socket.SendAsync(data, remoteEP);

        public void Stop()
        {
            _socket?.Stop();
            _socket = null!;
        }
    }
}
