using System;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.Sim
{
    public struct InterpolatedUnitState
    {
        public int UnitId;
        public int SquadId;
        public Vector3 WorldPos3D;
        public float FacingRadians;
        public UnitState State;
        public Role Role;
        public Behavior CurrentBehavior;
        public SkillCategory ActiveSkillCategory;
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
        public byte CoverPercent;
        public byte TerrainFlags;
        public float CoverFacingRadians;
        public bool IsFlanked;
        public bool IsBreaching;
        public UnitDropState DropState;
        public float AltitudeMeters;

        public readonly bool IsAirborne => DropState != UnitDropState.Grounded;
        public readonly bool IsIndoors => !IsAirborne && (TerrainFlags & (byte)CellFlags.BuildingInterior) != 0;
        public readonly bool AtWindow => !IsAirborne && (TerrainFlags & (byte)CellFlags.BuildingWindow) != 0;
        public readonly bool AtDoorway => !IsAirborne && (TerrainFlags & (byte)CellFlags.BuildingDoorway) != 0;
        public readonly float EffectiveCoverPct => IsAirborne ? 0f : CoverPercent * 0.01f;
    }

    public sealed class InterpolatedFrameState
    {
        public int Tick;
        public float TimeSec;
        public Phase Phase;
        public int AliveSquads;
        public int AliveUnits;
        public FlightPathSnapshot FlightPath;

        public Vector3 ZoneCenter3D;
        public float ZoneRadius;
        public Vector3 NextZoneCenter3D;
        public float NextZoneRadius;
        public int ZoneStage;
        public bool ZoneIsShrinking;
        public float SecondsUntilShrink;

        public readonly InterpolatedUnitState[] Units = new InterpolatedUnitState[SnapshotBuffer.MaxUnits];
        public readonly SquadSnapshot[] Squads = new SquadSnapshot[SnapshotBuffer.MaxSquads];
        public readonly GlooWallSnapshot[] GlooWalls = new GlooWallSnapshot[SnapshotBuffer.MaxGlooWalls];
        public int ActiveGlooWallCount;
        public readonly VehicleSnapshot[] Vehicles = new VehicleSnapshot[SnapshotBuffer.MaxVehicles];
        public int ActiveVehicleCount;
    }

    public static class SnapshotInterpolator
    {
        public const float HeightPerLevelMeters = 3.2f;
        public const float AltitudeVisualScale = 0.085f; // 420m -> +35.7 units in 3D view

        public static Vector3 SimToWorld3D(Vec2 simPos, MapData map, float extraY = 0f)
        {
            var cell = map.GetCellAtWorld(simPos);
            float y = cell.HeightLevel * HeightPerLevelMeters + extraY;
            return new Vector3(simPos.X, y, simPos.Y);
        }

        public static void Interpolate(
            SnapshotBuffer prev,
            SnapshotBuffer curr,
            float alpha,
            MapData map,
            InterpolatedFrameState output)
        {
            alpha = Math.Clamp(alpha, 0f, 1f);

            output.Tick = curr.Tick;
            output.TimeSec = (prev.Tick + (curr.Tick - prev.Tick) * alpha) * 0.1f;
            output.Phase = curr.Phase;
            output.AliveSquads = curr.AliveSquads;
            output.AliveUnits = curr.AliveUnits;

            // Interpolate Transport Aircraft Flight Path
            var fp = curr.FlightPath;
            if (prev.FlightPath.IsActive && curr.FlightPath.IsActive)
            {
                fp.CurrentPos = Vec2.Lerp(prev.FlightPath.CurrentPos, curr.FlightPath.CurrentPos, alpha);
            }
            output.FlightPath = fp;

            // Interpolate Safe Zone
            Vec2 zCenter = Vec2.Lerp(prev.Zone.CurrentCenter, curr.Zone.CurrentCenter, alpha);
            output.ZoneCenter3D = new Vector3(zCenter.X, 0f, zCenter.Y);
            output.ZoneRadius = prev.Zone.CurrentRadius + (curr.Zone.CurrentRadius - prev.Zone.CurrentRadius) * alpha;
            output.NextZoneCenter3D = new Vector3(curr.Zone.NextCenter.X, 0f, curr.Zone.NextCenter.Y);
            output.NextZoneRadius = curr.Zone.NextRadius;
            output.ZoneStage = curr.Zone.Stage;
            output.ZoneIsShrinking = curr.Zone.IsShrinking;
            output.SecondsUntilShrink = curr.Zone.SecondsUntilShrink;

            // Interpolate 48 Units
            for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
            {
                ref readonly UnitSnapshot pu = ref prev.Units[u];
                ref readonly UnitSnapshot cu = ref curr.Units[u];

                Vec2 pos2D = Vec2.Lerp(pu.Position, cu.Position, alpha);
                float hPrev = pu.HeightLevel * HeightPerLevelMeters;
                float hCurr = cu.HeightLevel * HeightPerLevelMeters;
                float alt = pu.AltitudeMeters + (cu.AltitudeMeters - pu.AltitudeMeters) * alpha;
                float y = (hPrev + (hCurr - hPrev) * alpha) + alt * AltitudeVisualScale;

                float angle = LerpAngle(pu.FacingRadians, cu.FacingRadians, alpha);
                float hp = pu.Hp + (cu.Hp - pu.Hp) * alpha;

                output.Units[u] = new InterpolatedUnitState
                {
                    UnitId = cu.UnitId,
                    SquadId = cu.SquadId,
                    WorldPos3D = new Vector3(pos2D.X, y, pos2D.Y),
                    FacingRadians = angle,
                    State = cu.State,
                    Role = cu.Role,
                    CurrentBehavior = cu.CurrentBehavior,
                    ActiveSkillCategory = cu.ActiveSkillCategory,
                    Hp = hp,
                    MaxHp = cu.MaxHp,
                    ArmorTier = cu.ArmorTier,
                    HelmetTier = cu.HelmetTier,
                    GlooCount = cu.GlooCount,
                    GrenadeCount = cu.GrenadeCount,
                    MedkitCount = cu.MedkitCount,
                    PrimaryWeaponIndex = cu.PrimaryWeaponIndex,
                    ActiveSkillCooldownRemaining = cu.ActiveSkillCooldownRemaining,
                    ActiveSkillDurationRemaining = cu.ActiveSkillDurationRemaining,
                    TargetUnitId = cu.TargetUnitId,
                    VehicleId = cu.VehicleId,
                    Kills = cu.Kills,
                    DamageDealt = cu.DamageDealt,
                    IsIgl = cu.IsIgl,
                    CoverPercent = cu.CoverPercent,
                    TerrainFlags = cu.TerrainFlags,
                    CoverFacingRadians = cu.CoverFacingRadians,
                    IsFlanked = cu.IsFlanked,
                    IsBreaching = cu.IsBreaching,
                    DropState = cu.DropState,
                    AltitudeMeters = alt
                };
            }

            // Copy Squads, GlooWalls, Vehicles
            Array.Copy(curr.Squads, 0, output.Squads, 0, SnapshotBuffer.MaxSquads);
            output.ActiveGlooWallCount = curr.ActiveGlooWallCount;
            if (curr.ActiveGlooWallCount > 0)
            {
                Array.Copy(curr.GlooWalls, 0, output.GlooWalls, 0, curr.ActiveGlooWallCount);
            }

            output.ActiveVehicleCount = curr.ActiveVehicleCount;
            for (int v = 0; v < curr.ActiveVehicleCount; v++)
            {
                var cv = curr.Vehicles[v];
                if (v < prev.ActiveVehicleCount && prev.Vehicles[v].Id == cv.Id)
                {
                    cv.Position = Vec2.Lerp(prev.Vehicles[v].Position, cv.Position, alpha);
                }
                output.Vehicles[v] = cv;
            }
        }

        private static float LerpAngle(float a, float b, float t)
        {
            float diff = (b - a) % (MathF.PI * 2f);
            if (diff > MathF.PI) diff -= MathF.PI * 2f;
            if (diff < -MathF.PI) diff += MathF.PI * 2f;
            return a + diff * t;
        }
    }
}
