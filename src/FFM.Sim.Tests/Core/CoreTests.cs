using System;
using FFM.Sim.Core;
using NUnit.Framework;

namespace FFM.Sim.Tests.Core
{
    [TestFixture]
    public sealed class CoreTests
    {
        [Test]
        public void Rng_SameSeed_ProducesIdenticalSequence()
        {
            var rngA = new Rng(123456UL);
            var rngB = new Rng(123456UL);

            for (int i = 0; i < 10000; i++)
            {
                Assert.That(rngA.NextUInt64(), Is.EqualTo(rngB.NextUInt64()));
                Assert.That(rngA.NextFloat01(), Is.EqualTo(rngB.NextFloat01()));
                Assert.That(rngA.NextInt(-50, 500), Is.EqualTo(rngB.NextInt(-50, 500)));
                Assert.That(rngA.Chance(0.37f), Is.EqualTo(rngB.Chance(0.37f)));
                Assert.That(rngA.NextGaussian(10f, 2.5f), Is.EqualTo(rngB.NextGaussian(10f, 2.5f)));
            }
        }

        [Test]
        public void Rng_Fork_IsIndependentAndDeterministic()
        {
            var rngMain1 = new Rng(999UL);
            var rngMain2 = new Rng(999UL);

            ulong firstMain1 = rngMain1.NextUInt64();
            Rng sub1A = rngMain1.Fork(1U);
            Rng sub1B = rngMain1.Fork(2U);
            ulong secondMain1 = rngMain1.NextUInt64();

            // rngMain2 does not call Fork between first and second draw
            ulong firstMain2 = rngMain2.NextUInt64();
            ulong secondMain2 = rngMain2.NextUInt64();
            Rng sub2A = rngMain2.Fork(1U);

            Assert.That(firstMain1, Is.EqualTo(firstMain2));
            Assert.That(secondMain1, Is.EqualTo(secondMain2));
            Assert.That(sub1A.NextUInt64(), Is.EqualTo(sub2A.NextUInt64()));
            Assert.That(sub1A.NextUInt64(), Is.Not.EqualTo(sub1B.NextUInt64()));
        }

        [Test]
        public void EventLog_OneMillionEvents_ZeroGcAllocation_AndDeterministicHash()
        {
            var logA = new EventLog(16384);
            var logB = new EventLog(16384);

            // Warm up JIT
            var warmEv = new SimEvent(0, SimEventType.MatchStarted);
            logA.Record(in warmEv);
            _ = logA.DrainEvents();
            logA.Reset();

            long memBefore = GC.GetAllocatedBytesForCurrentThread();
            int drainedTotal = 0;

            for (int i = 0; i < 1_000_000; i++)
            {
                var ev = new SimEvent(
                    tick: i,
                    type: (SimEventType)(i % 20),
                    actorUnitIdx: i % 48,
                    targetUnitIdx: (i + 7) % 48,
                    squadIdx: i % 12,
                    targetSquadIdx: (i + 3) % 12,
                    pos: new Vec2(i * 0.5f, i * 0.25f),
                    value: i * 0.1f,
                    extraCode: i & 255);
                logA.Record(in ev);

                if ((i & 1023) == 1023)
                {
                    ReadOnlySpan<SimEvent> drained = logA.DrainEvents();
                    drainedTotal += drained.Length;
                }
            }

            long memAfter = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(memAfter - memBefore, Is.EqualTo(0L), "EventLog recorded and drained 1e6 events with non-zero heap allocation.");
            Assert.That(drainedTotal, Is.EqualTo(976 * 1024));

            for (int i = 0; i < 1_000_000; i++)
            {
                var ev = new SimEvent(
                    tick: i,
                    type: (SimEventType)(i % 20),
                    actorUnitIdx: i % 48,
                    targetUnitIdx: (i + 7) % 48,
                    squadIdx: i % 12,
                    targetSquadIdx: (i + 3) % 12,
                    pos: new Vec2(i * 0.5f, i * 0.25f),
                    value: i * 0.1f,
                    extraCode: i & 255);
                logB.Record(in ev);
            }

            Assert.That(logA.DeterminismHash, Is.EqualTo(logB.DeterminismHash));
        }

        [Test]
        public void Vec2_And_GridIndex_BasicOperationsWork()
        {
            var a = new Vec2(3f, 4f);
            Assert.That(a.Length, Is.EqualTo(5f).Within(1e-5f));

            var cell = new GridIndex(10, 20);
            Vec2 world = cell.ToWorldCenter(8f);
            Assert.That(world, Is.EqualTo(new Vec2(84f, 164f)));

            GridIndex roundTrip = GridIndex.FromWorld(world, 8f, 256, 256);
            Assert.That(roundTrip, Is.EqualTo(cell));
        }

        [Test]
        public void Pool_RentAndReturn_ZeroAllocationWhenPrewarmed()
        {
            var pool = new Pool<byte[]>(16, () => new byte[64], arr => Array.Clear(arr, 0, arr.Length), prewarm: true);
            _ = pool.Rent();
            pool.Return(new byte[64]);

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1000; i++)
            {
                byte[] item = pool.Rent();
                item[0] = 42;
                pool.Return(item);
            }
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.That(after - before, Is.EqualTo(0L));
        }
    }
}
