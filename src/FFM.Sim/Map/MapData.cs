using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using FFM.Sim.Core;

namespace FFM.Sim.Map
{
    [Flags]
    public enum CellFlags : byte
    {
        None = 0,
        Walkable = 1 << 0,
        BlocksLos = 1 << 1,
        BuildingInterior = 1 << 2,
        Road = 1 << 3,
        Water = 1 << 4,
        BuildingWindow = 1 << 5,
        BuildingDoorway = 1 << 6,
        TacticalLowCover = 1 << 7
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MapCell
    {
        public byte HeightLevel; // 0 = Low, 1 = Ground, 2 = HighGround
        public byte CoverValue;  // 0..100
        public CellFlags Flags;
        public byte PoiId;       // 0 = None, 1..N = POI index

        public readonly bool IsWalkable => (Flags & CellFlags.Walkable) != 0;
        public readonly bool BlocksLos => (Flags & CellFlags.BlocksLos) != 0;
        public readonly bool IsBuilding => (Flags & CellFlags.BuildingInterior) != 0;
        public readonly bool IsWindow => (Flags & CellFlags.BuildingWindow) != 0;
        public readonly bool IsDoorway => (Flags & CellFlags.BuildingDoorway) != 0;
        public readonly bool IsTacticalLowCover => (Flags & CellFlags.TacticalLowCover) != 0;
        public readonly bool IsRoad => (Flags & CellFlags.Road) != 0;
        public readonly bool IsWater => (Flags & CellFlags.Water) != 0;
        public readonly float CoverNormalized => CoverValue * 0.01f;
    }

    public sealed class PoiDef
    {
        [JsonPropertyName("poiId")] public byte PoiId { get; set; }
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("centerX")] public float CenterX { get; set; }
        [JsonPropertyName("centerY")] public float CenterY { get; set; }
        [JsonPropertyName("radiusMeters")] public float RadiusMeters { get; set; } = 95f;
        [JsonPropertyName("lootTierWeight")] public float LootTierWeight { get; set; } = 1.0f;
        [JsonPropertyName("isHotspot")] public bool IsHotspot { get; set; }
        [JsonPropertyName("heightLevel")] public byte HeightLevel { get; set; } = 1;

        [JsonIgnore]
        public Vec2 Center => new Vec2(CenterX, CenterY);
    }

    public sealed class BuildingFootprintDef
    {
        [JsonPropertyName("buildingId")] public int BuildingId { get; set; }
        [JsonPropertyName("poiId")] public byte PoiId { get; set; }
        [JsonPropertyName("minCellX")] public int MinCellX { get; set; }
        [JsonPropertyName("minCellY")] public int MinCellY { get; set; }
        [JsonPropertyName("widthCells")] public int WidthCells { get; set; }
        [JsonPropertyName("heightCells")] public int HeightCells { get; set; }
        [JsonPropertyName("heightMeters")] public float HeightMeters { get; set; } = 6.5f;
    }

    public sealed class PoisFileConfig
    {
        [JsonPropertyName("mapId")] public string MapId { get; set; } = "bermuda_lite";
        [JsonPropertyName("sizeMeters")] public float SizeMeters { get; set; } = 2000f;
        [JsonPropertyName("gridDimension")] public int GridDimension { get; set; } = 256;
        [JsonPropertyName("pois")] public List<PoiDef> Pois { get; set; } = new();
        [JsonPropertyName("buildings")] public List<BuildingFootprintDef> Buildings { get; set; } = new();
    }

    public sealed class LootClusterSpawnDef
    {
        [JsonPropertyName("clusterId")] public int ClusterId { get; set; }
        [JsonPropertyName("poiId")] public byte PoiId { get; set; }
        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
        [JsonPropertyName("richness")] public float Richness { get; set; } = 1.0f;
    }

    public sealed class VehicleSpawnDef
    {
        [JsonPropertyName("vehicleId")] public int VehicleId { get; set; }
        [JsonPropertyName("x")] public float X { get; set; }
        [JsonPropertyName("y")] public float Y { get; set; }
        [JsonPropertyName("maxHp")] public float MaxHp { get; set; } = 450f;
        [JsonPropertyName("speedMultiplier")] public float SpeedMultiplier { get; set; } = 2.0f;
    }

    public sealed class SpawnsFileConfig
    {
        [JsonPropertyName("lootClusters")] public List<LootClusterSpawnDef> LootClusters { get; set; } = new();
        [JsonPropertyName("vehicles")] public List<VehicleSpawnDef> Vehicles { get; set; } = new();
    }

    /// <summary>
    /// 2000m x 2000m tactical map with 256x256 fine cells (7.8125m/cell) and 64x64 macro navigation grid (31.25m/macro-cell).
    /// </summary>
    public sealed class MapData
    {
        public const float MapSizeMeters = 2000f;
        public const int FineGridSize = 256;
        public const float FineCellMeters = MapSizeMeters / FineGridSize; // 7.8125m
        public const int MacroGridSize = 64;
        public const int FinePerMacro = FineGridSize / MacroGridSize;    // 4
        public const float MacroCellMeters = MapSizeMeters / MacroGridSize; // 31.25m

        private readonly MapCell[] _cells = new MapCell[FineGridSize * FineGridSize];
        private readonly byte[] _macroWalkableCost = new byte[MacroGridSize * MacroGridSize]; // 0 = blocked, 1..255 = traversal cost

        public List<PoiDef> Pois { get; } = new();
        public List<BuildingFootprintDef> Buildings { get; } = new();
        public List<LootClusterSpawnDef> LootClusters { get; } = new();
        public List<VehicleSpawnDef> VehicleSpawns { get; } = new();

        public MapCell[] RawCells => _cells;

        public ref MapCell GetCellRef(int x, int y)
        {
            x = Math.Clamp(x, 0, FineGridSize - 1);
            y = Math.Clamp(y, 0, FineGridSize - 1);
            return ref _cells[y * FineGridSize + x];
        }

        public MapCell GetCell(int x, int y)
        {
            x = Math.Clamp(x, 0, FineGridSize - 1);
            y = Math.Clamp(y, 0, FineGridSize - 1);
            return _cells[y * FineGridSize + x];
        }

        public MapCell GetCellAtWorld(Vec2 pos)
        {
            WorldToFineCell(pos, out int cx, out int cy);
            return GetCell(cx, cy);
        }

        public static void WorldToFineCell(Vec2 pos, out int cx, out int cy)
        {
            cx = Math.Clamp((int)(pos.X / FineCellMeters), 0, FineGridSize - 1);
            cy = Math.Clamp((int)(pos.Y / FineCellMeters), 0, FineGridSize - 1);
        }

        public static Vec2 FineCellToWorldCenter(int cx, int cy)
        {
            return new Vec2((cx + 0.5f) * FineCellMeters, (cy + 0.5f) * FineCellMeters);
        }

        public static void WorldToMacroCell(Vec2 pos, out int mx, out int my)
        {
            mx = Math.Clamp((int)(pos.X / MacroCellMeters), 0, MacroGridSize - 1);
            my = Math.Clamp((int)(pos.Y / MacroCellMeters), 0, MacroGridSize - 1);
        }

        public static Vec2 MacroCellToWorldCenter(int mx, int my)
        {
            return new Vec2((mx + 0.5f) * MacroCellMeters, (my + 0.5f) * MacroCellMeters);
        }

        public byte GetMacroCost(int mx, int my)
        {
            if ((uint)mx >= MacroGridSize || (uint)my >= MacroGridSize) return 0;
            return _macroWalkableCost[my * MacroGridSize + mx];
        }

        public void RebuildMacroGrid()
        {
            for (int my = 0; my < MacroGridSize; my++)
            {
                for (int mx = 0; mx < MacroGridSize; mx++)
                {
                    int walkableCount = 0;
                    int roadCount = 0;
                    int highGroundCount = 0;

                    int baseX = mx * FinePerMacro;
                    int baseY = my * FinePerMacro;

                    for (int dy = 0; dy < FinePerMacro; dy++)
                    {
                        for (int dx = 0; dx < FinePerMacro; dx++)
                        {
                            var c = _cells[(baseY + dy) * FineGridSize + (baseX + dx)];
                            if (c.IsWalkable) walkableCount++;
                            if (c.IsRoad) roadCount++;
                            if (c.HeightLevel == 2) highGroundCount++;
                        }
                    }

                    if (walkableCount < 4)
                    {
                        _macroWalkableCost[my * MacroGridSize + mx] = 0; // Blocked macro cell
                    }
                    else
                    {
                        int cost = 10;
                        if (roadCount >= 3) cost = 8;
                        else if (highGroundCount >= 8) cost = 12;
                        _macroWalkableCost[my * MacroGridSize + mx] = (byte)cost;
                    }
                }
            }
        }

        public Vec2 ClampToWalkableWorld(Vec2 desired)
        {
            float clampedX = Math.Clamp(desired.X, 20f, MapSizeMeters - 20f);
            float clampedY = Math.Clamp(desired.Y, 20f, MapSizeMeters - 20f);
            var pos = new Vec2(clampedX, clampedY);

            WorldToFineCell(pos, out int cx, out int cy);
            if (GetCell(cx, cy).IsWalkable)
            {
                return pos;
            }

            // Spiral search for nearest walkable cell
            for (int r = 1; r <= 16; r++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                        int nx = cx + dx;
                        int ny = cy + dy;
                        if ((uint)nx < FineGridSize && (uint)ny < FineGridSize && GetCell(nx, ny).IsWalkable)
                        {
                            return FineCellToWorldCenter(nx, ny);
                        }
                    }
                }
            }

            return new Vec2(1000f, 1000f);
        }

        public void SaveGridBinary(string filePath)
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using var fs = File.Create(filePath);
            using var bw = new BinaryWriter(fs);
            bw.Write(0x4D474646); // Magic "FFGM"
            bw.Write(1);          // Version 1
            bw.Write(FineGridSize);
            for (int i = 0; i < _cells.Length; i++)
            {
                bw.Write(_cells[i].HeightLevel);
                bw.Write(_cells[i].CoverValue);
                bw.Write((byte)_cells[i].Flags);
                bw.Write(_cells[i].PoiId);
            }
        }

        public void LoadGridBinary(string filePath)
        {
            using var fs = File.OpenRead(filePath);
            using var br = new BinaryReader(fs);
            int magic = br.ReadInt32();
            int version = br.ReadInt32();
            int size = br.ReadInt32();
            if (magic != 0x4D474646 || version != 1 || size != FineGridSize)
            {
                throw new InvalidDataException($"Invalid grid.bin header in {filePath}");
            }
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].HeightLevel = br.ReadByte();
                _cells[i].CoverValue = br.ReadByte();
                _cells[i].Flags = (CellFlags)br.ReadByte();
                _cells[i].PoiId = br.ReadByte();
            }
            RebuildMacroGrid();
        }

        public static MapData LoadFromDirectory(string mapDir)
        {
            var map = new MapData();
            string gridPath = Path.Combine(mapDir, "grid.bin");
            string poisPath = Path.Combine(mapDir, "pois.json");
            string spawnsPath = Path.Combine(mapDir, "spawns.json");

            if (!File.Exists(gridPath) || !File.Exists(poisPath) || !File.Exists(spawnsPath))
            {
                // Auto-generate deterministic Bermuda Lite if files are missing
                return GenerateBermudaLite();
            }

            map.LoadGridBinary(gridPath);
            var poisCfg = Data.ConfigLoader.LoadJsonFile<PoisFileConfig>(poisPath);
            map.Pois.AddRange(poisCfg.Pois);
            map.Buildings.AddRange(poisCfg.Buildings);

            var spawnsCfg = Data.ConfigLoader.LoadJsonFile<SpawnsFileConfig>(spawnsPath);
            map.LootClusters.AddRange(spawnsCfg.LootClusters);
            map.VehicleSpawns.AddRange(spawnsCfg.Vehicles);

            map.ApplyBuildingAndCoverTopology();
            map.RebuildMacroGrid();
            return map;
        }

        /// <summary>
        /// Stamps 4x3 building doorway/window/interior/corner topology and surrounding TacticalLowCover props.
        /// Ensures 100% parity whether MapData is loaded from disk or generated procedurally.
        /// </summary>
        public void ApplyBuildingAndCoverTopology()
        {
            // 1. Stamp building footprints:
            //    - 4 corners: BlocksLos pillars (CoverValue = 90)
            //    - 2 entrances: South (dx=1, dy=bh-1) & North (dx=2, dy=0) -> Walkable | BuildingInterior | BuildingDoorway (CoverValue = 45)
            //    - 4 window bays: (2, bh-1), (1, 0), (0, 1), (bw-1, 1) -> Walkable | BuildingInterior | BuildingWindow (CoverValue = 85)
            //    - 2 core floor cells: (1, 1), (2, 1) -> Walkable | BuildingInterior (CoverValue = 82)
            for (int i = 0; i < Buildings.Count; i++)
            {
                var bld = Buildings[i];
                int bx = bld.MinCellX;
                int by = bld.MinCellY;
                int bw = bld.WidthCells;
                int bh = bld.HeightCells;

                for (int dy = 0; dy < bh; dy++)
                {
                    for (int dx = 0; dx < bw; dx++)
                    {
                        ref MapCell c = ref GetCellRef(bx + dx, by + dy);
                        c.PoiId = bld.PoiId;

                        bool isCorner = (dx == 0 || dx == bw - 1) && (dy == 0 || dy == bh - 1);
                        bool isDoorway = (dx == 1 && dy == bh - 1) || (dx == Math.Min(2, bw - 2) && dy == 0);
                        bool isPerimeter = dx == 0 || dx == bw - 1 || dy == 0 || dy == bh - 1;

                        if (isCorner)
                        {
                            c.CoverValue = 90;
                            c.Flags = CellFlags.BlocksLos;
                        }
                        else if (isDoorway)
                        {
                            c.CoverValue = 45;
                            c.Flags = CellFlags.Walkable | CellFlags.BuildingInterior | CellFlags.BuildingDoorway;
                        }
                        else if (isPerimeter)
                        {
                            c.CoverValue = 85;
                            c.Flags = CellFlags.Walkable | CellFlags.BuildingInterior | CellFlags.BuildingWindow;
                        }
                        else
                        {
                            c.CoverValue = 82;
                            c.Flags = CellFlags.Walkable | CellFlags.BuildingInterior;
                        }
                    }
                }
            }

            // 2. Stamp outdoor TacticalLowCover (sandbag walls, concrete jersey barriers, supply crates)
            //    around building courtyards and POI approaches
            Span<(int dx, int dy, byte cover)> coverOffsets = stackalloc (int, int, byte)[]
            {
                (-2, 1, 76),
                (5, 1, 76),
                (1, 4, 74),
                (2, -2, 72)
            };

            for (int i = 0; i < Buildings.Count; i++)
            {
                var bld = Buildings[i];
                for (int k = 0; k < coverOffsets.Length; k++)
                {
                    // Deterministically pick 2 courtyard covers per building so courtyards aren't overcrowded
                    if (((i + k) & 1) != 0) continue;
                    int cx = bld.MinCellX + coverOffsets[k].dx;
                    int cy = bld.MinCellY + coverOffsets[k].dy;
                    if ((uint)cx >= FineGridSize || (uint)cy >= FineGridSize) continue;

                    ref MapCell c = ref GetCellRef(cx, cy);
                    if (c.IsWalkable && !c.IsBuilding && !c.IsWater && FindBuildingAtCell(cx, cy) < 0)
                    {
                        c.CoverValue = Math.Max(c.CoverValue, coverOffsets[k].cover);
                        c.Flags |= CellFlags.TacticalLowCover;
                    }
                }
            }

            // 3. Stamp deterministic roadside & ridge TacticalLowCover positions around POIs
            for (int p = 0; p < Pois.Count; p++)
            {
                WorldToFineCell(Pois[p].Center, out int pcx, out int pcy);
                for (int ring = 0; ring < 6; ring++)
                {
                    float ang = ring * (MathF.PI / 3f) + p * 0.42f;
                    int distCells = 8 + (ring % 2) * 3;
                    int cx = Math.Clamp(pcx + (int)MathF.Round(MathF.Cos(ang) * distCells), 6, FineGridSize - 7);
                    int cy = Math.Clamp(pcy + (int)MathF.Round(MathF.Sin(ang) * distCells), 6, FineGridSize - 7);

                    ref MapCell c = ref GetCellRef(cx, cy);
                    if (c.IsWalkable && !c.IsBuilding && !c.IsWater && FindBuildingAtCell(cx, cy) < 0)
                    {
                        c.CoverValue = Math.Max(c.CoverValue, (byte)75);
                        c.Flags |= CellFlags.TacticalLowCover;
                    }
                }
            }
        }

        public int FindBuildingAtCell(int cx, int cy)
        {
            for (int i = 0; i < Buildings.Count; i++)
            {
                var b = Buildings[i];
                if (cx >= b.MinCellX && cx < b.MinCellX + b.WidthCells &&
                    cy >= b.MinCellY && cy < b.MinCellY + b.HeightCells)
                {
                    return i;
                }
            }
            return -1;
        }

        public int FindBuildingAtWorld(Vec2 pos)
        {
            WorldToFineCell(pos, out int cx, out int cy);
            return FindBuildingAtCell(cx, cy);
        }

        public Vec2 GetNearestDoorwayWorld(int buildingIndex, Vec2 fromPos)
        {
            if ((uint)buildingIndex >= (uint)Buildings.Count) return fromPos;
            var b = Buildings[buildingIndex];
            Vec2 southDoor = FineCellToWorldCenter(b.MinCellX + 1, b.MinCellY + b.HeightCells - 1);
            Vec2 northDoor = FineCellToWorldCenter(b.MinCellX + Math.Min(2, b.WidthCells - 2), b.MinCellY);
            return Vec2.DistanceSquared(fromPos, southDoor) <= Vec2.DistanceSquared(fromPos, northDoor)
                ? southDoor
                : northDoor;
        }

        public float GetCellCoverFacingRadians(int cx, int cy, float defaultFacing)
        {
            MapCell c = GetCell(cx, cy);
            if (c.IsWindow || c.IsDoorway)
            {
                int bIdx = FindBuildingAtCell(cx, cy);
                if (bIdx >= 0)
                {
                    var b = Buildings[bIdx];
                    if (cy == b.MinCellY) return -MathF.PI * 0.5f;                  // North window/door faces North (-Y)
                    if (cy == b.MinCellY + b.HeightCells - 1) return MathF.PI * 0.5f; // South window/door faces South (+Y)
                    if (cx == b.MinCellX) return MathF.PI;                          // West window faces West (-X)
                    if (cx == b.MinCellX + b.WidthCells - 1) return 0f;             // East window faces East (+X)
                }
            }
            return defaultFacing;
        }

        public float GetCellCoverFacingRadians(Vec2 unitPos, Vec2 threatPos)
        {
            WorldToFineCell(unitPos, out int cx, out int cy);
            float defaultFacing = MathF.Atan2(threatPos.Y - unitPos.Y, threatPos.X - unitPos.X);
            return GetCellCoverFacingRadians(cx, cy, defaultFacing);
        }

        /// <summary>
        /// Deterministically generates the Bermuda Lite 2000x2000m map with 14 iconic POIs,
        /// elevation ridges, buildings, cover clusters, roads, and 8 vehicles.
        /// </summary>
        public static MapData GenerateBermudaLite(ulong seed = 10042026UL)
        {
            var rng = new Rng(seed);
            var map = new MapData();

            var poiSpecs = new (byte id, string code, string name, float x, float y, float radius, float tierWeight, bool hotspot, byte height)[]
            {
                (1,  "poi_peak",        "Peak",            1020f, 1040f, 125f, 1.50f, true,  2),
                (2,  "poi_clocktower",  "Clock Tower",      560f, 1180f, 115f, 1.45f, true,  1),
                (3,  "poi_pochinok",    "Pochinok",        1360f, 1340f, 110f, 1.25f, false, 1),
                (4,  "poi_mill",        "Mill",            1580f,  520f, 105f, 1.35f, true,  2),
                (5,  "poi_mars",        "Mars Electric",    960f, 1620f, 115f, 1.20f, false, 1),
                (6,  "poi_factory",     "Factory",          680f, 1520f, 110f, 1.35f, true,  1),
                (7,  "poi_bimasakti",   "Bimasakti Strip",  980f,  740f, 110f, 1.30f, true,  1),
                (8,  "poi_katulistiwa", "Katulistiwa",      640f,  680f, 100f, 1.15f, false, 1),
                (9,  "poi_kotatua",     "Kota Tua",        1420f, 1650f, 100f, 1.10f, false, 1),
                (10, "poi_sentosa",     "Sentosa",         1680f, 1520f, 105f, 1.30f, false, 1),
                (11, "poi_observatory", "Observatory",      380f,  520f, 105f, 1.35f, false, 2),
                (12, "poi_hangar",      "Hangar",           440f, 1460f, 105f, 1.15f, false, 1),
                (13, "poi_plantation",  "Plantation",      1260f,  460f, 110f, 1.15f, false, 1),
                (14, "poi_rimnam",      "Rim Nam Village",  360f,  960f, 100f, 1.10f, false, 0)
            };

            foreach (var p in poiSpecs)
            {
                map.Pois.Add(new PoiDef
                {
                    PoiId = p.id,
                    Id = p.code,
                    Name = p.name,
                    CenterX = p.x,
                    CenterY = p.y,
                    RadiusMeters = p.radius,
                    LootTierWeight = p.tierWeight,
                    IsHotspot = p.hotspot,
                    HeightLevel = p.height
                });
            }

            // Initialize base terrain
            for (int y = 0; y < FineGridSize; y++)
            {
                for (int x = 0; x < FineGridSize; x++)
                {
                    Vec2 world = FineCellToWorldCenter(x, y);
                    bool isBorderWater = x < 3 || x >= FineGridSize - 3 || y < 3 || y >= FineGridSize - 3;

                    byte height = 1;
                    // Central Peak ridge + NE Mill ridge + NW Observatory ridge
                    float distPeak = Vec2.Distance(world, new Vec2(1020f, 1040f));
                    float distMill = Vec2.Distance(world, new Vec2(1580f, 520f));
                    float distObs = Vec2.Distance(world, new Vec2(380f, 520f));
                    if (distPeak < 210f || distMill < 160f || distObs < 150f)
                    {
                        height = 2;
                    }
                    else if (world.X < 420f && world.Y > 860f && world.Y < 1100f)
                    {
                        height = 0; // Rim Nam basin
                    }

                    byte baseCover = (byte)rng.NextInt(8, 28);
                    if (rng.Chance(0.08f)) baseCover = (byte)rng.NextInt(35, 65); // Natural rock/tree cover

                    byte poiId = 0;
                    for (int i = 0; i < map.Pois.Count; i++)
                    {
                        if (Vec2.Distance(world, map.Pois[i].Center) <= map.Pois[i].RadiusMeters)
                        {
                            poiId = map.Pois[i].PoiId;
                            height = map.Pois[i].HeightLevel;
                            baseCover = (byte)Math.Max((int)baseCover, 35);
                            break;
                        }
                    }

                    ref MapCell cell = ref map.GetCellRef(x, y);
                    cell.HeightLevel = height;
                    cell.CoverValue = isBorderWater ? (byte)0 : baseCover;
                    cell.Flags = isBorderWater ? CellFlags.Water : CellFlags.Walkable;
                    cell.PoiId = poiId;
                }
            }

            // Carve main road network connecting POIs
            for (int i = 0; i < map.Pois.Count; i++)
            {
                PoiDef a = map.Pois[i];
                PoiDef b = map.Pois[(i + 1) % map.Pois.Count];
                CarveRoad(map, a.Center, b.Center);
            }

            // Stamp buildings inside each POI
            int buildingId = 1;
            int clusterId = 1;
            foreach (var poi in map.Pois)
            {
                WorldToFineCell(poi.Center, out int pcx, out int pcy);
                int bCount = poi.IsHotspot ? 5 : 3;
                for (int b = 0; b < bCount; b++)
                {
                    float angle = (b / (float)bCount) * MathF.PI * 2f + 0.3f;
                    int offsetCells = 4 + (b % 2) * 3;
                    int bx = Math.Clamp(pcx + (int)MathF.Round(MathF.Cos(angle) * offsetCells) - 2, 8, FineGridSize - 12);
                    int by = Math.Clamp(pcy + (int)MathF.Round(MathF.Sin(angle) * offsetCells) - 2, 8, FineGridSize - 12);
                    int bw = 4;
                    int bh = 3;

                    map.Buildings.Add(new BuildingFootprintDef
                    {
                        BuildingId = buildingId++,
                        PoiId = poi.PoiId,
                        MinCellX = bx,
                        MinCellY = by,
                        WidthCells = bw,
                        HeightCells = bh,
                        HeightMeters = poi.IsHotspot ? 8.0f : 5.5f
                    });

                    Vec2 bCenter = FineCellToWorldCenter(bx + bw / 2, by + bh / 2);
                    map.LootClusters.Add(new LootClusterSpawnDef
                    {
                        ClusterId = clusterId++,
                        PoiId = poi.PoiId,
                        X = bCenter.X,
                        Y = bCenter.Y,
                        Richness = poi.LootTierWeight
                    });
                }
            }

            map.ApplyBuildingAndCoverTopology();

            // Spawn 8 vehicles near major POI roads
            for (int v = 0; v < 8; v++)
            {
                PoiDef poi = map.Pois[v % map.Pois.Count];
                Vec2 spawnPos = map.ClampToWalkableWorld(poi.Center + new Vec2(45f, -35f));
                map.VehicleSpawns.Add(new VehicleSpawnDef
                {
                    VehicleId = v + 1,
                    X = spawnPos.X,
                    Y = spawnPos.Y,
                    MaxHp = 450f,
                    SpeedMultiplier = 2.0f
                });
            }

            map.RebuildMacroGrid();
            return map;
        }

        private static void CarveRoad(MapData map, Vec2 from, Vec2 to)
        {
            WorldToFineCell(from, out int x0, out int y0);
            WorldToFineCell(to, out int x1, out int y1);

            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                ref MapCell c = ref map.GetCellRef(x0, y0);
                if (!c.IsWater)
                {
                    c.Flags = CellFlags.Walkable | CellFlags.Road;
                    c.CoverValue = Math.Min(c.CoverValue, (byte)12);
                }
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
