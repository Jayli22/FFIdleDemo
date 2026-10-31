using System;
using System.Collections.Generic;

namespace FFM.Sim.Data
{
    public static class ConfigValidator
    {
        public static List<string> Validate(ConfigBundle bundle)
        {
            var errors = new List<string>();

            if (bundle.Attributes.ScaleMin < 1f || bundle.Attributes.ScaleMax > 20f || bundle.Attributes.ScaleMin >= bundle.Attributes.ScaleMax)
            {
                errors.Add("Attributes scale must be within [1, 20].");
            }
            if (bundle.Attributes.Attributes.Count < 16)
            {
                errors.Add($"Expected at least 16 attribute definitions, found {bundle.Attributes.Attributes.Count}.");
            }

            if (bundle.Roles.Roles.Count != 5)
            {
                errors.Add($"Expected 5 roles (Rusher, SecondRusher, Sniper, Support, Grenadier), found {bundle.Roles.Roles.Count}.");
            }

            if (bundle.Weapons.Weapons.Count < 5)
            {
                errors.Add("Expected at least 5 weapon definitions.");
            }
            foreach (var w in bundle.Weapons.Weapons)
            {
                if (string.IsNullOrWhiteSpace(w.WeaponId)) errors.Add("WeaponId cannot be empty.");
                if (w.Damage <= 0f || w.RangeMeters <= 0f) errors.Add($"Weapon {w.WeaponId} has invalid damage or range.");
                if (w.AccClose <= 0f || w.AccClose > 1f || w.AccMid <= 0f || w.AccMid > 1f || w.AccFar <= 0f || w.AccFar > 1f)
                {
                    errors.Add($"Weapon {w.WeaponId} accuracy curve values must be in (0, 1].");
                }
            }

            if (bundle.Skills.Skills.Count < 5)
            {
                errors.Add("Expected at least 5 active skills.");
            }

            if (bundle.Scoring.PlacementPoints.Count != 12)
            {
                errors.Add("Scoring.PlacementPoints must have exactly 12 entries for 12 squads.");
            }
            if (bundle.Scoring.ChampionRushThreshold <= 0)
            {
                errors.Add("Scoring.ChampionRushThreshold must be positive.");
            }

            if (bundle.SampleTeams.Count < 12)
            {
                errors.Add($"Expected at least 12 sample teams, found {bundle.SampleTeams.Count}.");
            }

            for (int i = 0; i < bundle.SampleTeams.Count; i++)
            {
                ValidateTeamLineup(bundle.SampleTeams[i], bundle.Skills, errors);
            }

            return errors;
        }

        public static void ValidateTeamLineup(TeamDef team, SkillsConfig? skillsCfg, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(team.TeamId))
            {
                errors.Add("TeamId cannot be empty.");
                return;
            }
            if (team.Lineup.Starters.Count != 4)
            {
                errors.Add($"Team {team.TeamId}: Lineup must have exactly 4 starters (found {team.Lineup.Starters.Count}).");
                return;
            }

            var usedPlayerIds = new HashSet<string>();
            var usedActives = new HashSet<string>();
            int domesticCount = 0;
            bool iglInStarters = false;

            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                var slot = team.Lineup.Starters[i];
                if (!usedPlayerIds.Add(slot.PlayerId))
                {
                    errors.Add($"Team {team.TeamId}: Duplicate player {slot.PlayerId} in starters.");
                }

                var player = team.FindPlayer(slot.PlayerId);
                if (player == null)
                {
                    errors.Add($"Team {team.TeamId}: Starter player {slot.PlayerId} not found in team roster.");
                }
                else
                {
                    if (string.Equals(player.Region, team.Region, StringComparison.OrdinalIgnoreCase))
                    {
                        domesticCount++;
                    }
                    ValidatePlayerAttributes(player, errors);
                }

                if (string.IsNullOrWhiteSpace(slot.Active))
                {
                    errors.Add($"Team {team.TeamId}: Starter {slot.PlayerId} missing active skill.");
                }
                else if (!usedActives.Add(slot.Active))
                {
                    errors.Add($"Team {team.TeamId}: Duplicate active skill '{slot.Active}' within squad (§4.3).");
                }

                if (skillsCfg != null && !string.IsNullOrWhiteSpace(slot.Active))
                {
                    var sk = skillsCfg.FindSkill(slot.Active);
                    if (sk == null || !sk.IsActive)
                    {
                        errors.Add($"Team {team.TeamId}: Invalid active skill '{slot.Active}'.");
                    }
                }

                if (slot.Passives.Count > 3)
                {
                    errors.Add($"Team {team.TeamId}: Starter {slot.PlayerId} has {slot.Passives.Count} passives (max 3 allowed).");
                }

                var uniquePassives = new HashSet<string>();
                for (int p = 0; p < slot.Passives.Count; p++)
                {
                    if (!uniquePassives.Add(slot.Passives[p]))
                    {
                        errors.Add($"Team {team.TeamId}: Starter {slot.PlayerId} has duplicate passive '{slot.Passives[p]}'.");
                    }
                }

                if (slot.PlayerId == team.Lineup.Igl)
                {
                    iglInStarters = true;
                }
            }

            if (domesticCount < 3)
            {
                errors.Add($"Team {team.TeamId}: At least 3 starters must belong to region '{team.Region}' (found {domesticCount}).");
            }

            if (!iglInStarters)
            {
                errors.Add($"Team {team.TeamId}: IGL '{team.Lineup.Igl}' must be one of the 4 starters.");
            }

            if (!string.IsNullOrEmpty(team.Lineup.Sub))
            {
                if (usedPlayerIds.Contains(team.Lineup.Sub))
                {
                    errors.Add($"Team {team.TeamId}: Sub '{team.Lineup.Sub}' cannot also be a starter.");
                }
                if (team.FindPlayer(team.Lineup.Sub) == null)
                {
                    errors.Add($"Team {team.TeamId}: Sub '{team.Lineup.Sub}' not found in roster.");
                }
            }
        }

        public static void ValidatePlayerAttributes(PlayerDef p, List<string> errors)
        {
            void CheckRange(string name, float val, float min = 1f, float max = 20f)
            {
                if (val < min || val > max)
                {
                    errors.Add($"Player {p.PlayerId} attribute {name}={val} out of range [{min}, {max}].");
                }
            }

            CheckRange("aim", p.Mechanical.Aim);
            CheckRange("recoil", p.Mechanical.Recoil);
            CheckRange("reflex", p.Mechanical.Reflex);
            CheckRange("movement", p.Mechanical.Movement);
            CheckRange("cqc", p.Mechanical.Cqc);
            CheckRange("longRange", p.Mechanical.LongRange);

            CheckRange("awareness", p.Tactical.Awareness);
            CheckRange("positioning", p.Tactical.Positioning);
            CheckRange("utility", p.Tactical.Utility);
            CheckRange("looting", p.Tactical.Looting);
            CheckRange("decision", p.Tactical.Decision);
            CheckRange("comms", p.Tactical.Comms);

            CheckRange("composure", p.Mental.Composure);
            CheckRange("teamwork", p.Mental.Teamwork);
            CheckRange("determination", p.Mental.Determination);

            CheckRange("consistency", p.Hidden.Consistency);
            CheckRange("bigMatch", p.Hidden.BigMatch);
            CheckRange("injuryProne", p.Hidden.InjuryProne);

            CheckRange("stamina", p.Condition.Stamina);
            CheckRange("form", p.Condition.Form, -3f, 3f);
            CheckRange("fatigue", p.Condition.Fatigue, 0f, 100f);
        }

        public static void ValidateOrThrow(ConfigBundle bundle)
        {
            List<string> errors = Validate(bundle);
            if (errors.Count > 0)
            {
                throw new InvalidOperationException("Configuration validation failed:\n - " + string.Join("\n - ", errors));
            }
        }
    }
}
