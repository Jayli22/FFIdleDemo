using System;
using FFM.Sim.Simulation;

namespace FFM.Client.Sim
{
    /// <summary>
    /// Pre-allocated ring buffer of SnapshotBuffers supporting zero-GC 10s live rewind (100 ticks)
    /// and 15s post-match highlight replay (§6.2, WP-11).
    /// </summary>
    public sealed class SnapshotRingBuffer
    {
        private readonly SnapshotBuffer[] _frames;
        private int _head;
        private int _count;

        public int Capacity => _frames.Length;
        public int Count => _count;

        public SnapshotRingBuffer(int capacity = 3600)
        {
            if (capacity < 120) capacity = 120;
            _frames = new SnapshotBuffer[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _frames[i] = new SnapshotBuffer();
            }
            _head = 0;
            _count = 0;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        public void Push(SnapshotBuffer source)
        {
            source.CopyTo(_frames[_head]);
            _head = (_head + 1) % _frames.Length;
            if (_count < _frames.Length)
            {
                _count++;
            }
        }

        public bool TryGetByTick(int tick, out SnapshotBuffer snapshot)
        {
            if (_count == 0)
            {
                snapshot = _frames[0];
                return false;
            }

            // Search backward from most recent frame
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - 1 - i + _frames.Length * 2) % _frames.Length;
                if (_frames[idx].Tick == tick)
                {
                    snapshot = _frames[idx];
                    return true;
                }
            }

            // Fallback to closest available frame
            int oldestIdx = (_head - _count + _frames.Length) % _frames.Length;
            int newestIdx = (_head - 1 + _frames.Length) % _frames.Length;
            if (tick <= _frames[oldestIdx].Tick)
            {
                snapshot = _frames[oldestIdx];
                return true;
            }
            snapshot = _frames[newestIdx];
            return true;
        }

        public int NewestTick
        {
            get
            {
                if (_count == 0) return 0;
                int newestIdx = (_head - 1 + _frames.Length) % _frames.Length;
                return _frames[newestIdx].Tick;
            }
        }

        public int OldestTick
        {
            get
            {
                if (_count == 0) return 0;
                int oldestIdx = (_head - _count + _frames.Length) % _frames.Length;
                return _frames[oldestIdx].Tick;
            }
        }
    }
}
