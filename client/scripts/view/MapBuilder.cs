using System;
using System.Collections.Generic;
using FFM.Client.Art;
using FFM.Client.Sim;
using FFM.Sim.Core;
using FFM.Sim.Map;
using Godot;

namespace FFM.Client.View
{
    /// <summary>
    /// Procedural 3D Bermuda-Lite Low-Poly Tactical Diorama Map Builder (§6.1, §6.7, WP-12).
    /// Renders animated ocean water, faceted island terrain with beaches/cliffs/roads,
    /// 6-style Low-Poly buildings with camera-distance roof fade, MultiMesh vegetation/cover props,
    /// POI tactical rings + Label3D headers, and shader-driven Safe/Electromagnetic Zone walls.
    /// </summary>
    public partial class MapBuilder : Node3D
    {
        private sealed class BuildingVisualEntry
        {
            public int BuildingId;
            public float MinWorldX;
            public float MaxWorldX;
            public float MinWorldZ;
            public float MaxWorldZ;
            public Vector3 CenterWorld;
            public StandardMaterial3D RoofMaterial = null!;
            public MeshInstance3D GarrisonRimInstance = null!;
            public StandardMaterial3D GarrisonRimMaterial = null!;
            public Label3D GarrisonLabel = null!;
            public float CurrentRoofAlpha = 0.88f;
        }

        private MeshInstance3D? _terrainMeshInstance;
        private MeshInstance3D? _waterMeshInstance;
        private Node3D? _buildingsRoot;
        private Node3D? _propsRoot;
        private Node3D? _poisRoot;
        private MeshInstance3D? _currentZoneCylinder;
        private MeshInstance3D? _nextZoneRing;
        private StandardMaterial3D? _wallMaterial;
        private readonly List<Label3D> _poiLabels = new();
        private readonly List<BuildingVisualEntry> _buildingVisuals = new();

        public void BuildMap(MapData map)
        {
            ClearChildren();

            BuildLightingAndEnvironment();
            BuildWaterPlane();
            BuildTerrainMesh(map);
            BuildBuildings(map);
            BuildCoverPropsMultiMesh(map);
            BuildPoiMarkers(map);
            BuildZoneIndicators();
        }

        public void UpdateZoneAndCamera(InterpolatedFrameState frame, float cameraDistance)
        {
            if (_currentZoneCylinder != null)
            {
                float r = Math.Max(5f, frame.ZoneRadius);
                _currentZoneCylinder.Position = new Vector3(frame.ZoneCenter3D.X, 21f, frame.ZoneCenter3D.Z);
                _currentZoneCylinder.Scale = new Vector3(r, 1f, r);
            }

            if (_nextZoneRing != null)
            {
                float nr = Math.Max(4f, frame.NextZoneRadius);
                _nextZoneRing.Position = new Vector3(frame.NextZoneCenter3D.X, 2.4f, frame.NextZoneCenter3D.Z);
                _nextZoneRing.Scale = new Vector3(nr, 1f, nr);
                _nextZoneRing.Visible = frame.ZoneStage > 0 && frame.NextZoneRadius > 1f;
            }

            float baseRoofAlpha = Math.Clamp((cameraDistance - 140f) / 210f, 0.20f, 0.92f);
            float pulse = 0.5f + 0.5f * MathF.Sin(frame.TimeSec * 4.5f);

            // Per-building Smart Roof Cutaway + Compound Garrison / Breach Rim & Tag
            for (int bIdx = 0; bIdx < _buildingVisuals.Count; bIdx++)
            {
                BuildingVisualEntry entry = _buildingVisuals[bIdx];
                int primarySquad = -1;
                int primaryCount = 0;
                bool contested = false;
                bool unitNear = false;
                int totalInside = 0;

                for (int uIdx = 0; uIdx < frame.Units.Length; uIdx++)
                {
                    ref readonly var u = ref frame.Units[uIdx];
                    if (u.State == FFM.Sim.Data.UnitState.Eliminated || u.WorldPos3D.LengthSquared() < 1f) continue;

                    float ux = u.WorldPos3D.X;
                    float uz = u.WorldPos3D.Z;

                    if (ux >= entry.MinWorldX && ux <= entry.MaxWorldX &&
                        uz >= entry.MinWorldZ && uz <= entry.MaxWorldZ)
                    {
                        totalInside++;
                        if (primarySquad == -1 || primarySquad == u.SquadId)
                        {
                            primarySquad = u.SquadId;
                            primaryCount++;
                        }
                        else
                        {
                            contested = true;
                        }
                    }
                    else if (ux >= entry.MinWorldX - 13f && ux <= entry.MaxWorldX + 13f &&
                             uz >= entry.MinWorldZ - 13f && uz <= entry.MaxWorldZ + 13f)
                    {
                        unitNear = true;
                        if (primarySquad != -1 && u.SquadId != primarySquad)
                        {
                            contested = true;
                        }
                    }
                }

                float targetAlpha = totalInside > 0
                    ? 0.06f
                    : (unitNear ? Math.Min(baseRoofAlpha, 0.24f) : baseRoofAlpha);

                entry.CurrentRoofAlpha = Mathf.Lerp(entry.CurrentRoofAlpha, targetAlpha, 0.22f);
                Color rc = entry.RoofMaterial.AlbedoColor;
                rc.A = entry.CurrentRoofAlpha;
                entry.RoofMaterial.AlbedoColor = rc;

                if (totalInside > 0 && primarySquad >= 0)
                {
                    entry.GarrisonRimInstance.Visible = true;
                    entry.GarrisonLabel.Visible = true;

                    if (contested)
                    {
                        Color breachCol = new Color(1.0f, 0.23f, 0.19f).Lerp(new Color(1.0f, 0.72f, 0.0f), pulse);
                        entry.GarrisonRimMaterial.AlbedoColor = new Color(breachCol.R, breachCol.G, breachCol.B, 0.68f);
                        entry.GarrisonLabel.Text = $"⚔️ BREACHING! (×{totalInside})";
                        entry.GarrisonLabel.Modulate = breachCol.Lightened(0.18f);
                    }
                    else
                    {
                        Color teamCol = BattlefieldView.GetSquadColor(primarySquad, null);
                        entry.GarrisonRimMaterial.AlbedoColor = new Color(teamCol.R, teamCol.G, teamCol.B, 0.48f + 0.14f * pulse);
                        entry.GarrisonLabel.Text = $"🏠 GARRISON ×{primaryCount}";
                        entry.GarrisonLabel.Modulate = teamCol.Lightened(0.24f);
                    }
                }
                else
                {
                    entry.GarrisonRimInstance.Visible = false;
                    entry.GarrisonLabel.Visible = false;
                }
            }

            // Scale POI labels gently with camera distance for broadcast legibility
            float labelScale = Math.Clamp(cameraDistance / 420f, 0.65f, 2.15f);
            for (int i = 0; i < _poiLabels.Count; i++)
            {
                _poiLabels[i].Scale = new Vector3(labelScale, labelScale, labelScale);
            }
        }

        private void ClearChildren()
        {
            _poiLabels.Clear();
            _buildingVisuals.Clear();
            foreach (Node child in GetChildren())
            {
                child.QueueFree();
            }
        }

        private void BuildLightingAndEnvironment()
        {
            var sun = new DirectionalLight3D
            {
                Name = "TacticalSunLight",
                RotationDegrees = new Vector3(-54f, 36f, 0f),
                LightColor = new Color(1.0f, 0.96f, 0.89f),
                LightEnergy = 1.22f,
                ShadowEnabled = true,
                DirectionalShadowMaxDistance = 950f,
                ShadowBias = 0.04f,
                ShadowNormalBias = 1.4f
            };
            AddChild(sun);

            var fillLight = new DirectionalLight3D
            {
                Name = "TacticalSkyFillLight",
                RotationDegrees = new Vector3(-40f, -144f, 0f),
                LightColor = new Color(0.54f, 0.72f, 0.96f),
                LightEnergy = 0.30f,
                ShadowEnabled = false
            };
            AddChild(fillLight);

            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.045f, 0.07f, 0.115f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.38f, 0.45f, 0.56f),
                AmbientLightEnergy = 0.82f,
                TonemapMode = Godot.Environment.ToneMapper.Aces,
                TonemapExposure = 1.04f,
                GlowEnabled = true,
                GlowIntensity = 0.42f,
                GlowBloom = 0.06f,
                FogEnabled = true,
                FogLightColor = new Color(0.08f, 0.13f, 0.21f),
                FogLightEnergy = 0.65f,
                FogDensity = 0.00028f
            };
            var worldEnv = new WorldEnvironment
            {
                Name = "DioramaEnvironment",
                Environment = env
            };
            AddChild(worldEnv);
        }

        private void BuildWaterPlane()
        {
            var planeMesh = new PlaneMesh
            {
                Size = new Vector2(2800f, 2800f),
                SubdivideWidth = 96,
                SubdivideDepth = 96
            };

            Material waterMat;
            Shader? shader = GD.Load<Shader>("res://client/shaders/water_ocean.gdshader");
            if (shader != null)
            {
                waterMat = new ShaderMaterial { Shader = shader };
            }
            else
            {
                waterMat = new StandardMaterial3D
                {
                    AlbedoColor = new Color(0.09f, 0.19f, 0.30f),
                    Roughness = 0.25f
                };
            }

            _waterMeshInstance = new MeshInstance3D
            {
                Name = "BermudaOceanWater",
                Mesh = planeMesh,
                MaterialOverride = waterMat,
                Position = new Vector3(1000f, -0.35f, 1000f)
            };
            AddChild(_waterMeshInstance);
        }

        private void BuildTerrainMesh(MapData map)
        {
            const int step = 2; // 128x128 mesh quads covering 256x256 fine cells
            int gridCount = MapData.FineGridSize / step;
            float quadSize = MapData.FineCellMeters * step;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            for (int gy = 0; gy < gridCount; gy++)
            {
                for (int gx = 0; gx < gridCount; gx++)
                {
                    int cx = gx * step;
                    int cy = gy * step;
                    MapCell cell = map.GetCell(cx, cy);

                    float x0 = gx * quadSize;
                    float z0 = gy * quadSize;
                    float x1 = x0 + quadSize;
                    float z1 = z0 + quadSize;

                    float h00 = GetSmoothedVertexHeight(map, cx, cy);
                    float h10 = GetSmoothedVertexHeight(map, Math.Min(cx + step, MapData.FineGridSize - 1), cy);
                    float h01 = GetSmoothedVertexHeight(map, cx, Math.Min(cy + step, MapData.FineGridSize - 1));
                    float h11 = GetSmoothedVertexHeight(map, Math.Min(cx + step, MapData.FineGridSize - 1), Math.Min(cy + step, MapData.FineGridSize - 1));

                    bool nearWater = IsNearWater(map, cx, cy);
                    float maxDiff = MathF.Max(MathF.Abs(h11 - h00), MathF.Abs(h10 - h01));
                    Color col = GetTacticalCellColor(cell, nearWater, maxDiff > 1.35f, gx, gy);

                    Vector3 v00 = new(x0, h00, z0);
                    Vector3 v10 = new(x1, h10, z0);
                    Vector3 v01 = new(x0, h01, z1);
                    Vector3 v11 = new(x1, h11, z1);

                    // Triangle 1 (faceted normal)
                    Vector3 n1 = (v10 - v00).Cross(v11 - v00).Normalized();
                    if (n1.Y < 0f) n1 = -n1;
                    st.SetColor(col);
                    st.SetNormal(n1);
                    st.AddVertex(v00);
                    st.AddVertex(v10);
                    st.AddVertex(v11);

                    // Triangle 2 (slightly varied facet shade)
                    Vector3 n2 = (v11 - v00).Cross(v01 - v00).Normalized();
                    if (n2.Y < 0f) n2 = -n2;
                    st.SetColor(col.Darkened(0.03f));
                    st.SetNormal(n2);
                    st.AddVertex(v00);
                    st.AddVertex(v11);
                    st.AddVertex(v01);
                }
            }

            var mat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 0.90f,
                Metallic = 0.04f
            };
            st.SetMaterial(mat);

            _terrainMeshInstance = new MeshInstance3D
            {
                Name = "BermudaLiteTerrain",
                Mesh = st.Commit()
            };
            AddChild(_terrainMeshInstance);
        }

        private static float GetSmoothedVertexHeight(MapData map, int cx, int cy)
        {
            float sum = 0f;
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = Math.Clamp(cx + dx, 0, MapData.FineGridSize - 1);
                    int ny = Math.Clamp(cy + dy, 0, MapData.FineGridSize - 1);
                    MapCell c = map.GetCell(nx, ny);
                    if (c.IsWater)
                    {
                        sum += -1.35f;
                    }
                    else
                    {
                        sum += c.HeightLevel * SnapshotInterpolator.HeightPerLevelMeters;
                    }
                    count++;
                }
            }
            return sum / count;
        }

        private static bool IsNearWater(MapData map, int cx, int cy)
        {
            for (int dy = -2; dy <= 2; dy += 2)
            {
                for (int dx = -2; dx <= 2; dx += 2)
                {
                    int nx = Math.Clamp(cx + dx, 0, MapData.FineGridSize - 1);
                    int ny = Math.Clamp(cy + dy, 0, MapData.FineGridSize - 1);
                    if (map.GetCell(nx, ny).IsWater) return true;
                }
            }
            return false;
        }

        private static Color GetTacticalCellColor(MapCell cell, bool nearWater, bool isCliffSlope, int gx, int gy)
        {
            float checker = ((gx + gy) & 1) == 0 ? 0.0f : 0.012f;

            if (cell.IsWater)
            {
                return new Color(0.08f, 0.14f, 0.21f); // Seabed under water shader
            }
            if (cell.IsRoad)
            {
                return new Color(0.25f + checker, 0.27f + checker, 0.31f + checker); // Asphalt road
            }
            if (isCliffSlope)
            {
                return new Color(0.30f + checker, 0.29f + checker, 0.27f + checker); // Low-poly rocky cliff facet
            }
            if (nearWater && cell.HeightLevel == 0)
            {
                return new Color(0.36f + checker, 0.33f + checker, 0.25f + checker); // Warm tactical sand beach
            }
            if (cell.PoiId > 0)
            {
                return cell.HeightLevel == 2
                    ? new Color(0.25f + checker, 0.29f + checker, 0.26f + checker)
                    : new Color(0.22f + checker, 0.26f + checker, 0.25f + checker);
            }
            return cell.HeightLevel switch
            {
                0 => new Color(0.15f + checker, 0.22f + checker, 0.18f + checker), // Basin grass
                2 => new Color(0.25f + checker, 0.31f + checker, 0.22f + checker), // Highland plateau
                _ => new Color(0.18f + checker, 0.25f + checker, 0.19f + checker)  // Mid-island turf
            };
        }

        private void BuildBuildings(MapData map)
        {
            _buildingsRoot = new Node3D { Name = "BuildingsRoot" };
            AddChild(_buildingsRoot);

            _wallMaterial = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 0.85f,
                Metallic = 0.05f
            };

            for (int i = 0; i < map.Buildings.Count; i++)
            {
                var b = map.Buildings[i];
                float minX = b.MinCellX * MapData.FineCellMeters;
                float minZ = b.MinCellY * MapData.FineCellMeters;
                float width = b.WidthCells * MapData.FineCellMeters;
                float depth = b.HeightCells * MapData.FineCellMeters;

                MapCell baseCell = map.GetCell(b.MinCellX + b.WidthCells / 2, b.MinCellY + b.HeightCells / 2);
                float baseY = baseCell.HeightLevel * SnapshotInterpolator.HeightPerLevelMeters;

                int styleIndex = Math.Abs(b.BuildingId * 7 + baseCell.PoiId * 3 + b.WidthCells) % 6;
                var (bodyMesh, roofMesh) = LowPolyMeshFactory.CreateBuildingMeshes(width, b.HeightMeters, depth, styleIndex);

                Vector3 buildingOrigin = new(minX + width * 0.5f, baseY, minZ + depth * 0.5f);

                var bodyInst = new MeshInstance3D
                {
                    Name = $"BldBody_{b.BuildingId}",
                    Mesh = bodyMesh,
                    MaterialOverride = _wallMaterial,
                    Position = buildingOrigin
                };
                _buildingsRoot.AddChild(bodyInst);

                var roofMat = new StandardMaterial3D
                {
                    VertexColorUseAsAlbedo = true,
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(1f, 1f, 1f, 0.88f),
                    Roughness = 0.76f
                };

                var roofInst = new MeshInstance3D
                {
                    Name = $"BldRoof_{b.BuildingId}",
                    Mesh = roofMesh,
                    MaterialOverride = roofMat,
                    Position = buildingOrigin
                };
                _buildingsRoot.AddChild(roofInst);

                // Tactical Garrison Perimeter Rim Frame + Floating Garrison/Breach Status Label
                var rimMat = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = new Color(0f, 0.85f, 1f, 0.55f),
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled
                };
                var rimInst = new MeshInstance3D
                {
                    Name = $"BldGarrisonRim_{b.BuildingId}",
                    Mesh = CreatePerimeterRimMesh(width + 2.2f, depth + 2.2f, 0.75f),
                    MaterialOverride = rimMat,
                    Position = buildingOrigin + new Vector3(0f, 0.32f, 0f),
                    Visible = false
                };
                _buildingsRoot.AddChild(rimInst);

                var garrisonLabel = new Label3D
                {
                    Name = $"BldGarrisonLabel_{b.BuildingId}",
                    Text = "🏠 GARRISON",
                    FontSize = 36,
                    OutlineSize = 11,
                    Modulate = new Color(0.0f, 0.88f, 1.0f),
                    OutlineModulate = new Color(0.03f, 0.05f, 0.09f, 0.96f),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    PixelSize = 0.16f,
                    Position = buildingOrigin + new Vector3(0f, b.HeightMeters + 3.4f, 0f),
                    Visible = false
                };
                _buildingsRoot.AddChild(garrisonLabel);

                _buildingVisuals.Add(new BuildingVisualEntry
                {
                    BuildingId = b.BuildingId,
                    MinWorldX = minX,
                    MaxWorldX = minX + width,
                    MinWorldZ = minZ,
                    MaxWorldZ = minZ + depth,
                    CenterWorld = buildingOrigin,
                    RoofMaterial = roofMat,
                    GarrisonRimInstance = rimInst,
                    GarrisonRimMaterial = rimMat,
                    GarrisonLabel = garrisonLabel,
                    CurrentRoofAlpha = 0.88f
                });
            }
        }

        private static ArrayMesh CreatePerimeterRimMesh(float width, float depth, float thickness)
        {
            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            float ix = hx - thickness;
            float iz = hz - thickness;

            Vector3 o0 = new(-hx, 0f, -hz), o1 = new(hx, 0f, -hz), o2 = new(hx, 0f, hz), o3 = new(-hx, 0f, hz);
            Vector3 i0 = new(-ix, 0f, -iz), i1 = new(ix, 0f, -iz), i2 = new(ix, 0f, iz), i3 = new(-ix, 0f, iz);

            AddFlatStripQuad(st, o0, o1, i1, i0);
            AddFlatStripQuad(st, o1, o2, i2, i1);
            AddFlatStripQuad(st, o2, o3, i3, i2);
            AddFlatStripQuad(st, o3, o0, i0, i3);

            st.GenerateNormals();
            return st.Commit();
        }

        private static void AddFlatStripQuad(SurfaceTool st, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3)
        {
            st.SetNormal(Vector3.Up);
            st.AddVertex(v0);
            st.AddVertex(v1);
            st.AddVertex(v2);

            st.AddVertex(v0);
            st.AddVertex(v2);
            st.AddVertex(v3);
        }

        private void BuildCoverPropsMultiMesh(MapData map)
        {
            _propsRoot = new Node3D { Name = "CoverPropsRoot" };
            AddChild(_propsRoot);

            var palmTransforms = new List<Transform3D>(512);
            var treeTransforms = new List<Transform3D>(768);
            var bushTransforms = new List<Transform3D>(768);
            var rockTransforms = new List<Transform3D>(512);
            var containerTransforms = new List<Transform3D>(256);
            var sandbagTransforms = new List<Transform3D>(512);
            var barrierTransforms = new List<Transform3D>(512);
            var tacticalCrateTransforms = new List<Transform3D>(512);

            // 1. Place 1:1 aligned TacticalLowCover props at exact Sim cell centers
            for (int cy = 2; cy < MapData.FineGridSize - 2; cy++)
            {
                for (int cx = 2; cx < MapData.FineGridSize - 2; cx++)
                {
                    MapCell cell = map.GetCell(cx, cy);
                    if (!cell.IsTacticalLowCover || cell.IsWater || cell.IsBuilding) continue;

                    Vec2 cellCenter = MapData.FineCellToWorldCenter(cx, cy);
                    float wx = cellCenter.X;
                    float wz = cellCenter.Y;
                    float wy = GetSmoothedVertexHeight(map, cx, cy);
                    if (wy < 0.05f) continue;

                    float coverFacing = map.GetCellCoverFacingRadians(cx, cy, 0f);
                    // Sim FacingRadians: 0 = +X, +PI/2 = +Z -> Godot Yaw around +Y is -coverFacing
                    Basis coverBasis = Basis.FromEuler(new Vector3(0f, -coverFacing, 0f));
                    var coverXform = new Transform3D(coverBasis, new Vector3(wx, wy, wz));

                    uint h = HashCell(cx, cy);
                    if (cell.IsRoad || (h % 3 == 0))
                    {
                        barrierTransforms.Add(coverXform);
                    }
                    else if (h % 3 == 1)
                    {
                        sandbagTransforms.Add(coverXform);
                    }
                    else
                    {
                        tacticalCrateTransforms.Add(coverXform);
                    }
                }
            }

            // 2. Place natural foliage & industrial yard cover props
            for (int cy = 4; cy < MapData.FineGridSize - 4; cy += 2)
            {
                for (int cx = 4; cx < MapData.FineGridSize - 4; cx += 2)
                {
                    MapCell cell = map.GetCell(cx, cy);
                    if (cell.IsWater || cell.IsRoad || cell.IsBuilding || cell.IsTacticalLowCover) continue;

                    // Keep clear buffer around building footprints
                    if (HasAdjacentBuildingOrRoad(map, cx, cy)) continue;

                    uint hash = HashCell(cx, cy);
                    bool nearWater = IsNearWater(map, cx, cy);

                    // Spawn deterministic cover props where cell has cover or on scenic island zones
                    bool shouldSpawn = cell.CoverValue >= 0.16f
                        || (nearWater && (hash % 11 == 0))
                        || (cell.PoiId == 0 && (hash % 19 == 0));

                    if (!shouldSpawn) continue;

                    float wx = (cx + 0.5f) * MapData.FineCellMeters + ((hash & 0xFF) / 255f - 0.5f) * 5.5f;
                    float wz = (cy + 0.5f) * MapData.FineCellMeters + (((hash >> 8) & 0xFF) / 255f - 0.5f) * 5.5f;
                    float wy = GetSmoothedVertexHeight(map, cx, cy);
                    if (wy < 0.05f) continue;

                    float yaw = ((hash >> 16) & 0xFF) / 255f * MathF.PI * 2f;
                    float scale = 0.85f + ((hash >> 24) & 0xFF) / 255f * 0.45f;

                    Basis basis = Basis.FromEuler(new Vector3(0f, yaw, 0f)).Scaled(new Vector3(scale, scale, scale));
                    var xform = new Transform3D(basis, new Vector3(wx, wy, wz));

                    if (cell.PoiId > 0 && (cell.PoiId % 3 == 0) && (hash % 5 == 0))
                    {
                        // Industrial shipping containers in compound yards
                        Basis cBasis = Basis.FromEuler(new Vector3(0f, ((hash & 1) == 0) ? 0f : MathF.PI * 0.5f, 0f));
                        containerTransforms.Add(new Transform3D(cBasis, new Vector3(wx, wy, wz)));
                    }
                    else if (nearWater || (cell.HeightLevel == 0 && (hash % 3 == 0)))
                    {
                        palmTransforms.Add(xform);
                    }
                    else if (cell.HeightLevel == 2 && (hash % 3 == 0))
                    {
                        rockTransforms.Add(xform);
                    }
                    else if ((hash % 5) < 2)
                    {
                        treeTransforms.Add(xform);
                    }
                    else if ((hash % 5) < 4)
                    {
                        bushTransforms.Add(xform);
                    }
                    else
                    {
                        rockTransforms.Add(xform);
                    }
                }
            }

            AddMultiMeshBatch("PalmTreesBatch", LowPolyMeshFactory.PropKind.PalmTree, palmTransforms);
            AddMultiMeshBatch("BroadleafTreesBatch", LowPolyMeshFactory.PropKind.BroadleafTree, treeTransforms);
            AddMultiMeshBatch("BushClumpsBatch", LowPolyMeshFactory.PropKind.BushClump, bushTransforms);
            AddMultiMeshBatch("RockClustersBatch", LowPolyMeshFactory.PropKind.RockCluster, rockTransforms);
            AddMultiMeshBatch("CargoContainersBatch", LowPolyMeshFactory.PropKind.CargoContainer, containerTransforms);
            AddMultiMeshBatch("SandbagWallsBatch", LowPolyMeshFactory.PropKind.SandbagWall, sandbagTransforms);
            AddMultiMeshBatch("ConcreteBarriersBatch", LowPolyMeshFactory.PropKind.ConcreteBarrier, barrierTransforms);
            AddMultiMeshBatch("TacticalCratesBatch", LowPolyMeshFactory.PropKind.TacticalCrateCover, tacticalCrateTransforms);
        }

        private void AddMultiMeshBatch(string name, LowPolyMeshFactory.PropKind kind, List<Transform3D> transforms)
        {
            if (_propsRoot == null || transforms.Count == 0) return;

            var multiMesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = LowPolyMeshFactory.GetPropMesh(kind),
                InstanceCount = transforms.Count
            };

            for (int i = 0; i < transforms.Count; i++)
            {
                multiMesh.SetInstanceTransform(i, transforms[i]);
            }

            var mmi = new MultiMeshInstance3D
            {
                Name = name,
                Multimesh = multiMesh,
                MaterialOverride = _wallMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.On
            };
            _propsRoot.AddChild(mmi);
        }

        private static bool HasAdjacentBuildingOrRoad(MapData map, int cx, int cy)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    MapCell c = map.GetCell(cx + dx, cy + dy);
                    if (c.IsBuilding || c.IsRoad) return true;
                }
            }
            return false;
        }

        private static uint HashCell(int x, int y)
        {
            uint h = (uint)(x * 374761393 + y * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }

        private void BuildPoiMarkers(MapData map)
        {
            _poisRoot = new Node3D { Name = "PoisRoot" };
            AddChild(_poisRoot);

            foreach (var poi in map.Pois)
            {
                float y = poi.HeightLevel * SnapshotInterpolator.HeightPerLevelMeters;
                var poiNode = new Node3D
                {
                    Name = $"POI_{poi.Id}",
                    Position = new Vector3(poi.CenterX, y, poi.CenterY)
                };

                var ringMesh = new CylinderMesh
                {
                    TopRadius = poi.RadiusMeters,
                    BottomRadius = poi.RadiusMeters,
                    Height = 0.35f,
                    RadialSegments = 36
                };
                var ringMat = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    AlbedoColor = poi.IsHotspot
                        ? new Color(1.0f, 0.72f, 0.0f, 0.09f)
                        : new Color(0.68f, 0.78f, 0.92f, 0.05f)
                };
                var ringInst = new MeshInstance3D
                {
                    Mesh = ringMesh,
                    MaterialOverride = ringMat,
                    Position = new Vector3(0f, 0.25f, 0f)
                };
                poiNode.AddChild(ringInst);

                var label = new Label3D
                {
                    Text = poi.IsHotspot ? $"★ {poi.Name.ToUpperInvariant()}" : poi.Name.ToUpperInvariant(),
                    FontSize = poi.IsHotspot ? 56 : 44,
                    OutlineSize = 14,
                    Modulate = poi.IsHotspot ? new Color(1.0f, 0.84f, 0.25f) : new Color(0.88f, 0.93f, 0.99f),
                    OutlineModulate = new Color(0.04f, 0.06f, 0.10f, 0.96f),
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    Position = new Vector3(0f, 24f, 0f),
                    PixelSize = 0.28f
                };
                poiNode.AddChild(label);
                _poiLabels.Add(label);

                _poisRoot.AddChild(poiNode);
            }
        }

        private void BuildZoneIndicators()
        {
            var zoneMesh = new CylinderMesh
            {
                TopRadius = 1f,
                BottomRadius = 1f,
                Height = 42f,
                RadialSegments = 72,
                CapTop = false,
                CapBottom = false
            };

            Material zoneMat;
            Shader? forcefieldShader = GD.Load<Shader>("res://client/shaders/zone_forcefield.gdshader");
            if (forcefieldShader != null)
            {
                zoneMat = new ShaderMaterial { Shader = forcefieldShader };
            }
            else
            {
                zoneMat = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    AlbedoColor = new Color(0.0f, 0.83f, 1.0f, 0.22f)
                };
            }

            _currentZoneCylinder = new MeshInstance3D
            {
                Name = "CurrentSafeZoneWall",
                Mesh = zoneMesh,
                MaterialOverride = zoneMat,
                Position = new Vector3(1000f, 21f, 1000f),
                Scale = new Vector3(1000f, 1f, 1000f)
            };
            AddChild(_currentZoneCylinder);

            var nextMesh = new CylinderMesh
            {
                TopRadius = 1f,
                BottomRadius = 1f,
                Height = 3.2f,
                RadialSegments = 64,
                CapTop = false,
                CapBottom = false
            };
            var nextMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.28f)
            };
            _nextZoneRing = new MeshInstance3D
            {
                Name = "NextSafeZoneRing",
                Mesh = nextMesh,
                MaterialOverride = nextMat,
                Position = new Vector3(1000f, 2.4f, 1000f),
                Scale = new Vector3(600f, 1f, 600f)
            };
            AddChild(_nextZoneRing);
        }
    }
}
