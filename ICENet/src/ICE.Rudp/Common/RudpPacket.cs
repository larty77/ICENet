using System;
using System.Threading;

using Timer = System.Threading.Timer;
using ICENet.Core.Helpers;

namespace ICENet.Rudp.Common
{
    internal sealed class RudpPacket : IRudpPoolable
    {
        public Action<Core.Data.Data>? Elapsed;

        public Action? Ended;

        public Core.Data.Data PendingData;

        private int _elapsedCount;

        private Timer _timer;

        public int Interval { get; set; }

        public int MaxElaspedCount { get; set; }


        public RudpPacket()
        {
            Ended = null;
            Elapsed = null;

            Interval = 0;
            MaxElaspedCount = 0;

            _elapsedCount = 0;

            _timer = new Timer(_ => Elapse(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            _timer.Change(Interval, Interval);
        }

        private void Elapse()
        {
            try
            {
                _elapsedCount++;

                if (_elapsedCount >= MaxElaspedCount) Ended?.Invoke();

                else Elapsed?.Invoke(PendingData);
            }
            catch(Exception exc)
            {
                Logger.Log(LogType.Error, "RUDP-TIMER ERROR", exc.Message);
            }
        }

        #region Pool

        void IRudpPoolable.Reset()
        {
            Ended = null;
            Elapsed = null;

            Interval = 0;
            MaxElaspedCount = 0;

            _elapsedCount = 0;

            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        void IRudpPoolable.Dispose()
        {
            Ended = null;
            Elapsed = null;

            Interval = 0;
            MaxElaspedCount = 0;

            _elapsedCount = 0;

            _timer.Dispose();
        }

        #endregion
    }
}
