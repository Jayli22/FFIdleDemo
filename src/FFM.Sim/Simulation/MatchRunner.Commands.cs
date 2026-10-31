using System;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Entities;

namespace FFM.Sim.Simulation
{
    public sealed partial class MatchRunner
    {
        /// <summary>
        /// Enqueues a Coach Tactical Command for the specified squad (WP-09).
        /// Enforces max 5 commands per match and 60s cooldown.
        /// Computes CMD-01 response delay ticks.
        /// </summary>
        public bool EnqueueCommand(TacticalCommand cmd)
        {
            if (_currentPhase == Phase.Finished) return false;
            if ((uint)cmd.SquadIndex >= SquadCount) return false;

            ref SquadRuntime sq = ref _squads[cmd.SquadIndex];
            if (sq.AliveCount <= 0) return false;
            if (sq.CommandsRemaining <= 0 || sq.CommandCooldownTicks > 0 || sq.HasPendingCommand)
            {
                return false;
            }

            var cCfg = _cfg.Commands;
            float iglDecision = GetActiveIglDecision(ref sq);
            float avgComms = GetSquadAverageComms(ref sq);

            // CMD-01: DelayTicks = clamp(round(BaseDelayTicks + (20 - iglDecision)*2.8 + (20 - avgComms)*2.4 - (Synergy/100)*20), 5, 80)
            float rawDelay = cCfg.BaseDelayTicks
                           + (20f - iglDecision) * cCfg.DecisionDelayFactor
                           + (20f - avgComms) * cCfg.CommsDelayFactor
                           - sq.Synergy * cCfg.SynergyDelayFactor * 0.35f;
            int delayTicks = Math.Clamp((int)MathF.Round(rawDelay), 5, 80);

            sq.CommandsRemaining--;
            sq.CommandCooldownTicks = (int)MathF.Round(cCfg.CooldownSec * 10f);
            sq.HasPendingCommand = true;
            sq.PendingCommand = cmd;
            sq.PendingCommandExecuteTick = _currentTick + delayTicks;

            _currSnap.Squads[cmd.SquadIndex].CommandsRemaining = sq.CommandsRemaining;
            _currSnap.Squads[cmd.SquadIndex].CommandCooldownTicks = sq.CommandCooldownTicks;
            _currSnap.Squads[cmd.SquadIndex].CommandCooldownSec = sq.CommandCooldownTicks * 0.1f;

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.CommandIssued,
                squadIdx: sq.SquadId,
                pos: cmd.TargetPosition,
                extraCode: (int)cmd.Type));

            return true;
        }

        /// <summary>
        /// Overload for issuing a Command to the player-controlled squad (`MatchConfig.PlayerSquadId`).
        /// </summary>
        public bool IssuePlayerCommand(Command cmd, Vec2 targetPos = default)
        {
            int playerSqIdx = 0;
            for (int s = 0; s < SquadCount; s++)
            {
                if (_matchCfg.Squads[s].SquadId == _matchCfg.PlayerSquadId)
                {
                    playerSqIdx = s;
                    break;
                }
            }
            return EnqueueCommand(new TacticalCommand(
                playerSqIdx,
                cmd.Type,
                targetPos,
                engageParam: cmd.EngageParam,
                endgameParam: cmd.EndgameParam));
        }

        private void StepCoachCommands()
        {
            var cCfg = _cfg.Commands;

            for (int s = 0; s < SquadCount; s++)
            {
                ref SquadRuntime sq = ref _squads[s];
                if (sq.CommandCooldownTicks > 0)
                {
                    sq.CommandCooldownTicks--;
                }

                if (sq.HasActiveCommand && _currentTick >= sq.ActiveCommandExpireTick)
                {
                    sq.HasActiveCommand = false;
                    sq.ActiveCommandCompliant = false;
                }

                if (sq.HasPendingCommand && _currentTick >= sq.PendingCommandExecuteTick)
                {
                    sq.HasPendingCommand = false;
                    ExecutePendingCoachCommand(ref sq, cCfg);
                }
            }
        }

        /// <summary>
        /// Formula CMD-02: Compliance probability governed by IGL Decision, Squad Comms, and Squad Synergy.
        /// </summary>
        private void ExecutePendingCoachCommand(ref SquadRuntime sq, CommandsBalanceConfig cCfg)
        {
            if (sq.AliveCount <= 0) return;

            float iglDecision = GetActiveIglDecision(ref sq);
            float avgComms = GetSquadAverageComms(ref sq);

            // CMD-02: P_comply = clamp(0.25 + iglDecision*0.018 + avgComms*0.014 + Synergy*0.0022, 0.35, 0.95)
            float pComply = Math.Clamp(
                cCfg.BaseComplianceProb
                + iglDecision * cCfg.DecisionComplianceScale
                + avgComms * cCfg.CommsComplianceScale
                + sq.Synergy * cCfg.SynergyComplianceScale,
                0.35f,
                0.95f);

            bool complied = _rng.Chance(pComply);
            TacticalCommand cmd = sq.PendingCommand;

            sq.HasActiveCommand = true;
            sq.ActiveCommand = cmd;
            sq.ActiveCommandCompliant = complied;
            sq.ActiveCommandExpireTick = _currentTick + 400; // Active for 40s

            if (complied)
            {
                switch (cmd.Type)
                {
                    case CommandType.RotateNow:
                        Vec2 target = cmd.TargetPosition.LengthSquared > 1f
                            ? cmd.TargetPosition
                            : EvaluateBestTacticalGoal(ref sq, GetSquadCentroid(sq.SquadId), Math.Min(20f, iglDecision + 4f));
                        SetSquadMacroGoalAndPath(ref sq, target);
                        break;

                    case CommandType.HoldPosition:
                        Vec2 holdPos = cmd.TargetPosition.LengthSquared > 1f ? cmd.TargetPosition : GetSquadCentroid(sq.SquadId);
                        SetSquadMacroGoalAndPath(ref sq, holdPos);
                        break;

                    case CommandType.ContestTarget:
                        sq.AggressionMod = 1.45f;
                        if (cmd.TargetPosition.LengthSquared > 1f)
                        {
                            SetSquadMacroGoalAndPath(ref sq, cmd.TargetPosition);
                        }
                        break;

                    case CommandType.AvoidFight:
                    case CommandType.SecurePlacement:
                        sq.AggressionMod = 0.55f;
                        sq.CoverBias = 1.4f;
                        break;

                    case CommandType.SetEngage:
                        sq.AggressionMod = cmd.EngageParam == EngageStyle.Contest ? 1.4f : (cmd.EngageParam == EngageStyle.Avoid ? 0.6f : 1.0f);
                        break;

                    case CommandType.EndgameFormation:
                        sq.HighGroundBias = cmd.EndgameParam == EndgameStyle.HighGround ? 1.7f : 1.0f;
                        sq.CoverBias = cmd.EndgameParam == EndgameStyle.Building ? 1.6f : 1.2f;
                        sq.IsGlooFortEndgame = cmd.EndgameParam == EndgameStyle.GlooFort;
                        break;

                    case CommandType.PopSkill:
                        for (int m = 0; m < UnitsPerSquad; m++)
                        {
                            ref UnitRuntime u = ref _units[sq.FirstUnitId + m];
                            if (u.State == UnitState.Alive && u.ActiveSkillCooldownTicks <= 0 && u.ActiveSkillIndex >= 0)
                            {
                                SkillDef sk = _cfg.Skills.Skills[u.ActiveSkillIndex];
                                u.ActiveSkillCooldownTicks = (int)MathF.Round(sk.CooldownSec * 8f);
                                u.ActiveSkillDurationTicks = (int)MathF.Round(sk.DurationSec * 10f);
                                u.SkillsActivated++;
                                _events.Record(new SimEvent(_currentTick, SimEventType.SkillTriggered, actorUnitIdx: u.UnitId, squadIdx: sq.SquadId, pos: u.Position, extraCode: u.ActiveSkillIndex));
                            }
                        }
                        break;
                }
            }

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.CommandExecuted,
                squadIdx: sq.SquadId,
                pos: cmd.TargetPosition,
                value: complied ? 1f : 0f,
                extraCode: (int)cmd.Type));
        }
    }
}
