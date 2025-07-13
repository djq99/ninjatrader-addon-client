using System;
using System.Threading;

namespace NT8Bridge.Util
{
    public class RingBuffer<T> : IDisposable
    {
        private readonly T[] _buffer;
        private readonly int _capacity;
        private volatile int _readIndex;
        private volatile int _writeIndex;
        private volatile int _count;
        private readonly object _lockObject = new object();

        public RingBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be positive", nameof(capacity));

            _capacity = capacity;
            _buffer = new T[capacity];
            _readIndex = 0;
            _writeIndex = 0;
            _count = 0;
        }

        public int Capacity => _capacity;
        public int Count => _count;
        public bool IsEmpty => _count == 0;
        public bool IsFull => _count == _capacity;

        public bool TryWrite(T item)
        {
            lock (_lockObject)
            {
                if (IsFull)
                    return false;

                _buffer[_writeIndex] = item;
                _writeIndex = (_writeIndex + 1) % _capacity;
                Interlocked.Increment(ref _count);
                return true;
            }
        }

        public void Write(T item)
        {
            while (!TryWrite(item))
            {
                // Wait for space to become available
                Thread.Sleep(1);
            }
        }

        public bool TryRead(out T item)
        {
            lock (_lockObject)
            {
                if (IsEmpty)
                {
                    item = default(T);
                    return false;
                }

                item = _buffer[_readIndex];
                _readIndex = (_readIndex + 1) % _capacity;
                Interlocked.Decrement(ref _count);
                return true;
            }
        }

        public T Read()
        {
            while (!TryRead(out T item))
            {
                // Wait for data to become available
                Thread.Sleep(1);
            }
            return item;
        }

        public void Clear()
        {
            lock (_lockObject)
            {
                Array.Clear(_buffer, 0, _capacity);
                _readIndex = 0;
                _writeIndex = 0;
                _count = 0;
            }
        }

        public void Dispose()
        {
            Clear();
        }
    }
} 