using System;
using System.Runtime.CompilerServices;

namespace FFM.Sim.Core
{
    /// <summary>
    /// Integer 2D grid coordinates for 256x256 fine grid or 64x64 coarse grid.
    /// </summary>
    public readonly struct GridIndex : IEquatable<GridIndex>
    {
        public readonly int X;
        public readonly int Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GridIndex(int x, int y)
        {
            X = x;
            Y = y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec2 ToWorldCenter(float cellSize = 8f)
        {
            return new Vec2((X + 0.5f) * cellSize, (Y + 0.5f) * cellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GridIndex FromWorld(Vec2 worldPos, float cellSize = 8f, int width = 256, int height = 256)
        {
            int gx = Math.Clamp((int)(worldPos.X / cellSize), 0, width - 1);
            int gy = Math.Clamp((int)(worldPos.Y / cellSize), 0, height - 1);
            return new GridIndex(gx, gy);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToFlatIndex(int width = 256) => Y * width + X;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GridIndex FromFlatIndex(int flatIndex, int width = 256)
        {
            return new GridIndex(flatIndex % width, flatIndex / width);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ManhattanDistance(GridIndex a, GridIndex b)
        {
            return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int ChebyshevDistance(GridIndex a, GridIndex b)
        {
            return Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public GridIndex Clamp(int width, int height)
        {
            return new GridIndex(Math.Clamp(X, 0, width - 1), Math.Clamp(Y, 0, height - 1));
        }

        public static bool operator ==(GridIndex left, GridIndex right) => left.Equals(right);
        public static bool operator !=(GridIndex left, GridIndex right) => !left.Equals(right);

        public bool Equals(GridIndex other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is GridIndex other && Equals(other);
        public override int GetHashCode() => (Y << 16) ^ X;
        public override string ToString() => $"[{X}, {Y}]";
    }

    /// <summary>
    /// Zero-GC spatial bucket index for up to 48 units on a 40x40 grid (50m cells over 2000x2000m).
    /// </summary>
    public sealed class SpatialGrid48
    {
        public const int GridDim = 40;
        public const float CellMeters = 2000f / GridDim; // 50m
        private readonly int[] _cellHead = new int[GridDim * GridDim];
        private readonly int[] _nextUnit = new int[48];
        private readonly Vec2[] _positions = new Vec2[48];

        public SpatialGrid48()
        {
            Clear();
        }

        public void Clear()
        {
            Array.Fill(_cellHead, -1);
            Array.Fill(_nextUnit, -1);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Insert(int unitId, Vec2 pos)
        {
            _positions[unitId] = pos;
            int cx = Math.Clamp((int)(pos.X / CellMeters), 0, GridDim - 1);
            int cy = Math.Clamp((int)(pos.Y / CellMeters), 0, GridDim - 1);
            int cellIdx = cy * GridDim + cx;
            _nextUnit[unitId] = _cellHead[cellIdx];
            _cellHead[cellIdx] = unitId;
        }

        public int QueryRadius(Vec2 center, float radius, Span<int> results)
        {
            float rSq = radius * radius;
            int minX = Math.Clamp((int)((center.X - radius) / CellMeters), 0, GridDim - 1);
            int maxX = Math.Clamp((int)((center.X + radius) / CellMeters), 0, GridDim - 1);
            int minY = Math.Clamp((int)((center.Y - radius) / CellMeters), 0, GridDim - 1);
            int maxY = Math.Clamp((int)((center.Y + radius) / CellMeters), 0, GridDim - 1);

            int count = 0;
            for (int cy = minY; cy <= maxY; cy++)
            {
                int rowOffset = cy * GridDim;
                for (int cx = minX; cx <= maxX; cx++)
                {
                    int u = _cellHead[rowOffset + cx];
                    while (u != -1)
                    {
                        if (Vec2.DistanceSquared(center, _positions[u]) <= rSq)
                        {
                            if (count < results.Length)
                            {
                                results[count++] = u;
                            }
                        }
                        u = _nextUnit[u];
                    }
                }
            }
            return count;
        }
    }
}
