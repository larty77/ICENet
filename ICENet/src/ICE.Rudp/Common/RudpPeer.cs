using System;
using System.Threading;
using System.Collections.Concurrent;

using Timer = System.Threading.Timer;

namespace ICENet.Rudp.Common
{
    internal abstract class RudpPeer
    {
        public enum ClientState
        {
            Disconnected,

            Connecting,

            Connected
        }

        public enum RudpHeaders : byte
        {
            ConnectRequest = 1,

            ConnectResponse,

            HearbeatRequest,

            HeartbeatResponse,

            Unreliable,

            Reliable,      

            Ack,
        }

        public const int DisconnectTimeout = 3000;

        public const int HeartbeatInterval = 750;

        public const int MinResendTime = 200;

        public const int MaxResendTime = 600;

        public const int MaxResendCount = 8;

        #region Common Calculations

        public static int CalculateResendTime(int smoothRtt) => Math.Min(Math.Max(MinResendTime, smoothRtt * 2), MaxResendTime);

        public static int CalculateSmoothRtt(int rtt, ref int oldSmoothRtt) => oldSmoothRtt = (int)((0.2 * rtt) + (0.8 * oldSmoothRtt));  

        #endregion
    }

    internal interface IRudpPoolable
    {
        void Reset();

        void Dispose();
    }

    internal sealed class RudpPool<T> : IDisposable where T : IRudpPoolable, new()
    {
        private ConcurrentQueue<T> _pool;       
        
        public int Count => _pool != null ? _pool.Count : 0;

        private readonly object _lock = new object();

        public RudpPool()
        {
            _pool = new ConcurrentQueue<T>();
        }

        public T Get()
        {
            T result;

            lock (_lock)
            {
                if (_pool!.TryDequeue(out result) is false)
                    result = new T();
            }

            return result;
        }

        public void Release(T poolable)
        {
            poolable.Reset();

            _pool!.Enqueue(poolable);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var poolable in _pool) poolable.Dispose();

                _pool.Clear();
            }
        }
    }

    internal sealed class RudpTimer : IRudpPoolable
    {
        public Action? Ended;

        public Action? Elapsed;

        public Action<RudpTimer>? ErrorHandled;

        public int Interval { get; set; }

        public int MaxElaspedCount { get; set; }

        private int _elapsedCount;

        private Timer _timer;

        public RudpTimer()
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
            _elapsedCount++;

            if (_elapsedCount >= MaxElaspedCount) Ended?.Invoke();

            else Elapsed?.Invoke();
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
