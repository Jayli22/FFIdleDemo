using System;
using System.Runtime.CompilerServices;

namespace FFM.Sim.Core
{
    public enum SimEventType : byte
    {
        MatchStarted = 0,
        DropSelected = 1,
        Landed = 2,
        Looted = 3,
        ZoneShrinkStart = 4,
        ZoneShrinkEnd = 5,
        RotationStarted = 6,
        EngagementStarted = 7,
        ShotFired = 8,
        GrenadeThrown = 9,
        Knocked = 10,
        Revived = 11,
        Eliminated = 12,
        SquadWiped = 13,
        GlooPlaced = 14,
        GlooDestroyed = 15,
        SkillTriggered = 16,
        VehicleMounted = 17,
        CommandIssued = 18,
        CommandExecuted = 19,
        ChampionRushEligible = 20,
        Booyah = 21,
        MatchEnded = 22,
        DropLaunched = 23,
        ParachuteOpened = 24,
        DropVeerOff = 25
    }

    /// <summary>
    /// Fixed-size value-type event payload to guarantee zero GC boxing on hot paths (§4.6).
    /// </summary>
    public readonly struct SimEvent
    {
        public readonly int Tick;
        public readonly SimEventType Type;
        public readonly short ActorUnitIdx;
        public readonly short TargetUnitIdx;
        public readonly sbyte SquadIdx;
        public readonly sbyte TargetSquadIdx;
        public readonly Vec2 Pos;
        public readonly Vec2 Pos2;
        public readonly float Value;
        public readonly int ExtraCode;
        public int ActorId => ActorUnitIdx;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SimEvent(
            int tick,
            SimEventType type,
            int actorUnitIdx = -1,
            int targetUnitIdx = -1,
            int squadIdx = -1,
            int targetSquadIdx = -1,
            Vec2 pos = default,
            Vec2 pos2 = default,
            float value = 0f,
            int extraCode = 0)
        {
            Tick = tick;
            Type = type;
            ActorUnitIdx = (short)actorUnitIdx;
            TargetUnitIdx = (short)targetUnitIdx;
            SquadIdx = (sbyte)squadIdx;
            TargetSquadIdx = (sbyte)targetSquadIdx;
            Pos = pos;
            Pos2 = pos2;
            Value = value;
            ExtraCode = extraCode;
        }
    }

    /// <summary>
    /// Pre-allocated ring buffer for simulation events with zero heap allocation per event.
    /// Maintains a running FNV-1a 64-bit hash over all recorded events for determinism checks.
    /// </summary>
    public sealed class EventLog
    {
        private const ulong FnvOffsetBasis = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private readonly SimEvent[] _buffer;
        private readonly SimEvent[] _drainScratch;
        private readonly int _mask;
        private long _writeTotal;
        private long _drainCursor;
        private ulong _runningHash;

        public int Capacity => _buffer.Length;
        public long TotalRecorded => _writeTotal;
        public ulong DeterminismHash => _runningHash;

        /// <param name="capacityPowerOfTwo">Must be a power of two (default 16384).</param>
        public EventLog(int capacityPowerOfTwo = 16384)
        {
            if (capacityPowerOfTwo <= 0 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            {
                throw new ArgumentException("Capacity must be a positive power of two.", nameof(capacityPowerOfTwo));
            }
            _buffer = new SimEvent[capacityPowerOfTwo];
            _drainScratch = new SimEvent[capacityPowerOfTwo];
            _mask = capacityPowerOfTwo - 1;
            Reset();
        }

        public void Reset()
        {
            _writeTotal = 0;
            _drainCursor = 0;
            _runningHash = FnvOffsetBasis;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Record(in SimEvent ev)
        {
            int idx = (int)(_writeTotal & _mask);
            _buffer[idx] = ev;
            _writeTotal++;
            UpdateHash(in ev);
        }

        /// <summary>
        /// Drains all events recorded since the last call to DrainEvents() without heap allocation.
        /// If more than Capacity events were written since the last drain, returns the most recent Capacity events.
        /// </summary>
        public ReadOnlySpan<SimEvent> DrainEvents()
        {
            long unread = _writeTotal - _drainCursor;
            if (unread <= 0)
            {
                return ReadOnlySpan<SimEvent>.Empty;
            }
            if (unread > _buffer.Length)
            {
                _drainCursor = _writeTotal - _buffer.Length;
                unread = _buffer.Length;
            }

            int count = (int)unread;
            int startIdx = (int)(_drainCursor & _mask);
            _drainCursor = _writeTotal;

            if (startIdx + count <= _buffer.Length)
            {
                return new ReadOnlySpan<SimEvent>(_buffer, startIdx, count);
            }

            int firstPart = _buffer.Length - startIdx;
            Array.Copy(_buffer, startIdx, _drainScratch, 0, firstPart);
            Array.Copy(_buffer, 0, _drainScratch, firstPart, count - firstPart);
            return new ReadOnlySpan<SimEvent>(_drainScratch, 0, count);
        }

        /// <summary>
        /// Copies all retained events in chronological order into the scratch buffer and returns a span.
        /// </summary>
        public ReadOnlySpan<SimEvent> PeekAllRetained()
        {
            long retained = Math.Min(_writeTotal, _buffer.Length);
            if (retained <= 0)
            {
                return ReadOnlySpan<SimEvent>.Empty;
            }
            long startSeq = _writeTotal - retained;
            int count = (int)retained;
            int startIdx = (int)(startSeq & _mask);

            if (startIdx + count <= _buffer.Length)
            {
                return new ReadOnlySpan<SimEvent>(_buffer, startIdx, count);
            }

            int firstPart = _buffer.Length - startIdx;
            Array.Copy(_buffer, startIdx, _drainScratch, 0, firstPart);
            Array.Copy(_buffer, 0, _drainScratch, firstPart, count - firstPart);
            return new ReadOnlySpan<SimEvent>(_drainScratch, 0, count);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateHash(in SimEvent ev)
        {
            ulong h = _runningHash;
            h = (h ^ (ulong)ev.Tick) * FnvPrime;
            h = (h ^ (ulong)ev.Type) * FnvPrime;
            h = (h ^ (ulong)ev.ActorUnitIdx) * FnvPrime;
            h = (h ^ (ulong)ev.TargetUnitIdx) * FnvPrime;
            h = (h ^ (ulong)ev.SquadIdx) * FnvPrime;
            h = (h ^ (ulong)ev.TargetSquadIdx) * FnvPrime;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(ev.Pos.X)) * FnvPrime;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(ev.Pos.Y)) * FnvPrime;
            h = (h ^ (uint)BitConverter.SingleToInt32Bits(ev.Value)) * FnvPrime;
            h = (h ^ (ulong)ev.ExtraCode) * FnvPrime;
            _runningHash = h;
        }
    }
}
