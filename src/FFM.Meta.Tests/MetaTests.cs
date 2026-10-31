using System;
using System.Collections.Generic;
using System.IO;
using FFM.Meta;
using FFM.Meta.Models;
using FFM.Meta.Save;
using FFM.Meta.Services;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using NUnit.Framework;

namespace FFM.Meta.Tests
{
    [TestFixture]
    public sealed class MetaTests
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

        [Test]
        public void WP18_TrainingService_AgeCurve_Determination_And_RestRecovery()
        {
            var service = new TrainingService();
            var team = _bundle.SampleTeams[0].Clone();

            // Configure a young prodigy (age 17, high determination) vs veteran (age 25, low determination)
            team.Players[0].Age = 17;
            team.Players[0].Mental.Determination = 18f;
            team.Players[0].Mechanical.Aim = 11f;

            team.Players[1].Age = 25;
            team.Players[1].Mental.Determination = 8f;
            team.Players[1].Mechanical.Aim = 11f;
            team.Players[1].Mechanical.Reflex = 13f;

            var rng = new Rng(1001UL);
            var focuses = new Dictionary<string, IndividualTrainingFocus>
            {
                [team.Players[0].PlayerId] = IndividualTrainingFocus.AimAndRecoil,
                [team.Players[1].PlayerId] = IndividualTrainingFocus.PositioningAndAwareness
            };

            var summary = service.ExecuteWeeklyTraining(team, 1, TeamTrainingFocus.Shooting, focuses, ref rng);

            Assert.That(summary.SynergyAfter, Is.GreaterThan(summary.SynergyBefore));
            float youngAimGain = team.Players[0].Mechanical.Aim - 11f;
            float vetAimGain = team.Players[1].Mechanical.Aim - 11f;
            Assert.That(youngAimGain, Is.GreaterThan(vetAimGain * 2.0f), "17yo high-Determination player should gain significantly more Aim than 25yo low-Determination player.");
            Assert.That(team.Players[1].Mechanical.Reflex, Is.LessThan(13f), "25yo veteran not focusing CQC/Aim should experience slight natural reflex decay.");

            // Test Rest week recovery
            team.Players[0].Condition.Fatigue = 60f;
            var restSummary = service.ExecuteWeeklyTraining(team, 2, TeamTrainingFocus.Rest, null, ref rng);
            Assert.That(team.Players[0].Condition.Fatigue, Is.EqualTo(32f).Within(0.01f));
            Assert.That(restSummary.PlayerReports[0].AttributeDeltas.Count, Is.EqualTo(0));
        }

        [Test]
        public void WP19_SeasonCalendar_Groups_PointRush_ChampionRush_And_Tiebreaker()
        {
            var cal = new SeasonCalendarService();
            var regStandings = cal.InitializeRegularSeasonStandings(_bundle.SampleTeams);
            Assert.That(regStandings.Count, Is.EqualTo(18));

            // Week 1 lobby has 12 teams (Groups A & B)
            var week1Teams = cal.GetMatchDayTeamIds(1, regStandings);
            Assert.That(week1Teams.Count, Is.EqualTo(12));

            // Transition to Point Rush keeps Top 12
            var prStandings = cal.TransitionToPointRush(regStandings);
            Assert.That(prStandings.Count, Is.EqualTo(12));

            // Transition to Grand Finals assigns Head-Start Points (10, 7, 5, 4, 3, 2, 1, 1, 0...)
            var finalsStandings = cal.TransitionToGrandFinals(prStandings);
            Assert.That(finalsStandings[0].HeadStartPoints, Is.EqualTo(10));
            Assert.That(finalsStandings[1].HeadStartPoints, Is.EqualTo(7));
            Assert.That(finalsStandings[11].HeadStartPoints, Is.EqualTo(0));

            // Test Champion Rush: Reaching 80 points makes team eligible for NEXT match, not immediately crowned even if Booyah
            string teamA = finalsStandings[0].TeamId;
            finalsStandings[0].PlacementPoints = 65; // +10 head start = 75
            var match1 = new MatchResult
            {
                Placements = new List<SquadPlacementResult>
                {
                    new() { SquadId = teamA, Placement = 1, PlacementPoints = 12, Kills = 5, Points = 17, Booyah = true }
                }
            };

            string? crowned1 = cal.ApplyMatchResult(finalsStandings, match1, isGrandFinals: true, matchNumberInFinals: 1);
            Assert.That(crowned1, Is.Null, "Reaching 80 pts on the current match only unlocks MatchPointEligible for the NEXT match.");
            Assert.That(finalsStandings[0].MatchPointEligible, Is.True);

            // Next match: winning Booyah while already MatchPointEligible crowns Champion!
            var match2 = new MatchResult
            {
                Placements = new List<SquadPlacementResult>
                {
                    new() { SquadId = teamA, Placement = 1, PlacementPoints = 12, Kills = 3, Points = 15, Booyah = true }
                }
            };
            string? crowned2 = cal.ApplyMatchResult(finalsStandings, match2, isGrandFinals: true, matchNumberInFinals: 2);
            Assert.That(crowned2, Is.EqualTo(teamA));

            // Test 4-level tiebreaker (TotalPoints -> Booyahs -> TotalKills -> LastMatchPlacement)
            var t1 = new LeagueStandingEntry { TeamId = "t1", PlacementPoints = 40, KillPoints = 20, Booyahs = 1, TotalKills = 20, LastMatchPlacement = 4 };
            var t2 = new LeagueStandingEntry { TeamId = "t2", PlacementPoints = 40, KillPoints = 20, Booyahs = 2, TotalKills = 15, LastMatchPlacement = 6 };
            var tieList = new List<LeagueStandingEntry> { t1, t2 };
            cal.SortStandings(tieList);
            Assert.That(tieList[0].TeamId, Is.EqualTo("t2"), "Higher Booyahs should win tiebreaker at equal TotalPoints.");
        }

        [Test]
        public void WP20_LineupValidator_Playbook_And_PostMatchAnalyzer()
        {
            var validator = new LineupValidator();
            var team = _bundle.SampleTeams[0].Clone();

            var errors = validator.ValidateLineup(team, _bundle);
            Assert.That(errors, Is.Empty, "Sample team lineup should be valid.");

            // Duplicate active skill should be caught
            string savedActive = team.Lineup.Starters[1].Active;
            team.Lineup.Starters[1].Active = team.Lineup.Starters[0].Active;
            var dupErrors = validator.ValidateLineup(team, _bundle);
            Assert.That(dupErrors.Count, Is.GreaterThan(0));
            team.Lineup.Starters[1].Active = savedActive;

            // Substitute starter with sub
            if (!string.IsNullOrEmpty(team.Lineup.Sub))
            {
                string oldStarter = team.Lineup.Starters[3].PlayerId;
                string subId = team.Lineup.Sub!;
                bool swapped = validator.SubstituteStarter(team, oldStarter, subId);
                Assert.That(swapped, Is.True);
                Assert.That(team.Lineup.Starters[3].PlayerId, Is.EqualTo(subId));
                Assert.That(team.Lineup.Sub, Is.EqualTo(oldStarter));
            }

            // Playbook service evaluation
            var pbService = new PlaybookService();
            pbService.UpdatePlaybook(team, DropStyle.Hot, RotationStyle.Early, EngageStyle.Contest, EndgameStyle.HighGround);
            var fitNotes = pbService.EvaluateTacticalFit(team);
            Assert.That(fitNotes.Count, Is.GreaterThan(0));

            // PostMatchAnalyzer generates 3 Key Moments & 3 Attribution Insights
            var analyzer = new PostMatchAnalyzer();
            var dummyResult = new MatchResult
            {
                DurationSec = 205f,
                Placements = new List<SquadPlacementResult>
                {
                    new() { SquadId = team.TeamId, Placement = 2, Kills = 6, PlacementPoints = 9, Points = 15, Booyah = false }
                }
            };
            var report = analyzer.AnalyzeMatch(dummyResult, team);
            Assert.That(report.KeyMoments.Count, Is.EqualTo(3));
            Assert.That(report.AttributionInsights.Count, Is.EqualTo(3));
        }

        [Test]
        public void WP21_Scouting_Transfer_Economy_And_PatchNotes()
        {
            var scouting = new ScoutingService();
            var player = _bundle.SampleTeams[0].Players[0];
            var entry = new ScoutingEntry { PlayerId = player.PlayerId };

            var unknownView = scouting.BuildPlayerView(player, entry.Stage);
            Assert.That(unknownView.Attributes["aim"].IsExact, Is.False);
            Assert.That(unknownView.HiddenTraitHints, Is.Empty, "Unknown stage should not reveal hidden trait hints.");

            scouting.InvestScoutingPoints(entry, 50);
            Assert.That(entry.Stage, Is.EqualTo(ScoutingStage.Range));

            scouting.InvestScoutingPoints(entry, 50);
            Assert.That(entry.Stage, Is.EqualTo(ScoutingStage.Exact));
            var exactView = scouting.BuildPlayerView(player, entry.Stage);
            Assert.That(exactView.Attributes["aim"].IsExact, Is.True);
            Assert.That(exactView.HiddenTraitHints.Count, Is.GreaterThan(0));

            // Transfer & Economy
            var transfers = new TransferService();
            var economy = new EconomyService();
            var rng = new Rng(42UL);
            var market = transfers.GenerateMarketListings(1, ref rng, 6);
            Assert.That(market.Count, Is.EqualTo(6));

            var team = _bundle.SampleTeams[0].Clone();
            while (team.Players.Count > 5) team.Players.RemoveAt(team.Players.Count - 1);
            var ledger = new List<FinanceLedgerEntry>();
            int cash = 500000;

            bool signed = transfers.TrySignPlayer(team, market, market[0].Player.PlayerId, economy, ledger, ref cash, 1, 1, out _);
            Assert.That(signed, Is.True);
            Assert.That(team.Players.Count, Is.EqualTo(6));
        }

        [Test]
        public void WP22_SaveService_Migration_And_FullSeasonCareerProgression()
        {
            var career = CareerController.CreateNewCareer(_bundle, _map, seed: 20260416UL);
            Assert.That(career.State.Season, Is.EqualTo(1));
            Assert.That(career.State.Week, Is.EqualTo(1));

            // Advance through all 12 weeks of Season 1 into Season 2 Week 1
            for (int w = 1; w <= 12; w++)
            {
                career.AdvanceWeekWithSimulation();
            }

            Assert.That(career.State.Season, Is.EqualTo(2));
            Assert.That(career.State.Week, Is.EqualTo(1));
            Assert.That(career.State.PatchVersion, Is.EqualTo("v1.1"));
            Assert.That(career.State.ReigningChampionTeamId, Is.Not.Null.And.Not.Empty);
            Assert.That(career.State.LastMatchReport, Is.Not.Null);

            // Round-trip save/load & legacy v0 schema migration test
            string json = career.SaveService.Serialize(career.State);
            var loaded = career.SaveService.Deserialize(json);
            Assert.That(loaded.Season, Is.EqualTo(2));
            Assert.That(loaded.SchemaVersion, Is.EqualTo(SaveService.CurrentSchemaVersion));

            string legacyJson = "{\"schemaVersion\":0,\"season\":1,\"week\":3,\"playerTeamId\":\"team_01\"}";
            var migrated = career.SaveService.Deserialize(legacyJson);
            Assert.That(migrated.SchemaVersion, Is.EqualTo(SaveService.CurrentSchemaVersion));
            Assert.That(migrated.PatchVersion, Is.EqualTo("v1.0"));
            Assert.That(migrated.CashBalance, Is.EqualTo(150000));
        }

        [Test]
        public void WP23_IdleClock_Vault_And_5DimensionPlayerProgression()
        {
            var career = CareerController.CreateNewCareer(_bundle, _map, seed: 20261018UL);
            var team = career.GetPlayerTeam();
            var player = team.Players[0];

            // 1. Continuous Idle Progression Clock & Idle Vault accumulation
            career.State.IdleClock.SpeedMode = IdleSpeedMode.Turbo4x;
            career.State.IdleClock.MatchDayPolicy = MatchDayAutoPolicy.PauseBeforeMatch;

            // Simulate 4 seconds at 4x speed (= 48 game hours, advancing from Day 1 into Day 3)
            var tickRes = career.TickIdleProgression(4.0f);
            Assert.That(tickRes.AdvancedHour, Is.True);
            Assert.That(tickRes.AdvancedDay, Is.True);
            Assert.That(career.State.IdleClock.CurrentDayOfWeek, Is.GreaterThanOrEqualTo(2));
            Assert.That(career.State.IdleClock.UnclaimedIdleCash, Is.GreaterThan(0));
            Assert.That(career.State.IdleClock.UnclaimedClubXp, Is.GreaterThan(0));
            Assert.That(career.State.IdleClock.UnclaimedTacticalPoints, Is.GreaterThan(0));

            // Claim Idle Vault rewards
            int cashBeforeVault = career.State.CashBalance;
            int xpBeforeVault = career.State.IdleClock.ClubXpPool;
            var (claimedCash, claimedXp, claimedTp) = career.ClaimIdleVaultRewards();
            Assert.That(claimedCash, Is.GreaterThan(0));
            Assert.That(claimedXp, Is.GreaterThan(0));
            Assert.That(claimedTp, Is.GreaterThan(0));
            Assert.That(career.State.CashBalance, Is.EqualTo(cashBeforeVault + claimedCash));
            Assert.That(career.State.IdleClock.ClubXpPool, Is.EqualTo(xpBeforeVault + claimedXp));
            Assert.That(career.State.IdleClock.UnclaimedIdleCash, Is.EqualTo(0));

            // 2. Level Up & Star Limit-Break
            var prog = player.GetProgression();
            prog.StarTier = 1;
            prog.Level = 1;
            int capLevel = PlayerProgressionData.GetStarBreakRequiredLevel(prog.StarTier);
            career.State.IdleClock.ClubXpPool = 50000;
            career.State.IdleClock.TacticalPoints = 500;
            career.State.CashBalance = 500000;

            while (prog.Level < capLevel)
            {
                bool leveled = career.LevelUpPlayer(player.PlayerId);
                Assert.That(leveled, Is.True);
            }
            Assert.That(prog.Level, Is.EqualTo(capLevel));

            bool starBroken = career.StarBreakPlayer(player.PlayerId);
            Assert.That(starBroken, Is.True);
            Assert.That(prog.StarTier, Is.EqualTo(2));

            // 3. Special Bootcamp Drills (+Aim/Recoil/CQC & +90 XP)
            float aimBefore = player.Mechanical.Aim;
            bool drilled = career.RunBootcampDrill(player.PlayerId, BootcampDrillType.LiveFireReflex);
            Assert.That(drilled, Is.True);
            Assert.That(player.Mechanical.Aim, Is.GreaterThan(aimBefore));

            // 4. Club Facilities Upgrade & Signature Trait Reforge
            int shootingLabBefore = career.State.Facilities.ShootingLabLevel;
            bool upgraded = career.UpgradeFacility("shooting");
            Assert.That(upgraded, Is.True);
            Assert.That(career.State.Facilities.ShootingLabLevel, Is.EqualTo(shootingLabBefore + 1));

            FFM.Meta.Services.SignatureTraitCatalog.EnsureStarterTraits(player);
            string traitBefore = player.SignatureTraits[0];
            bool traitSwitched = career.CyclePlayerSignatureTrait(player.PlayerId, 0, 40);
            Assert.That(traitSwitched, Is.True);
            Assert.That(player.SignatureTraits[0], Is.Not.EqualTo(traitBefore));

            // 5. Save/Load persistence of progressions, facilities & idle clock
            string json = career.SaveService.Serialize(career.State);
            var roundtrip = career.SaveService.Deserialize(json);
            Assert.That(roundtrip.Facilities.ShootingLabLevel, Is.EqualTo(shootingLabBefore + 1));
            Assert.That(roundtrip.Teams[0].Players[0].GetProgression().StarTier, Is.EqualTo(prog.StarTier));
        }
    }
}
