using System;
using System.Collections.Generic;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;

namespace FFM.Sim.Simulation
{
    public sealed class MatchDayStandingRow
    {
        public string SquadId { get; set; } = string.Empty;
        public string SquadName { get; set; } = string.Empty;
        public int MatchesPlayed { get; set; }
        public int HeadStartPoints { get; set; }
        public int PlacementPoints { get; set; }
        public int KillPoints { get; set; }
        public int TotalPoints => HeadStartPoints + PlacementPoints + KillPoints;
        public int Booyahs { get; set; }
        public int LastMatchPlacement { get; set; } = 12;
        public bool ChampionRushEligible { get; set; }
    }

    public sealed class MatchDayResult
    {
        public List<MatchResult> Matches { get; set; } = new();
        public List<MatchDayStandingRow> Standings { get; set; } = new();
        public string? CrownedChampionSquadId { get; set; }
        public Dictionary<string, float> FinalFatigue { get; set; } = new();
    }

    public static class MatchDayRunner
    {
        public static MatchDayResult RunMatchDay(
            ConfigBundle bundle,
            MapData map,
            List<SquadDef> squads,
            ulong baseSeed,
            int maxMatches = 6,
            bool isChampionRushFinals = false,
            int championRushThreshold = 80,
            Dictionary<string, int>? headStartPoints = null,
            Dictionary<string, float>? initialFatigue = null)
        {
            var result = new MatchDayResult();
            var standingsMap = new Dictionary<string, MatchDayStandingRow>(StringComparer.Ordinal);
            var eligibleSet = new HashSet<string>(StringComparer.Ordinal);
            var fatigueCarry = new Dictionary<string, float>(StringComparer.Ordinal);

            if (initialFatigue != null)
            {
                foreach (var kv in initialFatigue)
                {
                    fatigueCarry[kv.Key] = kv.Value;
                }
            }

            for (int i = 0; i < squads.Count; i++)
            {
                var sq = squads[i];
                int hs = headStartPoints != null && headStartPoints.TryGetValue(sq.SquadId, out int h) ? h : 0;
                bool elig = isChampionRushFinals && hs >= championRushThreshold;
                if (elig) eligibleSet.Add(sq.SquadId);

                standingsMap[sq.SquadId] = new MatchDayStandingRow
                {
                    SquadId = sq.SquadId,
                    SquadName = sq.Name,
                    HeadStartPoints = hs,
                    ChampionRushEligible = elig
                };

                for (int m = 0; m < sq.Members.Count; m++)
                {
                    var p = sq.Members[m].Player;
                    if (!fatigueCarry.ContainsKey(p.PlayerId))
                    {
                        fatigueCarry[p.PlayerId] = p.Condition.Fatigue;
                    }
                }
            }

            var seedRng = new Rng(baseSeed == 0 ? 1UL : baseSeed);

            for (int matchIdx = 1; matchIdx <= maxMatches; matchIdx++)
            {
                ulong matchSeed = seedRng.NextUInt64();
                var currentPoints = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var kv in standingsMap)
                {
                    currentPoints[kv.Key] = kv.Value.TotalPoints;
                }

                var cfg = new MatchConfig
                {
                    Seed = matchSeed,
                    MapId = "bermuda_lite",
                    TickHz = 10,
                    Squads = squads,
                    PlayerSquadId = squads.Count > 0 ? squads[0].SquadId : string.Empty,
                    FatigueIn = new Dictionary<string, float>(fatigueCarry),
                    Context = new MatchContextDef
                    {
                        MatchIndexOfDay = matchIdx,
                        IsFinals = isChampionRushFinals,
                        Standings = currentPoints,
                        ChampionRush = new ChampionRushContextDef
                        {
                            Enabled = isChampionRushFinals,
                            Threshold = championRushThreshold,
                            Eligible = new List<string>(eligibleSet)
                        }
                    }
                };

                var runner = new MatchRunner(bundle, map, cfg);
                while (!runner.IsFinished)
                {
                    runner.Step();
                }

                var matchRes = runner.GetMatchResult();
                result.Matches.Add(matchRes);

                // Update fatigue carry
                foreach (var kv in matchRes.PlayerStats)
                {
                    fatigueCarry[kv.Key] = kv.Value.FatigueOut;
                }

                // Update standings
                for (int i = 0; i < matchRes.Placements.Count; i++)
                {
                    var pl = matchRes.Placements[i];
                    if (standingsMap.TryGetValue(pl.SquadId, out var row))
                    {
                        row.MatchesPlayed++;
                        row.PlacementPoints += pl.PlacementPoints;
                        row.KillPoints += pl.Kills * bundle.Scoring.KillPoints;
                        if (pl.Booyah) row.Booyahs++;
                        row.LastMatchPlacement = pl.Placement;
                    }
                }

                if (isChampionRushFinals)
                {
                    // Check if any already-eligible team won Booyah in this match
                    if (!string.IsNullOrEmpty(matchRes.ChampionRush.Crowned))
                    {
                        result.CrownedChampionSquadId = matchRes.ChampionRush.Crowned;
                        break;
                    }

                    // Otherwise update NewlyEligible for subsequent matches
                    foreach (var kv in standingsMap)
                    {
                        if (kv.Value.TotalPoints >= championRushThreshold)
                        {
                            kv.Value.ChampionRushEligible = true;
                            eligibleSet.Add(kv.Key);
                        }
                    }
                }
            }

            var list = new List<MatchDayStandingRow>(standingsMap.Values);
            SortStandings(list, result.CrownedChampionSquadId);
            result.Standings = list;

            if (isChampionRushFinals && string.IsNullOrEmpty(result.CrownedChampionSquadId) && list.Count > 0)
            {
                // Cap reached: highest points wins
                result.CrownedChampionSquadId = list[0].SquadId;
            }

            result.FinalFatigue = fatigueCarry;
            return result;
        }

        public static void SortStandings(List<MatchDayStandingRow> rows, string? crownedSquadId = null)
        {
            rows.Sort((a, b) =>
            {
                if (!string.IsNullOrEmpty(crownedSquadId))
                {
                    bool aCrowned = a.SquadId == crownedSquadId;
                    bool bCrowned = b.SquadId == crownedSquadId;
                    if (aCrowned != bCrowned) return aCrowned ? -1 : 1;
                }

                int cmp = b.TotalPoints.CompareTo(a.TotalPoints);
                if (cmp != 0) return cmp;

                // Tiebreaker 1: Total Placement Points
                cmp = b.PlacementPoints.CompareTo(a.PlacementPoints);
                if (cmp != 0) return cmp;

                // Tiebreaker 2: Total Booyahs
                cmp = b.Booyahs.CompareTo(a.Booyahs);
                if (cmp != 0) return cmp;

                // Tiebreaker 3: Last match placement ascending
                cmp = a.LastMatchPlacement.CompareTo(b.LastMatchPlacement);
                if (cmp != 0) return cmp;

                return string.CompareOrdinal(a.SquadId, b.SquadId);
            });
        }
    }
}
