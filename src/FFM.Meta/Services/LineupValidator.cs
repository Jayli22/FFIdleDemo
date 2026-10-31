using System;
using System.Collections.Generic;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class LineupValidator
    {
        public List<string> ValidateLineup(TeamDef team, ConfigBundle? bundle = null)
        {
            var errors = new List<string>();
            if (team == null)
            {
                errors.Add("Team definition is null.");
                return errors;
            }

            if (team.Lineup.Starters.Count != 4)
            {
                errors.Add($"Lineup must have exactly 4 starters (found {team.Lineup.Starters.Count}).");
            }

            var seenPlayers = new HashSet<string>(StringComparer.Ordinal);
            var seenActives = new HashSet<string>(StringComparer.Ordinal);
            bool iglInStarters = false;

            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                var slot = team.Lineup.Starters[i];
                if (string.IsNullOrWhiteSpace(slot.PlayerId))
                {
                    errors.Add($"Starter slot #{i + 1} has an empty playerId.");
                    continue;
                }

                if (!seenPlayers.Add(slot.PlayerId))
                {
                    errors.Add($"Duplicate starter playerId '{slot.PlayerId}'.");
                }

                if (team.FindPlayer(slot.PlayerId) == null)
                {
                    errors.Add($"Starter '{slot.PlayerId}' does not belong to team '{team.TeamId}'.");
                }

                if (slot.PlayerId == team.Lineup.Igl)
                {
                    iglInStarters = true;
                }

                if (!Enum.TryParse<Role>(slot.Role, true, out _))
                {
                    errors.Add($"Starter '{slot.PlayerId}' has invalid role '{slot.Role}'.");
                }

                if (string.IsNullOrWhiteSpace(slot.Active))
                {
                    errors.Add($"Starter '{slot.PlayerId}' must equip 1 active skill.");
                }
                else
                {
                    if (!seenActives.Add(slot.Active))
                    {
                        errors.Add($"Duplicate active skill '{slot.Active}' across squad starters (1Active uniqueness required).");
                    }
                    if (bundle != null && bundle.Skills.FindSkill(slot.Active) == null)
                    {
                        errors.Add($"Unknown active skill '{slot.Active}' on '{slot.PlayerId}'.");
                    }
                }

                if (slot.Passives.Count != 3)
                {
                    errors.Add($"Starter '{slot.PlayerId}' must equip exactly 3 passive skills (found {slot.Passives.Count}).");
                }
                else
                {
                    var seenPassives = new HashSet<string>(StringComparer.Ordinal);
                    for (int p = 0; p < slot.Passives.Count; p++)
                    {
                        string pid = slot.Passives[p];
                        if (!seenPassives.Add(pid))
                        {
                            errors.Add($"Starter '{slot.PlayerId}' has duplicate passive skill '{pid}'.");
                        }
                        if (bundle != null && bundle.Skills.FindSkill(pid) == null)
                        {
                            errors.Add($"Unknown passive skill '{pid}' on '{slot.PlayerId}'.");
                        }
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(team.Lineup.Igl) || !iglInStarters)
            {
                errors.Add($"IGL '{team.Lineup.Igl}' must be one of the 4 active starters.");
            }

            if (!string.IsNullOrWhiteSpace(team.Lineup.Sub))
            {
                if (seenPlayers.Contains(team.Lineup.Sub!))
                {
                    errors.Add($"Substitute '{team.Lineup.Sub}' cannot simultaneously be a starter.");
                }
                if (team.FindPlayer(team.Lineup.Sub!) == null)
                {
                    errors.Add($"Substitute '{team.Lineup.Sub}' does not belong to team '{team.TeamId}'.");
                }
            }

            return errors;
        }

        public bool SubstituteStarter(TeamDef team, string starterOutId, string subInId)
        {
            if (team == null) throw new ArgumentNullException(nameof(team));
            if (team.FindPlayer(subInId) == null) return false;

            int starterIdx = -1;
            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                if (team.Lineup.Starters[i].PlayerId == starterOutId)
                {
                    starterIdx = i;
                    break;
                }
            }
            if (starterIdx < 0) return false;

            // Ensure subInId is not already a starter
            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                if (team.Lineup.Starters[i].PlayerId == subInId) return false;
            }

            team.Lineup.Starters[starterIdx].PlayerId = subInId;
            team.Lineup.Sub = starterOutId;

            if (team.Lineup.Igl == starterOutId)
            {
                team.Lineup.Igl = subInId;
            }

            // Slight synergy adjustment when rotating lineup
            team.Synergy = Math.Clamp(team.Synergy - 2.0f, 0f, 100f);
            return true;
        }
    }
}
