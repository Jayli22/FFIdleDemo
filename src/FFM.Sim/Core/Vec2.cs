using System;
using System.Runtime.CompilerServices;

namespace FFM.Sim.Core
{
    /// <summary>
    /// Value-type 2D vector on the horizontal XZ plane for deterministic simulation.
    /// </summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public static readonly Vec2 One = new Vec2(1f, 1f);
        public static readonly Vec2 Right = new Vec2(1f, 0f);
        public static readonly Vec2 Up = new Vec2(0f, 1f);

        public readonly float X;
        public readonly float Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float LengthSquared
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => X * X + Y * Y;
        }

        public float Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MathF.Sqrt(X * X + Y * Y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec2 Normalized()
        {
            float lenSq = X * X + Y * Y;
            if (lenSq < 1e-8f)
            {
                return Zero;
            }
            float invLen = 1.0f / MathF.Sqrt(lenSq);
            return new Vec2(X * invLen, Y * invLen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(Vec2 a, Vec2 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(Vec2 a, Vec2 b)
        {
            return MathF.Sqrt(DistanceSquared(a, b));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(Vec2 a, Vec2 b)
        {
            return a.X * b.X + a.Y * b.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 MoveTowards(Vec2 current, Vec2 target, float maxDistanceDelta)
        {
            float dx = target.X - current.X;
            float dy = target.Y - current.Y;
            float sqDist = dx * dx + dy * dy;
            if (sqDist == 0f || (maxDistanceDelta >= 0f && sqDist <= maxDistanceDelta * maxDistanceDelta))
            {
                return target;
            }
            float dist = MathF.Sqrt(sqDist);
            return new Vec2(current.X + dx / dist * maxDistanceDelta, current.Y + dy / dist * maxDistanceDelta);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Vec2 Clamp(float minX, float maxX, float minY, float maxY)
        {
            return new Vec2(Math.Clamp(X, minX, maxX), Math.Clamp(Y, minY, maxY));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 FromAngle(float radians)
        {
            return new Vec2(MathF.Cos(radians), MathF.Sin(radians));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.X, -a.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator *(Vec2 a, float d) => new Vec2(a.X * d, a.Y * d);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator *(float d, Vec2 a) => new Vec2(a.X * d, a.Y * d);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vec2 operator /(Vec2 a, float d) => new Vec2(a.X / d, a.Y / d);

        public static bool operator ==(Vec2 left, Vec2 right) => left.Equals(right);
        public static bool operator !=(Vec2 left, Vec2 right) => !left.Equals(right);

        public bool Equals(Vec2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object? obj) => obj is Vec2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X:F2}, {Y:F2})";
    }
}
