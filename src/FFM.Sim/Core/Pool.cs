using System;
using System.Runtime.CompilerServices;

namespace FFM.Sim.Core
{
    /// <summary>
    /// Pre-allocated object pool for reusable reference types.
    /// </summary>
    public sealed class Pool<T> where T : class
    {
        private readonly T[] _items;
        private readonly Func<T> _factory;
        private readonly Action<T>? _reset;
        private int _count;

        public int AvailableCount => _count;
        public int Capacity => _items.Length;

        public Pool(int capacity, Func<T> factory, Action<T>? reset = null, bool prewarm = true)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new T[capacity];
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _reset = reset;

            if (prewarm)
            {
                for (int i = 0; i < capacity; i++)
                {
                    _items[i] = _factory();
                }
                _count = capacity;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Rent()
        {
            if (_count > 0)
            {
                _count--;
                T item = _items[_count];
                _items[_count] = null!;
                return item;
            }
            return _factory();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(T item)
        {
            _reset?.Invoke(item);
            if (_count < _items.Length)
            {
                _items[_count] = item;
                _count++;
            }
        }
    }
}
