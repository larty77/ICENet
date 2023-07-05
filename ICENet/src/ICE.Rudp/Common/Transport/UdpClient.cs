using System;
using System.Net;
using System.Threading.Tasks;

using ICENet.Core.Helpers;
using ICENet.Core.Transport;
using ICENet.Rudp.Transport.Protocol;

namespace ICENet.Rudp.Transport
{
    public class UdpClient : IClient
    {
        public IPEndPoint LocalEndPoint => (IPEndPoint)_client!.LocalEp;

        public IPEndPoint RemoteEndPoint => (IPEndPoint)_client!.RemoteEp;

        public event Action<byte[]>? Received;

        private Client? _client;

        private bool _isRunning = false;

        public bool TryConnect(IPEndPoint remoteEndPoint, IPEndPoint localEndPoint)
        {
            try
            {
                _client = new Client();
                _client.Bind(localEndPoint);
                _client.Connect(remoteEndPoint);
            }

            catch (Exception exc)
            {
                Logger.Log(LogType.Error, "(TRANSPORT) CONNECT ERROR", exc.Message);

                return false;
            }


            Logger.Log(LogType.Info, "(TRANSPORT) CONNECT", $"Socket connected successfully! LocalEndPoint: {LocalEndPoint}!");

            _isRunning = true;

            Receiving();

            return true;
        }

        private async void Receiving()
        {
            while (_isRunning)
            {
                try
                {
                    var receiveResult = await _client!.ReceiveAsync();

                    if (receiveResult.Exception?.Message != null) Logger.Log(LogType.Error, "(TRANSPORT) RECEIVE ERROR", receiveResult.Exception.Message);

                    if (receiveResult.Exception?.Message != null) continue;

                    if (receiveResult.Bytes.Length >= 1) _ = Task.Run(() => Received?.Invoke(receiveResult.Bytes));
                }
                catch (Exception exc) { Logger.Log(LogType.Error, "(TRANSPORT) RECEIVE ERROR", exc.Message); }
            }
        }

        public async void Send(byte[] bytes)
        {
            try
            {
                var sendResult = await _client!.SendAsync(bytes);

                if (sendResult != null) Logger.Log(LogType.Error, "(TRANSPORT) SEND ERROR", sendResult.Message);
            }
            catch(Exception exc) { Logger.Log(LogType.Error, "(TRANSPORT) SEND ERROR", exc.Message); }
        }

        public void Stop()
        {
            _client?.Stop();
            _client = null;
            _isRunning = false;
        }
    }
}
