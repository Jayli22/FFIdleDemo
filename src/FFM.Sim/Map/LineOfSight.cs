using System;
using FFM.Sim.Core;

namespace FFM.Sim.Map
{
    public struct DynamicLosBlocker
    {
        public int Id;
        public Vec2 Position;
        public float Radius;
        public float Hp;
        public int OwnerUnitId;
        public int SpawnTick;
        public int ExpireTick;
    }

    /// <summary>
    /// Zero-GC Bresenham 2D raycast on the 256x256 fine tactical grid with height rules and Gloo Wall blockers.
    /// </summary>
    public static class LineOfSight
    {
        public static bool HasLineOfSight(
            MapData map,
            Vec2 fromWorld,
            Vec2 toWorld,
            DynamicLosBlocker[]? dynamicBlockers = null,
            int blockerCount = 0)
        {
            if (FindBlockingGlooWallIndex(fromWorld, toWorld, dynamicBlockers, blockerCount) >= 0)
            {
                return false;
            }

            return HasTerrainLineOfSight(map, fromWorld, toWorld);
        }

        /// <summary>
        /// Returns the index in <paramref name="dynamicBlockers"/> of the first Gloo Wall blocking the shot from <paramref name="fromWorld"/> to <paramref name="toWorld"/>, or -1 if unblocked by Gloo Walls.
        /// </summary>
        public static int FindBlockingGlooWallIndex(
            Vec2 fromWorld,
            Vec2 toWorld,
            DynamicLosBlocker[]? dynamicBlockers,
            int blockerCount)
        {
            if (dynamicBlockers == null || blockerCount <= 0) return -1;

            for (int i = 0; i < blockerCount; i++)
            {
                ref readonly DynamicLosBlocker b = ref dynamicBlockers[i];
                if (b.Hp <= 0f) continue;

                // Ignore blocker if shooter is standing right behind their own gloo wall (< 2.6m)
                float distFromShooter = Vec2.Distance(fromWorld, b.Position);
                if (distFromShooter <= 2.6f) continue;

                if (SegmentIntersectsCircle(fromWorld, toWorld, b.Position, b.Radius))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Checks only static terrain/building line of sight, ignoring dynamic Gloo Walls.
        /// Used for target awareness, grenade arcs over Gloo Walls, Skyler Riptide, and Gloo-breaking focus fire.
        /// </summary>
        public static bool HasTerrainLineOfSight(MapData map, Vec2 fromWorld, Vec2 toWorld)
        {
            MapData.WorldToFineCell(fromWorld, out int x0, out int y0);
            MapData.WorldToFineCell(toWorld, out int x1, out int y1);

            MapCell startCell = map.GetCell(x0, y0);
            MapCell endCell = map.GetCell(x1, y1);
            byte hFrom = startCell.HeightLevel;
            byte hTo = endCell.HeightLevel;
            byte maxEndpointHeight = Math.Max(hFrom, hTo);
            bool bothOutdoor = !startCell.IsBuilding && !endCell.IsBuilding;
            int buildingIntermediateCount = 0;

            int startX = x0;
            int startY = y0;
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                if ((x0 != startX || y0 != startY) && (x0 != x1 || y0 != y1))
                {
                    MapCell cell = map.GetCell(x0, y0);
                    if (maxEndpointHeight == 2)
                    {
                        // High ground endpoint: only blocked if intermediate cell is also Height 2 AND BlocksLos
                        if (cell.HeightLevel >= 2 && cell.BlocksLos)
                        {
                            return false;
                        }
                    }
                    else
                    {
                        // Same/low ground: blocked by any BlocksLos wall or higher terrain ridge
                        if (cell.BlocksLos || cell.HeightLevel > maxEndpointHeight)
                        {
                            return false;
                        }
                    }

                    if (cell.IsBuilding)
                    {
                        // Outdoor-to-outdoor shots cannot shoot straight through a building interior/window
                        if (bothOutdoor)
                        {
                            return false;
                        }

                        buildingIntermediateCount++;
                        // Cannot shoot across multiple building cells (e.g., from back wall through room to front wall)
                        if (buildingIntermediateCount >= 2)
                        {
                            return false;
                        }
                    }
                }

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }

            return true;
        }

        public static bool SegmentIntersectsCircle(Vec2 a, Vec2 b, Vec2 center, float radius)
        {
            Vec2 ab = b - a;
            float lenSq = ab.LengthSquared;
            if (lenSq < 1e-6f)
            {
                return Vec2.DistanceSquared(a, center) <= radius * radius;
            }
            float t = Vec2.Dot(center - a, ab) / lenSq;
            if (t <= 0.02f || t >= 0.98f)
            {
                return false;
            }
            Vec2 closest = a + ab * t;
            return Vec2.DistanceSquared(closest, center) <= radius * radius;
        }
    }
}
