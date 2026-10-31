using System;
using System.Collections.Generic;
using System.IO;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using NUnit.Framework;

namespace FFM.Sim.Tests.Simulation
{
    [TestFixture]
    public class SimulationTests
    {
        private ConfigBundle _bundle = null!;
        private MapData _map = null!;
        private List<SquadDef> _squads12 = null!;

        private static string FindRepoRoot()
        {
            string? dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null)
            {
                if (File.Exists(Path.Combine(dir, "FFM.sln")))
                {
                    return dir;
                }
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new DirectoryNotFoundException("Could not locate repo root containing FFM.sln");
        }

        [OneTimeSetUp]
        public void SetUp()
        {
            string root = FindRepoRoot();
            string configRoot = Path.Combine(root, "configs");
            _bundle = ConfigLoader.LoadFromDirectory(configRoot);
            _map = MapData.LoadFromDirectory(Path.Combine(configRoot, "maps", "bermuda_lite"));

            _squads12 = new List<SquadDef>(12);
            for (int i = 0; i < 12; i++)
            {
                _squads12.Add(SquadDef.FromTeamDef(_bundle.SampleTeams[i]));
            }
        }

        private MatchConfig CreateDefaultMatchConfig(ulong seed = 20260416UL)
        {
            return new MatchConfig
            {
                Seed = seed,
                MapId = "bermuda_lite",
                TickHz = 10,
                Squads = _squads12,
                PlayerSquadId = _squads12[0].SquadId
            };
        }

        [Test]
        public void MatchRunner_CompletesWithValidPlacementsAndBooyah()
        {
            var cfg = CreateDefaultMatchConfig(10001UL);
            var runner = new MatchRunner(_bundle, _map, cfg);

            while (!runner.IsFinished)
            {
                runner.Step();
            }

            MatchResult res = runner.GetMatchResult();
            Assert.That(res.Placements.Count, Is.EqualTo(12));
            Assert.That(res.DurationSec, Is.GreaterThanOrEqualTo(60f).And.LessThanOrEqualTo(360f));

            int booyahs = 0;
            for (int i = 0; i < 12; i++)
            {
                Assert.That(res.Placements[i].Placement, Is.EqualTo(i + 1));
                if (res.Placements[i].Booyah) booyahs++;
            }
            Assert.That(booyahs, Is.EqualTo(1));
            Assert.That(res.PlayerStats.Count, Is.EqualTo(48));
        }

        [Test]
        public void MatchRunner_Is100PercentDeterministicAcrossIdenticalSeeds()
        {
            for (ulong s = 1; s <= 5; s++)
            {
                ulong seed = 900000UL + s * 7919UL;
                var runnerA = new MatchRunner(_bundle, _map, CreateDefaultMatchConfig(seed));
                var runnerB = new MatchRunner(_bundle, _map, CreateDefaultMatchConfig(seed));

                while (!runnerA.IsFinished) runnerA.Step();
                while (!runnerB.IsFinished) runnerB.Step();

                var resA = runnerA.GetMatchResult();
                var resB = runnerB.GetMatchResult();

                Assert.That(resA.EventHash, Is.EqualTo(resB.EventHash), $"Determinism hash mismatch on seed {seed}");
                Assert.That(resA.DurationSec, Is.EqualTo(resB.DurationSec));

                for (int i = 0; i < 12; i++)
                {
                    Assert.That(resA.Placements[i].SquadId, Is.EqualTo(resB.Placements[i].SquadId));
                    Assert.That(resA.Placements[i].Kills, Is.EqualTo(resB.Placements[i].Kills));
                    Assert.That(resA.Placements[i].Points, Is.EqualTo(resB.Placements[i].Points));
                }
            }
        }

        [Test]
        public void MatchRunner_Step_AllocatesZeroBytesOnHotPath()
        {
            var runner = new MatchRunner(_bundle, _map, CreateDefaultMatchConfig(77777UL));

            // Warm-up 50 ticks so JIT compiles all methods
            for (int i = 0; i < 50; i++)
            {
                runner.Step();
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200 && !runner.IsFinished; i++)
            {
                runner.Step();
            }
            long after = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(after - before, Is.EqualTo(0L), "MatchRunner.Step() must allocate 0 bytes on the managed heap");
        }

        [Test]
        public void CoachCommands_RespectLimitCooldownAndEmitEvents()
        {
            var runner = new MatchRunner(_bundle, _map, CreateDefaultMatchConfig(424242UL));

            // Advance out of Drop phase into Loot phase (Tick > 100)
            for (int i = 0; i < 120; i++)
            {
                runner.Step();
            }

            bool accepted1 = runner.IssuePlayerCommand(new Command(CommandType.RotateNow));
            Assert.That(accepted1, Is.True, "First command in Loot phase should be accepted");
            Assert.That(runner.CurrSnapshot.Squads[0].CommandsRemaining, Is.EqualTo(4));

            // Immediate second command should fail due to 60s (600-tick) cooldown
            bool acceptedImmediate = runner.IssuePlayerCommand(new Command(CommandType.SecurePlacement));
            Assert.That(acceptedImmediate, Is.False, "Command during cooldown should be rejected");

            // Step 80 ticks so pending command delay expires and executes
            for (int i = 0; i < 80; i++)
            {
                runner.Step();
            }

            var events = runner.Events.PeekAllRetained();
            bool foundIssued = false;
            bool foundExecuted = false;
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].Type == SimEventType.CommandIssued) foundIssued = true;
                if (events[i].Type == SimEventType.CommandExecuted) foundExecuted = true;
            }
            Assert.That(foundIssued, Is.True);
            Assert.That(foundExecuted, Is.True);
        }

        [Test]
        public void MatchDayRunner_AccumulatesFatigueAndAppliesChampionRushAndTiebreakers()
        {
            var headStart = new Dictionary<string, int>
            {
                [_squads12[0].SquadId] = 78,
                [_squads12[1].SquadId] = 75
            };

            var mdResult = MatchDayRunner.RunMatchDay(
                _bundle,
                _map,
                _squads12,
                baseSeed: 555666UL,
                maxMatches: 6,
                isChampionRushFinals: true,
                championRushThreshold: 80,
                headStartPoints: headStart);

            Assert.That(mdResult.Matches.Count, Is.GreaterThanOrEqualTo(1).And.LessThanOrEqualTo(6));
            Assert.That(mdResult.Standings.Count, Is.EqualTo(12));
            Assert.That(mdResult.CrownedChampionSquadId, Is.Not.Null.And.Not.Empty);

            // Verify fatigue increased across matches
            foreach (var kv in mdResult.FinalFatigue)
            {
                Assert.That(kv.Value, Is.GreaterThan(0f));
            }

            // Verify 3-level tiebreaker sort explicitly
            var tieRows = new List<MatchDayStandingRow>
            {
                new() { SquadId = "A", HeadStartPoints = 0, PlacementPoints = 30, KillPoints = 20, Booyahs = 1, LastMatchPlacement = 4 },
                new() { SquadId = "B", HeadStartPoints = 0, PlacementPoints = 35, KillPoints = 15, Booyahs = 0, LastMatchPlacement = 6 },
                new() { SquadId = "C", HeadStartPoints = 0, PlacementPoints = 35, KillPoints = 15, Booyahs = 2, LastMatchPlacement = 5 },
                new() { SquadId = "D", HeadStartPoints = 0, PlacementPoints = 35, KillPoints = 15, Booyahs = 2, LastMatchPlacement = 2 }
            };
            MatchDayRunner.SortStandings(tieRows);
            Assert.That(tieRows[0].SquadId, Is.EqualTo("D")); // 50 pts, 35 placePts, 2 booyahs, last place 2
            Assert.That(tieRows[1].SquadId, Is.EqualTo("C")); // 50 pts, 35 placePts, 2 booyahs, last place 5
            Assert.That(tieRows[2].SquadId, Is.EqualTo("B")); // 50 pts, 35 placePts, 0 booyahs
            Assert.That(tieRows[3].SquadId, Is.EqualTo("A")); // 50 pts, 30 placePts
        }

        [Test]
        public void IndoorAndDirectionalCover_PopulatesSnapshotsAndTacticalCombatFlags()
        {
            var runner = new MatchRunner(_bundle, _map, CreateDefaultMatchConfig(314159UL));
            int indoorUnitTicks = 0;
            int lowCoverUnitTicks = 0;
            int tacticalShotFlagsCount = 0;

            while (!runner.IsFinished)
            {
                runner.Step();
                var snap = runner.CurrSnapshot;
                for (int u = 0; u < MatchRunner.TotalUnits; u++)
                {
                    ref readonly var us = ref snap.Units[u];
                    if (us.State == UnitState.Eliminated) continue;
                    if ((us.TerrainFlags & (byte)CellFlags.BuildingInterior) != 0) indoorUnitTicks++;
                    if ((us.TerrainFlags & (byte)CellFlags.TacticalLowCover) != 0) lowCoverUnitTicks++;
                }

                var events = runner.Events.PeekAllRetained();
                for (int e = 0; e < events.Length; e++)
                {
                    if (events[e].Tick == snap.Tick && events[e].Type == SimEventType.ShotFired)
                    {
                        if ((events[e].ExtraCode & (32 | 64 | 128 | 256)) != 0)
                        {
                            tacticalShotFlagsCount++;
                        }
                    }
                }
            }

            Assert.That(indoorUnitTicks, Is.GreaterThan(0), "Units should enter and garrison buildings during the match");
            Assert.That(lowCoverUnitTicks, Is.GreaterThan(0), "Units should utilize outdoor TacticalLowCover during the match");
            Assert.That(tacticalShotFlagsCount, Is.GreaterThan(0), "Combat should emit directional cover/flank/CQC/window tactical shot flags");
        }
    }
}
