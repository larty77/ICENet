using System;
using System.Net;
using System.Threading.Tasks;

namespace ICENet.Rudp.Transport.Protocol
{
    public readonly struct UdpReceiveResult
    {
        public UdpReceiveResult(byte[] bytes, IPEndPoint remoteEP, Exception exception)
        {
            Bytes = bytes;
            RemoteEndPoint = remoteEP;
            Exception = exception;
        }

        public readonly byte[] Bytes;

        public readonly IPEndPoint RemoteEndPoint;

        public readonly Exception Exception;
    }

    internal class Socket
    {


        public EndPoint LocalEp => _socket?.Client.LocalEndPoint!;

        public EndPoint RemoteEp => _socket?.Client.RemoteEndPoint!;

        private System.Net.Sockets.UdpClient? _socket;

        private IPEndPoint? _localEP;

        public void Bind(IPEndPoint localEP) => _localEP = localEP;

        public void Start()
        {
            _socket = new System.Net.Sockets.UdpClient();
            _socket.Client.Bind(_localEP ?? new IPEndPoint(0, 0)!);
        }

        public void Connect(IPEndPoint remoteEP)
        {
            _socket = new System.Net.Sockets.UdpClient();
            _socket.Client.Bind(_localEP ?? new IPEndPoint(0, 0)!);

            _socket.Connect(remoteEP);
        }

        public Task<UdpReceiveResult> ReceiveAsync()
        {
            if (_socket is null) throw new NullReferenceException(nameof(_socket));

            return Task.Factory.FromAsync(_socket.BeginReceive, (result) =>
            {
                try
                {
                    IPEndPoint remoteEp = new IPEndPoint(0, 0);

                    var data = _socket.EndReceive(result, ref remoteEp);

                    return new UdpReceiveResult(data, remoteEp, null!);
                }
                catch (Exception exception)
                {
                    return new UdpReceiveResult(null!, null!, exception);
                }
            }, null);
        }

        public Task<Exception> SendAsync(byte[] data)
        {
            if (_socket is null) throw new NullReferenceException(nameof(_socket));

            var tcs = new TaskCompletionSource<Exception>();

            _socket.BeginSend(data, data.Length, ar =>
            {
                try
                {
                    _socket.EndSend(ar);
                    tcs.SetResult(null!);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public Task<Exception> SendAsync(byte[] data, IPEndPoint remoteEp)
        {
            if (_socket is null) throw new NullReferenceException(nameof(_socket));

            var tcs = new TaskCompletionSource<Exception>();

            _socket.BeginSend(data, data.Length, remoteEp, result =>
            {
                try
                {
                    _socket.EndSend(result);
                    tcs.SetResult(null!);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            }, null);

            return tcs.Task;
        }

        public void Stop()
        {
            _socket?.Close();
            _socket?.Dispose();
            _socket = null;
        }
    }
}
