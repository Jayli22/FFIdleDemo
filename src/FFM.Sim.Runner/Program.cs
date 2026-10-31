using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;

namespace FFM.Sim.Runner
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string repoRoot = FindRepoRoot();
            string configRoot = Path.Combine(repoRoot, "configs");
            ConfigBundle bundle = ConfigLoader.LoadFromDirectory(configRoot);
            MapData map = MapData.LoadFromDirectory(Path.Combine(configRoot, "maps", "bermuda_lite"));

            bool runSmoke = false;
            bool runSensitivity = false;
            ulong replaySeed = 0;
            bool hasReplay = false;
            int matches = 200;
            ulong baseSeed = 20260416UL;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (a == "--smoke") runSmoke = true;
                else if (a == "--sensitivity") runSensitivity = true;
                else if (a == "--replay" && i + 1 < args.Length && ulong.TryParse(args[++i], out ulong rs))
                {
                    hasReplay = true;
                    replaySeed = rs;
                }
                else if (a == "--matches" && i + 1 < args.Length && int.TryParse(args[++i], out int m))
                {
                    matches = Math.Max(12, m);
                }
                else if (a == "--seed" && i + 1 < args.Length && ulong.TryParse(args[++i], out ulong s))
                {
                    baseSeed = s;
                }
            }

            if (hasReplay)
            {
                return RunSingleReplay(bundle, map, replaySeed);
            }

            int exitCode = 0;
            if (runSmoke || (!runSmoke && !runSensitivity))
            {
                int code = RunSmokeSuite(bundle, map, matches, baseSeed);
                if (code != 0) exitCode = code;
            }

            if (runSensitivity)
            {
                int code = RunSensitivitySuite(bundle, map, baseSeed);
                if (code != 0) exitCode = code;
            }

            return exitCode;
        }

        private static int RunSingleReplay(ConfigBundle bundle, MapData map, ulong seed)
        {
            var squads = BuildSquadsFromSample(bundle, 12);
            var cfg = new MatchConfig
            {
                Seed = seed,
                MapId = "bermuda_lite",
                TickHz = 10,
                Squads = squads,
                PlayerSquadId = squads[0].SquadId
            };

            var runner = new MatchRunner(bundle, map, cfg);
            while (!runner.IsFinished)
            {
                runner.Step();
            }

            var res = runner.GetMatchResult();
            string json = JsonSerializer.Serialize(res, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            return 0;
        }

        private static int RunSmokeSuite(ConfigBundle bundle, MapData map, int matchCount, ulong baseSeed)
        {
            Console.WriteLine($"=== FFM.Sim.Runner Smoke & Balance Suite ({matchCount} matches, seed={baseSeed}) ===");

            // 1. Determinism Check (20 seeds)
            int detMatches = 20;
            int detPassed = 0;
            var baseSquads = BuildSquadsFromSample(bundle, 12);
            for (int i = 0; i < detMatches; i++)
            {
                ulong seed = baseSeed + (ulong)(i * 1009 + 7);
                var r1 = RunOneMatch(bundle, map, baseSquads, seed);
                var r2 = RunOneMatch(bundle, map, baseSquads, seed);
                if (r1.EventHash == r2.EventHash) detPassed++;
            }
            bool passDet = detPassed == detMatches;
            Console.WriteLine($"[Determinism] {detPassed}/{detMatches} identical EventHash -> {(passDet ? "PASS" : "FAIL")}");

            // 2. Main Smoke Benchmark & Telemetry
            // Build a 12-squad pro-league lobby with controlled skill tiers (1..20 scale: from 11.6 to 14.1)
            var tieredSquads = BuildTieredLobby(out double[] squadRatings);
            var totalPoints = new int[12];
            var blockPoints = new int[12];
            var booyahs = new int[12];
            double totalDurationSec = 0;
            long totalKills = 0;
            long totalRevives = 0;
            long totalGloo = 0;
            long totalSkills = 0;
            double sumBlockSpearman = 0;
            int blockCount = 0;

            var sw = Stopwatch.StartNew();
            for (int m = 0; m < matchCount; m++)
            {
                ulong seed = baseSeed + 50000UL + (ulong)m * 7919UL;
                var res = RunOneMatch(bundle, map, tieredSquads, seed);
                totalDurationSec += res.DurationSec;

                for (int p = 0; p < res.Placements.Count; p++)
                {
                    var pl = res.Placements[p];
                    int sqIdx = FindSquadIndex(tieredSquads, pl.SquadId);
                    if (sqIdx >= 0)
                    {
                        totalPoints[sqIdx] += pl.Points;
                        blockPoints[sqIdx] += pl.Points;
                        if (pl.Booyah) booyahs[sqIdx]++;
                        totalKills += pl.Kills;
                    }
                }

                // §10.2: Spearman rho over 12-match tournament samples
                if ((m + 1) % 12 == 0)
                {
                    sumBlockSpearman += ComputeSpearman(squadRatings, blockPoints.Select(x => (double)x).ToArray());
                    blockCount++;
                    Array.Clear(blockPoints, 0, blockPoints.Length);
                }

                foreach (var ps in res.PlayerStats.Values)
                {
                    totalRevives += ps.Revives;
                    totalGloo += ps.Gloo;
                    totalSkills += ps.SkillUses;
                }
            }
            sw.Stop();

            double msPerMatch = sw.Elapsed.TotalMilliseconds / matchCount;
            double avgDurationSec = totalDurationSec / matchCount;
            double avgKillsPerSquad = (double)totalKills / (matchCount * 12.0);
            double avgRevivesPerMatch = (double)totalRevives / matchCount;
            double avgGlooPerMatch = (double)totalGloo / matchCount;
            double avgSkillsPerMatch = (double)totalSkills / matchCount;

            double spearman = blockCount > 0
                ? sumBlockSpearman / blockCount
                : ComputeSpearman(squadRatings, totalPoints.Select(x => (double)x).ToArray());
            double maxBooyahRate = booyahs.Max() / (double)matchCount;
            double minBooyahRate = booyahs.Min() / (double)matchCount;

            // 3. Coach Command Lift Test (paired seeds with vs without adaptive coach commands)
            int cmdMatches = Math.Min(120, matchCount);
            int ptsNoCmd = 0;
            int ptsWithCmd = 0;
            var equalSquads = BuildEqualSquads(12.5f);
            for (int m = 0; m < cmdMatches; m++)
            {
                ulong seed = baseSeed + 900000UL + (ulong)m * 4099UL;
                var resNo = RunOneMatch(bundle, map, equalSquads, seed, useSmartCoach: false);
                var resYes = RunOneMatch(bundle, map, equalSquads, seed, useSmartCoach: true);

                ptsNoCmd += GetSquadPoints(resNo, equalSquads[0].SquadId);
                ptsWithCmd += GetSquadPoints(resYes, equalSquads[0].SquadId);
            }
            double cmdLiftPerMatch = (ptsWithCmd - ptsNoCmd) / (double)cmdMatches;

            // 4. Playbook Non-Dominance Test (3 styles x 4 squads each in equal-rating lobby)
            int pbMatches = Math.Min(120, matchCount);
            var pbSquads = BuildPlaybookTestLobby(12.5f);
            var pbPoints = new int[3]; // 0: Hot/Contest, 1: Cold/Avoid, 2: Flex/Balanced
            for (int m = 0; m < pbMatches; m++)
            {
                ulong seed = baseSeed + 1200000UL + (ulong)m * 6151UL;
                var res = RunOneMatch(bundle, map, pbSquads, seed);
                for (int p = 0; p < res.Placements.Count; p++)
                {
                    var pl = res.Placements[p];
                    int sqIdx = FindSquadIndex(pbSquads, pl.SquadId);
                    if (sqIdx >= 0)
                    {
                        pbPoints[sqIdx % 3] += pl.Points;
                    }
                }
            }
            int pbTotalPts = Math.Max(1, pbPoints.Sum());
            double maxPlaybookShare = pbPoints.Max() / (double)pbTotalPts; // 3 buckets: ideal = 33.3%, cap <= 42%

            bool passSpeed = msPerMatch < 50.0;
            bool passDuration = avgDurationSec >= 180.0 && avgDurationSec <= 270.0;
            bool passSpearman = spearman >= 0.60 && spearman <= 0.85;
            bool passTopBooyah = maxBooyahRate <= 0.28;
            bool passBotBooyah = minBooyahRate >= 0.01;
            bool passPlaybook = maxPlaybookShare <= 0.42;

            Console.WriteLine($"[Speed]       {msPerMatch:F2} ms/match (Target < 50ms) -> {(passSpeed ? "PASS" : "FAIL")}");
            Console.WriteLine($"[Duration]    {avgDurationSec:F1} s (Target 180s~270s) -> {(passDuration ? "PASS" : "FAIL")}");
            Console.WriteLine($"[Spearman]    rho(12-match) = {spearman:F3} (Target 0.60~0.85) -> {(passSpearman ? "PASS" : "FAIL")}");
            Console.WriteLine($"[Top1 Booyah] {maxBooyahRate * 100.0:F1}% (Target <= 28%) -> {(passTopBooyah ? "PASS" : "FAIL")}");
            Console.WriteLine($"[Bot1 Booyah] {minBooyahRate * 100.0:F1}% (Target >= 1.0%) -> {(passBotBooyah ? "PASS" : "FAIL")}");
            Console.WriteLine($"[Playbook]    Max 3-archetype share = {maxPlaybookShare * 100.0:F1}% (Target <= 42%) -> {(passPlaybook ? "PASS" : "FAIL")}");
            Console.WriteLine($"[Telemetry]   Kills/Squad={avgKillsPerSquad:F2}, Revives/Match={avgRevivesPerMatch:F1}, Gloo/Match={avgGlooPerMatch:F1}, Skills/Match={avgSkillsPerMatch:F1}, CoachLift={cmdLiftPerMatch:+0.00;-0.00} pts/match");

            bool allPass = passDet && passSpeed && passDuration && passSpearman && passTopBooyah && passBotBooyah && passPlaybook;
            Console.WriteLine($"=== Smoke Suite Result: {(allPass ? "ALL PASSED" : "FAILED")} ===");
            return allPass ? 0 : 1;
        }

        private static int RunSensitivitySuite(ConfigBundle bundle, MapData map, ulong baseSeed)
        {
            Console.WriteLine("=== FFM.Sim.Runner Attribute Sensitivity Suite (§10.2) ===");
            string[] allAttrs =
            {
                "Aim", "Recoil", "Reflex", "Movement",
                "Cqc", "LongRange", "Awareness", "Positioning",
                "Utility", "Looting", "Decision", "Comms"
            };
            var coreAttrs = new HashSet<string>(StringComparer.Ordinal)
            {
                "Aim", "Positioning", "Cqc", "Decision", "Comms", "Utility"
            };

            int trials = 200;

            bool allCorePass = true;
            bool allVisiblePass = true;

            foreach (string attr in allAttrs)
            {
                var testLobby = BuildEqualSquads(11.5f);
                // Boost 3 playbook-balanced squads (Squad 0=Hot, Squad 1=Cold, Squad 2=Flex) by +5 on 1..20 scale
                // and compare their average per-squad points against the 9 unboosted baseline squads in the same matches
                for (int s = 0; s < 3; s++)
                {
                    foreach (var slot in testLobby[s].Members)
                    {
                        BoostAttribute(slot.Player, attr, 5f);
                    }
                }

                double boostedSum = 0;
                double baselineSum = 0;
                for (int m = 0; m < trials; m++)
                {
                    ulong seed = baseSeed + 300000UL + (ulong)m * 3571UL;
                    var res = RunOneMatch(bundle, map, testLobby, seed);
                    for (int s = 0; s < 12; s++)
                    {
                        int pts = GetSquadPoints(res, testLobby[s].SquadId);
                        if (s < 3) boostedSum += pts;
                        else baselineSum += pts;
                    }
                }

                double boostedAvg = boostedSum / (3.0 * trials);
                double baselineAvg = baselineSum / (9.0 * trials);
                double liftPct = ((boostedAvg - baselineAvg) / Math.Max(0.1, baselineAvg)) * 100.0;
                bool isCore = coreAttrs.Contains(attr);
                bool pass = isCore
                    ? (liftPct >= 12.0 && liftPct <= 45.0)
                    : (liftPct >= 8.0 && liftPct <= 45.0);

                if (isCore && !pass) allCorePass = false;
                if (!pass) allVisiblePass = false;

                Console.WriteLine($"  [{attr,-12}] +5 Lift = {liftPct:+0.0;-0.0}% {(isCore ? "(Core 12%~45%)" : "(Visible >=8%)")} -> {(pass ? "PASS" : "FAIL")}");
            }

            // 8-week training simulation (+1.1 on primary attributes + Synergy gain on 1..20 scale)
            var trainedLobby = BuildEqualSquads(11.5f);
            for (int s = 0; s < 3; s++)
            {
                trainedLobby[s].Synergy = Math.Min(100f, trainedLobby[s].Synergy + 7f);
                foreach (var slot in trainedLobby[s].Members)
                {
                    BoostAttribute(slot.Player, "Aim", 1.0f);
                    BoostAttribute(slot.Player, "Positioning", 1.0f);
                    BoostAttribute(slot.Player, "Cqc", 0.9f);
                    BoostAttribute(slot.Player, "Decision", 0.9f);
                    BoostAttribute(slot.Player, "Comms", 0.9f);
                }
            }
            double trainedSum = 0;
            double untrainedSum = 0;
            for (int m = 0; m < trials; m++)
            {
                ulong seed = baseSeed + 300000UL + (ulong)m * 3571UL;
                var res = RunOneMatch(bundle, map, trainedLobby, seed);
                for (int s = 0; s < 12; s++)
                {
                    int pts = GetSquadPoints(res, trainedLobby[s].SquadId);
                    if (s < 3) trainedSum += pts;
                    else untrainedSum += pts;
                }
            }
            double trainedAvg = trainedSum / (3.0 * trials);
            double untrainedAvg = untrainedSum / (9.0 * trials);
            double trainLiftPct = ((trainedAvg - untrainedAvg) / Math.Max(0.1, untrainedAvg)) * 100.0;
            bool passTrain = trainLiftPct >= 8.0 && trainLiftPct <= 24.0;
            Console.WriteLine($"  [8-Week Train] Lift = {trainLiftPct:+0.0;-0.0}% (Target +8%~+24%) -> {(passTrain ? "PASS" : "FAIL")}");

            bool overall = allCorePass && allVisiblePass && passTrain;
            Console.WriteLine($"=== Sensitivity Suite Result: {(overall ? "ALL PASSED" : "FAILED")} ===");
            return overall ? 0 : 1;
        }

        private static MatchResult RunOneMatch(
            ConfigBundle bundle,
            MapData map,
            List<SquadDef> squads,
            ulong seed,
            bool useSmartCoach = false)
        {
            var cfg = new MatchConfig
            {
                Seed = seed,
                MapId = "bermuda_lite",
                TickHz = 10,
                Squads = squads,
                PlayerSquadId = squads[0].SquadId
            };

            var runner = new MatchRunner(bundle, map, cfg);
            while (!runner.IsFinished)
            {
                if (useSmartCoach && runner.CurrentTick % 100 == 0 && runner.CurrentPhase >= Phase.Loot)
                {
                    ref readonly var sqSnap = ref runner.CurrSnapshot.Squads[0];
                    if (sqSnap.AliveCount > 0 && sqSnap.CommandsRemaining > 0 && sqSnap.CommandCooldownTicks == 0)
                    {
                        float distToZone = Vec2.Distance(sqSnap.CenterPos, runner.CurrSnapshot.Zone.NextCenter);
                        if (distToZone > runner.CurrSnapshot.Zone.NextRadius * 0.75f)
                        {
                            runner.IssuePlayerCommand(new Command(CommandType.RotateNow));
                        }
                        else if (sqSnap.AliveCount <= 2)
                        {
                            runner.IssuePlayerCommand(new Command(CommandType.SecurePlacement));
                        }
                        else if (runner.CurrentPhase == Phase.EndGame)
                        {
                            runner.IssuePlayerCommand(new Command(CommandType.EndgameFormation, endgameParam: EndgameStyle.HighGround));
                        }
                    }
                }
                runner.Step();
            }

            return runner.GetMatchResult();
        }

        private static int GetSquadPoints(MatchResult res, string squadId)
        {
            for (int i = 0; i < res.Placements.Count; i++)
            {
                if (res.Placements[i].SquadId == squadId) return res.Placements[i].Points;
            }
            return 0;
        }

        private static int FindSquadIndex(List<SquadDef> squads, string squadId)
        {
            for (int i = 0; i < squads.Count; i++)
            {
                if (squads[i].SquadId == squadId) return i;
            }
            return -1;
        }

        private static List<SquadDef> BuildSquadsFromSample(ConfigBundle bundle, int count)
        {
            var list = new List<SquadDef>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(SquadDef.FromTeamDef(bundle.SampleTeams[i]));
            }
            return list;
        }

        private static List<SquadDef> BuildTieredLobby(out double[] ratings)
        {
            // 12 pro-league squads with base ratings smoothly spaced on 1..20 scale (11.7 to 13.8)
            ratings = new double[12];
            var list = new List<SquadDef>(12);
            for (int i = 0; i < 12; i++)
            {
                float r = 11.7f + (2.1f * i / 11f);
                ratings[i] = r;
                list.Add(CreateSyntheticSquad(i, $"t_tier_{i:D2}", $"Tier {i + 1}", r, i % 3));
            }
            return list;
        }

        private static List<SquadDef> BuildEqualSquads(float rating)
        {
            var list = new List<SquadDef>(12);
            for (int i = 0; i < 12; i++)
            {
                list.Add(CreateSyntheticSquad(i, $"t_eq_{i:D2}", $"Equal {i + 1}", rating, i % 3));
            }
            return list;
        }

        private static List<SquadDef> BuildPlaybookTestLobby(float rating)
        {
            var list = new List<SquadDef>(12);
            for (int i = 0; i < 12; i++)
            {
                list.Add(CreateSyntheticSquad(i, $"t_pb_{i:D2}", $"PB {i + 1}", rating, i % 3));
            }
            return list;
        }

        private static SquadDef CreateSyntheticSquad(int idx, string id, string name, float rating, int styleIdx)
        {
            var pb = styleIdx switch
            {
                0 => new PlaybookDef
                {
                    Drop = "Hot",
                    Rotation = "Early",
                    Engage = "Contest",
                    Endgame = "HighGround"
                },
                1 => new PlaybookDef
                {
                    Drop = "Cold",
                    Rotation = "Edge",
                    Engage = "Avoid",
                    Endgame = "GlooFort"
                },
                _ => new PlaybookDef
                {
                    Drop = "Flex",
                    Rotation = "Early",
                    Engage = "Balanced",
                    Endgame = "Building"
                }
            };

            var sq = new SquadDef
            {
                SquadId = id,
                Name = name,
                ColorHex = "#FF5500",
                Synergy = 65f + (rating - 12f) * 3f,
                Playbook = pb
            };

            Role[] roles = { Role.Rusher, Role.SecondRusher, Role.Sniper, Role.Support };
            string[] actives = { "sk_dash", "sk_shield", "sk_scan", "sk_heal" };

            for (int m = 0; m < 4; m++)
            {
                var p = new PlayerDef
                {
                    PlayerId = $"{id}_p{m}",
                    Name = $"{name}_{m}",
                    Age = 19,
                    Region = "SEA",
                    Mechanical = new MechanicalAttributes
                    {
                        Aim = rating,
                        Recoil = rating,
                        Reflex = rating,
                        Movement = rating,
                        Cqc = rating,
                        LongRange = rating
                    },
                    Tactical = new TacticalAttributes
                    {
                        Awareness = rating,
                        Positioning = rating,
                        Utility = rating,
                        Looting = rating,
                        Decision = rating,
                        Comms = rating
                    },
                    Mental = new MentalAttributes
                    {
                        Composure = rating,
                        Teamwork = rating,
                        Determination = rating
                    },
                    Hidden = new HiddenAttributes
                    {
                        Consistency = 14f,
                        BigMatch = 13f,
                        InjuryProne = 4f
                    },
                    Condition = new PlayerCondition
                    {
                        Stamina = 14f,
                        Form = 0f,
                        Fatigue = 5f
                    }
                };
                p.RoleProficiency[roles[m].ToString()] = 15f;
                p.SkillProficiency[actives[m]] = 15f;

                sq.Members.Add(new SquadRuntimePlayerSlot
                {
                    Role = roles[m],
                    ActiveSkillId = actives[m],
                    PassiveSkillIds = new List<string> { "pk_armor_pen", "pk_move_speed", "pk_fast_revive" },
                    IsIgl = m == 3,
                    Player = p
                });
            }

            return sq;
        }

        private static void BoostAttribute(PlayerDef p, string attr, float delta)
        {
            switch (attr)
            {
                case "Aim": p.Mechanical.Aim = Math.Clamp(p.Mechanical.Aim + delta, 1f, 20f); break;
                case "Recoil": p.Mechanical.Recoil = Math.Clamp(p.Mechanical.Recoil + delta, 1f, 20f); break;
                case "Reflex": p.Mechanical.Reflex = Math.Clamp(p.Mechanical.Reflex + delta, 1f, 20f); break;
                case "Movement": p.Mechanical.Movement = Math.Clamp(p.Mechanical.Movement + delta, 1f, 20f); break;
                case "Cqc": p.Mechanical.Cqc = Math.Clamp(p.Mechanical.Cqc + delta, 1f, 20f); break;
                case "LongRange": p.Mechanical.LongRange = Math.Clamp(p.Mechanical.LongRange + delta, 1f, 20f); break;
                case "Awareness": p.Tactical.Awareness = Math.Clamp(p.Tactical.Awareness + delta, 1f, 20f); break;
                case "Positioning": p.Tactical.Positioning = Math.Clamp(p.Tactical.Positioning + delta, 1f, 20f); break;
                case "Utility": p.Tactical.Utility = Math.Clamp(p.Tactical.Utility + delta, 1f, 20f); break;
                case "Looting": p.Tactical.Looting = Math.Clamp(p.Tactical.Looting + delta, 1f, 20f); break;
                case "Decision": p.Tactical.Decision = Math.Clamp(p.Tactical.Decision + delta, 1f, 20f); break;
                case "Comms": p.Tactical.Comms = Math.Clamp(p.Tactical.Comms + delta, 1f, 20f); break;
            }
        }

        private static double ComputeSpearman(double[] x, double[] y)
        {
            int n = x.Length;
            double[] rx = GetRanks(x);
            double[] ry = GetRanks(y);

            double meanX = rx.Average();
            double meanY = ry.Average();
            double num = 0, denX = 0, denY = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = rx[i] - meanX;
                double dy = ry[i] - meanY;
                num += dx * dy;
                denX += dx * dx;
                denY += dy * dy;
            }
            return (denX > 0 && denY > 0) ? num / Math.Sqrt(denX * denY) : 0.0;
        }

        private static double[] GetRanks(double[] values)
        {
            int n = values.Length;
            var indexed = values.Select((v, idx) => (v, idx)).OrderBy(t => t.v).ToArray();
            var ranks = new double[n];
            int i = 0;
            while (i < n)
            {
                int j = i;
                while (j + 1 < n && Math.Abs(indexed[j + 1].v - indexed[i].v) < 1e-9) j++;
                double avgRank = (i + j) / 2.0 + 1.0;
                for (int k = i; k <= j; k++) ranks[indexed[k].idx] = avgRank;
                i = j + 1;
            }
            return ranks;
        }

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "FFM.sln"))) return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            return Directory.GetCurrentDirectory();
        }
    }
}
