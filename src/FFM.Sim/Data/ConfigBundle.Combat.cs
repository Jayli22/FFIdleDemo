using System.Text.Json.Serialization;

namespace FFM.Sim.Data
{
    public sealed class EngagementBalanceConfig
    {
        [JsonPropertyName("detectionBasePerTick")] public float DetectionBasePerTick { get; set; } = 0.08f;
        [JsonPropertyName("detectionAwarenessScale")] public float DetectionAwarenessScale { get; set; } = 0.025f;
        [JsonPropertyName("initiativeAwarenessWeight")] public float InitiativeAwarenessWeight { get; set; } = 0.5f;
        [JsonPropertyName("initiativeReflexWeight")] public float InitiativeReflexWeight { get; set; } = 0.3f;
        [JsonPropertyName("initiativePosAdvWeight")] public float InitiativePosAdvWeight { get; set; } = 10.0f;
        [JsonPropertyName("initiativeNoiseStdDev")] public float InitiativeNoiseStdDev { get; set; } = 2.0f;
        [JsonPropertyName("roundDurationTicks")] public int RoundDurationTicks { get; set; } = 5;

        [JsonPropertyName("closeRangeMaxMeters")] public float CloseRangeMaxMeters { get; set; } = 25f;
        [JsonPropertyName("midRangeMaxMeters")] public float MidRangeMaxMeters { get; set; } = 70f;
        [JsonPropertyName("aimBaseOffset")] public float AimBaseOffset { get; set; } = 0.5f;
        [JsonPropertyName("aimDivisor")] public float AimDivisor { get; set; } = 40f;
        [JsonPropertyName("roleModBase")] public float RoleModBase { get; set; } = 0.90f;
        [JsonPropertyName("roleModScale")] public float RoleModScale { get; set; } = 0.008f;
        [JsonPropertyName("coverMitigationWeight")] public float CoverMitigationWeight { get; set; } = 0.60f;
        [JsonPropertyName("movementNeutral")] public float MovementNeutral { get; set; } = 10f;
        [JsonPropertyName("movementDivisor")] public float MovementDivisor { get; set; } = 60f;
        [JsonPropertyName("heightAdvantagePerLevel")] public float HeightAdvantagePerLevel { get; set; } = 0.12f;
        [JsonPropertyName("composureBaseline")] public float ComposureBaseline { get; set; } = 16f;
        [JsonPropertyName("composureEndgamePenaltyPerPoint")] public float ComposureEndgamePenaltyPerPoint { get; set; } = 0.015f;
        [JsonPropertyName("recoilDivisor")] public float RecoilDivisor { get; set; } = 400f;
        [JsonPropertyName("minHitChance")] public float MinHitChance { get; set; } = 0.02f;
        [JsonPropertyName("maxHitChance")] public float MaxHitChance { get; set; } = 0.95f;

        [JsonPropertyName("armorMitigationPerTier")] public float ArmorMitigationPerTier { get; set; } = 0.12f;
        [JsonPropertyName("tradeRadiusMeters")] public float TradeRadiusMeters { get; set; } = 20f;
        [JsonPropertyName("tradeBaseChance")] public float TradeBaseChance { get; set; } = 0.15f;
        [JsonPropertyName("tradeTeamworkDivisor")] public float TradeTeamworkDivisor { get; set; } = 60f;
        [JsonPropertyName("tradeSynergyDivisor")] public float TradeSynergyDivisor { get; set; } = 300f;
        [JsonPropertyName("tradeSecondRusherDivisor")] public float TradeSecondRusherDivisor { get; set; } = 100f;

        [JsonPropertyName("bleedOutSec")] public float BleedOutSec { get; set; } = 30f;
        [JsonPropertyName("baseReviveSec")] public float BaseReviveSec { get; set; } = 6.0f;
        [JsonPropertyName("reviveTeamworkScale")] public float ReviveTeamworkScale { get; set; } = 0.12f;
        [JsonPropertyName("reviveSupportBonusSec")] public float ReviveSupportBonusSec { get; set; } = 1.0f;
        [JsonPropertyName("reviveRangeMeters")] public float ReviveRangeMeters { get; set; } = 3.5f;
    }

    public sealed class FatigueBalanceConfig
    {
        [JsonPropertyName("consistencyNormalDivisor")] public float ConsistencyNormalDivisor { get; set; } = 25f;
        [JsonPropertyName("offDayPenaltyMean")] public float OffDayPenaltyMean { get; set; } = 2.0f;
        [JsonPropertyName("offDayPenaltySpread")] public float OffDayPenaltySpread { get; set; } = 1.0f;
        [JsonPropertyName("bigMatchFinalsScale")] public float BigMatchFinalsScale { get; set; } = 0.30f;

        [JsonPropertyName("fatigueGainBasePerMatch")] public float FatigueGainBasePerMatch { get; set; } = 11.0f;
        [JsonPropertyName("fatigueGainStaminaScale")] public float FatigueGainStaminaScale { get; set; } = 0.35f;
        [JsonPropertyName("fatigueThresholdBase")] public float FatigueThresholdBase { get; set; } = 25.0f;
        [JsonPropertyName("fatigueThresholdStaminaScale")] public float FatigueThresholdStaminaScale { get; set; } = 1.0f;
        [JsonPropertyName("fatiguePenaltyDivisor")] public float FatiguePenaltyDivisor { get; set; } = 12.0f;
    }

    public sealed partial class ConfigBundle
    {
        public EngagementBalanceConfig Engagement { get; set; } = new();
        public FatigueBalanceConfig Fatigue { get; set; } = new();
    }
}
