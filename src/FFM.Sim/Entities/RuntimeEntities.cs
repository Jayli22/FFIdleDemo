using FFM.Sim.Core;
using FFM.Sim.Data;

namespace FFM.Sim.Entities
{
    public struct EffectiveAttributes
    {
        public float Aim;
        public float Recoil;
        public float Reflex;
        public float Movement;
        public float Cqc;
        public float LongRange;

        public float Awareness;
        public float Positioning;
        public float Utility;
        public float Looting;
        public float Decision;
        public float Comms;

        public float Composure;
        public float Teamwork;
        public float Determination;
        public float Stamina;

        public float RoleProficiency;
        public float ActiveSkillProficiency;
        public float ArmorMitigationBonus;

        // Weapon Mastery & Signature Trait bonuses
        public int MasteryTierSg;
        public int MasteryTierSmg;
        public int MasteryTierAr;
        public int MasteryTierDmr;
        public int MasteryTierSr;
        public float HeadshotChanceBonus;
        public float WeaponMasteryDamageBonus;
        public float WeaponMasterySpreadReduction;
        public float DropSpeedMultiplier;
        public float CqcFirstShotBonus;
        public float LongRangeArmorPenBonus;
        public float GlooHpMultiplier;
        public float GrenadeRadiusBonus;
        public float ReviveSpeedMultiplier;
        public float ReviveBonusHp;
        public float ZoneDpsReduction;
        public int ExtraCommandsFromIgl;
        public bool HasEagleEye;

        public readonly int GetWeaponMasteryTier(string category) => category switch
        {
            "SG" => MasteryTierSg,
            "SMG" => MasteryTierSmg,
            "AR" => MasteryTierAr,
            "DMR" => MasteryTierDmr,
            "SR" => MasteryTierSr,
            _ => 0
        };
    }

    public struct UnitRuntime
    {
        public int UnitId;
        public int SquadId;
        public int SlotIndexInSquad; // 0..3
        public Role Role;
        public bool IsIgl;

        public EffectiveAttributes Eff;

        public Vec2 Position;
        public Vec2 DesiredPosition;
        public float FacingRadians;
        public byte HeightLevel;
        public UnitState State;

        public UnitDropState DropState;
        public float AltitudeMeters;
        public Vec2 LandingTargetPos;
        public float DropSpeedMultiplier;

        public float Hp;
        public float MaxHp;
        public int ArmorTier;
        public int HelmetTier;
        public float ArmorDurability;

        public int PrimaryWeaponIndex;   // Index into ConfigBundle.Weapons.Weapons
        public int SecondaryWeaponIndex; // Index into ConfigBundle.Weapons.Weapons
        public int GlooCount;
        public int GrenadeCount;
        public int MedkitCount;

        public int ActiveSkillIndex;     // Index into ConfigBundle.Skills.Skills
        public SkillCategory ActiveSkillCategory;
        public int ActiveSkillCooldownTicks;
        public int ActiveSkillDurationTicks;

        public int TargetUnitId;         // -1 if none
        public int ConsecutiveRoundsInEngagement;
        public int LastAttackerUnitId;
        public int LastDamagedTick;
        public int SpottedEnemyMask;     // Bitmask of squads spotted

        public int KnockedRemainTicks;
        public int ReviveProgressTicks;
        public int ReviverUnitId;

        public int HealProgressTicks;
        public int LootCooldownTicks;
        public int VehicleId;            // -1 if not in vehicle

        public int Kills;
        public float DamageDealt;
        public float DamageTaken;
        public int KnockdownsScored;
        public int RevivesCompleted;
        public int GlooWallsPlaced;
        public int GrenadesThrown;
        public int SkillsActivated;

        public float CoverFacingRadians;
        public int LastFlankedTick;
        public int LastBreachTick;
    }

    public struct SquadRuntime
    {
        public int SquadId;
        public int FirstUnitId;          // SquadId * 4
        public float Synergy;
        public int IglUnitId;

        // Playbook resolved modifiers
        public float HotspotWeightMod;
        public float EarlyRotateSecOffset;
        public float AggressionMod;
        public float HighGroundBias;
        public float CoverBias;
        public bool IsGlooFortEndgame;

        // Navigation & Macro AI state
        public byte DropPoiId;
        public Vec2 DropLaunchPos;
        public int DropLaunchTick;
        public bool HasVeeredOff;
        public Vec2 MacroGoal;
        public int CurrentWaypointIndex;
        public int WaypointCount;
        public int NextAiEvalTick;
        public int PendingRotateTargetReadyTick;
        public Vec2 PendingMacroGoal;

        // Combat & Focus Fire state
        public int FocusTargetUnitId;
        public int UnderFireFromSquadId;
        public int UnderFireTick;

        // Coach Command state (WP-09)
        public int CommandsRemaining;
        public int CommandCooldownTicks;
        public bool HasPendingCommand;
        public TacticalCommand PendingCommand;
        public int PendingCommandExecuteTick;
        public bool HasActiveCommand;
        public TacticalCommand ActiveCommand;
        public int ActiveCommandExpireTick;
        public bool ActiveCommandCompliant;

        // Match stats
        public int AliveCount;
        public int TotalKills;
        public float TotalDamage;
        public int Placement;
        public int EliminatedTick;
    }

    public struct VehicleRuntime
    {
        public int Id;
        public Vec2 Position;
        public float Hp;
        public float MaxHp;
        public float SpeedMultiplier;
        public int DriverUnitId;
        public int SquadId;
    }
}
