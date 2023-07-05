using System;
using System.Net;

namespace ICENet.Core.Transport
{
    public interface IClient : IPeer
    {
        IPEndPoint RemoteEndPoint { get; }

        event Action<byte[]>? Received;

        public void AddReceiveListener(Action<byte[]> listener) => Received += listener;

        public void RemoveReceiveListener(Action<byte[]> listener) => Received -= listener;

        bool TryConnect(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint);

        void Send(byte[] bytes);

        void Stop();
    }
}
