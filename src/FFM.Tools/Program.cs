using System;
using System.IO;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Tools.AssetLint;

namespace FFM.Tools
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string workspaceRoot = Directory.GetCurrentDirectory();
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--root" && i + 1 < args.Length)
                {
                    workspaceRoot = args[++i];
                }
            }

            bool genSample18 = Array.Exists(args, a => a == "--gen-sample18");
            bool genMap = Array.Exists(args, a => a == "--gen-map");
            bool runAssetLint = Array.Exists(args, a => a == "--asset-lint");

            if (genSample18)
            {
                string targetPath = Path.Combine(workspaceRoot, "configs", "teams", "sample18.json");
                var teams = ConfigLoader.GenerateDeterministicSample18(20261016UL);
                ConfigLoader.SaveJsonFile(targetPath, new TeamsFileConfig { Teams = teams });
                Console.WriteLine($"Generated 18 sample teams ({teams.Count * 5} players) -> {targetPath}");
            }

            if (genMap)
            {
                string mapDir = Path.Combine(workspaceRoot, "configs", "maps", "bermuda_lite");
                Directory.CreateDirectory(mapDir);
                MapData map = MapData.GenerateBermudaLite(10042026UL);
                map.SaveGridBinary(Path.Combine(mapDir, "grid.bin"));
                ConfigLoader.SaveJsonFile(Path.Combine(mapDir, "pois.json"), new PoisFileConfig
                {
                    MapId = "bermuda_lite",
                    SizeMeters = MapData.MapSizeMeters,
                    GridDimension = MapData.FineGridSize,
                    Pois = map.Pois,
                    Buildings = map.Buildings
                });
                ConfigLoader.SaveJsonFile(Path.Combine(mapDir, "spawns.json"), new SpawnsFileConfig
                {
                    LootClusters = map.LootClusters,
                    Vehicles = map.VehicleSpawns
                });
                Console.WriteLine($"Generated Bermuda Lite map ({map.Pois.Count} POIs, {map.Buildings.Count} buildings, {map.VehicleSpawns.Count} vehicles) -> {mapDir}");
            }

            if (runAssetLint)
            {
                var report = AssetLinter.Run(workspaceRoot);
                Console.WriteLine($"AssetLint scanned {report.ScannedFiles} asset files.");
                if (!report.IsValid)
                {
                    foreach (string v in report.Violations)
                    {
                        Console.Error.WriteLine($"[ASSET LINT VIOLATION] {v}");
                    }
                    return 1;
                }
                Console.WriteLine("AssetLint passed with 0 violations.");
            }

            return 0;
        }
    }
}
