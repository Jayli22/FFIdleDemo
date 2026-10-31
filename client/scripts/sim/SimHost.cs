using System;
using System.Collections.Generic;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.Sim
{
    /// <summary>
    /// Godot Node that hosts a deterministic 10Hz MatchRunner, maintains a 3600-frame SnapshotRingBuffer,
    /// drives 60fps interpolated frame state, and supports 0x/1x/2x/4x/SkipToEnd, 10s Live Rewind, and
    /// 15s Post-Match Highlight Replay (§6.2, WP-11).
    /// </summary>
    public partial class SimHost : Node
    {
        public const float TickIntervalSec = 0.1f;

        private ISimulation? _sim;
        private readonly SnapshotRingBuffer _ringBuffer = new(3600);
        private readonly List<SimEvent> _recordedEvents = new(4096);
        private int _eventPlaybackCursor;

        private float _accumulatorSec;
        private float _speedMultiplier = 1f;
        private int _playerSquadIndex;
        private bool _matchResultEmitted;

        // Replay / Rewind state
        private bool _isReplaying;
        private int _replayCurrentTick;
        private int _replayEndTick;
        private float _replayAccumulatorSec;
        private string _replayLabel = string.Empty;
        private bool _isPostMatchClip;

        public InterpolatedFrameState CurrentFrame { get; } = new();
        public ISimulation? Simulation => _sim;
        public MapData? Map => _sim?.Map;
        public int PlayerSquadIndex => _playerSquadIndex;
        public float SpeedMultiplier => _speedMultiplier;
        public bool IsPaused => _speedMultiplier <= 0.001f;
        public bool IsReplaying => _isReplaying;
        public string ReplayLabel => _replayLabel;
        public bool IsFinished => _sim?.IsFinished ?? false;

        public event Action<SimEvent>? EventDispatched;
        public event Action<MatchResult>? MatchCompleted;

        public void StartMatch(ISimulation sim, int playerSquadIndex = 0)
        {
            _sim = sim ?? throw new ArgumentNullException(nameof(sim));
            _playerSquadIndex = Math.Clamp(playerSquadIndex, 0, SnapshotBuffer.MaxSquads - 1);
            _ringBuffer.Clear();
            _recordedEvents.Clear();
            _eventPlaybackCursor = 0;
            _accumulatorSec = 0f;
            _speedMultiplier = 1f;
            _isReplaying = false;
            _isPostMatchClip = false;
            _matchResultEmitted = false;

            _ringBuffer.Push(_sim.CurrSnapshot);
            DrainAndDispatchLiveEvents();
            SnapshotInterpolator.Interpolate(_sim.PrevSnapshot, _sim.CurrSnapshot, 1f, _sim.Map, CurrentFrame);
        }

        public void SetSpeed(float multiplier)
        {
            _speedMultiplier = Math.Clamp(multiplier, 0f, 8f);
        }

        public void TogglePause()
        {
            _speedMultiplier = IsPaused ? 1f : 0f;
        }

        /// <summary>
        /// Steps remaining ticks of the match immediately while recording all snapshots and events
        /// so post-match 15s highlight clips remain 100% playable.
        /// </summary>
        public void SkipToEnd()
        {
            if (_sim == null) return;
            _isReplaying = false;
            _isPostMatchClip = false;

            int safety = 4000;
            while (!_sim.IsFinished && safety-- > 0)
            {
                _sim.Step();
                _ringBuffer.Push(_sim.CurrSnapshot);
                var span = _sim.Events.DrainEvents();
                for (int i = 0; i < span.Length; i++)
                {
                    _recordedEvents.Add(span[i]);
                }
            }

            SnapshotInterpolator.Interpolate(_sim.PrevSnapshot, _sim.CurrSnapshot, 1f, _sim.Map, CurrentFrame);
            CheckAndEmitMatchCompletion();
        }

        /// <summary>
        /// Rewinds 10 seconds (100 ticks) and plays back through the SnapshotRingBuffer before seamlessly resuming live simulation.
        /// </summary>
        public void Rewind10Seconds()
        {
            if (_sim == null || _ringBuffer.Count < 5) return;

            int newest = _ringBuffer.NewestTick;
            int targetStart = Math.Max(_ringBuffer.OldestTick, newest - 100);
            _isReplaying = true;
            _isPostMatchClip = false;
            _replayCurrentTick = targetStart;
            _replayEndTick = newest;
            _replayAccumulatorSec = 0f;
            _replayLabel = "REPLAY -10s";
            SeekEventCursorToTick(_replayCurrentTick);
            if (_speedMultiplier <= 0.001f)
            {
                _speedMultiplier = 1f;
            }
        }

        /// <summary>
        /// Plays a 15-second (150-tick) post-match highlight clip centered around the given tick (§8.4, WP-11).
        /// </summary>
        public void PlayHighlightClip(int centerTick, string title = "KEY MOMENT REPLAY", float durationSec = 15f)
        {
            if (_sim == null || _ringBuffer.Count < 2) return;

            int halfTicks = (int)MathF.Round(durationSec * 5f);
            int startTick = Math.Clamp(centerTick - halfTicks, _ringBuffer.OldestTick, _ringBuffer.NewestTick);
            int endTick = Math.Clamp(startTick + halfTicks * 2, startTick + 1, _ringBuffer.NewestTick);

            _isReplaying = true;
            _isPostMatchClip = true;
            _replayCurrentTick = startTick;
            _replayEndTick = endTick;
            _replayAccumulatorSec = 0f;
            _replayLabel = title;
            _speedMultiplier = 1f;
            SeekEventCursorToTick(_replayCurrentTick);
        }

        public void StopHighlightClip()
        {
            if (_isReplaying)
            {
                _isReplaying = false;
                _isPostMatchClip = false;
                _replayLabel = string.Empty;
            }
        }

        public bool IssueCoachCommand(
            CommandType type,
            Vec2 targetPos = default,
            EngageStyle engageParam = EngageStyle.Balanced,
            EndgameStyle endgameParam = EndgameStyle.HighGround)
        {
            if (_sim == null || _sim.IsFinished || _isReplaying) return false;
            var cmd = new TacticalCommand(_playerSquadIndex, type, targetPos, -1, engageParam, endgameParam);
            return _sim.EnqueueCommand(cmd);
        }

        public bool OverridePlayerDropPoi(int poiId)
        {
            if (_sim == null || _sim.IsFinished || _isReplaying) return false;
            bool ok = _sim.OverridePlayerDropPoi(_playerSquadIndex, poiId);
            if (ok)
            {
                SnapshotInterpolator.Interpolate(_sim.PrevSnapshot, _sim.CurrSnapshot, 1f, _sim.Map, CurrentFrame);
            }
            return ok;
        }

        public override void _Process(double delta)
        {
            if (_sim == null) return;

            float dt = (float)delta;
            if (_isReplaying)
            {
                ProcessReplay(dt);
                return;
            }

            if (!_sim.IsFinished && _speedMultiplier > 0.001f)
            {
                _accumulatorSec += dt * _speedMultiplier;
                int maxStepsThisFrame = 8;
                while (_accumulatorSec >= TickIntervalSec && !_sim.IsFinished && maxStepsThisFrame-- > 0)
                {
                    _accumulatorSec -= TickIntervalSec;
                    _sim.Step();
                    _ringBuffer.Push(_sim.CurrSnapshot);
                    DrainAndDispatchLiveEvents();
                }

                float alpha = Math.Clamp(_accumulatorSec / TickIntervalSec, 0f, 1f);
                SnapshotInterpolator.Interpolate(_sim.PrevSnapshot, _sim.CurrSnapshot, alpha, _sim.Map, CurrentFrame);
            }
            else
            {
                float alpha = _sim.IsFinished ? 1f : Math.Clamp(_accumulatorSec / TickIntervalSec, 0f, 1f);
                SnapshotInterpolator.Interpolate(_sim.PrevSnapshot, _sim.CurrSnapshot, alpha, _sim.Map, CurrentFrame);
            }

            CheckAndEmitMatchCompletion();
        }

        private void ProcessReplay(float dt)
        {
            if (_sim == null) return;

            float effectiveSpeed = _speedMultiplier > 0.001f ? _speedMultiplier : 1f;
            _replayAccumulatorSec += dt * effectiveSpeed;

            while (_replayAccumulatorSec >= TickIntervalSec && _replayCurrentTick < _replayEndTick)
            {
                _replayAccumulatorSec -= TickIntervalSec;
                _replayCurrentTick++;
                DispatchRecordedEventsForTick(_replayCurrentTick);
            }

            int nextTick = Math.Min(_replayCurrentTick + 1, _replayEndTick);
            _ringBuffer.TryGetByTick(_replayCurrentTick, out SnapshotBuffer prevSnap);
            _ringBuffer.TryGetByTick(nextTick, out SnapshotBuffer currSnap);

            float alpha = Math.Clamp(_replayAccumulatorSec / TickIntervalSec, 0f, 1f);
            SnapshotInterpolator.Interpolate(prevSnap, currSnap, alpha, _sim.Map, CurrentFrame);

            if (_replayCurrentTick >= _replayEndTick)
            {
                _isReplaying = false;
                _replayLabel = string.Empty;
                if (_isPostMatchClip)
                {
                    _isPostMatchClip = false;
                    _speedMultiplier = 0f;
                }
            }
        }

        private void DrainAndDispatchLiveEvents()
        {
            if (_sim == null) return;
            var span = _sim.Events.DrainEvents();
            for (int i = 0; i < span.Length; i++)
            {
                SimEvent ev = span[i];
                _recordedEvents.Add(ev);
                EventDispatched?.Invoke(ev);
            }
        }

        private void SeekEventCursorToTick(int tick)
        {
            _eventPlaybackCursor = 0;
            while (_eventPlaybackCursor < _recordedEvents.Count && _recordedEvents[_eventPlaybackCursor].Tick < tick)
            {
                _eventPlaybackCursor++;
            }
        }

        private void DispatchRecordedEventsForTick(int tick)
        {
            while (_eventPlaybackCursor < _recordedEvents.Count && _recordedEvents[_eventPlaybackCursor].Tick <= tick)
            {
                SimEvent ev = _recordedEvents[_eventPlaybackCursor];
                if (ev.Tick == tick)
                {
                    EventDispatched?.Invoke(ev);
                }
                _eventPlaybackCursor++;
            }
        }

        private void CheckAndEmitMatchCompletion()
        {
            if (_sim != null && _sim.IsFinished && !_matchResultEmitted && !_isReplaying)
            {
                _matchResultEmitted = true;
                MatchCompleted?.Invoke(_sim.GetMatchResult());
            }
        }
    }
}
