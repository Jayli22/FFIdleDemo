using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FFM.Sim.Data
{
    public sealed class MechanicalAttributes
    {
        [JsonPropertyName("aim")] public float Aim { get; set; } = 10f;
        [JsonPropertyName("recoil")] public float Recoil { get; set; } = 10f;
        [JsonPropertyName("reflex")] public float Reflex { get; set; } = 10f;
        [JsonPropertyName("movement")] public float Movement { get; set; } = 10f;
        [JsonPropertyName("cqc")] public float Cqc { get; set; } = 10f;
        [JsonPropertyName("longRange")] public float LongRange { get; set; } = 10f;

        public MechanicalAttributes Clone() => new MechanicalAttributes
        {
            Aim = Aim,
            Recoil = Recoil,
            Reflex = Reflex,
            Movement = Movement,
            Cqc = Cqc,
            LongRange = LongRange
        };
    }

    public sealed class TacticalAttributes
    {
        [JsonPropertyName("awareness")] public float Awareness { get; set; } = 10f;
        [JsonPropertyName("positioning")] public float Positioning { get; set; } = 10f;
        [JsonPropertyName("utility")] public float Utility { get; set; } = 10f;
        [JsonPropertyName("looting")] public float Looting { get; set; } = 10f;
        [JsonPropertyName("decision")] public float Decision { get; set; } = 10f;
        [JsonPropertyName("comms")] public float Comms { get; set; } = 10f;

        public TacticalAttributes Clone() => new TacticalAttributes
        {
            Awareness = Awareness,
            Positioning = Positioning,
            Utility = Utility,
            Looting = Looting,
            Decision = Decision,
            Comms = Comms
        };
    }

    public sealed class MentalAttributes
    {
        [JsonPropertyName("composure")] public float Composure { get; set; } = 10f;
        [JsonPropertyName("teamwork")] public float Teamwork { get; set; } = 10f;
        [JsonPropertyName("determination")] public float Determination { get; set; } = 10f;

        public MentalAttributes Clone() => new MentalAttributes
        {
            Composure = Composure,
            Teamwork = Teamwork,
            Determination = Determination
        };
    }

    public sealed class HiddenAttributes
    {
        [JsonPropertyName("consistency")] public float Consistency { get; set; } = 10f;
        [JsonPropertyName("bigMatch")] public float BigMatch { get; set; } = 10f;
        [JsonPropertyName("injuryProne")] public float InjuryProne { get; set; } = 5f;

        public HiddenAttributes Clone() => new HiddenAttributes
        {
            Consistency = Consistency,
            BigMatch = BigMatch,
            InjuryProne = InjuryProne
        };
    }

    public sealed class PlayerCondition
    {
        [JsonPropertyName("stamina")] public float Stamina { get; set; } = 12f;
        [JsonPropertyName("form")] public float Form { get; set; } = 0f;
        [JsonPropertyName("fatigue")] public float Fatigue { get; set; } = 0f;
        [JsonIgnore] public bool AutoRestThreshold { get; set; } = true;

        public PlayerCondition Clone() => new PlayerCondition
        {
            Stamina = Stamina,
            Form = Form,
            Fatigue = Fatigue,
            AutoRestThreshold = AutoRestThreshold
        };
    }

    public sealed class PlayerDef
    {
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonIgnore] public string Nickname => Name;
        [JsonPropertyName("age")] public int Age { get; set; } = 19;
        [JsonPropertyName("region")] public string Region { get; set; } = "SEA";
        [JsonPropertyName("mechanical")] public MechanicalAttributes Mechanical { get; set; } = new();
        [JsonPropertyName("tactical")] public TacticalAttributes Tactical { get; set; } = new();
        [JsonPropertyName("mental")] public MentalAttributes Mental { get; set; } = new();
        [JsonPropertyName("hidden")] public HiddenAttributes Hidden { get; set; } = new();
        [JsonPropertyName("condition")] public PlayerCondition Condition { get; set; } = new();
        [JsonPropertyName("roleProficiency")] public Dictionary<string, float> RoleProficiency { get; set; } = new();
        [JsonPropertyName("skillProficiency")] public Dictionary<string, float> SkillProficiency { get; set; } = new();

        [JsonIgnore] public int Level { get => this.GetProgression().Level; set => this.GetProgression().Level = value; }
        [JsonIgnore] public int StarRating { get => this.GetProgression().StarTier; set => this.GetProgression().StarTier = value; }
        [JsonIgnore] public float Potential { get => this.GetProgression().PotentialRating; set => this.GetProgression().PotentialRating = value; }
        [JsonIgnore] public int CurrentXp { get => this.GetProgression().Xp; set => this.GetProgression().Xp = value; }
        [JsonIgnore] public List<string> SignatureTraits => this.GetProgression().SignatureTraitIds;
        [JsonIgnore] public WeaponMasteryBlock WeaponMastery => new(this.GetProgression().WeaponMastery);

        public int GetMaxLevelForStar() => PlayerProgressionData.GetStarBreakRequiredLevel(StarRating);
        public float GetEffectiveAttributeCap() => MathF.Min(Potential, PlayerProgressionData.GetStarAttributeCap(StarRating));
        public int GetXpRequiredForNextLevel() => PlayerProgressionData.GetRequiredXpForNextLevel(Level);

        public float GetRoleProficiency(Role role)
        {
            string key = role.ToString();
            return RoleProficiency.TryGetValue(key, out float val) ? val : 8f;
        }

        public float GetSkillProficiency(string skillId)
        {
            if (string.IsNullOrEmpty(skillId)) return 8f;
            return SkillProficiency.TryGetValue(skillId, out float val) ? val : 8f;
        }

        /// <summary>
        /// Calculates the true average combat/tactical rating (1..20 scale) for headless evaluation & Spearman correlation.
        /// Note: Never exposed as a single power number in client UI (§1.2).
        /// </summary>
        public float CalculateTrueRating()
        {
            float mech = (Mechanical.Aim + Mechanical.Recoil + Mechanical.Reflex + Mechanical.Movement + Mechanical.Cqc + Mechanical.LongRange) / 6f;
            float tact = (Tactical.Awareness + Tactical.Positioning + Tactical.Utility + Tactical.Looting + Tactical.Decision + Tactical.Comms) / 6f;
            float ment = (Mental.Composure + Mental.Teamwork + Hidden.Consistency + Condition.Stamina) / 4f;
            return mech * 0.45f + tact * 0.35f + ment * 0.20f;
        }

        public PlayerDef Clone()
        {
            var copy = new PlayerDef
            {
                PlayerId = PlayerId,
                Name = Name,
                Age = Age,
                Region = Region,
                Mechanical = Mechanical.Clone(),
                Tactical = Tactical.Clone(),
                Mental = Mental.Clone(),
                Hidden = Hidden.Clone(),
                Condition = Condition.Clone(),
                RoleProficiency = new Dictionary<string, float>(RoleProficiency),
                SkillProficiency = new Dictionary<string, float>(SkillProficiency)
            };
            return copy;
        }
    }

    public sealed class LineupStarterSlot
    {
        [JsonPropertyName("playerId")] public string PlayerId { get; set; } = string.Empty;
        [JsonPropertyName("role")] public string Role { get; set; } = "Rusher";
        [JsonPropertyName("active")] public string Active { get; set; } = "sk_dash";
        [JsonPropertyName("passives")] public List<string> Passives { get; set; } = new();

        public Role GetRoleEnum() => Enum.TryParse<Role>(Role, true, out var r) ? r : Data.Role.Rusher;

        public LineupStarterSlot Clone() => new LineupStarterSlot
        {
            PlayerId = PlayerId,
            Role = Role,
            Active = Active,
            Passives = new List<string>(Passives)
        };
    }

    public sealed class LineupDef
    {
        [JsonPropertyName("starters")] public List<LineupStarterSlot> Starters { get; set; } = new();
        [JsonPropertyName("sub")] public string? Sub { get; set; }
        [JsonPropertyName("igl")] public string Igl { get; set; } = string.Empty;

        public LineupDef Clone()
        {
            var copy = new LineupDef
            {
                Sub = Sub,
                Igl = Igl
            };
            foreach (var s in Starters)
            {
                copy.Starters.Add(s.Clone());
            }
            return copy;
        }
    }

    public sealed class PlaybookDef
    {
        [JsonPropertyName("drop")] public string Drop { get; set; } = "Flex";
        [JsonPropertyName("rotation")] public string Rotation { get; set; } = "Early";
        [JsonPropertyName("engage")] public string Engage { get; set; } = "Balanced";
        [JsonPropertyName("endgame")] public string Endgame { get; set; } = "HighGround";
        [JsonPropertyName("primaryPoiId")] public int PrimaryPoiId { get; set; }
        [JsonPropertyName("backupPoiId")] public int BackupPoiId { get; set; }

        public DropStyle GetDropStyle() => Enum.TryParse<DropStyle>(Drop, true, out var v) ? v : DropStyle.Flex;
        public RotationStyle GetRotationStyle() => Enum.TryParse<RotationStyle>(Rotation, true, out var v) ? v : RotationStyle.Early;
        public EngageStyle GetEngageStyle() => Enum.TryParse<EngageStyle>(Engage, true, out var v) ? v : EngageStyle.Balanced;
        public EndgameStyle GetEndgameStyle() => Enum.TryParse<EndgameStyle>(Endgame, true, out var v) ? v : EndgameStyle.HighGround;

        public PlaybookDef Clone() => new PlaybookDef
        {
            Drop = Drop,
            Rotation = Rotation,
            Engage = Engage,
            Endgame = Endgame,
            PrimaryPoiId = PrimaryPoiId,
            BackupPoiId = BackupPoiId
        };
    }

    public sealed class TeamDef
    {
        [JsonPropertyName("teamId")] public string TeamId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("region")] public string Region { get; set; } = "SEA";
        [JsonPropertyName("colorHex")] public string ColorHex { get; set; } = "#FF5500";
        [JsonPropertyName("lineup")] public LineupDef Lineup { get; set; } = new();
        [JsonPropertyName("playbook")] public PlaybookDef Playbook { get; set; } = new();
        [JsonPropertyName("synergy")] public float Synergy { get; set; } = 60f;
        [JsonPropertyName("players")] public List<PlayerDef> Players { get; set; } = new();

        public PlayerDef? FindPlayer(string playerId)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].PlayerId == playerId)
                {
                    return Players[i];
                }
            }
            return null;
        }

        public float CalculateStarterRating()
        {
            if (Lineup.Starters.Count == 0) return 10f;
            float sum = 0f;
            int count = 0;
            for (int i = 0; i < Lineup.Starters.Count; i++)
            {
                var p = FindPlayer(Lineup.Starters[i].PlayerId);
                if (p != null)
                {
                    sum += p.CalculateTrueRating();
                    count++;
                }
            }
            return count > 0 ? (sum / count) + (Synergy - 50f) * 0.03f : 10f;
        }

        public TeamDef Clone()
        {
            var copy = new TeamDef
            {
                TeamId = TeamId,
                Name = Name,
                Region = Region,
                ColorHex = ColorHex,
                Lineup = Lineup.Clone(),
                Playbook = Playbook.Clone(),
                Synergy = Synergy
            };
            for (int i = 0; i < Players.Count; i++)
            {
                copy.Players.Add(Players[i].Clone());
            }
            return copy;
        }
    }

    public sealed class TeamsFileConfig
    {
        [JsonPropertyName("teams")] public List<TeamDef> Teams { get; set; } = new();
    }
}
