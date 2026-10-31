using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Sim.Data;

namespace FFM.Meta.Services
{
    public sealed class SeasonCalendarService
    {
        public static readonly int[] PointRushHeadStartTable =
        {
            10, 7, 5, 4, 3, 2, 1, 1, 0, 0, 0, 0
        };

        public SeasonStage GetStageForWeek(int week)
        {
            if (week <= 8) return SeasonStage.RegularSeason;
            if (week <= 10) return SeasonStage.PointRush;
            if (week == 11) return SeasonStage.GrandFinals;
            return SeasonStage.Offseason;
        }

        public List<LeagueStandingEntry> InitializeRegularSeasonStandings(IReadOnlyList<TeamDef> teams)
        {
            if (teams == null) throw new ArgumentNullException(nameof(teams));

            var standings = new List<LeagueStandingEntry>(teams.Count);
            for (int i = 0; i < teams.Count; i++)
            {
                standings.Add(new LeagueStandingEntry
                {
                    TeamId = teams[i].TeamId,
                    TeamName = teams[i].Name,
                    GroupIndex = i % 3, // 0=A, 1=B, 2=C (6 teams each for 18 teams)
                    LastMatchPlacement = 12
                });
            }
            SortStandings(standings);
            return standings;
        }

        /// <summary>
        /// Returns the 12 participating team IDs for a given week.
        /// Weeks 1..8: Round-robin pairing of two 6-team groups (AB, BC, AC).
        /// Weeks 9..11: Top 12 qualified teams in the active stage standings.
        /// </summary>
        public List<string> GetMatchDayTeamIds(int week, IReadOnlyList<LeagueStandingEntry> currentStandings)
        {
            if (currentStandings == null) throw new ArgumentNullException(nameof(currentStandings));

            SeasonStage stage = GetStageForWeek(week);
            var result = new List<string>(12);

            if (stage == SeasonStage.RegularSeason)
            {
                int pairMode = (week - 1) % 3;
                int groupA = pairMode switch { 0 => 0, 1 => 1, _ => 0 };
                int groupB = pairMode switch { 0 => 1, 1 => 2, _ => 2 };

                for (int i = 0; i < currentStandings.Count; i++)
                {
                    var entry = currentStandings[i];
                    if (entry.GroupIndex == groupA || entry.GroupIndex == groupB)
                    {
                        result.Add(entry.TeamId);
                    }
                }
            }
            else if (stage == SeasonStage.PointRush || stage == SeasonStage.GrandFinals)
            {
                int count = Math.Min(12, currentStandings.Count);
                for (int i = 0; i < count; i++)
                {
                    result.Add(currentStandings[i].TeamId);
                }
            }

            return result;
        }

        /// <summary>
        /// Transitions from Regular Season (after Week 8) to Point Rush (Week 9), keeping Top 12 teams.
        /// </summary>
        public List<LeagueStandingEntry> TransitionToPointRush(IReadOnlyList<LeagueStandingEntry> regularSeasonStandings)
        {
            var sorted = new List<LeagueStandingEntry>(regularSeasonStandings);
            SortStandings(sorted);

            int qualifyCount = Math.Min(12, sorted.Count);
            var pointRush = new List<LeagueStandingEntry>(qualifyCount);
            for (int i = 0; i < qualifyCount; i++)
            {
                var src = sorted[i];
                pointRush.Add(new LeagueStandingEntry
                {
                    TeamId = src.TeamId,
                    TeamName = src.TeamName,
                    GroupIndex = src.GroupIndex,
                    LastMatchPlacement = 12
                });
            }
            return pointRush;
        }

        /// <summary>
        /// Transitions from Point Rush (after Week 10) to Grand Finals (Week 11), awarding Head-Start Points.
        /// </summary>
        public List<LeagueStandingEntry> TransitionToGrandFinals(IReadOnlyList<LeagueStandingEntry> pointRushStandings)
        {
            var sorted = new List<LeagueStandingEntry>(pointRushStandings);
            SortStandings(sorted);

            int count = Math.Min(12, sorted.Count);
            var finals = new List<LeagueStandingEntry>(count);
            for (int i = 0; i < count; i++)
            {
                var src = sorted[i];
                int headStart = i < PointRushHeadStartTable.Length ? PointRushHeadStartTable[i] : 0;
                finals.Add(new LeagueStandingEntry
                {
                    TeamId = src.TeamId,
                    TeamName = src.TeamName,
                    GroupIndex = src.GroupIndex,
                    HeadStartPoints = headStart,
                    LastMatchPlacement = i + 1
                });
            }
            SortStandings(finals);
            return finals;
        }

        /// <summary>
        /// Applies a single MatchResult to the active standings.
        /// If in Grand Finals, enforces the FFWS Champion Rush 80pt threshold & 8-match cap rule.
        /// Returns the Champion TeamId if a champion was crowned on this match; otherwise null.
        /// </summary>
        public string? ApplyMatchResult(
            List<LeagueStandingEntry> standings,
            MatchResult matchResult,
            bool isGrandFinals,
            int matchNumberInFinals = 1,
            int championRushThreshold = 80,
            int maxFinalsMatches = 8)
        {
            if (standings == null) throw new ArgumentNullException(nameof(standings));
            if (matchResult == null) throw new ArgumentNullException(nameof(matchResult));

            var byId = new Dictionary<string, LeagueStandingEntry>(StringComparer.Ordinal);
            for (int i = 0; i < standings.Count; i++)
            {
                byId[standings[i].TeamId] = standings[i];
            }

            string? championId = null;

            for (int i = 0; i < matchResult.Placements.Count; i++)
            {
                var p = matchResult.Placements[i];
                if (!byId.TryGetValue(p.SquadId, out var entry)) continue;

                // Check if team was ALREADY MatchPointEligible entering this match and won Booyah
                bool wasEligibleBeforeMatch = entry.MatchPointEligible;

                entry.MatchesPlayed += 1;
                entry.PlacementPoints += p.PlacementPoints;
                entry.KillPoints += p.Kills;
                entry.TotalKills += p.Kills;
                entry.LastMatchPlacement = p.Placement;
                if (p.Booyah || p.Placement == 1)
                {
                    entry.Booyahs += 1;
                }

                if (isGrandFinals)
                {
                    if (wasEligibleBeforeMatch && (p.Booyah || p.Placement == 1))
                    {
                        entry.WonChampionRush = true;
                        championId = entry.TeamId;
                    }
                    else if (entry.TotalPoints >= championRushThreshold)
                    {
                        // Becomes eligible starting NEXT match (§3.7 / §7.2)
                        entry.MatchPointEligible = true;
                    }
                }
            }

            SortStandings(standings);

            if (isGrandFinals && championId == null && matchNumberInFinals >= maxFinalsMatches && standings.Count > 0)
            {
                // Match 8 hard cap reached: #1 on the leaderboard after tiebreakers wins Championship
                standings[0].WonChampionRush = true;
                championId = standings[0].TeamId;
            }

            return championId;
        }

        /// <summary>
        /// 4-level FFWS Tiebreaker (§7.2):
        /// 1. WonChampionRush (Grand Finals winner always #1)
        /// 2. TotalPoints descending
        /// 3. Booyahs descending
        /// 4. TotalKills descending
        /// 5. LastMatchPlacement ascending
        /// </summary>
        public void SortStandings(List<LeagueStandingEntry> standings)
        {
            standings.Sort(CompareEntries);
        }

        public static int CompareEntries(LeagueStandingEntry a, LeagueStandingEntry b)
        {
            if (a.WonChampionRush != b.WonChampionRush)
                return a.WonChampionRush ? -1 : 1;

            int cmpPoints = b.TotalPoints.CompareTo(a.TotalPoints);
            if (cmpPoints != 0) return cmpPoints;

            int cmpBooyahs = b.Booyahs.CompareTo(a.Booyahs);
            if (cmpBooyahs != 0) return cmpBooyahs;

            int cmpKills = b.TotalKills.CompareTo(a.TotalKills);
            if (cmpKills != 0) return cmpKills;

            int cmpLast = a.LastMatchPlacement.CompareTo(b.LastMatchPlacement);
            if (cmpLast != 0) return cmpLast;

            return string.CompareOrdinal(a.TeamId, b.TeamId);
        }
    }
}
