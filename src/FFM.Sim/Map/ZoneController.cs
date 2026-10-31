using System;
using FFM.Sim.Core;

namespace FFM.Sim.Map
{
    public struct ZoneStageDef
    {
        public int Stage;
        public int WaitTicks;
        public int ShrinkTicks;
        public float StartRadius;
        public float TargetRadius;
        public float DamagePerSecond;
    }

    /// <summary>
    /// 6-stage deterministic Safe Zone controller (WP-03).
    /// Ensures each next circle is nested inside the current circle and centered on walkable terrain.
    /// </summary>
    public sealed class ZoneController
    {
        private static readonly ZoneStageDef[] Stages = new ZoneStageDef[]
        {
            new ZoneStageDef { Stage = 1, WaitTicks = 380, ShrinkTicks = 320, StartRadius = 1000f, TargetRadius = 660f, DamagePerSecond = 2.0f },
            new ZoneStageDef { Stage = 2, WaitTicks = 280, ShrinkTicks = 280, StartRadius = 660f,  TargetRadius = 420f, DamagePerSecond = 4.0f },
            new ZoneStageDef { Stage = 3, WaitTicks = 240, ShrinkTicks = 240, StartRadius = 420f,  TargetRadius = 250f, DamagePerSecond = 7.5f },
            new ZoneStageDef { Stage = 4, WaitTicks = 200, ShrinkTicks = 200, StartRadius = 250f,  TargetRadius = 135f, DamagePerSecond = 12.0f },
            new ZoneStageDef { Stage = 5, WaitTicks = 160, ShrinkTicks = 180, StartRadius = 135f,  TargetRadius = 60f,  DamagePerSecond = 18.0f },
            new ZoneStageDef { Stage = 6, WaitTicks = 120, ShrinkTicks = 160, StartRadius = 60f,   TargetRadius = 8f,   DamagePerSecond = 28.0f }
        };

        public int CurrentStageIndex { get; private set; } // 0..5 (Stage 1..6)
        public int CurrentStageNumber => CurrentStageIndex + 1;
        public int StageTickCounter { get; private set; }
        public bool IsShrinking { get; private set; }

        public Vec2 CurrentCenter { get; private set; }
        public float CurrentRadius { get; private set; }
        public Vec2 StartShrinkCenter { get; private set; }
        public float StartShrinkRadius { get; private set; }
        public Vec2 NextCenter { get; private set; }
        public float NextRadius { get; private set; }
        public float CurrentDps { get; private set; }

        public float SecondsUntilShrink
        {
            get
            {
                if (CurrentStageIndex >= Stages.Length) return 0f;
                var st = Stages[CurrentStageIndex];
                if (IsShrinking) return 0f;
                return Math.Max(0, st.WaitTicks - StageTickCounter) * 0.1f;
            }
        }

        public void Initialize(MapData map, ref Rng rng)
        {
            CurrentStageIndex = 0;
            StageTickCounter = 0;
            IsShrinking = false;

            CurrentCenter = new Vec2(1000f, 1000f);
            CurrentRadius = Stages[0].StartRadius;
            StartShrinkCenter = CurrentCenter;
            StartShrinkRadius = CurrentRadius;
            CurrentDps = Stages[0].DamagePerSecond;

            NextRadius = Stages[0].TargetRadius;
            NextCenter = SampleNestedCenter(map, ref rng, CurrentCenter, CurrentRadius, NextRadius);
        }

        public bool Step(MapData map, ref Rng rng, out bool stageChanged)
        {
            stageChanged = false;
            if (CurrentStageIndex >= Stages.Length)
            {
                // Final collapse after Stage 6
                CurrentRadius = Math.Max(0f, CurrentRadius - 0.5f);
                return false;
            }

            ref readonly ZoneStageDef st = ref Stages[CurrentStageIndex];
            StageTickCounter++;

            if (!IsShrinking)
            {
                if (StageTickCounter >= st.WaitTicks)
                {
                    IsShrinking = true;
                    StageTickCounter = 0;
                    StartShrinkCenter = CurrentCenter;
                    StartShrinkRadius = CurrentRadius;
                    stageChanged = true;
                }
            }
            else
            {
                float t = Math.Clamp(StageTickCounter / (float)st.ShrinkTicks, 0f, 1f);
                CurrentCenter = Vec2.Lerp(StartShrinkCenter, NextCenter, t);
                CurrentRadius = StartShrinkRadius + (NextRadius - StartShrinkRadius) * t;

                if (StageTickCounter >= st.ShrinkTicks)
                {
                    CurrentCenter = NextCenter;
                    CurrentRadius = NextRadius;
                    IsShrinking = false;
                    StageTickCounter = 0;
                    CurrentStageIndex++;
                    stageChanged = true;

                    if (CurrentStageIndex < Stages.Length)
                    {
                        ref readonly ZoneStageDef nextSt = ref Stages[CurrentStageIndex];
                        CurrentDps = nextSt.DamagePerSecond;
                        NextRadius = nextSt.TargetRadius;
                        NextCenter = SampleNestedCenter(map, ref rng, CurrentCenter, CurrentRadius, NextRadius);
                    }
                }
            }

            return stageChanged;
        }

        public bool IsOutsideZone(Vec2 worldPos)
        {
            return Vec2.DistanceSquared(worldPos, CurrentCenter) > CurrentRadius * CurrentRadius;
        }

        public float DistanceToNextSafeEdge(Vec2 worldPos)
        {
            float dist = Vec2.Distance(worldPos, NextCenter);
            return Math.Max(0f, dist - NextRadius);
        }

        private static Vec2 SampleNestedCenter(MapData map, ref Rng rng, Vec2 curCenter, float curRadius, float nextRadius)
        {
            float maxOffset = Math.Max(0f, (curRadius - nextRadius) * 0.78f);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = rng.NextFloat(0f, MathF.PI * 2f);
                float dist = MathF.Sqrt(rng.NextFloat01()) * maxOffset;
                Vec2 candidate = curCenter + Vec2.FromAngle(angle) * dist;
                candidate = new Vec2(
                    Math.Clamp(candidate.X, 160f, MapData.MapSizeMeters - 160f),
                    Math.Clamp(candidate.Y, 160f, MapData.MapSizeMeters - 160f));

                if (map.GetCellAtWorld(candidate).IsWalkable)
                {
                    return candidate;
                }
            }
            return map.ClampToWalkableWorld(curCenter);
        }
    }
}
