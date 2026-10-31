using System;
using System.IO;
using FFM.Sim.Core;
using FFM.Sim.Map;
using NUnit.Framework;

namespace FFM.Sim.Tests.Map
{
    [TestFixture]
    public class MapTests
    {
        private static string GetMapDir()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "FFM.sln")))
                {
                    return Path.Combine(dir, "configs", "maps", "bermuda_lite");
                }
                dir = Path.GetDirectoryName(dir) ?? string.Empty;
            }
            throw new DirectoryNotFoundException("Could not find repo root.");
        }

        [Test]
        public void BermudaLite_LoadsBinaryGridAndPois_AndPathsBetweenAllPoisWithZeroGC()
        {
            MapData map = MapData.LoadFromDirectory(GetMapDir());
            Assert.That(map.Pois.Count, Is.EqualTo(14));
            Assert.That(map.Buildings.Count, Is.GreaterThanOrEqualTo(40));
            Assert.That(map.VehicleSpawns.Count, Is.EqualTo(8));

            var astar = new CoarseAStar();
            var waypoints = new Vec2[64];

            // Warmup
            int warmCount = astar.FindPath(map, map.Pois[0].Center, map.Pois[1].Center, waypoints);
            Assert.That(warmCount, Is.GreaterThan(0));

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            int totalPaths = 0;
            for (int i = 0; i < map.Pois.Count; i++)
            {
                for (int j = i + 1; j < map.Pois.Count; j++)
                {
                    int wpCount = astar.FindPath(map, map.Pois[i].Center, map.Pois[j].Center, waypoints);
                    if (wpCount > 0) totalPaths++;
                }
            }
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(totalPaths, Is.EqualTo(14 * 13 / 2));
            Assert.That(afterBytes - beforeBytes, Is.EqualTo(0), "CoarseAStar allocated heap memory during pathfinding!");
        }

        [Test]
        public void LineOfSight_RespectsGlooWallBlockerAndElevation()
        {
            MapData map = MapData.LoadFromDirectory(GetMapDir());
            Vec2 a = new Vec2(800f, 800f);
            Vec2 b = new Vec2(830f, 800f);

            bool clearLos = LineOfSight.HasLineOfSight(map, a, b);
            Assert.That(clearLos, Is.True);

            var blockers = new DynamicLosBlocker[]
            {
                new DynamicLosBlocker
                {
                    Id = 1,
                    Position = new Vec2(815f, 800f),
                    Radius = 3.0f,
                    Hp = 200f
                }
            };

            bool blockedByGloo = LineOfSight.HasLineOfSight(map, a, b, blockers, 1);
            Assert.That(blockedByGloo, Is.False);
        }

        [Test]
        public void ZoneController_AdvancesAll6Stages_NestedAndDeterministic()
        {
            MapData map = MapData.LoadFromDirectory(GetMapDir());
            var rng1 = new Rng(777UL);
            var rng2 = new Rng(777UL);

            var z1 = new ZoneController();
            var z2 = new ZoneController();
            z1.Initialize(map, ref rng1);
            z2.Initialize(map, ref rng2);

            for (int tick = 0; tick < 3200; tick++)
            {
                z1.Step(map, ref rng1, out _);
                z2.Step(map, ref rng2, out _);
            }

            Assert.That(z1.CurrentStageNumber, Is.GreaterThanOrEqualTo(6));
            Assert.That(z1.CurrentCenter, Is.EqualTo(z2.CurrentCenter));
            Assert.That(z1.CurrentRadius, Is.EqualTo(z2.CurrentRadius));
        }

        [Test]
        public void BuildingTopology_StampsCornersDoorwaysWindowsInterior_AndTacticalLowCover()
        {
            MapData map = MapData.LoadFromDirectory(GetMapDir());
            Assert.That(map.Buildings.Count, Is.EqualTo(52));

            foreach (var b in map.Buildings)
            {
                int corners = 0;
                int doorways = 0;
                int windows = 0;
                int deepInterior = 0;

                for (int dy = 0; dy < b.HeightCells; dy++)
                {
                    for (int dx = 0; dx < b.WidthCells; dx++)
                    {
                        MapCell c = map.GetCell(b.MinCellX + dx, b.MinCellY + dy);
                        if (c.BlocksLos && !c.IsWalkable) corners++;
                        else if (c.IsDoorway && c.IsBuilding && c.IsWalkable) doorways++;
                        else if (c.IsWindow && c.IsBuilding && c.IsWalkable) windows++;
                        else if (c.IsBuilding && c.IsWalkable) deepInterior++;
                    }
                }

                Assert.That(corners, Is.EqualTo(4), $"Building {b.BuildingId} should have 4 corner pillars");
                Assert.That(doorways, Is.EqualTo(2), $"Building {b.BuildingId} should have 2 doorways");
                Assert.That(windows, Is.EqualTo(4), $"Building {b.BuildingId} should have 4 windows");
                Assert.That(deepInterior, Is.EqualTo(2), $"Building {b.BuildingId} should have 2 core interior cells");
            }

            int lowCoverCount = 0;
            for (int y = 0; y < MapData.FineGridSize; y++)
            {
                for (int x = 0; x < MapData.FineGridSize; x++)
                {
                    if (map.GetCell(x, y).IsTacticalLowCover) lowCoverCount++;
                }
            }
            Assert.That(lowCoverCount, Is.GreaterThanOrEqualTo(60), "Map should have outdoor TacticalLowCover cells");
        }

        [Test]
        public void LineOfSight_BlocksOutdoorRaysThroughBuildingInterior_AllowsWindowPeek()
        {
            MapData map = MapData.LoadFromDirectory(GetMapDir());
            var b = map.Buildings[0];

            // Outdoor North of building to Outdoor South of building (passes through doorway/interior)
            Vec2 northOutdoor = MapData.FineCellToWorldCenter(b.MinCellX + 1, b.MinCellY - 2);
            Vec2 southOutdoor = MapData.FineCellToWorldCenter(b.MinCellX + 1, b.MinCellY + b.HeightCells + 1);
            Assert.That(LineOfSight.HasTerrainLineOfSight(map, northOutdoor, southOutdoor), Is.False,
                "Outdoor-to-outdoor ray crossing through a building interior must be blocked");

            // Unit standing at South window (dx=2, dy=bh-1) shooting South to outdoor target
            Vec2 southWindow = MapData.FineCellToWorldCenter(b.MinCellX + 2, b.MinCellY + b.HeightCells - 1);
            Vec2 southTarget = MapData.FineCellToWorldCenter(b.MinCellX + 2, b.MinCellY + b.HeightCells + 2);
            Assert.That(LineOfSight.HasTerrainLineOfSight(map, southWindow, southTarget), Is.True,
                "Unit at a building window must have clear LOS outward");
        }
    }
}
