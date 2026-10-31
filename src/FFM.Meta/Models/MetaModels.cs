using System;
using System.Collections.Generic;
using FFM.Sim.Data;

namespace FFM.Meta.Models
{
    public enum TeamTrainingFocus
    {
        Shooting = 0,
        Tactics = 1,
        Coordination = 2,
        Rest = 3
    }

    public enum IndividualTrainingFocus
    {
        RoleMastery = 0,
        SkillMastery = 1,
        AimAndRecoil = 2,
        CloseQuarters = 3,
        LongRange = 4,
        PositioningAndAwareness = 5,
        UtilityAndLooting = 6,
        MentalAndStamina = 7
    }

    public enum SeasonStage
    {
        RegularSeason = 0,
        PointRush = 1,
        GrandFinals = 2,
        Offseason = 3
    }

    public enum ScoutingStage
    {
        Unknown = 0,
        Range = 1,
        Exact = 2
    }

    public sealed class PlayerTrainingReport
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;
        public Dictionary<string, float> AttributeDeltas { get; set; } = new();
        public float FatigueBefore { get; set; }
        public float FatigueAfter { get; set; }
        public float FormAfter { get; set; }
        public bool TriggeredMinorBurnout { get; set; }
    }

    public sealed class WeeklyTrainingSummary
    {
        public int Week { get; set; }
        public TeamTrainingFocus TeamFocus { get; set; }
        public float SynergyBefore { get; set; }
        public float SynergyAfter { get; set; }
        public List<PlayerTrainingReport> PlayerReports { get; set; } = new();
    }

    public sealed class LeagueStandingEntry
    {
        public string TeamId { get; set; } = string.Empty;
        public string TeamName { get; set; } = string.Empty;
        public int GroupIndex { get; set; } // 0=A, 1=B, 2=C
        public int MatchesPlayed { get; set; }
        public int PlacementPoints { get; set; }
        public int KillPoints { get; set; }
        public int HeadStartPoints { get; set; }
        public int TotalPoints => PlacementPoints + KillPoints + HeadStartPoints;
        public int Booyahs { get; set; }
        public int TotalKills { get; set; }
        public int LastMatchPlacement { get; set; } = 12;
        public bool MatchPointEligible { get; set; }
        public bool WonChampionRush { get; set; }
    }

    public sealed class ScoutingEntry
    {
        public string PlayerId { get; set; } = string.Empty;
        public ScoutingStage Stage { get; set; } = ScoutingStage.Unknown;
        public int ProgressPoints { get; set; }
    }

    public sealed class ScoutedAttributeView
    {
        public string AttributeName { get; set; } = string.Empty;
        public float MinEstimate { get; set; }
        public float MaxEstimate { get; set; }
        public bool IsExact { get; set; }

        public string DisplayText => IsExact
            ? $"{MinEstimate:F1}"
            : $"{MathF.Floor(MinEstimate):F0}~{MathF.Ceiling(MaxEstimate):F0}";
    }

    public sealed class ScoutedPlayerView
    {
        public string PlayerId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public ScoutingStage Stage { get; set; }
        public string OverallTierTag { get; set; } = "B";
        public Dictionary<string, ScoutedAttributeView> Attributes { get; set; } = new();
        public List<string> HiddenTraitHints { get; set; } = new();
        public int EstimatedMarketValue { get; set; }
        public int EstimatedWeeklySalary { get; set; }
    }

    public sealed class TransferListing
    {
        public PlayerDef Player { get; set; } = new();
        public string CurrentTeamId { get; set; } = string.Empty; // Empty = Free Agent
        public int BuyoutFee { get; set; }
        public int AskedWeeklySalary { get; set; }
    }

    public sealed class FinanceLedgerEntry
    {
        public int Season { get; set; }
        public int Week { get; set; }
        public string Category { get; set; } = string.Empty;
        public int Amount { get; set; }
        public int BalanceAfter { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    public sealed class PatchModifierDef
    {
        public string PatchVersion { get; set; } = "v1.0";
        public string Title { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public Dictionary<string, float> WeaponCategoryDamageMultipliers { get; set; } = new();
        public Dictionary<string, float> SkillCooldownMultipliers { get; set; } = new();
        public List<string> TacticalHighlights { get; set; } = new();
    }

    public sealed class KeyMomentItem
    {
        public int Tick { get; set; }
        public float TimeSec => Tick * 0.1f;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public float WorldX { get; set; }
        public float WorldY { get; set; }
    }

    public sealed class AttributionInsightItem
    {
        public string Dimension { get; set; } = string.Empty; // Mechanical / Tactical / Macro / Command
        public bool IsPositive { get; set; }
        public string Headline { get; set; } = string.Empty;
        public string CausalDetail { get; set; } = string.Empty;
        public string RecommendedAction { get; set; } = string.Empty;
    }

    public sealed class PostMatchReport
    {
        public string FocusSquadId { get; set; } = string.Empty;
        public int Placement { get; set; }
        public int Kills { get; set; }
        public int TotalPoints { get; set; }
        public bool Booyah { get; set; }
        public List<KeyMomentItem> KeyMoments { get; set; } = new();
        public List<AttributionInsightItem> AttributionInsights { get; set; } = new();
    }

    public enum MatchDayAutoPolicy
    {
        AutoSimIdle = 0,
        AutoQuickSim = 0,
        AutoWatch3D = 1,
        AutoLaunch3D = 1,
        PauseBeforeMatch = 2,
        PauseOnMatchDay = 2
    }

    public enum IdleSpeedMode
    {
        Paused = 0,
        Normal1x = 1,
        Fast2x = 2,
        Turbo4x = 4
    }

    public enum BootcampDrillType
    {
        LiveFireReflex = 0,
        TacticalSandbox = 1,
        GlooGymnastics = 2,
        SpaRecovery = 3
    }

    public static class ClubFacilitiesState
    {
        public static int GetUpgradeCost(int currentLevel) => ClubFacilityState.GetUpgradeCost(currentLevel);
    }

    public sealed class ClubFacilityState
    {
        public int ShootingLabLevel { get; set; } = 1;
        public int TacticalLabLevel { get; set; } = 1;
        public int RecoveryCenterLevel { get; set; } = 1;
        public int MediaStudioLevel { get; set; } = 1;

        public float MechanicalGainMultiplier => 1f + (ShootingLabLevel - 1) * 0.08f;
        public float TacticalGainMultiplier => 1f + (TacticalLabLevel - 1) * 0.08f;
        public float FatigueRecoveryMultiplier => 1f + (RecoveryCenterLevel - 1) * 0.10f;
        public float SponsorIncomeMultiplier => 1f + (MediaStudioLevel - 1) * 0.12f;

        public int GetLevel(string facilityKey) => facilityKey switch
        {
            "shooting" => ShootingLabLevel,
            "tactical" => TacticalLabLevel,
            "recovery" => RecoveryCenterLevel,
            "media" => MediaStudioLevel,
            _ => 1
        };

        public static int GetUpgradeCost(int currentLevel) => currentLevel switch
        {
            1 => 25000,
            2 => 45000,
            3 => 75000,
            4 => 120000,
            _ => 999999
        };
    }

    public sealed class IdleClockState
    {
        public int CurrentDayOfWeek { get; set; } = 1; // 1=Mon .. 7=Sun (Match Day)
        public float HourProgress { get; set; } = 8.0f; // 0..24
        public IdleSpeedMode SpeedMode { get; set; } = IdleSpeedMode.Normal1x;
        public MatchDayAutoPolicy MatchDayPolicy { get; set; } = MatchDayAutoPolicy.AutoSimIdle;

        public bool IsPaused
        {
            get => SpeedMode == IdleSpeedMode.Paused;
            set => SpeedMode = value ? IdleSpeedMode.Paused : IdleSpeedMode.Normal1x;
        }

        public float SpeedMultiplier
        {
            get => SpeedMode == IdleSpeedMode.Paused ? 1f : (int)SpeedMode;
            set
            {
                if (value >= 3.5f) SpeedMode = IdleSpeedMode.Turbo4x;
                else if (value >= 1.5f) SpeedMode = IdleSpeedMode.Fast2x;
                else SpeedMode = IdleSpeedMode.Normal1x;
            }
        }

        public float DayProgress01
        {
            get => Math.Clamp(HourProgress / 24f, 0f, 1f);
            set => HourProgress = Math.Clamp(value, 0f, 1f) * 24f;
        }

        // Idle Rewards Vault & Club Progression Currency
        public int UnclaimedIdleCash { get; set; } = 1200;
        public int UnclaimedClubXp { get; set; } = 160;
        public int UnclaimedTacticalPoints { get; set; } = 18;

        public int UnclaimedVaultCash
        {
            get => UnclaimedIdleCash;
            set => UnclaimedIdleCash = value;
        }

        public int UnclaimedVaultXp
        {
            get => UnclaimedClubXp;
            set => UnclaimedClubXp = value;
        }

        public int ClubXpPool { get; set; } = 600;
        public int TacticalPoints { get; set; } = 120;

        public List<string> IdleFeedLog { get; set; } = new()
        {
            "Club Training Base online — continuous idle training & sponsor stream active."
        };

        public List<string> RecentLogs => IdleFeedLog;

        public string DayShortName => CurrentDayOfWeek switch
        {
            1 => "MON",
            2 => "TUE",
            3 => "WED",
            4 => "THU",
            5 => "FRI",
            6 => "SAT",
            7 => "SUN (MATCH DAY)",
            _ => "MON"
        };

        public void PushLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            IdleFeedLog.Insert(0, message);
            if (IdleFeedLog.Count > 24)
            {
                IdleFeedLog.RemoveAt(IdleFeedLog.Count - 1);
            }
        }
    }

    public sealed class IdleTickResult
    {
        public bool AdvancedHour { get; set; }
        public bool AdvancedDay { get; set; }
        public bool TriggeredAutoSimMatchWeek { get; set; }
        public bool TriggeredAutoLaunch3DMatch { get; set; }
        public bool PausedAtMatchDay { get; set; }
        public PostMatchReport? AutoQuickSimReport { get; set; }
        public bool ShouldAutoLaunch3DMatch => TriggeredAutoLaunch3DMatch;
        public List<string> NewLogEntries { get; set; } = new();
    }
}
