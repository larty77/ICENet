using System;
using System.Collections.Concurrent;

namespace ICENet.Rudp.Common
{
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
}
