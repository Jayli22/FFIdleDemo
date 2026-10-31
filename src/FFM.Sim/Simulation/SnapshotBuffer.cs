using System;
using FFM.Sim.Core;
using FFM.Sim.Data;

namespace FFM.Sim.Simulation
{
    public struct UnitSnapshot
    {
        public int UnitId;
        public int SquadId;
        public Vec2 Position;
        public float FacingRadians;
        public byte HeightLevel;
        public UnitState State;
        public Role Role;
        public float Hp;
        public float MaxHp;
        public int ArmorTier;
        public int HelmetTier;
        public int GlooCount;
        public int GrenadeCount;
        public int MedkitCount;
        public int PrimaryWeaponIndex;
        public float ActiveSkillCooldownRemaining;
        public float ActiveSkillDurationRemaining;
        public int TargetUnitId;
        public int VehicleId;
        public int Kills;
        public float DamageDealt;
        public bool IsIgl;
        public Behavior CurrentBehavior;
        public SkillCategory ActiveSkillCategory;
        public byte CoverPercent;
        public byte TerrainFlags;
        public float CoverFacingRadians;
        public bool IsFlanked;
        public bool IsBreaching;
        public UnitDropState DropState;
        public float AltitudeMeters;
    }

    public struct SquadSnapshot
    {
        public int SquadId;
        public int AliveCount;
        public int TotalKills;
        public float TotalDamage;
        public int Placement;
        public Vec2 CenterPos;
        public Vec2 MacroGoal;
        public SquadMode CurrentMode;
        public int CommandsRemaining;
        public int CommandCooldownTicks;
        public float CommandCooldownSec;
        public CommandType ActiveCommand;
        public Vec2 ActiveCommandTarget;
        public bool HasActiveCommand;
        public bool ActiveCommandCompliant;
        public byte DropPoiId;
        public Vec2 DropLaunchPos;
        public int DropLaunchTick;
    }

    public struct GlooWallSnapshot
    {
        public int Id;
        public Vec2 Position;
        public float Radius;
        public float Hp;
        public float MaxHp;
        public int OwnerSquadId;
    }

    public struct VehicleSnapshot
    {
        public int Id;
        public Vec2 Position;
        public float Hp;
        public float MaxHp;
        public int DriverUnitId;
        public int SquadId;
    }

    public struct ZoneSnapshot
    {
        public int Stage;
        public bool IsShrinking;
        public Vec2 CurrentCenter;
        public float CurrentRadius;
        public Vec2 NextCenter;
        public float NextRadius;
        public float SecondsUntilShrink;
        public float CurrentDps;
    }

    public struct FlightPathSnapshot
    {
        public bool IsActive;
        public Vec2 StartPos;
        public Vec2 EndPos;
        public Vec2 CurrentPos;
        public float FacingRadians;
        public float AltitudeMeters;

        public readonly Vec2 PlaneCurrentPos => CurrentPos;
        public readonly Vec2 PlaneForwardDir => new(MathF.Cos(FacingRadians), MathF.Sin(FacingRadians));
    }

    /// <summary>
    /// Preallocated, zero-GC snapshot of a single 10Hz simulation frame (§0.5 & WP-04).
    /// Supports double buffering (PrevSnapshot / CurrSnapshot) and ring-buffer copying for 10s rewind.
    /// </summary>
    public sealed class SnapshotBuffer
    {
        public const int MaxUnits = 48;
        public const int MaxSquads = 12;
        public const int MaxGlooWalls = 32;
        public const int MaxVehicles = 8;

        public int Tick;
        public Phase Phase;
        public int AliveSquads;
        public int AliveUnits;
        public ZoneSnapshot Zone;
        public FlightPathSnapshot FlightPath;

        public readonly UnitSnapshot[] Units = new UnitSnapshot[MaxUnits];
        public readonly SquadSnapshot[] Squads = new SquadSnapshot[MaxSquads];
        public readonly GlooWallSnapshot[] GlooWalls = new GlooWallSnapshot[MaxGlooWalls];
        public int ActiveGlooWallCount;
        public readonly VehicleSnapshot[] Vehicles = new VehicleSnapshot[MaxVehicles];
        public int ActiveVehicleCount;

        public void CopyTo(SnapshotBuffer dst)
        {
            dst.Tick = Tick;
            dst.Phase = Phase;
            dst.AliveSquads = AliveSquads;
            dst.AliveUnits = AliveUnits;
            dst.Zone = Zone;
            dst.FlightPath = FlightPath;
            dst.ActiveGlooWallCount = ActiveGlooWallCount;
            dst.ActiveVehicleCount = ActiveVehicleCount;

            Array.Copy(Units, 0, dst.Units, 0, MaxUnits);
            Array.Copy(Squads, 0, dst.Squads, 0, MaxSquads);
            if (ActiveGlooWallCount > 0)
            {
                Array.Copy(GlooWalls, 0, dst.GlooWalls, 0, ActiveGlooWallCount);
            }
            if (ActiveVehicleCount > 0)
            {
                Array.Copy(Vehicles, 0, dst.Vehicles, 0, ActiveVehicleCount);
            }
        }
    }
}
