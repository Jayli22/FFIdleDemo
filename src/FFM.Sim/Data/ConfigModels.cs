using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FFM.Sim.Data
{
    public sealed class AttributeMetaDef
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("group")] public string Group { get; set; } = "mechanical";
        [JsonPropertyName("min")] public float Min { get; set; } = 1f;
        [JsonPropertyName("max")] public float Max { get; set; } = 20f;
        [JsonPropertyName("visibleToPlayer")] public bool VisibleToPlayer { get; set; } = true;
        [JsonPropertyName("dominantScenario")] public string DominantScenario { get; set; } = string.Empty;
        [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    }

    public sealed class AttributesConfig
    {
        [JsonPropertyName("scaleMin")] public float ScaleMin { get; set; } = 1f;
        [JsonPropertyName("scaleMax")] public float ScaleMax { get; set; } = 20f;
        [JsonPropertyName("attributes")] public List<AttributeMetaDef> Attributes { get; set; } = new();
    }

    public sealed class RoleDef
    {
        [JsonPropertyName("role")] public string Role { get; set; } = "Rusher";
        [JsonPropertyName("forwardOffsetMeters")] public float ForwardOffsetMeters { get; set; } = 0f;
        [JsonPropertyName("sideOffsetMeters")] public float SideOffsetMeters { get; set; } = 0f;
        [JsonPropertyName("highGroundPreference")] public float HighGroundPreference { get; set; } = 0f;
        [JsonPropertyName("preferredWeaponCategory")] public string PreferredWeaponCategory { get; set; } = "AR";
        [JsonPropertyName("primaryAttributes")] public List<string> PrimaryAttributes { get; set; } = new();
    }

    public sealed class RolesConfig
    {
        [JsonPropertyName("roles")] public List<RoleDef> Roles { get; set; } = new();

        private RoleDef?[]? _roleCache;

        public RoleDef GetRoleDef(Role role)
        {
            int idx = (int)role;
            if (_roleCache != null && (uint)idx < (uint)_roleCache.Length && _roleCache[idx] != null)
            {
                return _roleCache[idx]!;
            }

            _roleCache ??= new RoleDef?[8];
            string name = role switch
            {
                Role.Rusher => "Rusher",
                Role.SecondRusher => "SecondRusher",
                Role.Sniper => "Sniper",
                Role.Support => "Support",
                Role.Grenadier => "Grenadier",
                _ => "Rusher"
            };

            for (int i = 0; i < Roles.Count; i++)
            {
                if (string.Equals(Roles[i].Role, name, StringComparison.OrdinalIgnoreCase))
                {
                    if ((uint)idx < (uint)_roleCache.Length) _roleCache[idx] = Roles[i];
                    return Roles[i];
                }
            }
            var fallback = Roles.Count > 0 ? Roles[0] : new RoleDef { Role = name };
            if ((uint)idx < (uint)_roleCache.Length) _roleCache[idx] = fallback;
            return fallback;
        }
    }

    public sealed class WeaponDef
    {
        [JsonPropertyName("weaponId")] public string WeaponId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("category")] public string Category { get; set; } = "AR";
        [JsonPropertyName("tier")] public int Tier { get; set; } = 1;
        [JsonPropertyName("damage")] public float Damage { get; set; } = 28f;
        [JsonPropertyName("rangeMeters")] public float RangeMeters { get; set; } = 90f;
        [JsonPropertyName("shotsPerRound")] public int ShotsPerRound { get; set; } = 3;
        [JsonPropertyName("accClose")] public float AccClose { get; set; } = 0.55f;
        [JsonPropertyName("accMid")] public float AccMid { get; set; } = 0.45f;
        [JsonPropertyName("accFar")] public float AccFar { get; set; } = 0.25f;

        private WeaponCategory? _cachedCategory;

        public WeaponCategory GetCategoryEnum()
        {
            if (_cachedCategory.HasValue) return _cachedCategory.Value;
            WeaponCategory cat = WeaponCategory.AR;
            if (string.Equals(Category, "SMG", StringComparison.OrdinalIgnoreCase)) cat = WeaponCategory.SMG;
            else if (string.Equals(Category, "SG", StringComparison.OrdinalIgnoreCase) || string.Equals(Category, "Shotgun", StringComparison.OrdinalIgnoreCase)) cat = WeaponCategory.Shotgun;
            else if (string.Equals(Category, "SR", StringComparison.OrdinalIgnoreCase) || string.Equals(Category, "Sniper", StringComparison.OrdinalIgnoreCase)) cat = WeaponCategory.Sniper;
            else if (string.Equals(Category, "DMR", StringComparison.OrdinalIgnoreCase)) cat = WeaponCategory.DMR;
            _cachedCategory = cat;
            return cat;
        }

        public float GetBaseAccuracy(float distMeters)
        {
            if (distMeters <= 25f) return AccClose;
            if (distMeters <= 70f)
            {
                float t = (distMeters - 25f) / 45f;
                return AccClose + (AccMid - AccClose) * t;
            }
            float tFar = Math.Clamp((distMeters - 70f) / Math.Max(20f, RangeMeters - 70f), 0f, 1f);
            return AccMid + (AccFar - AccMid) * tFar;
        }
    }

    public sealed class ThrowableConfigDef
    {
        [JsonPropertyName("minRangeMeters")] public float MinRangeMeters { get; set; } = 12f;
        [JsonPropertyName("maxRangeMeters")] public float MaxRangeMeters { get; set; } = 48f;
        [JsonPropertyName("blastRadiusMeters")] public float BlastRadiusMeters { get; set; } = 7f;
        [JsonPropertyName("baseDamage")] public float BaseDamage { get; set; } = 68f;
        [JsonPropertyName("glooDamageMultiplier")] public float GlooDamageMultiplier { get; set; } = 2.5f;
        [JsonPropertyName("baseTriggerChance")] public float BaseTriggerChance { get; set; } = 0.25f;
    }

    public sealed class WeaponsConfig
    {
        [JsonPropertyName("weapons")] public List<WeaponDef> Weapons { get; set; } = new();
        [JsonPropertyName("throwable")] public ThrowableConfigDef Throwable { get; set; } = new();
    }

    public sealed class SkillDef
    {
        [JsonPropertyName("skillId")] public string SkillId { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("isActive")] public bool IsActive { get; set; } = true;
        [JsonPropertyName("category")] public string Category { get; set; } = "Shield";
        [JsonPropertyName("tier")] public int Tier { get; set; } = 1;
        [JsonPropertyName("cooldownSec")] public float CooldownSec { get; set; } = 45f;
        [JsonPropertyName("durationSec")] public float DurationSec { get; set; } = 5f;
        [JsonPropertyName("effectValue")] public float EffectValue { get; set; } = 0.25f;
        [JsonPropertyName("bonusAttribute")] public string BonusAttribute { get; set; } = string.Empty;

        private SkillCategory? _cachedCategory;

        public SkillCategory GetCategoryEnum()
        {
            if (_cachedCategory.HasValue) return _cachedCategory.Value;
            SkillCategory cat = SkillCategory.Passive;
            if (string.Equals(Category, "Shield", StringComparison.OrdinalIgnoreCase)) cat = SkillCategory.Shield;
            else if (string.Equals(Category, "Dash", StringComparison.OrdinalIgnoreCase)) cat = SkillCategory.Dash;
            else if (string.Equals(Category, "Heal", StringComparison.OrdinalIgnoreCase) || string.Equals(Category, "HealAura", StringComparison.OrdinalIgnoreCase)) cat = SkillCategory.Heal;
            else if (string.Equals(Category, "Scan", StringComparison.OrdinalIgnoreCase)) cat = SkillCategory.Scan;
            else if (string.Equals(Category, "Deploy", StringComparison.OrdinalIgnoreCase) || string.Equals(Category, "Deployable", StringComparison.OrdinalIgnoreCase)) cat = SkillCategory.Deploy;
            _cachedCategory = cat;
            return cat;
        }
    }

    public sealed class SkillsConfig
    {
        [JsonPropertyName("skills")] public List<SkillDef> Skills { get; set; } = new();

        public SkillDef? FindSkill(string skillId)
        {
            for (int i = 0; i < Skills.Count; i++)
            {
                if (Skills[i].SkillId == skillId) return Skills[i];
            }
            return null;
        }
    }

    public sealed class ScoringConfig
    {
        [JsonPropertyName("placementPoints")] public List<int> PlacementPoints { get; set; } = new();
        [JsonPropertyName("killPoints")] public int KillPoints { get; set; } = 1;
        [JsonPropertyName("pointRushHeadstart")] public List<int> PointRushHeadstart { get; set; } = new();
        [JsonPropertyName("championRushThreshold")] public int ChampionRushThreshold { get; set; } = 80;
        [JsonPropertyName("championRushMaxMatches")] public int ChampionRushMaxMatches { get; set; } = 8;
        [JsonPropertyName("matchesPerDay")] public int MatchesPerDay { get; set; } = 6;

        public int GetPlacementPoints(int placement)
        {
            int idx = placement - 1;
            if (idx >= 0 && idx < PlacementPoints.Count)
            {
                return PlacementPoints[idx];
            }
            return 0;
        }
    }

    public sealed class PlaybookOptionMod
    {
        [JsonPropertyName("style")] public string Style { get; set; } = string.Empty;
        [JsonPropertyName("hotspotWeightMod")] public float HotspotWeightMod { get; set; } = 1f;
        [JsonPropertyName("earlyRotateSecOffset")] public float EarlyRotateSecOffset { get; set; } = 0f;
        [JsonPropertyName("aggressionMod")] public float AggressionMod { get; set; } = 1f;
        [JsonPropertyName("highGroundBias")] public float HighGroundBias { get; set; } = 1f;
        [JsonPropertyName("coverBias")] public float CoverBias { get; set; } = 1f;
    }

    public sealed class PlaybooksConfig
    {
        [JsonPropertyName("dropOptions")] public List<PlaybookOptionMod> DropOptions { get; set; } = new();
        [JsonPropertyName("rotationOptions")] public List<PlaybookOptionMod> RotationOptions { get; set; } = new();
        [JsonPropertyName("engageOptions")] public List<PlaybookOptionMod> EngageOptions { get; set; } = new();
        [JsonPropertyName("endgameOptions")] public List<PlaybookOptionMod> EndgameOptions { get; set; } = new();
    }

    public sealed class TrainingProgramDef
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("targetAttributes")] public List<string> TargetAttributes { get; set; } = new();
        [JsonPropertyName("growthPerHour")] public float GrowthPerHour { get; set; } = 0.018f;
        [JsonPropertyName("fatiguePerHour")] public float FatiguePerHour { get; set; } = 0.6f;
        [JsonPropertyName("synergyGainPerHour")] public float SynergyGainPerHour { get; set; } = 0f;
    }

    public sealed class TrainingConfig
    {
        [JsonPropertyName("weeklyHoursBudget")] public int WeeklyHoursBudget { get; set; } = 60;
        [JsonPropertyName("baseDeterminationDivisor")] public float BaseDeterminationDivisor { get; set; } = 40f;
        [JsonPropertyName("peakAgeMax")] public int PeakAgeMax { get; set; } = 21;
        [JsonPropertyName("veteranAgeThreshold")] public int VeteranAgeThreshold { get; set; } = 24;
        [JsonPropertyName("veteranMechanicalMultiplier")] public float VeteranMechanicalMultiplier { get; set; } = 0.35f;
        [JsonPropertyName("nightlyFatigueRecovery")] public float NightlyFatigueRecovery { get; set; } = 25f;
        [JsonPropertyName("synergyPerMatchSameLineup")] public float SynergyPerMatchSameLineup { get; set; } = 2f;
        [JsonPropertyName("synergySubPenalty")] public float SynergySubPenalty { get; set; } = 15f;
        [JsonPropertyName("programs")] public List<TrainingProgramDef> Programs { get; set; } = new();
    }

    public sealed class SplitTemplateConfig
    {
        [JsonPropertyName("totalTeams")] public int TotalTeams { get; set; } = 18;
        [JsonPropertyName("groupsCount")] public int GroupsCount { get; set; } = 3;
        [JsonPropertyName("teamsPerMatch")] public int TeamsPerMatch { get; set; } = 12;
        [JsonPropertyName("regularSeasonWeeks")] public int RegularSeasonWeeks { get; set; } = 4;
        [JsonPropertyName("matchDaysPerWeek")] public int MatchDaysPerWeek { get; set; } = 2;
        [JsonPropertyName("matchesPerDay")] public int MatchesPerDay { get; set; } = 6;
        [JsonPropertyName("pointRushMatches")] public int PointRushMatches { get; set; } = 12;
        [JsonPropertyName("finalsQualifiers")] public int FinalsQualifiers { get; set; } = 12;
        [JsonPropertyName("maxImportsPerLineup")] public int MaxImportsPerLineup { get; set; } = 1;
    }

    /// <summary>
    /// Root partial configuration bundle shared across Sim, Meta, Runner, and Client (§0.1).
    /// </summary>
    public sealed partial class ConfigBundle
    {
        public AttributesConfig Attributes { get; set; } = new();
        public RolesConfig Roles { get; set; } = new();
        public WeaponsConfig Weapons { get; set; } = new();
        public SkillsConfig Skills { get; set; } = new();
        public ScoringConfig Scoring { get; set; } = new();
        public PlaybooksConfig Playbooks { get; set; } = new();
        public TrainingConfig Training { get; set; } = new();
        public SplitTemplateConfig SplitTemplate { get; set; } = new();
        public List<TeamDef> SampleTeams { get; set; } = new();
    }
}
