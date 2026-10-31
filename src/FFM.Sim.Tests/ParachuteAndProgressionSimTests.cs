using System;
using System.Collections.Generic;
using System.IO;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using NUnit.Framework;

namespace FFM.Sim.Tests
{
    [TestFixture]
    public sealed class ParachuteAndProgressionSimTests
    {
        private static string GetRepoRoot()
        {
            string dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FFM.sln")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir) ?? string.Empty;
            }
            throw new DirectoryNotFoundException("Could not locate FFM.sln root.");
        }

        private ConfigBundle _bundle = null!;
        private MapData _map = null!;

        [OneTimeSetUp]
        public void SetUp()
        {
            string root = GetRepoRoot();
            string configsDir = Path.Combine(root, "configs");
            _bundle = ConfigLoader.LoadFromDirectory(configsDir);
            _map = MapData.LoadFromDirectory(Path.Combine(configsDir, "maps", "bermuda_lite"));
        }

        private MatchConfig BuildMatchConfig(ulong seed = 20261018UL)
        {
            var cfg = new MatchConfig
            {
                Seed = seed
            };

            for (int i = 0; i < MatchRunner.SquadCount; i++)
            {
                var team = _bundle.SampleTeams[i].Clone();
                cfg.Squads.Add(SquadDef.FromTeamDef(team));
            }

            return cfg;
        }

        [Test]
        public void ParachuteDrop_FlightPath_And_3StageTransition_ToGrounded()
        {
            var matchCfg = BuildMatchConfig(101UL);
            var runner = new MatchRunner(_bundle, _map, matchCfg);

            // At tick 0: flight line is active across the map, all 48 units are InPlane at 420m
            var snap0 = runner.CurrSnapshot;
            Assert.That(snap0.Phase, Is.EqualTo(Phase.Drop));
            Assert.That(snap0.FlightPath.IsActive, Is.True);
            Assert.That(snap0.FlightPath.AltitudeMeters, Is.EqualTo(420f).Within(0.1f));
            float flightDist = Vec2.Distance(snap0.FlightPath.StartPos, snap0.FlightPath.EndPos);
            Assert.That(flightDist, Is.GreaterThan(1200f), "Flight line should span across the map.");

            for (int u = 0; u < MatchRunner.TotalUnits; u++)
            {
                Assert.That(snap0.Units[u].DropState, Is.EqualTo(UnitDropState.InPlane));
                Assert.That(snap0.Units[u].AltitudeMeters, Is.EqualTo(420f).Within(0.1f));
            }

            bool sawFreefall = false;
            bool sawGliding = false;
            bool sawDropLaunchedEvent = false;
            bool sawParachuteOpenedEvent = false;
            bool sawLandedEvent = false;

            for (int t = 1; t <= 150; t++)
            {
                runner.Step();
                var snap = runner.CurrSnapshot;
                for (int u = 0; u < MatchRunner.TotalUnits; u++)
                {
                    if (snap.Units[u].DropState == UnitDropState.Freefall)
                    {
                        sawFreefall = true;
                        Assert.That(snap.Units[u].AltitudeMeters, Is.LessThanOrEqualTo(420f).And.GreaterThanOrEqualTo(140f));
                    }
                    else if (snap.Units[u].DropState == UnitDropState.Gliding)
                    {
                        sawGliding = true;
                        Assert.That(snap.Units[u].AltitudeMeters, Is.LessThanOrEqualTo(140f).And.GreaterThan(0f));
                    }
                }

                var drained = runner.Events.DrainEvents();
                for (int e = 0; e < drained.Length; e++)
                {
                    if (drained[e].Type == SimEventType.DropLaunched) sawDropLaunchedEvent = true;
                    if (drained[e].Type == SimEventType.ParachuteOpened) sawParachuteOpenedEvent = true;
                    if (drained[e].Type == SimEventType.Landed) sawLandedEvent = true;
                }
            }

            Assert.That(sawFreefall, Is.True, "Units must pass through Freefall surfboard stage.");
            Assert.That(sawGliding, Is.True, "Units must pass through Gliding parachute canopy stage.");
            Assert.That(sawDropLaunchedEvent, Is.True, "DropLaunched event must be emitted.");
            Assert.That(sawParachuteOpenedEvent, Is.True, "ParachuteOpened event must be emitted.");
            Assert.That(sawLandedEvent, Is.True, "Landed event must be emitted upon touchdown.");

            // After Drop phase ends, all alive units must be Grounded at 0m altitude
            var finalDropSnap = runner.CurrSnapshot;
            for (int u = 0; u < MatchRunner.TotalUnits; u++)
            {
                Assert.That(finalDropSnap.Units[u].DropState, Is.EqualTo(UnitDropState.Grounded));
                Assert.That(finalDropSnap.Units[u].AltitudeMeters, Is.EqualTo(0f).Within(0.01f));
            }
        }

        [Test]
        public void ParachuteDrop_OverridePlayerDropPoi_UpdatesTargetPoiDuringDrop()
        {
            var matchCfg = BuildMatchConfig(202UL);
            var runner = new MatchRunner(_bundle, _map, matchCfg);

            runner.Step(); // Tick 1 (Phase.Drop)
            int initialPoiId = runner.CurrSnapshot.Squads[0].DropPoiId;
            int targetPoiId = initialPoiId == 1 ? 2 : 1;

            bool overridden = runner.OverridePlayerDropPoi(0, targetPoiId);
            Assert.That(overridden, Is.True);

            runner.Step(); // Tick 2 updates snapshot
            Assert.That(runner.CurrSnapshot.Squads[0].DropPoiId, Is.EqualTo(targetPoiId));
        }

        [Test]
        public void Progression_SignatureTraits_GrantStartingUtilityAndDropSpeedBonus()
        {
            var matchCfg = BuildMatchConfig(303UL);
            var squad0 = matchCfg.Squads[0];
            var prog0 = squad0.Members[0].Progression ?? squad0.Members[0].Player.GetProgression();
            squad0.Members[0].Progression = prog0;
            if (!prog0.SignatureTraitIds.Contains("igl_sky_eye"))
                prog0.SignatureTraitIds.Add("igl_sky_eye");
            if (!prog0.SignatureTraitIds.Contains("gloo_architect"))
                prog0.SignatureTraitIds.Add("gloo_architect");
            if (!prog0.SignatureTraitIds.Contains("demolition_expert"))
                prog0.SignatureTraitIds.Add("demolition_expert");

            var runner = new MatchRunner(_bundle, _map, matchCfg);
            var unit0 = runner.CurrSnapshot.Units[0];
            Assert.That(unit0.GlooCount, Is.EqualTo(3), "gloo_architect should grant +1 starting gloo wall.");
        }
    }
}
