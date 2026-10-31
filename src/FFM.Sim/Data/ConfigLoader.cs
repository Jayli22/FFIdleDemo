using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FFM.Sim.Core;

namespace FFM.Sim.Data
{
    public static class ConfigLoader
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true
        };

        public static ConfigBundle LoadFromDirectory(string configsDir)
        {
            if (!Directory.Exists(configsDir))
            {
                throw new DirectoryNotFoundException($"Configs directory not found: {configsDir}");
            }

            var bundle = new ConfigBundle
            {
                Attributes = LoadJsonFile<AttributesConfig>(Path.Combine(configsDir, "attributes.json")),
                Roles = LoadJsonFile<RolesConfig>(Path.Combine(configsDir, "roles.json")),
                Weapons = LoadJsonFile<WeaponsConfig>(Path.Combine(configsDir, "weapons.json")),
                Skills = LoadJsonFile<SkillsConfig>(Path.Combine(configsDir, "skills.json")),
                Scoring = LoadJsonFile<ScoringConfig>(Path.Combine(configsDir, "scoring.json")),
                Playbooks = LoadJsonFile<PlaybooksConfig>(Path.Combine(configsDir, "playbooks.json")),
                Training = LoadJsonFile<TrainingConfig>(Path.Combine(configsDir, "training.json")),
                Engagement = LoadJsonFile<EngagementBalanceConfig>(Path.Combine(configsDir, "balance", "engagement.json")),
                SquadAi = LoadJsonFile<SquadAiBalanceConfig>(Path.Combine(configsDir, "balance", "squad_ai.json")),
                UnitAi = LoadJsonFile<UnitAiBalanceConfig>(Path.Combine(configsDir, "balance", "unit_ai.json")),
                Commands = LoadJsonFile<CommandsBalanceConfig>(Path.Combine(configsDir, "balance", "commands.json")),
                Fatigue = LoadJsonFile<FatigueBalanceConfig>(Path.Combine(configsDir, "balance", "fatigue.json")),
                SplitTemplate = LoadJsonFile<SplitTemplateConfig>(Path.Combine(configsDir, "season", "split_template.json"))
            };

            var teamsFile = LoadJsonFile<TeamsFileConfig>(Path.Combine(configsDir, "teams", "sample18.json"));
            bundle.SampleTeams = teamsFile.Teams;

            ConfigValidator.ValidateOrThrow(bundle);
            return bundle;
        }

        public static T LoadJsonFile<T>(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"Required configuration file missing: {filePath}", filePath);
            }
            string json = File.ReadAllText(filePath);
            T? obj = JsonSerializer.Deserialize<T>(json, JsonOptions);
            if (obj == null)
            {
                throw new InvalidDataException($"Failed to deserialize configuration file: {filePath}");
            }
            return obj;
        }

        public static void SaveJsonFile<T>(string filePath, T data)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            string json = JsonSerializer.Serialize(data, JsonOptions);
            File.WriteAllText(filePath, json);
        }

        /// <summary>
        /// Deterministically generates the 18 sample teams (90 players, mean=11, sigma=3, 3 tiers)
        /// satisfying all §4.3 lineup rules.
        /// </summary>
        public static List<TeamDef> GenerateDeterministicSample18(ulong seed = 20261016UL)
        {
            var rng = new Rng(seed);
            string[] teamInfos = new[]
            {
                // Tier 1 (Strong: base mean ~13.8)
                "t_evos|EVOS Divine|SEA|#2274A5",
                "t_buriram|Buriram United|SEA|#003F88",
                "t_fluxo|Fluxo Esports|BR|#7B2CBF",
                "t_loud|LOUD GG|BR|#00CC66",
                "t_falcons|Team Falcons|MEA|#2EC4B6",
                "t_rrq|RRQ Kazu|SEA|#FF9F1C",
                // Tier 2 (Mid: base mean ~11.0)
                "t_onic|ONIC Olympus|SEA|#F7B801",
                "t_cggg|CGGG Thailand|SEA|#E71D36",
                "t_pain|paiN Gaming|BR|#D90429",
                "t_corinthians|Corinthians FF|BR|#8D99AE",
                "t_wag|WAG Esports|VN|#FB5607",
                "t_pbox|PBox Flash|VN|#FF006E",
                // Tier 3 (Developing: base mean ~8.2)
                "t_indostars|Indostars|SEA|#3A86FF",
                "t_attack|Attack All Around|SEA|#8338EC",
                "t_magic|Magic Squad|BR|#06D6A0",
                "t_allglory|All Glory Gaming|LATAM|#118AB2",
                "t_ignite|Ignite Esports|LATAM|#EF476F",
                "t_alpha|Alpha7 Lite|BR|#8AC926"
            };

            string[] playerAliases = new[]
            {
                "Ryo", "Geday", "AimGod", "Bion", "AbaaaX", "Wassana", "Moshi", "Joena", "GETHIGH", "TheCruz",
                "Bops", "Butazin", "Giuh", "MT7", "Syaz", "Cauan7", "Lost21", "Noda", "Draxx", "Kroonos",
                "Cobra", "Peter", "Razor", "Duties", "Lega", "Dutzz", "Maal", "wings", "Garfield", "Jars",
                "Pudin", "Tay", "JapaBKR", "VitinXP", "Nando9", "Ousado", "Razure", " Zen", "Kraken", "Zack",
                "Alaan", " Beowulf", "Hector", "R7", "Viper", "Kira", "Shadow", "Zero", "Ace", "Nova",
                "Blaze", "Storm", "Frost", "Bolt", "Hawk", "Wolf", "Lynx", "Bear", "Fox", "Crow",
                "jin", "Ken", "Ryu", "Kai", "Leo", "Max", "Sam", "Ray", "Jay", "Zed",
                "Ash", "Sol", "Lux", "Nox", "Vex", "Jax", "Pyke", "Sion", "Talon", "Ekko",
                "Kael", "Dante", "Vergil", "Nero", "Cloud", "Seph", "ZackF", "Tidus", "Auron", "Balth"
            };

            string[] activeSkills = new[] { "sk_dash", "sk_shield", "sk_scan", "sk_heal", "sk_deploy" };
            string[] passiveSkills = new[] { "sk_p_aim", "sk_p_move", "sk_p_armor", "sk_p_gloo", "sk_p_heal", "sk_p_loot" };
            Role[] starterRoles = new[] { Role.Rusher, Role.SecondRusher, Role.Sniper, Role.Support };
            string[] dropStyles = new[] { "Hot", "Flex", "Cold" };
            string[] rotStyles = new[] { "Early", "Edge", "Late" };
            string[] engageStyles = new[] { "Contest", "Balanced", "Avoid" };
            string[] endgameStyles = new[] { "HighGround", "Building", "GlooFort" };

            var teams = new List<TeamDef>(18);
            int playerCounter = 1;

            for (int t = 0; t < 18; t++)
            {
                string[] parts = teamInfos[t].Split('|');
                int tier = t / 6; // 0 = Strong, 1 = Mid, 2 = Developing
                float tierMean = tier == 0 ? 13.8f : (tier == 1 ? 11.0f : 8.2f);
                float synergy = tier == 0 ? rng.NextInt(74, 88) : (tier == 1 ? rng.NextInt(58, 72) : rng.NextInt(42, 56));

                var team = new TeamDef
                {
                    TeamId = parts[0],
                    Name = parts[1],
                    Region = parts[2],
                    ColorHex = parts[3],
                    Synergy = synergy,
                    Playbook = new PlaybookDef
                    {
                        Drop = dropStyles[t % 3],
                        Rotation = rotStyles[(t / 2) % 3],
                        Engage = engageStyles[tier == 0 ? (t % 2) : (tier == 2 ? 2 : (t % 3))],
                        Endgame = endgameStyles[t % 3]
                    }
                };

                for (int pIdx = 0; pIdx < 5; pIdx++)
                {
                    string pid = $"p_{playerCounter:D3}";
                    string pname = playerAliases[(playerCounter - 1) % playerAliases.Length].Trim();
                    playerCounter++;

                    Role primaryRole = pIdx < 4 ? starterRoles[pIdx] : Role.Grenadier;

                    float SampleAttr(float roleBonus = 0f)
                    {
                        float val = rng.NextGaussian(tierMean + roleBonus, 2.0f);
                        return MathF.Round(Math.Clamp(val, 2f, 19f), 1);
                    }

                    var player = new PlayerDef
                    {
                        PlayerId = pid,
                        Name = pname,
                        Age = rng.NextInt(17, 26),
                        Region = parts[2],
                        Mechanical = new MechanicalAttributes
                        {
                            Aim = SampleAttr(),
                            Recoil = SampleAttr(),
                            Reflex = SampleAttr(primaryRole == Role.Rusher ? 1.5f : 0f),
                            Movement = SampleAttr(primaryRole == Role.Rusher ? 1.2f : 0f),
                            Cqc = SampleAttr(primaryRole == Role.Rusher || primaryRole == Role.SecondRusher ? 1.5f : -0.5f),
                            LongRange = SampleAttr(primaryRole == Role.Sniper ? 2.0f : 0f)
                        },
                        Tactical = new TacticalAttributes
                        {
                            Awareness = SampleAttr(),
                            Positioning = SampleAttr(),
                            Utility = SampleAttr(primaryRole == Role.Support || primaryRole == Role.Grenadier ? 1.5f : 0f),
                            Looting = SampleAttr(),
                            Decision = SampleAttr(pIdx == 3 ? 1.5f : 0f),
                            Comms = SampleAttr(pIdx == 3 ? 1.5f : 0f)
                        },
                        Mental = new MentalAttributes
                        {
                            Composure = SampleAttr(),
                            Teamwork = SampleAttr(primaryRole == Role.SecondRusher || primaryRole == Role.Support ? 1.2f : 0f),
                            Determination = SampleAttr()
                        },
                        Hidden = new HiddenAttributes
                        {
                            Consistency = SampleAttr(),
                            BigMatch = SampleAttr(),
                            InjuryProne = MathF.Round(rng.NextFloat(2f, 12f), 1)
                        },
                        Condition = new PlayerCondition
                        {
                            Stamina = SampleAttr(),
                            Form = 0f,
                            Fatigue = 0f
                        }
                    };

                    foreach (Role r in Enum.GetValues<Role>())
                    {
                        float prof = (r == primaryRole) ? Math.Clamp(tierMean + 3f, 8f, 19f) : Math.Clamp(tierMean - 3f, 4f, 14f);
                        player.RoleProficiency[r.ToString()] = MathF.Round(prof, 1);
                    }

                    for (int s = 0; s < activeSkills.Length; s++)
                    {
                        float sProf = (s == pIdx) ? Math.Clamp(tierMean + 2.5f, 8f, 19f) : Math.Clamp(tierMean - 2f, 5f, 15f);
                        player.SkillProficiency[activeSkills[s]] = MathF.Round(sProf, 1);
                    }

                    team.Players.Add(player);

                    if (pIdx < 4)
                    {
                        team.Lineup.Starters.Add(new LineupStarterSlot
                        {
                            PlayerId = pid,
                            Role = primaryRole.ToString(),
                            Active = activeSkills[pIdx],
                            Passives = new List<string>
                            {
                                passiveSkills[(pIdx * 2) % passiveSkills.Length],
                                passiveSkills[(pIdx * 2 + 1) % passiveSkills.Length],
                                passiveSkills[(pIdx * 2 + 2) % passiveSkills.Length]
                            }
                        });
                    }
                    else
                    {
                        team.Lineup.Sub = pid;
                    }
                }

                // Support (4th starter) is IGL by default
                team.Lineup.Igl = team.Lineup.Starters[3].PlayerId;
                teams.Add(team);
            }

            return teams;
        }
    }
}
