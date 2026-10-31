using System;
using System.Runtime.CompilerServices;

namespace FFM.Sim.Core
{
    /// <summary>
    /// Deterministic xorshift128+ random number generator (ADR-0003).
    /// System.Random is strictly forbidden in FFM.Sim.
    /// </summary>
    public sealed class Rng
    {
        private readonly ulong _rootSeed;
        private ulong _s0;
        private ulong _s1;

        public ulong RootSeed => _rootSeed;
        public ulong State0 => _s0;
        public ulong State1 => _s1;

        public Rng(ulong seed)
        {
            _rootSeed = seed;
            ulong z = seed + 0x9E3779B97F4A7C15UL;
            _s0 = SplitMix64(ref z);
            _s1 = SplitMix64(ref z);
            if (_s0 == 0UL && _s1 == 0UL)
            {
                _s1 = 0xCAFEBABEDEADBEEFUL;
            }
        }

        public Rng(ulong rootSeed, ulong s0, ulong s1)
        {
            _rootSeed = rootSeed;
            _s0 = s0;
            _s1 = (s0 == 0UL && s1 == 0UL) ? 1UL : s1;
        }

        /// <summary>
        /// Forks an independent deterministic substream based on streamId without mutating the parent stream.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Rng Fork(uint streamId)
        {
            ulong subSeed = _rootSeed ^ ((ulong)(streamId + 1U) * 0xBF58476D1CE4E5B9UL);
            return new Rng(subSeed);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong SplitMix64(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextUInt64()
        {
            ulong s1 = _s0;
            ulong s0 = _s1;
            ulong result = s0 + s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 18) ^ (s0 >> 5);
            return result;
        }

        /// <summary>
        /// Returns a float in [0.0f, 1.0f).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat01()
        {
            return (NextUInt64() >> 40) * (1.0f / (1UL << 24));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float NextFloat(float minInclusive, float maxExclusive)
        {
            return minInclusive + (maxExclusive - minInclusive) * NextFloat01();
        }

        /// <summary>
        /// Returns an integer in [minInclusive, maxExclusive).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }
            uint range = (uint)(maxExclusive - minInclusive);
            uint sample = (uint)(NextUInt64() >> 32);
            return minInclusive + (int)(((ulong)sample * range) >> 32);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Chance(float probability)
        {
            if (probability <= 0f) return false;
            if (probability >= 1f) return true;
            return NextFloat01() < probability;
        }

        /// <summary>
        /// Deterministic Marsaglia polar Gaussian sample N(mean, stdDev).
        /// Avoids trigonometric functions for cross-platform consistency.
        /// </summary>
        public float NextGaussian(float mean = 0f, float stdDev = 1f)
        {
            if (stdDev <= 0f) return mean;
            for (int attempt = 0; attempt < 16; attempt++)
            {
                float u = NextFloat01() * 2f - 1f;
                float v = NextFloat01() * 2f - 1f;
                float s = u * u + v * v;
                if (s > 1e-6f && s < 1f)
                {
                    float mul = MathF.Sqrt(-2f * MathF.Log(s) / s);
                    return mean + stdDev * (u * mul);
                }
            }
            return mean;
        }
    }
}
