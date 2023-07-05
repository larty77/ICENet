using System;
using System.Net;

namespace ICENet.Core.Transport
{
    public interface IListener : IPeer
    {
        event Action<byte[], IPEndPoint>? Received;

        public void AddReceiveListener(Action<byte[], IPEndPoint> listener) => Received += listener;

        public void RemoveReceiveListener(Action<byte[], IPEndPoint> listener) => Received -= listener;

        bool TryStart(IPEndPoint localEndPoint);

        void Send(byte[] bytes, IPEndPoint remoteEndPoint);

        void Stop();
    }
}
