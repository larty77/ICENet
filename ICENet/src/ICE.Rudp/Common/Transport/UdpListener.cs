using System;
using System.Net;
using System.Threading.Tasks;

using ICENet.Core.Helpers;
using ICENet.Core.Transport;
using ICENet.Rudp.Transport.Protocol;

namespace ICENet.Rudp.Transport
{
    public class UdpListener : IListener
    {
        public IPEndPoint LocalEndPoint => (IPEndPoint)_listener!.LocalEp;

        public event Action<byte[], IPEndPoint>? Received;

        private Listener? _listener;

        private bool _isRunning = false;

        public bool TryStart(IPEndPoint localEndPoint)
        {
            try
            {
                _listener = new Listener();
                _listener.Bind(localEndPoint);
                _listener.Start();
            }

            catch (Exception exc)
            {
                Logger.Log(LogType.Error, "(TRANSPORT) START ERROR", exc.Message);

                return false;
            }

            Logger.Log(LogType.Info, "(TRANSPORT) START", $"Socket connected successfully! LocalEndPoint: {LocalEndPoint}!");

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
                    var receiveResult = await _listener!.ReceiveAsync();

                    if (receiveResult.Exception?.Message != null) Logger.Log(LogType.Error, "(TRANSPORT) RECEIVE ERROR", receiveResult.Exception.Message);

                    if (receiveResult.Exception?.Message != null) continue;

                    if (receiveResult.Bytes.Length >= 1) _ = Task.Run(() => Received?.Invoke(receiveResult.Bytes, receiveResult.RemoteEndPoint));
                }
                catch (Exception exc) { Logger.Log(LogType.Error, "(TRANSPORT) RECEIVE ERROR", exc.Message); }
            }
        }

        public async void Send(byte[] bytes, IPEndPoint remoteEndPoint)
        {
            try
            {
                var sendResult = await _listener!.SendAsync(bytes, remoteEndPoint);

                if (sendResult != null) Logger.Log(LogType.Error, "(TRANSPORT) SEND ERROR", sendResult.Message);
            }
            catch(Exception exc) { Logger.Log(LogType.Error, "(TRANSPORT) SEND ERROR", exc.Message); }
        }

        public void Stop()
        {
            _listener?.Stop();
            _listener = null;
            _isRunning = false;
        }
    }
}
