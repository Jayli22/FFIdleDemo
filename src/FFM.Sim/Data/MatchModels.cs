using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using FFM.Sim.Core;

namespace FFM.Sim.Data
{
    public sealed class WeaponMasteryBlock
    {
        private readonly Dictionary<string, float> _dict;

        public WeaponMasteryBlock(Dictionary<string, float> dict)
        {
            _dict = dict ?? new Dictionary<string, float>();
        }

        public float Shotgun => Get("SG");
        public float Smg => Get("SMG");
        public float Ar => Get("AR");
        public float Dmr => Get("DMR");
        public float Sr => Get("SR");

        private float Get(string key) => _dict.TryGetValue(key, out float val) ? val : 25f;

        public static int GetMasteryTier(float points) => PlayerProgressionData.GetWeaponMasteryTier(points);
    }

    public static class RoleProficiencyExtensions
    {
        public static float GetProficiency(this Dictionary<string, float> dict, Role role)
        {
            if (dict != null && dict.TryGetValue(role.ToString(), out float v))
            {
                return v <= 20f ? v * 5f : v;
            }
            return 60f;
        }
    }

    public sealed class SignatureTraitDef
    {
        public string Id { get; init; } = string.Empty;
        public string TraitId => Id;
        public string NameCn { get; init; } = string.Empty;
        public string Name => NameCn;
        public string NameEn { get; init; } = string.Empty;
        public string DescCn { get; init; } = string.Empty;
        public int UnlockStarTier { get; init; } = 2;
    }

    public static class SignatureTraitCatalog
    {
        public static readonly IReadOnlyList<SignatureTraitDef> All = new List<SignatureTraitDef>
        {
            new() { Id = "rusher_entry_king", NameCn = "破点王", NameEn = "Entry King", DescCn = "近战(≤15m)首轮命中率+12%", UnlockStarTier = 2 },
            new() { Id = "igl_sky_eye", NameCn = "天眼指挥", NameEn = "Sky Eye IGL", DescCn = "跳伞侦察偏航与刷圈预判+18%", UnlockStarTier = 2 },
            new() { Id = "gloo_architect", NameCn = "冰墙大师", NameEn = "Gloo Architect", DescCn = "开局额外+1冰墙，冰墙耐久+15%", UnlockStarTier = 2 },
            new() { Id = "headshot_machine", NameCn = "拉枪机器", NameEn = "One-Tap God", DescCn = "拉枪爆头触发概率+6%", UnlockStarTier = 3 },
            new() { Id = "zone_survivor", NameCn = "抗毒专家", NameEn = "Blue Zone Runner", DescCn = "电网毒圈承受伤害-20%", UnlockStarTier = 3 },
            new() { Id = "field_medic", NameCn = "妙手支援", NameEn = "Miracle Medic", DescCn = "扶人速度+25%，扶起额外回复30 HP", UnlockStarTier = 3 },
            new() { Id = "clutch_gene", NameCn = "大心脏", NameEn = "Big Stage Heart", DescCn = "残局/决赛抗压+2.5，决赛绝不低迷", UnlockStarTier = 5 },
            new() { Id = "demolition_expert", NameCn = "灌雷宗师", NameEn = "Nade King", DescCn = "开局额外+1手雷，爆炸半径+12%", UnlockStarTier = 5 }
        };

        public static SignatureTraitDef? Find(string id)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Id == id) return All[i];
            }
            return null;
        }

        public static (string title, string desc) GetTraitDisplay(string id)
        {
            var def = Find(id);
            if (def != null)
            {
                return ($"{def.NameCn} ({def.NameEn})", def.DescCn);
            }
            return (id, "Signature Trait");
        }

        public static void EnsureStarterTraits(PlayerDef player)
        {
            if (player == null) return;
            _ = player.GetProgression();
        }
    }

    public sealed class PlayerProgressionData
    {
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonPropertyName("level")] public int Level { get; set; } = 1;
        [JsonPropertyName("xp")] public int Xp { get; set; }
        [JsonPropertyName("starTier")] public int StarTier { get; set; } = 1;
        [JsonPropertyName("potentialRating")] public float PotentialRating { get; set; } = 16.5f;
        [JsonPropertyName("idleProgress01")] public float IdleProgress01 { get; set; }
        [JsonPropertyName("autoRestEnabled")] public bool AutoRestEnabled { get; set; } = true;
        [JsonPropertyName("isAutoResting")] public bool IsAutoResting { get; set; }

        [JsonIgnore]
        public bool IsCurrentlyResting
        {
            get => IsAutoResting;
            set => IsAutoResting = value;
        }

        [JsonIgnore]
        public int XpToNextLevel => GetRequiredXpForNextLevel(Level);

        [JsonPropertyName("weaponMastery")] public Dictionary<string, float> WeaponMastery { get; set; } = new()
        {
            ["SG"] = 25f,
            ["SMG"] = 25f,
            ["AR"] = 25f,
            ["DMR"] = 25f,
            ["SR"] = 25f
        };
        [JsonPropertyName("signatureTraitIds")] public List<string> SignatureTraitIds { get; set; } = new();

        public PlayerProgressionData Clone()
        {
            return new PlayerProgressionData
            {
                PlayerId = PlayerId,
                Level = Level,
                Xp = Xp,
                StarTier = StarTier,
                PotentialRating = PotentialRating,
                IdleProgress01 = IdleProgress01,
                AutoRestEnabled = AutoRestEnabled,
                IsAutoResting = IsAutoResting,
                WeaponMastery = new Dictionary<string, float>(WeaponMastery),
                SignatureTraitIds = new List<string>(SignatureTraitIds)
            };
        }

        public static int GetWeaponMasteryTier(float points)
        {
            if (points >= 85f) return 3;
            if (points >= 60f) return 2;
            if (points >= 30f) return 1;
            return 0;
        }

        public static string GetWeaponMasteryTierLabel(int tier) => tier switch
        {
            3 => "宗师 GM",
            2 => "专精 EXP",
            1 => "熟练 PRO",
            _ => "新手 NOV"
        };

        public static float GetStarAttributeCap(int starTier) => starTier switch
        {
            1 => 14.5f,
            2 => 16.0f,
            3 => 17.5f,
            4 => 19.0f,
            _ => 20.0f
        };

        public static int GetRequiredXpForNextLevel(int currentLevel) => 100 + Math.Max(1, currentLevel) * 65;

        public static int GetStarBreakRequiredLevel(int starTier) => Math.Clamp(starTier, 1, 5) * 6;

        public static int GetStarBreakCashCost(int starTier) => Math.Clamp(starTier, 1, 5) * 15000;

        public static int GetStarBreakTacticalPointsCost(int starTier) => Math.Clamp(starTier, 1, 5) * 30;
    }

    public static class PlayerProgressionExtensions
    {
        private static readonly Dictionary<string, PlayerProgressionData> Registry = new();

        public static PlayerProgressionData GetProgression(this PlayerDef player)
        {
            if (player == null) return new PlayerProgressionData();
            string key = string.IsNullOrEmpty(player.PlayerId) ? player.Nickname : player.PlayerId;
            if (!string.IsNullOrEmpty(key) && Registry.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var created = CreateDefaultProgression(player);
            if (!string.IsNullOrEmpty(key))
            {
                Registry[key] = created;
            }
            return created;
        }

        public static void SetProgression(string playerId, PlayerProgressionData data)
        {
            if (string.IsNullOrEmpty(playerId) || data == null) return;
            data.PlayerId = playerId;
            Registry[playerId] = data;
        }

        public static Dictionary<string, PlayerProgressionData> ExportAll()
        {
            var result = new Dictionary<string, PlayerProgressionData>();
            foreach (var kv in Registry)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                {
                    result[kv.Key] = kv.Value.Clone();
                }
            }
            return result;
        }

        public static Dictionary<string, PlayerProgressionData> ExportAll(IEnumerable<PlayerDef> players)
        {
            var result = new Dictionary<string, PlayerProgressionData>();
            foreach (var p in players)
            {
                if (p == null || string.IsNullOrEmpty(p.PlayerId)) continue;
                result[p.PlayerId] = p.GetProgression().Clone();
            }
            return result;
        }

        public static void ImportAll(Dictionary<string, PlayerProgressionData>? map)
        {
            if (map == null) return;
            foreach (var kv in map)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Value != null)
                {
                    kv.Value.PlayerId = kv.Key;
                    Registry[kv.Key] = kv.Value;
                }
            }
        }

        public static PlayerProgressionData CreateDefaultProgression(PlayerDef p)
        {
            float baseRating = p.CalculateTrueRating();
            int level = Math.Clamp((int)MathF.Round((baseRating - 9.5f) * 2.2f), 1, 18);
            int starTier = baseRating >= 14.6f ? 3 : (baseRating >= 12.4f ? 2 : 1);
            int age = Math.Clamp(p.Age, 16, 28);
            float potential = Math.Clamp(baseRating + (24 - age) * 0.35f + 1.8f, 14.5f, 19.6f);
            float starCap = PlayerProgressionData.GetStarAttributeCap(starTier);
            if (potential < starCap) potential = Math.Min(19.6f, starCap + 0.5f);

            string bestRole = "Rusher";
            float bestRoleVal = 0f;
            foreach (var kv in p.RoleProficiency)
            {
                if (kv.Value > bestRoleVal)
                {
                    bestRoleVal = kv.Value;
                    bestRole = kv.Key;
                }
            }

            float sg = 28f, smg = 32f, ar = 36f, dmr = 28f, sr = 24f;
            switch (bestRole)
            {
                case "Rusher":
                    sg = 64f; smg = 58f; ar = 42f;
                    break;
                case "SecondRusher":
                    smg = 62f; ar = 56f; dmr = 44f;
                    break;
                case "Grenadier":
                    ar = 60f; smg = 48f; sg = 45f;
                    break;
                case "Sniper":
                    sr = 68f; dmr = 58f; smg = 38f;
                    break;
                case "Support":
                    ar = 58f; dmr = 54f; smg = 46f;
                    break;
            }

            var data = new PlayerProgressionData
            {
                PlayerId = p.PlayerId,
                Level = level,
                Xp = (level * 37) % PlayerProgressionData.GetRequiredXpForNextLevel(level),
                StarTier = starTier,
                PotentialRating = potential,
                IdleProgress01 = 0.15f,
                AutoRestEnabled = true,
                IsAutoResting = false,
                WeaponMastery = new Dictionary<string, float>
                {
                    ["SG"] = sg,
                    ["SMG"] = smg,
                    ["AR"] = ar,
                    ["DMR"] = dmr,
                    ["SR"] = sr
                }
            };

            if (starTier >= 2)
            {
                string t1 = bestRole switch
                {
                    "Rusher" => "rusher_entry_king",
                    "Sniper" => "headshot_machine",
                    "Support" => "igl_sky_eye",
                    "Grenadier" => "gloo_architect",
                    _ => "gloo_architect"
                };
                data.SignatureTraitIds.Add(t1);
            }
            if (starTier >= 3)
            {
                string t2 = bestRole switch
                {
                    "Support" => "field_medic",
                    "Rusher" => "headshot_machine",
                    _ => "zone_survivor"
                };
                if (!data.SignatureTraitIds.Contains(t2))
                {
                    data.SignatureTraitIds.Add(t2);
                }
            }

            return data;
        }
    }


    public sealed class SquadRuntimePlayerSlot
    {
        [JsonPropertyName("player")] public PlayerDef Player { get; set; } = new();
        [JsonPropertyName("role")] public Role Role { get; set; } = Role.Rusher;
        [JsonPropertyName("activeSkillId")] public string ActiveSkillId { get; set; } = "sk_dash";
        [JsonPropertyName("passiveSkillIds")] public List<string> PassiveSkillIds { get; set; } = new();
        [JsonPropertyName("isIgl")] public bool IsIgl { get; set; }
        [JsonPropertyName("progression")] public PlayerProgressionData? Progression { get; set; }
    }

    public sealed class SquadDef
    {
        [JsonPropertyName("squadId")] public string SquadId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("colorHex")] public string ColorHex { get; set; } = "#FF5500";
        [JsonPropertyName("synergy")] public float Synergy { get; set; } = 60f;
        [JsonPropertyName("playbook")] public PlaybookDef Playbook { get; set; } = new();
        [JsonPropertyName("members")] public List<SquadRuntimePlayerSlot> Members { get; set; } = new();

        public static SquadDef FromTeamDef(TeamDef team)
        {
            var sq = new SquadDef
            {
                SquadId = team.TeamId,
                Name = team.Name,
                ColorHex = team.ColorHex,
                Synergy = team.Synergy,
                Playbook = team.Playbook.Clone()
            };

            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                var slot = team.Lineup.Starters[i];
                var p = team.FindPlayer(slot.PlayerId);
                if (p == null) continue;

                sq.Members.Add(new SquadRuntimePlayerSlot
                {
                    Player = p.Clone(),
                    Role = slot.GetRoleEnum(),
                    ActiveSkillId = slot.Active,
                    PassiveSkillIds = new List<string>(slot.Passives),
                    IsIgl = slot.PlayerId == team.Lineup.Igl,
                    Progression = p.GetProgression().Clone()
                });
            }

            if (sq.Members.Count > 0)
            {
                bool hasIgl = false;
                for (int i = 0; i < sq.Members.Count; i++)
                {
                    if (sq.Members[i].IsIgl) { hasIgl = true; break; }
                }
                if (!hasIgl) sq.Members[0].IsIgl = true;
            }

            return sq;
        }
    }

    public sealed class ChampionRushContextDef
    {
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("threshold")] public int Threshold { get; set; } = 80;
        [JsonPropertyName("eligible")] public List<string> Eligible { get; set; } = new();
    }

    public sealed class MatchContextDef
    {
        [JsonPropertyName("matchIndexOfDay")] public int MatchIndexOfDay { get; set; } = 1;
        [JsonPropertyName("isFinals")] public bool IsFinals { get; set; }
        [JsonPropertyName("standings")] public Dictionary<string, int> Standings { get; set; } = new();
        [JsonPropertyName("championRush")] public ChampionRushContextDef ChampionRush { get; set; } = new();
    }

    public sealed class MatchConfig
    {
        [JsonPropertyName("seed")] public ulong Seed { get; set; } = 123456UL;
        [JsonPropertyName("mapId")] public string MapId { get; set; } = "bermuda_lite";
        [JsonPropertyName("tickHz")] public int TickHz { get; set; } = 10;
        [JsonPropertyName("squads")] public List<SquadDef> Squads { get; set; } = new();
        [JsonPropertyName("playerSquadId")] public string PlayerSquadId { get; set; } = "t_evos";
        [JsonPropertyName("context")] public MatchContextDef Context { get; set; } = new();
        [JsonPropertyName("fatigueIn")] public Dictionary<string, float> FatigueIn { get; set; } = new();
    }

    public readonly struct Command
    {
        public readonly CommandType Type;
        public readonly EngageStyle EngageParam;
        public readonly TargetPriorityMode PriorityParam;
        public readonly bool BoolParam;
        public readonly EndgameStyle EndgameParam;

        public Command(
            CommandType type,
            EngageStyle engageParam = EngageStyle.Balanced,
            TargetPriorityMode priorityParam = TargetPriorityMode.Weakest,
            bool boolParam = true,
            EndgameStyle endgameParam = EndgameStyle.HighGround)
        {
            Type = type;
            EngageParam = engageParam;
            PriorityParam = priorityParam;
            BoolParam = boolParam;
            EndgameParam = endgameParam;
        }
    }

    public readonly struct TacticalCommand
    {
        public readonly int SquadIndex;
        public readonly CommandType Type;
        public readonly Vec2 TargetPosition;
        public readonly int TargetSquadIndex;
        public readonly EngageStyle EngageParam;
        public readonly EndgameStyle EndgameParam;

        public TacticalCommand(
            int squadIndex,
            CommandType type,
            Vec2 targetPosition = default,
            int targetSquadIndex = -1,
            EngageStyle engageParam = EngageStyle.Balanced,
            EndgameStyle endgameParam = EndgameStyle.HighGround)
        {
            SquadIndex = squadIndex;
            Type = type;
            TargetPosition = targetPosition;
            TargetSquadIndex = targetSquadIndex;
            EngageParam = engageParam;
            EndgameParam = endgameParam;
        }
    }

    public sealed class SquadPlacementResult
    {
        [JsonPropertyName("squadId")] public string SquadId { get; set; } = string.Empty;
        [JsonPropertyName("placement")] public int Placement { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("placementPoints")] public int PlacementPoints { get; set; }
        [JsonPropertyName("points")] public int Points { get; set; }
        [JsonPropertyName("booyah")] public bool Booyah { get; set; }
    }

    public sealed class PlayerMatchStats
    {
        [JsonPropertyName("dmg")] public int Dmg { get; set; }
        [JsonPropertyName("knocks")] public int Knocks { get; set; }
        [JsonPropertyName("kills")] public int Kills { get; set; }
        [JsonPropertyName("revives")] public int Revives { get; set; }
        [JsonPropertyName("survivedSec")] public int SurvivedSec { get; set; }
        [JsonPropertyName("gloo")] public int Gloo { get; set; }
        [JsonPropertyName("grenades")] public int Grenades { get; set; }
        [JsonPropertyName("skillUses")] public int SkillUses { get; set; }
        [JsonPropertyName("offDay")] public bool OffDay { get; set; }
        [JsonPropertyName("fatigueOut")] public float FatigueOut { get; set; }
    }

    public sealed class KeyMomentDto
    {
        [JsonPropertyName("tick")] public int Tick { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("squadId")] public string SquadId { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }

    public sealed class ChampionRushResultDto
    {
        [JsonPropertyName("newlyEligible")] public List<string> NewlyEligible { get; set; } = new();
        [JsonPropertyName("crowned")] public string? Crowned { get; set; }
    }

    public sealed class MatchResult
    {
        [JsonPropertyName("seed")] public ulong Seed { get; set; }
        [JsonPropertyName("durationSec")] public float DurationSec { get; set; }
        [JsonPropertyName("eventHash")] public ulong EventHash { get; set; }
        [JsonPropertyName("placements")] public List<SquadPlacementResult> Placements { get; set; } = new();
        [JsonPropertyName("playerStats")] public Dictionary<string, PlayerMatchStats> PlayerStats { get; set; } = new();
        [JsonPropertyName("keyMoments")] public List<KeyMomentDto> KeyMoments { get; set; } = new();
        [JsonPropertyName("championRush")] public ChampionRushResultDto ChampionRush { get; set; } = new();
    }
}
