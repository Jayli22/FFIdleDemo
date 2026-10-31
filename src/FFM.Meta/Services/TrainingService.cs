using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Core;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class TrainingService
    {
        public WeeklyTrainingSummary ExecuteWeeklyTraining(
            TeamDef team,
            int week,
            TeamTrainingFocus teamFocus,
            IReadOnlyDictionary<string, IndividualTrainingFocus>? individualFocuses,
            ref Rng rng)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));

            float synergyBefore = team.Synergy;
            float synergyDelta = teamFocus switch
            {
                TeamTrainingFocus.Coordination => 3.5f,
                TeamTrainingFocus.Tactics => 1.8f,
                TeamTrainingFocus.Shooting => 1.2f,
                TeamTrainingFocus.Rest => 0.5f,
                _ => 1.0f
            };
            team.Synergy = Math.Clamp(team.Synergy + synergyDelta, 0f, 100f);

            var summary = new WeeklyTrainingSummary
            {
                Week = week,
                TeamFocus = teamFocus,
                SynergyBefore = synergyBefore,
                SynergyAfter = team.Synergy
            };

            var starterLookup = new Dictionary<string, LineupStarterSlot>(StringComparer.Ordinal);
            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                var s = team.Lineup.Starters[i];
                if (!string.IsNullOrEmpty(s.PlayerId))
                {
                    starterLookup[s.PlayerId] = s;
                }
            }

            for (int i = 0; i < team.Players.Count; i++)
            {
                PlayerDef player = team.Players[i];
                IndividualTrainingFocus indFocus = IndividualTrainingFocus.AimAndRecoil;
                if (individualFocuses != null && individualFocuses.TryGetValue(player.PlayerId, out var customFocus))
                {
                    indFocus = customFocus;
                }

                var report = TrainSinglePlayer(
                    player,
                    teamFocus,
                    indFocus,
                    starterLookup.TryGetValue(player.PlayerId, out var slot) ? slot : null,
                    ref rng);

                summary.PlayerReports.Add(report);
            }

            return summary;
        }

        private static PlayerTrainingReport TrainSinglePlayer(
            PlayerDef p,
            TeamTrainingFocus teamFocus,
            IndividualTrainingFocus indFocus,
            LineupStarterSlot? starterSlot,
            ref Rng rng)
        {
            var report = new PlayerTrainingReport
            {
                PlayerId = p.PlayerId,
                PlayerName = p.Name,
                FatigueBefore = p.Condition.Fatigue
            };

            if (teamFocus == TeamTrainingFocus.Rest)
            {
                p.Condition.Fatigue = Math.Clamp(p.Condition.Fatigue - 28f, 0f, 100f);
                p.Condition.Form = Math.Clamp(p.Condition.Form + 0.6f, -3f, 3f);
                report.FatigueAfter = p.Condition.Fatigue;
                report.FormAfter = p.Condition.Form;
                return report;
            }

            // Age growth multipliers (§7.1)
            float mechAgeMult;
            float tactAgeMult;
            if (p.Age <= 19)
            {
                mechAgeMult = 1.35f;
                tactAgeMult = 1.05f;
            }
            else if (p.Age <= 22)
            {
                mechAgeMult = 1.00f;
                tactAgeMult = 1.10f;
            }
            else
            {
                mechAgeMult = 0.55f;
                tactAgeMult = 1.25f;
                // Slight natural reflex decay for veterans 23+ unless focusing CQC/Aim
                if (indFocus != IndividualTrainingFocus.AimAndRecoil && indFocus != IndividualTrainingFocus.CloseQuarters)
                {
                    float decay = -0.015f;
                    p.Mechanical.Reflex = Math.Clamp(p.Mechanical.Reflex + decay, 1f, 20f);
                    AddDelta(report.AttributeDeltas, "reflex", decay);
                }
            }

            // Determination multiplier (0.75x .. 1.25x)
            float detMult = 0.75f + Math.Clamp(p.Mental.Determination, 1f, 20f) * 0.025f;

            // Fatigue & Burnout check
            float staminaMitigation = Math.Clamp(p.Condition.Stamina, 1f, 20f) * 0.25f;
            float netFatigueGain = 6.5f - staminaMitigation;
            p.Condition.Fatigue = Math.Clamp(p.Condition.Fatigue + netFatigueGain, 0f, 100f);

            float burnoutMult = 1.0f;
            if (p.Condition.Fatigue > 75f)
            {
                report.TriggeredMinorBurnout = true;
                burnoutMult = 0.45f;
                p.Condition.Form = Math.Clamp(p.Condition.Form - 0.8f, -3f, 3f);
            }
            else
            {
                float formDrift = rng.NextFloat(-0.15f, 0.20f);
                p.Condition.Form = Math.Clamp(p.Condition.Form + formDrift, -3f, 3f);
            }

            float baseGain = 0.08f * detMult * burnoutMult * rng.NextFloat(0.9f, 1.1f);

            // Team focus contribution (40% weight)
            switch (teamFocus)
            {
                case TeamTrainingFocus.Shooting:
                    ApplyGain(p, report, "aim", baseGain * 0.45f * mechAgeMult);
                    ApplyGain(p, report, "recoil", baseGain * 0.40f * mechAgeMult);
                    break;
                case TeamTrainingFocus.Tactics:
                    ApplyGain(p, report, "positioning", baseGain * 0.45f * tactAgeMult);
                    ApplyGain(p, report, "decision", baseGain * 0.40f * tactAgeMult);
                    break;
                case TeamTrainingFocus.Coordination:
                    ApplyGain(p, report, "comms", baseGain * 0.45f * tactAgeMult);
                    ApplyGain(p, report, "teamwork", baseGain * 0.40f * tactAgeMult);
                    break;
            }

            // Individual focus contribution (60% weight)
            switch (indFocus)
            {
                case IndividualTrainingFocus.RoleMastery:
                {
                    string roleKey = starterSlot != null ? starterSlot.Role : Role.Rusher.ToString();
                    float cur = p.RoleProficiency.TryGetValue(roleKey, out float v) ? v : 10f;
                    float delta = ApplyDiminishing(cur, baseGain * 1.15f * tactAgeMult);
                    p.RoleProficiency[roleKey] = Math.Clamp(cur + delta, 1f, 20f);
                    AddDelta(report.AttributeDeltas, $"role:{roleKey}", delta);
                    break;
                }
                case IndividualTrainingFocus.SkillMastery:
                {
                    string skillKey = starterSlot != null && !string.IsNullOrEmpty(starterSlot.Active)
                        ? starterSlot.Active
                        : "sk_dash";
                    float cur = p.SkillProficiency.TryGetValue(skillKey, out float v) ? v : 10f;
                    float delta = ApplyDiminishing(cur, baseGain * 1.15f * tactAgeMult);
                    p.SkillProficiency[skillKey] = Math.Clamp(cur + delta, 1f, 20f);
                    AddDelta(report.AttributeDeltas, $"skill:{skillKey}", delta);
                    break;
                }
                case IndividualTrainingFocus.AimAndRecoil:
                    ApplyGain(p, report, "aim", baseGain * 0.65f * mechAgeMult);
                    ApplyGain(p, report, "recoil", baseGain * 0.60f * mechAgeMult);
                    break;
                case IndividualTrainingFocus.CloseQuarters:
                    ApplyGain(p, report, "cqc", baseGain * 0.65f * mechAgeMult);
                    ApplyGain(p, report, "reflex", baseGain * 0.55f * mechAgeMult);
                    ApplyGain(p, report, "movement", baseGain * 0.35f * mechAgeMult);
                    break;
                case IndividualTrainingFocus.LongRange:
                    ApplyGain(p, report, "longRange", baseGain * 0.70f * mechAgeMult);
                    ApplyGain(p, report, "aim", baseGain * 0.45f * mechAgeMult);
                    break;
                case IndividualTrainingFocus.PositioningAndAwareness:
                    ApplyGain(p, report, "positioning", baseGain * 0.65f * tactAgeMult);
                    ApplyGain(p, report, "awareness", baseGain * 0.60f * tactAgeMult);
                    break;
                case IndividualTrainingFocus.UtilityAndLooting:
                    ApplyGain(p, report, "utility", baseGain * 0.65f * tactAgeMult);
                    ApplyGain(p, report, "looting", baseGain * 0.60f * tactAgeMult);
                    break;
                case IndividualTrainingFocus.MentalAndStamina:
                    ApplyGain(p, report, "composure", baseGain * 0.55f * tactAgeMult);
                    ApplyGain(p, report, "stamina", baseGain * 0.55f);
                    break;
            }

            report.FatigueAfter = p.Condition.Fatigue;
            report.FormAfter = p.Condition.Form;
            return report;
        }

        public void AdvanceOffseasonAges(IReadOnlyList<TeamDef> teams)
        {
            for (int t = 0; t < teams.Count; t++)
            {
                var team = teams[t];
                for (int i = 0; i < team.Players.Count; i++)
                {
                    var p = team.Players[i];
                    p.Age += 1;
                    p.Condition.Fatigue = 0f;
                    p.Condition.Form = 0f;
                }
            }
        }

        private static float ApplyDiminishing(float currentValue, float rawGain, float cap = 20f)
        {
            if (currentValue >= cap) return 0f;
            float headroom = Math.Clamp((cap + 0.5f - currentValue) / 10f, 0.12f, 1.25f);
            return rawGain * headroom;
        }

        private static void ApplyGain(PlayerDef p, PlayerTrainingReport? report, string attr, float rawGain)
        {
            var prog = p.GetProgression();
            float cap = MathF.Min(prog.PotentialRating, PlayerProgressionData.GetStarAttributeCap(prog.StarTier));

            switch (attr)
            {
                case "aim":
                {
                    float d = ApplyDiminishing(p.Mechanical.Aim, rawGain, cap);
                    p.Mechanical.Aim = Math.Clamp(p.Mechanical.Aim + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "recoil":
                {
                    float d = ApplyDiminishing(p.Mechanical.Recoil, rawGain, cap);
                    p.Mechanical.Recoil = Math.Clamp(p.Mechanical.Recoil + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "reflex":
                {
                    float d = ApplyDiminishing(p.Mechanical.Reflex, rawGain, cap);
                    p.Mechanical.Reflex = Math.Clamp(p.Mechanical.Reflex + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "movement":
                {
                    float d = ApplyDiminishing(p.Mechanical.Movement, rawGain, cap);
                    p.Mechanical.Movement = Math.Clamp(p.Mechanical.Movement + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "cqc":
                {
                    float d = ApplyDiminishing(p.Mechanical.Cqc, rawGain, cap);
                    p.Mechanical.Cqc = Math.Clamp(p.Mechanical.Cqc + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "longRange":
                {
                    float d = ApplyDiminishing(p.Mechanical.LongRange, rawGain, cap);
                    p.Mechanical.LongRange = Math.Clamp(p.Mechanical.LongRange + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "awareness":
                {
                    float d = ApplyDiminishing(p.Tactical.Awareness, rawGain, cap);
                    p.Tactical.Awareness = Math.Clamp(p.Tactical.Awareness + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "positioning":
                {
                    float d = ApplyDiminishing(p.Tactical.Positioning, rawGain, cap);
                    p.Tactical.Positioning = Math.Clamp(p.Tactical.Positioning + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "utility":
                {
                    float d = ApplyDiminishing(p.Tactical.Utility, rawGain, cap);
                    p.Tactical.Utility = Math.Clamp(p.Tactical.Utility + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "looting":
                {
                    float d = ApplyDiminishing(p.Tactical.Looting, rawGain, cap);
                    p.Tactical.Looting = Math.Clamp(p.Tactical.Looting + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "decision":
                {
                    float d = ApplyDiminishing(p.Tactical.Decision, rawGain, cap);
                    p.Tactical.Decision = Math.Clamp(p.Tactical.Decision + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "comms":
                {
                    float d = ApplyDiminishing(p.Tactical.Comms, rawGain, cap);
                    p.Tactical.Comms = Math.Clamp(p.Tactical.Comms + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "composure":
                {
                    float d = ApplyDiminishing(p.Mental.Composure, rawGain, cap);
                    p.Mental.Composure = Math.Clamp(p.Mental.Composure + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "teamwork":
                {
                    float d = ApplyDiminishing(p.Mental.Teamwork, rawGain, cap);
                    p.Mental.Teamwork = Math.Clamp(p.Mental.Teamwork + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
                case "stamina":
                {
                    float d = ApplyDiminishing(p.Condition.Stamina, rawGain, cap);
                    p.Condition.Stamina = Math.Clamp(p.Condition.Stamina + d, 1f, cap);
                    if (report != null) AddDelta(report.AttributeDeltas, attr, d);
                    break;
                }
            }
        }

        private static void AddDelta(Dictionary<string, float> map, string key, float delta)
        {
            if (map.TryGetValue(key, out float prev))
            {
                map[key] = prev + delta;
            }
            else
            {
                map[key] = delta;
            }
        }

        /// <summary>
        /// Continuous hourly idle progression tick: advances per-player IdleProgress01, handles Smart Auto-Rest,
        /// awards micro-attribute growth, weapon mastery, player XP, and fills the Club Idle Vault.
        /// </summary>
        public void StepPlayerHourlyIdleTraining(
            TeamDef team,
            ClubFacilityState facilities,
            TeamTrainingFocus teamFocus,
            IReadOnlyDictionary<string, IndividualTrainingFocus>? individualFocuses,
            IdleClockState idle,
            ref Rng rng)
        {
            if (team == null) return;

            // Accumulate passive club sponsor income & XP into the Idle Vault each hour
            int sponsorCashPerHour = (int)MathF.Round(110f * facilities.SponsorIncomeMultiplier);
            idle.UnclaimedIdleCash += sponsorCashPerHour;
            idle.UnclaimedClubXp += 12 + facilities.ShootingLabLevel * 2 + facilities.TacticalLabLevel * 2;
            if (((int)idle.HourProgress) % 4 == 0)
            {
                idle.UnclaimedTacticalPoints += 2 + facilities.TacticalLabLevel;
            }

            var starterLookup = new Dictionary<string, LineupStarterSlot>(StringComparer.Ordinal);
            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                var s = team.Lineup.Starters[i];
                if (!string.IsNullOrEmpty(s.PlayerId))
                {
                    starterLookup[s.PlayerId] = s;
                }
            }

            for (int i = 0; i < team.Players.Count; i++)
            {
                PlayerDef p = team.Players[i];
                var prog = p.GetProgression();

                // Smart Auto-Rest check (65% trigger -> recover down to 20%)
                if (prog.AutoRestEnabled)
                {
                    if (!prog.IsCurrentlyResting && p.Condition.Fatigue >= 65f)
                    {
                        prog.IsCurrentlyResting = true;
                        idle.PushLog($"[{idle.DayShortName}] {p.Name} entered Smart Auto-Rest (Fatigue {p.Condition.Fatigue:F0}%).");
                    }
                    else if (prog.IsCurrentlyResting && p.Condition.Fatigue <= 20f)
                    {
                        prog.IsCurrentlyResting = false;
                        idle.PushLog($"[{idle.DayShortName}] {p.Name} recovered (Fatigue {p.Condition.Fatigue:F0}%) and resumed training.");
                    }
                }
                else
                {
                    prog.IsCurrentlyResting = false;
                }

                if (teamFocus == TeamTrainingFocus.Rest || prog.IsCurrentlyResting)
                {
                    float rec = 2.4f * facilities.FatigueRecoveryMultiplier;
                    p.Condition.Fatigue = Math.Clamp(p.Condition.Fatigue - rec, 0f, 100f);
                    p.Condition.Form = Math.Clamp(p.Condition.Form + 0.03f, -3f, 3f);
                    continue;
                }

                float detMult = 0.80f + Math.Clamp(p.Mental.Determination, 1f, 20f) * 0.02f;
                float cycleSpeed = 0.22f * detMult * ((facilities.MechanicalGainMultiplier + facilities.TacticalGainMultiplier) * 0.5f);
                prog.IdleProgress01 += cycleSpeed;

                float staminaMit = Math.Clamp(p.Condition.Stamina, 1f, 20f) * 0.012f;
                p.Condition.Fatigue = Math.Clamp(p.Condition.Fatigue + Math.Max(0.12f, 0.42f - staminaMit), 0f, 100f);

                if (prog.IdleProgress01 >= 1f)
                {
                    prog.IdleProgress01 -= 1f;

                    IndividualTrainingFocus indFocus = IndividualTrainingFocus.AimAndRecoil;
                    if (individualFocuses != null && individualFocuses.TryGetValue(p.PlayerId, out var cf))
                    {
                        indFocus = cf;
                    }

                    float microGain = 0.035f * detMult;
                    string trainedLabel = ApplyMicroTrainingCycle(
                        p,
                        prog,
                        indFocus,
                        facilities,
                        starterLookup.TryGetValue(p.PlayerId, out var slot) ? slot : null,
                        microGain);

                    // Grant Player XP and check automatic level-up
                    bool leveledUp =GrantPlayerXp(p, prog, 38);
                    if (leveledUp)
                    {
                        idle.PushLog($"★ [{idle.DayShortName}] {p.Name} reached Lv.{prog.Level}! All core attributes +0.12!");
                    }
                    else if (rng.Chance(0.30f))
                    {
                        idle.PushLog($"[{idle.DayShortName}] {p.Name} completed {trainedLabel} drill (+{microGain:F2}).");
                    }
                }
            }
        }

        private static string ApplyMicroTrainingCycle(
            PlayerDef p,
            PlayerProgressionData prog,
            IndividualTrainingFocus indFocus,
            ClubFacilityState facilities,
            LineupStarterSlot? slot,
            float microGain)
        {
            float mGain = microGain * facilities.MechanicalGainMultiplier;
            float tGain = microGain * facilities.TacticalGainMultiplier;
            float cap = MathF.Min(prog.PotentialRating, PlayerProgressionData.GetStarAttributeCap(prog.StarTier));

            // Also train weapon mastery based on focus or role (0..100 scale)
            string weapKey = indFocus switch
            {
                IndividualTrainingFocus.CloseQuarters => "SG",
                IndividualTrainingFocus.LongRange => "SR",
                IndividualTrainingFocus.AimAndRecoil => "AR",
                IndividualTrainingFocus.RoleMastery => "SMG",
                _ => "DMR"
            };
            float curWeap = prog.WeaponMastery.TryGetValue(weapKey, out float wv) ? wv : 30f;
            prog.WeaponMastery[weapKey] = Math.Clamp(curWeap + mGain * 4.5f, 1f, 100f);

            switch (indFocus)
            {
                case IndividualTrainingFocus.RoleMastery:
                {
                    string roleKey = slot != null ? slot.Role : Role.Rusher.ToString();
                    float cur = p.RoleProficiency.TryGetValue(roleKey, out float v) ? v : 10f;
                    p.RoleProficiency[roleKey] = Math.Clamp(cur + ApplyDiminishing(cur, tGain * 1.15f, cap), 1f, cap);
                    return $"Role ({roleKey})";
                }
                case IndividualTrainingFocus.SkillMastery:
                {
                    string skillKey = slot != null && !string.IsNullOrEmpty(slot.Active) ? slot.Active : "sk_dash";
                    float cur = p.SkillProficiency.TryGetValue(skillKey, out float v) ? v : 10f;
                    p.SkillProficiency[skillKey] = Math.Clamp(cur + ApplyDiminishing(cur, tGain * 1.15f, cap), 1f, cap);
                    return $"Skill ({skillKey})";
                }
                case IndividualTrainingFocus.AimAndRecoil:
                    ApplyGain(p, null, "aim", mGain * 0.70f);
                    ApplyGain(p, null, "recoil", mGain * 0.65f);
                    return "Aim & Recoil";
                case IndividualTrainingFocus.CloseQuarters:
                    ApplyGain(p, null, "cqc", mGain * 0.70f);
                    ApplyGain(p, null, "reflex", mGain * 0.60f);
                    return "CQC & Reflex";
                case IndividualTrainingFocus.LongRange:
                    ApplyGain(p, null, "longRange", mGain * 0.75f);
                    ApplyGain(p, null, "aim", mGain * 0.50f);
                    return "Long-Range & Marksman";
                case IndividualTrainingFocus.PositioningAndAwareness:
                    ApplyGain(p, null, "positioning", tGain * 0.70f);
                    ApplyGain(p, null, "awareness", tGain * 0.65f);
                    return "Positioning & Awareness";
                case IndividualTrainingFocus.UtilityAndLooting:
                    ApplyGain(p, null, "utility", tGain * 0.70f);
                    ApplyGain(p, null, "looting", tGain * 0.65f);
                    return "Gloo & Utility";
                case IndividualTrainingFocus.MentalAndStamina:
                    ApplyGain(p, null, "composure", tGain * 0.65f);
                    ApplyGain(p, null, "stamina", tGain * 0.65f);
                    return "Clutch Composure";
                default:
                    ApplyGain(p, null, "aim", mGain * 0.60f);
                    return "General Combat";
            }
        }

        public static bool GrantPlayerXp(PlayerDef p, PlayerProgressionData prog, int xpAmount)
        {
            if (prog.Level >= 30 || xpAmount <= 0) return false;
            prog.Xp += xpAmount;
            bool leveled = false;

            while (prog.Level < 30 && prog.Xp >= prog.XpToNextLevel)
            {
                prog.Xp -= prog.XpToNextLevel;
                prog.Level++;
                leveled = true;

                // Leveling up grants all-around micro-boosts up to current Star/Potential cap
                ApplyGain(p, null, "aim", 0.12f);
                ApplyGain(p, null, "recoil", 0.10f);
                ApplyGain(p, null, "reflex", 0.10f);
                ApplyGain(p, null, "cqc", 0.12f);
                ApplyGain(p, null, "longRange", 0.10f);
                ApplyGain(p, null, "awareness", 0.12f);
                ApplyGain(p, null, "positioning", 0.12f);
                ApplyGain(p, null, "utility", 0.10f);
                ApplyGain(p, null, "decision", 0.10f);
                ApplyGain(p, null, "composure", 0.10f);
            }

            return leveled;
        }

        public bool LevelUpPlayerWithClubXp(PlayerDef p, IdleClockState idle)
        {
            var prog = p.GetProgression();
            if (prog.Level >= 30) return false;
            int needed = Math.Max(1, prog.XpToNextLevel - prog.Xp);
            if (idle.ClubXpPool < needed) return false;

            idle.ClubXpPool -= needed;
            GrantPlayerXp(p, prog, needed);
            idle.PushLog($"★ Promoted {p.Name} to Lv.{prog.Level} using {needed} Club XP!");
            return true;
        }

        public bool StarLimitBreakPlayer(PlayerDef p, ref int cashBalance, IdleClockState idle)
        {
            var prog = p.GetProgression();
            if (prog.StarTier >= 5) return false;
            int reqLevel = PlayerProgressionData.GetStarBreakRequiredLevel(prog.StarTier);
            if (prog.Level < reqLevel) return false;

            int cashCost = PlayerProgressionData.GetStarBreakCashCost(prog.StarTier);
            int tpCost = PlayerProgressionData.GetStarBreakTacticalPointsCost(prog.StarTier);
            if (cashBalance < cashCost || idle.TacticalPoints < tpCost) return false;

            cashBalance -= cashCost;
            idle.TacticalPoints -= tpCost;
            prog.StarTier++;
            prog.PotentialRating = Math.Clamp(prog.PotentialRating + 0.6f, 14f, 20f);

            // Unlock a new Signature Esports Trait at 3★ and 5★ if slots available
            if ((prog.StarTier == 3 && prog.SignatureTraitIds.Count < 2) ||
                (prog.StarTier == 5 && prog.SignatureTraitIds.Count < 3))
            {
                for (int i = 0; i < SignatureTraitCatalog.All.Count; i++)
                {
                    string tid = SignatureTraitCatalog.All[i].TraitId;
                    if (!prog.SignatureTraitIds.Contains(tid))
                    {
                        prog.SignatureTraitIds.Add(tid);
                        idle.PushLog($"★★ {p.Name} limit-broke to {prog.StarTier}★ and awakened trait [{SignatureTraitCatalog.All[i].Name}]!");
                        return true;
                    }
                }
            }

            idle.PushLog($"★★ {p.Name} limit-broke to {prog.StarTier}★! Attribute cap raised to {PlayerProgressionData.GetStarAttributeCap(prog.StarTier):F1}.");
            return true;
        }

        public bool ExecuteBootcampDrill(PlayerDef p, BootcampDrillType drill, ref int cashBalance, IdleClockState idle)
        {
            const int drillCashCost = 8000;
            const int drillTpCost = 15;

            if (cashBalance < drillCashCost || idle.TacticalPoints < drillTpCost)
            {
                return false;
            }

            cashBalance -= drillCashCost;
            idle.TacticalPoints -= drillTpCost;
            var prog = p.GetProgression();

            switch (drill)
            {
                case BootcampDrillType.LiveFireReflex:
                    ApplyGain(p, null, "aim", 0.28f);
                    ApplyGain(p, null, "recoil", 0.25f);
                    ApplyGain(p, null, "cqc", 0.25f);
                    GrantPlayerXp(p, prog, 90);
                    idle.PushLog($"🎯 {p.Name} completed Live-Fire Reflex Bootcamp (+Aim/Recoil/CQC & +90 XP).");
                    break;
                case BootcampDrillType.TacticalSandbox:
                    ApplyGain(p, null, "decision", 0.28f);
                    ApplyGain(p, null, "positioning", 0.26f);
                    ApplyGain(p, null, "comms", 0.24f);
                    GrantPlayerXp(p, prog, 90);
                    idle.PushLog($"🗺 {p.Name} completed Tactical Sandbox Review (+Decision/Pos/Comms & +90 XP).");
                    break;
                case BootcampDrillType.GlooGymnastics:
                    ApplyGain(p, null, "utility", 0.32f);
                    ApplyGain(p, null, "reflex", 0.22f);
                    ApplyGain(p, null, "movement", 0.22f);
                    GrantPlayerXp(p, prog, 90);
                    idle.PushLog($"🛡 {p.Name} completed Gloo Wall Gymnastics (+Utility/Reflex/Move & +90 XP).");
                    break;
                case BootcampDrillType.SpaRecovery:
                    p.Condition.Fatigue = Math.Clamp(p.Condition.Fatigue - 45f, 0f, 100f);
                    p.Condition.Form = Math.Clamp(p.Condition.Form + 1.1f, -3f, 3f);
                    prog.IsCurrentlyResting = false;
                    idle.PushLog($"♨ {p.Name} completed Cryo & Spa Recovery (-45% Fatigue, +1.1 Form).");
                    break;
            }

            return true;
        }

        public bool UpgradeClubFacility(ClubFacilityState facilities, string facilityKey, ref int cashBalance, IdleClockState idle)
        {
            int curLevel = facilities.GetLevel(facilityKey);
            if (curLevel >= 5) return false;
            int cost = ClubFacilityState.GetUpgradeCost(curLevel);
            if (cashBalance < cost) return false;

            cashBalance -= cost;
            switch (facilityKey)
            {
                case "shooting":
                    facilities.ShootingLabLevel++;
                    idle.PushLog($"🏛 Upgraded Shooting Lab to Lv.{facilities.ShootingLabLevel} (+{(facilities.ShootingLabLevel - 1) * 8}% Mech Growth)!");
                    return true;
                case "tactical":
                    facilities.TacticalLabLevel++;
                    idle.PushLog($"🏛 Upgraded Tactical Analytics Lab to Lv.{facilities.TacticalLabLevel} (+{(facilities.TacticalLabLevel - 1) * 8}% Tact Growth)!");
                    return true;
                case "recovery":
                    facilities.RecoveryCenterLevel++;
                    idle.PushLog($"🏛 Upgraded Sports Recovery Center to Lv.{facilities.RecoveryCenterLevel} (+{(facilities.RecoveryCenterLevel - 1) * 10}% Fatigue Recovery)!");
                    return true;
                case "media":
                    facilities.MediaStudioLevel++;
                    idle.PushLog($"🏛 Upgraded Esports Media Studio to Lv.{facilities.MediaStudioLevel} (+{(facilities.MediaStudioLevel - 1) * 12}% Idle Cash)!");
                    return true;
                default:
                    return false;
            }
        }

        public void AwardPostMatchProgression(TeamDef team, MatchResult result, IdleClockState idle)
        {
            if (team == null || result == null) return;

            int placement = 12;
            int teamKills = 0;
            for (int i = 0; i < result.Placements.Count; i++)
            {
                if (result.Placements[i].SquadId == team.TeamId)
                {
                    placement = result.Placements[i].Placement;
                    teamKills = result.Placements[i].Kills;
                    break;
                }
            }

            int matchXp = 85 + Math.Max(0, (12 - placement) * 14) + teamKills * 12;
            idle.ClubXpPool += matchXp / 2;
            idle.TacticalPoints += 12 + Math.Max(0, 8 - placement) * 2;

            for (int i = 0; i < team.Players.Count; i++)
            {
                var p = team.Players[i];
                var prog = p.GetProgression();
                bool leveled = GrantPlayerXp(p, prog, matchXp);
                if (leveled)
                {
                    idle.PushLog($"🏆 Post-Match XP promoted {p.Name} to Lv.{prog.Level}!");
                }
            }
        }

        public bool CycleSignatureTrait(PlayerDef p, int slotIndex, IdleClockState idle, int tpCost = 40)
        {
            if (p == null || idle == null) return false;
            SignatureTraitCatalog.EnsureStarterTraits(p);
            if (slotIndex < 0 || slotIndex >= p.SignatureTraits.Count) return false;
            if (idle.TacticalPoints < tpCost) return false;

            var catalog = SignatureTraitCatalog.All;
            if (catalog.Count == 0) return false;

            var usedInOtherSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < p.SignatureTraits.Count; i++)
            {
                if (i != slotIndex)
                {
                    usedInOtherSlots.Add(p.SignatureTraits[i]);
                }
            }

            string currentId = p.SignatureTraits[slotIndex];
            int startIdx = -1;
            for (int i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].TraitId == currentId)
                {
                    startIdx = i;
                    break;
                }
            }

            for (int step = 1; step <= catalog.Count; step++)
            {
                var candidate = catalog[(startIdx + step + catalog.Count) % catalog.Count];
                if (!usedInOtherSlots.Contains(candidate.TraitId))
                {
                    idle.TacticalPoints -= tpCost;
                    p.SignatureTraits[slotIndex] = candidate.TraitId;
                    idle.PushLog($"🦅 {p.Name} Reforged Trait Slot #{slotIndex + 1} -> [{candidate.Name}] (-{tpCost} TP).");
                    return true;
                }
            }

            return false;
        }
    }

    public static class SignatureTraitCatalog
    {
        public static IReadOnlyList<SignatureTraitDef> All => FFM.Sim.Data.SignatureTraitCatalog.All;

        public static void EnsureStarterTraits(PlayerDef player) => FFM.Sim.Data.SignatureTraitCatalog.EnsureStarterTraits(player);

        public static (string title, string desc) GetTraitDisplay(string traitId) => FFM.Sim.Data.SignatureTraitCatalog.GetTraitDisplay(traitId);
    }
}
