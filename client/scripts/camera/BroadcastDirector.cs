using System;
using FFM.Client.Sim;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.Camera
{
    /// <summary>
    /// Automated Esports Broadcast OB Director (§6.4, WP-14).
    /// Scores active engagements, knockdowns, player squad status, and endgame circles
    /// with a 3.5-second minimum dwell hysteresis to prevent camera whip.
    /// </summary>
    public partial class BroadcastDirector : Node
    {
        public const float MinDwellSeconds = 3.5f;

        private readonly float[] _squadHeatScores = new float[SnapshotBuffer.MaxSquads];
        private float _dwellTimerSec;
        private int _currentFocusSquad;
        private bool _autoDirectorEnabled = true;
        private int _playerSquadIndex;
        private string _currentHeadline = "BROADCAST OB · DROP PHASE";

        public bool AutoDirectorEnabled
        {
            get => _autoDirectorEnabled;
            set => _autoDirectorEnabled = value;
        }

        public int CurrentFocusSquad => _currentFocusSquad;
        public string CurrentHeadline => _currentHeadline;

        public void ResetDirector(int playerSquadIndex = 0)
        {
            _playerSquadIndex = Math.Clamp(playerSquadIndex, 0, SnapshotBuffer.MaxSquads - 1);
            _currentFocusSquad = _playerSquadIndex;
            _dwellTimerSec = 0f;
            Array.Clear(_squadHeatScores, 0, _squadHeatScores.Length);
            _currentHeadline = "BROADCAST OB · READY FOR DROP";
        }

        public void OnSimEvent(SimEvent ev, MatchConfig? matchCfg)
        {
            int sq = ev.SquadIdx;
            if ((uint)sq < SnapshotBuffer.MaxSquads)
            {
                float boost = ev.Type switch
                {
                    SimEventType.EngagementStarted => 38f,
                    SimEventType.ShotFired => 4.5f,
                    SimEventType.GrenadeThrown => 16f,
                    SimEventType.Knocked => 48f,
                    SimEventType.Eliminated => 42f,
                    SimEventType.SquadWiped => 60f,
                    SimEventType.SkillTriggered => 24f,
                    SimEventType.CommandExecuted => 45f,
                    SimEventType.Booyah => 100f,
                    _ => 0f
                };

                if (sq == _playerSquadIndex)
                {
                    boost *= 1.45f;
                }
                _squadHeatScores[sq] = Math.Min(160f, _squadHeatScores[sq] + boost);
            }

            if ((uint)ev.TargetSquadIdx < SnapshotBuffer.MaxSquads && ev.TargetSquadIdx == _playerSquadIndex)
            {
                _squadHeatScores[_playerSquadIndex] = Math.Min(160f, _squadHeatScores[_playerSquadIndex] + 35f);
            }

            string actorTag = GetSquadTag(matchCfg, ev.SquadIdx);
            string targetTag = GetSquadTag(matchCfg, ev.TargetSquadIdx);

            switch (ev.Type)
            {
                case SimEventType.DropLaunched:
                    _currentHeadline = $"AIRDROP LAUNCH · {actorTag} DIVES ON SKY SURFBOARDS";
                    break;
                case SimEventType.ParachuteOpened:
                    _currentHeadline = $"CANOPY DEPLOYED · {actorTag} GLIDING TO TARGET POI";
                    break;
                case SimEventType.DropVeerOff:
                    _currentHeadline = $"MID-AIR VEER-OFF · {actorTag} DIVERTS FROM CONTESTED POI";
                    break;
                case SimEventType.Landed:
                    if (ev.SquadIdx == _playerSquadIndex)
                    {
                        _currentHeadline = $"TOUCHDOWN · {actorTag} HITS THE GROUND RUNNING";
                    }
                    break;
                case SimEventType.EngagementStarted:
                    _currentHeadline = $"CLASH ALERT · {actorTag} ENGAGES {targetTag}";
                    break;
                case SimEventType.Knocked:
                    _currentHeadline = $"KNOCKDOWN · {actorTag} DOWNS {targetTag} PLAYER";
                    break;
                case SimEventType.SquadWiped:
                    _currentHeadline = $"SQUAD WIPE · {targetTag} ELIMINATED BY {actorTag}";
                    break;
                case SimEventType.SkillTriggered:
                    _currentHeadline = $"ACTIVE SKILL · {actorTag} POPS TACTICAL ABILITY";
                    break;
                case SimEventType.CommandExecuted:
                    string status = ev.Value >= 0.5f ? "IGL COMPLIED" : "IGL OVERRODE";
                    _currentHeadline = $"COACH RADIO · {actorTag} ({status})";
                    break;
                case SimEventType.Booyah:
                    _currentHeadline = $"★ BOOYAH! {actorTag} CONQUERS BERMUDA ★";
                    break;
            }
        }

        public void UpdateDirector(InterpolatedFrameState frame, CameraRig cameraRig, float dt)
        {
            // Decay heat scores smoothly
            float decay = MathF.Exp(-0.85f * dt);
            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                _squadHeatScores[s] *= decay;
            }

            _dwellTimerSec += dt;
            if (!_autoDirectorEnabled) return;

            // Cinematic 3-stage camera framing during Parachute Drop phase
            if (frame.Phase == Phase.Drop && (uint)_playerSquadIndex < SnapshotBuffer.MaxSquads)
            {
                int leadUnitIdx = _playerSquadIndex * 4;
                if ((uint)leadUnitIdx < SnapshotBuffer.MaxUnits)
                {
                    UnitDropState dropState = frame.Units[leadUnitIdx].DropState;
                    if (dropState == UnitDropState.InPlane)
                    {
                        _currentFocusSquad = _playerSquadIndex;
                        cameraRig.FollowSquad(_playerSquadIndex, 310f);
                        return;
                    }
                    if (dropState == UnitDropState.Freefall || dropState == UnitDropState.Gliding)
                    {
                        _currentFocusSquad = _playerSquadIndex;
                        cameraRig.FollowSquad(_playerSquadIndex, 210f);
                        return;
                    }
                }
            }

            // Evaluate best squad to follow
            int bestSquad = _currentFocusSquad;
            float bestScore = -1f;

            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                SquadSnapshot sq = frame.Squads[s];
                if (sq.AliveCount <= 0) continue;

                float score = _squadHeatScores[s];
                if (s == _playerSquadIndex)
                {
                    score += 36f; // Strong baseline preference for player's squad
                }
                if (sq.CurrentMode == SquadMode.Contest || sq.CurrentMode == SquadMode.Push)
                {
                    score += 28f;
                }
                if (frame.AliveSquads <= 4)
                {
                    score += 25f;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    bestSquad = s;
                }
            }

            bool currentWiped = (uint)_currentFocusSquad < SnapshotBuffer.MaxSquads &&
                                frame.Squads[_currentFocusSquad].AliveCount <= 0;

            if (currentWiped || (bestSquad != _currentFocusSquad && _dwellTimerSec >= MinDwellSeconds && bestScore > _squadHeatScores[_currentFocusSquad] + 18f))
            {
                _currentFocusSquad = bestSquad;
                _dwellTimerSec = 0f;
                float camDist = frame.AliveSquads <= 3 ? 190f : 235f;
                cameraRig.FollowSquad(_currentFocusSquad, camDist);
            }
            else if (cameraRig.Mode == CameraViewMode.TopDownMacro && frame.TimeSec >= 8.0f)
            {
                cameraRig.FollowSquad(_currentFocusSquad, 235f);
            }
        }

        private static string GetSquadTag(MatchConfig? cfg, int squadIdx)
        {
            if (cfg != null && (uint)squadIdx < cfg.Squads.Count)
            {
                return FFM.Client.View.BattlefieldView.GetShortTag(cfg.Squads[squadIdx].Name, squadIdx);
            }
            return squadIdx >= 0 ? $"SQ{squadIdx + 1:00}" : "FIELD";
        }
    }
}
