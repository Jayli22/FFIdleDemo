using System;
using System.Collections.Generic;
using System.IO;
using FFM.Sim.Data;
using NUnit.Framework;

namespace FFM.Sim.Tests.Data
{
    [TestFixture]
    public class ConfigTests
    {
        private static string GetRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FFM.sln")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir) ?? string.Empty;
            }
            throw new DirectoryNotFoundException("Could not locate repository root containing FFM.sln.");
        }

        [Test]
        public void LoadFromDirectory_LoadsAllConfigsAndSample18_WithZeroValidationErrors()
        {
            string configsDir = Path.Combine(GetRepoRoot(), "configs");
            ConfigBundle bundle = ConfigLoader.LoadFromDirectory(configsDir);

            Assert.That(bundle.Attributes.Attributes.Count, Is.EqualTo(19));
            Assert.That(bundle.Roles.Roles.Count, Is.EqualTo(5));
            Assert.That(bundle.Weapons.Weapons.Count, Is.EqualTo(15));
            Assert.That(bundle.Skills.Skills.Count, Is.EqualTo(11));
            Assert.That(bundle.Scoring.PlacementPoints.Count, Is.EqualTo(12));
            Assert.That(bundle.SampleTeams.Count, Is.EqualTo(18));

            int totalPlayers = 0;
            foreach (var team in bundle.SampleTeams)
            {
                totalPlayers += team.Players.Count;
                Assert.That(team.Lineup.Starters.Count, Is.EqualTo(4));
            }
            Assert.That(totalPlayers, Is.EqualTo(90));
        }

        [Test]
        public void ConfigValidator_RejectsDuplicateActiveSkillsAndOutOfBoundsAttributes()
        {
            string configsDir = Path.Combine(GetRepoRoot(), "configs");
            ConfigBundle bundle = ConfigLoader.LoadFromDirectory(configsDir);

            // Introduce duplicate active skill in Team 0
            TeamDef team0 = bundle.SampleTeams[0];
            team0.Lineup.Starters[1].Active = team0.Lineup.Starters[0].Active;

            // Introduce out-of-range attribute
            team0.Players[0].Mechanical.Aim = 25f;

            List<string> errors = ConfigValidator.Validate(bundle);
            Assert.That(errors.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(errors, Has.Some.Contains("Duplicate active skill"));
            Assert.That(errors, Has.Some.Contains("out of range"));
        }

        [Test]
        public void ConfigValidator_RejectsMoreThanOneImportStarter()
        {
            string configsDir = Path.Combine(GetRepoRoot(), "configs");
            ConfigBundle bundle = ConfigLoader.LoadFromDirectory(configsDir);

            TeamDef team0 = bundle.SampleTeams[0];
            team0.Players[0].Region = "EU";
            team0.Players[1].Region = "NA";

            List<string> errors = new();
            ConfigValidator.ValidateTeamLineup(team0, bundle.Skills, errors);
            Assert.That(errors, Has.Some.Contains("At least 3 starters must belong to region"));
        }
    }
}
