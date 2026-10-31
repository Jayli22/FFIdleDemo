using System.Text.Json.Serialization;

namespace FFM.Sim.Data
{
    public sealed class SquadAiBalanceConfig
    {
        [JsonPropertyName("evalIntervalTicks")] public int EvalIntervalTicks { get; set; } = 20;
        [JsonPropertyName("decisionNoiseDivisor")] public float DecisionNoiseDivisor { get; set; } = 4.0f;
        [JsonPropertyName("commsDelayMultiplierTicks")] public int CommsDelayMultiplierTicks { get; set; } = 2;
        [JsonPropertyName("zoneUrgencyWeight")] public float ZoneUrgencyWeight { get; set; } = 1.5f;
        [JsonPropertyName("highGroundWeight")] public float HighGroundWeight { get; set; } = 0.8f;
        [JsonPropertyName("coverWeight")] public float CoverWeight { get; set; } = 0.9f;
        [JsonPropertyName("threatAvoidanceWeight")] public float ThreatAvoidanceWeight { get; set; } = 1.1f;
        [JsonPropertyName("lootDeficitWeight")] public float LootDeficitWeight { get; set; } = 1.0f;
        [JsonPropertyName("earlyRotateLeadBaseSec")] public float EarlyRotateLeadBaseSec { get; set; } = 18.0f;
        [JsonPropertyName("decisionRotateBonusSecPerPoint")] public float DecisionRotateBonusSecPerPoint { get; set; } = 1.6f;
    }

    public sealed class UnitAiBalanceConfig
    {
        [JsonPropertyName("baseMoveSpeedMetersPerSec")] public float BaseMoveSpeedMetersPerSec { get; set; } = 5.4f;
        [JsonPropertyName("movementSpeedScalePerPoint")] public float MovementSpeedScalePerPoint { get; set; } = 0.015f;
        [JsonPropertyName("healHpThresholdRatio")] public float HealHpThresholdRatio { get; set; } = 0.45f;
        [JsonPropertyName("healDurationSec")] public float HealDurationSec { get; set; } = 4.0f;
        [JsonPropertyName("healAmount")] public float HealAmount { get; set; } = 45f;
        [JsonPropertyName("coverSearchRadiusCells")] public int CoverSearchRadiusCells { get; set; } = 4;
        [JsonPropertyName("positioningTemperatureScale")] public float PositioningTemperatureScale { get; set; } = 2.5f;
        [JsonPropertyName("lootBaseIntervalTicks")] public int LootBaseIntervalTicks { get; set; } = 35;
        [JsonPropertyName("lootingSpeedReductionTicksPerPoint")] public float LootingSpeedReductionTicksPerPoint { get; set; } = 1.2f;
        [JsonPropertyName("lootingQualityBonusPerPoint")] public float LootingQualityBonusPerPoint { get; set; } = 0.035f;
    }

    public sealed class CommandsBalanceConfig
    {
        [JsonPropertyName("maxCommandsPerMatch")] public int MaxCommandsPerMatch { get; set; } = 5;
        [JsonPropertyName("cooldownSec")] public float CooldownSec { get; set; } = 60f;
        [JsonPropertyName("baseDelayTicks")] public int BaseDelayTicks { get; set; } = 8;
        [JsonPropertyName("decisionDelayFactor")] public float DecisionDelayFactor { get; set; } = 2.8f;
        [JsonPropertyName("commsDelayFactor")] public float CommsDelayFactor { get; set; } = 2.4f;
        [JsonPropertyName("synergyDelayFactor")] public float SynergyDelayFactor { get; set; } = 0.55f;
        [JsonPropertyName("baseComplianceProb")] public float BaseComplianceProb { get; set; } = 0.25f;
        [JsonPropertyName("decisionComplianceScale")] public float DecisionComplianceScale { get; set; } = 0.018f;
        [JsonPropertyName("commsComplianceScale")] public float CommsComplianceScale { get; set; } = 0.014f;
        [JsonPropertyName("synergyComplianceScale")] public float SynergyComplianceScale { get; set; } = 0.0022f;
        [JsonPropertyName("utilityBiasBonus")] public float UtilityBiasBonus { get; set; } = 5.0f;
    }

    public sealed partial class ConfigBundle
    {
        public SquadAiBalanceConfig SquadAi { get; set; } = new();
        public UnitAiBalanceConfig UnitAi { get; set; } = new();
        public CommandsBalanceConfig Commands { get; set; } = new();
    }
}
