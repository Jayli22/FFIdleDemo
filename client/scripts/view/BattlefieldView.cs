using System;
using System.Collections.Generic;
using FFM.Client.Art;
using FFM.Client.Sim;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.View
{
    /// <summary>
    /// Coordinates all 48 articulated Low-Poly UnitViews, curved energy Gloo Walls,
    /// Low-Poly Tactical SUVs, Weapon-Differentiated Shot Tracers, Parabolic 3D Grenades,
    /// Explosion Domes, 3D Impact/Shockwave Pulse Rings, 3D Floating Combat Text,
    /// and the Player Squad 3D Tactical Waypoint Beacon (§6.3, §6.6, WP-13).
    /// </summary>
    public partial class BattlefieldView : Node3D
    {
        private struct ActiveTracer
        {
            public MeshInstance3D Instance;
            public float RemainingSec;
        }

        private struct ActiveGrenadeArc
        {
            public MeshInstance3D Instance;
            public Vector3 StartPos;
            public Vector3 BouncePos;
            public Vector3 EndPos;
            public bool HasIndoorBounce;
            public bool BounceTriggered;
            public float ElapsedSec;
            public float DurationSec;
        }

        private struct ActiveExplosionDome
        {
            public MeshInstance3D Instance;
            public StandardMaterial3D Material;
            public float ElapsedSec;
            public float DurationSec;
        }

        private struct ActivePulseRing
        {
            public MeshInstance3D Instance;
            public StandardMaterial3D Material;
            public float StartRadius;
            public float MaxRadius;
            public Color BaseColor;
            public float ElapsedSec;
            public float DurationSec;
        }

        private struct ActiveFloatingText
        {
            public Label3D Label;
            public Vector3 StartPos;
            public Color BaseColor;
            public float BaseScale;
            public float ElapsedSec;
            public float DurationSec;
        }

        private readonly UnitView[] _unitViews = new UnitView[SnapshotBuffer.MaxUnits];
        private readonly float[] _unitBreachCooldownSec = new float[SnapshotBuffer.MaxUnits];
        private readonly Label3D[] _squadBanners = new Label3D[SnapshotBuffer.MaxSquads];
        private readonly string[] _squadTags = new string[SnapshotBuffer.MaxSquads];
        private readonly Color[] _squadColors = new Color[SnapshotBuffer.MaxSquads];
        private readonly Vector3[] _smoothedSquadBannerPos = new Vector3[SnapshotBuffer.MaxSquads];

        // Zero-allocation Screen-Space Anti-Overlap & Body-Occlusion Solver buffers
        private readonly float[] _bodyScreenX = new float[SnapshotBuffer.MaxUnits];
        private readonly float[] _bodyScreenY = new float[SnapshotBuffer.MaxUnits];
        private readonly float[] _baseHudScreenX = new float[SnapshotBuffer.MaxUnits];
        private readonly float[] _baseHudScreenY = new float[SnapshotBuffer.MaxUnits];
        private readonly float[] _hudOffX = new float[SnapshotBuffer.MaxUnits];
        private readonly float[] _hudOffY = new float[SnapshotBuffer.MaxUnits];
        private readonly bool[] _unitClustered = new bool[SnapshotBuffer.MaxUnits];
        private int _floatingTextSequence;

        private readonly MeshInstance3D[] _glooMeshes = new MeshInstance3D[SnapshotBuffer.MaxGlooWalls];
        private readonly MeshInstance3D[] _vehicleMeshes = new MeshInstance3D[SnapshotBuffer.MaxVehicles];
        private readonly Vec2[] _lastVehicleSimPos = new Vec2[SnapshotBuffer.MaxVehicles];
        private readonly float[] _vehicleYaw = new float[SnapshotBuffer.MaxVehicles];
        private readonly HashSet<int> _activeGlooIds = new(64);
        private readonly Dictionary<int, Vector3> _lastGlooPosById = new(64);

        private readonly Stack<MeshInstance3D> _tracerPool = new(96);
        private readonly List<ActiveTracer> _activeTracers = new(96);

        private readonly Stack<MeshInstance3D> _grenadePool = new(16);
        private readonly List<ActiveGrenadeArc> _activeGrenades = new(16);

        private readonly Stack<(MeshInstance3D Mesh, StandardMaterial3D Mat)> _explosionPool = new(16);
        private readonly List<ActiveExplosionDome> _activeExplosions = new(16);

        private readonly Stack<(MeshInstance3D Mesh, StandardMaterial3D Mat)> _pulseRingPool = new(32);
        private readonly List<ActivePulseRing> _activePulseRings = new(32);

        private readonly Stack<Label3D> _floatingTextPool = new(40);
        private readonly List<ActiveFloatingText> _activeFloatingTexts = new(40);

        private Node3D? _playerCommandBeacon;
        private MeshInstance3D? _beaconRing;
        private MeshInstance3D? _beaconPillar;
        private Label3D? _beaconLabel;
        private StandardMaterial3D? _beaconMat;

        private Node3D? _transportAircraftNode;
        private MeshInstance3D? _leftPropellerMesh;
        private MeshInstance3D? _rightPropellerMesh;
        private Label3D? _aircraftLabel;
        private MeshInstance3D? _flightCorridorBeam;
        private MeshInstance3D? _playerDropTrajectoryBeam;
        private float _propellerSpinAngle;

        private ShaderMaterial? _sharedUnitShaderMat;
        private Material? _glooMaterial;
        private StandardMaterial3D? _vehicleMaterial;
        private StandardMaterial3D? _tracerMaterial;
        private StandardMaterial3D? _sniperTracerMaterial;
        private StandardMaterial3D? _shotgunTracerMaterial;
        private IReadOnlyList<SkillDef>? _skillsCatalog;
        private MapData? _map;
        private int _playerSquadId;
        private bool _initialized;
        private InterpolatedFrameState? _lastFrame;

        private static readonly Color[] DefaultBroadcastPalette =
        {
            new(1.00f, 0.72f, 0.00f), // #FFB800 Free Fire Gold (Player / Squad 0)
            new(0.00f, 0.83f, 1.00f), // #00D4FF Electric Cyan
            new(1.00f, 0.28f, 0.34f), // #FF4757 Crimson Red
            new(0.18f, 0.84f, 0.45f), // #2ED573 Emerald Green
            new(0.66f, 0.38f, 0.96f), // #A861F5 Royal Purple
            new(1.00f, 0.50f, 0.15f), // #FF8026 Blaze Orange
            new(0.20f, 0.60f, 1.00f), // #3399FF Cobalt Blue
            new(0.95f, 0.38f, 0.76f), // #F261C2 Neon Magenta
            new(0.58f, 0.88f, 0.18f), // #94E02E Chartreuse
            new(0.00f, 0.82f, 0.72f), // #00D1B8 Turquoise
            new(0.96f, 0.84f, 0.28f), // #F5D647 Lemon Gold
            new(0.85f, 0.52f, 0.36f)  // #D9855C Copper Bronze
        };

        public static Color GetSquadColor(int squadId, string? hexOverride = null)
        {
            if (!string.IsNullOrWhiteSpace(hexOverride))
            {
                return Color.FromHtml(hexOverride);
            }
            return DefaultBroadcastPalette[Math.Abs(squadId) % DefaultBroadcastPalette.Length];
        }

        public static string GetShortTag(string? name, int squadIdx)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return squadIdx >= 0 ? $"T{squadIdx + 1:00}" : "MAP";
            }
            string trimmed = name.Trim().ToUpperInvariant();
            return trimmed.Length <= 4 ? trimmed : trimmed[..4];
        }

        public void InitializeBattlefield(MapData map, MatchConfig matchCfg, ConfigBundle? config = null, int playerSquadId = 0)
        {
            _map = map;
            _playerSquadId = playerSquadId;
            _skillsCatalog = config?.Skills?.Skills;
            _activeGlooIds.Clear();
            _lastGlooPosById.Clear();
            ClearPoolChildren();

            var unitShader = GD.Load<Shader>("res://client/shaders/unit_team_color.gdshader");
            _sharedUnitShaderMat = new ShaderMaterial { Shader = unitShader };

            var glooShader = GD.Load<Shader>("res://client/shaders/gloo_energy_shield.gdshader");
            if (glooShader != null)
            {
                _glooMaterial = new ShaderMaterial { Shader = glooShader };
            }
            else
            {
                _glooMaterial = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    AlbedoColor = new Color(0.0f, 0.85f, 1.0f, 0.72f)
                };
            }

            _vehicleMaterial = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Metallic = 0.28f,
                Roughness = 0.46f
            };

            _tracerMaterial = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1.0f, 0.90f, 0.38f, 0.92f)
            };

            _sniperTracerMaterial = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.15f, 0.96f, 1.0f, 0.96f)
            };

            _shotgunTracerMaterial = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1.0f, 0.58f, 0.18f, 0.90f)
            };

            // 1. Create 48 Articulated Low-Poly UnitViews & 12 Elevated Squad Tactical Banners
            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                SquadDef? sqCfg = s < matchCfg.Squads.Count ? matchCfg.Squads[s] : null;
                bool isPlayer = (s == _playerSquadId);

                Color teamCol = isPlayer
                    ? DefaultBroadcastPalette[0]
                    : GetSquadColor(s, sqCfg?.ColorHex);
                string tag = GetShortTag(sqCfg?.Name, s);

                _squadTags[s] = tag;
                _squadColors[s] = teamCol;
                _smoothedSquadBannerPos[s] = Vector3.Zero;

                var sqBanner = new Label3D
                {
                    Name = $"SquadBanner_{s:00}_{tag}",
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FontSize = isPlayer ? 28 : 25,
                    OutlineSize = 8,
                    Modulate = teamCol.Lightened(0.32f),
                    OutlineModulate = new Color(0.02f, 0.04f, 0.08f, 0.96f),
                    PixelSize = isPlayer ? 0.108f : 0.096f,
                    Visible = false
                };
                AddChild(sqBanner);
                _squadBanners[s] = sqBanner;

                for (int m = 0; m < 4; m++)
                {
                    int uIdx = s * 4 + m;
                    string pName = $"{tag}_P{m + 1}";
                    Role role = Role.Rusher;
                    bool isIgl = (m == 0);

                    if (sqCfg != null && m < sqCfg.Members.Count)
                    {
                        pName = sqCfg.Members[m].Player.Name;
                        role = sqCfg.Members[m].Role;
                        isIgl = sqCfg.Members[m].IsIgl;
                    }

                    var uv = new UnitView { Name = $"Unit_{uIdx:00}_{pName}" };
                    AddChild(uv);
                    uv.Initialize(
                        uIdx,
                        s,
                        $"{tag}·{pName}",
                        teamCol,
                        _sharedUnitShaderMat,
                        role,
                        isIgl,
                        config?.Weapons?.Weapons);
                    _unitViews[uIdx] = uv;
                }
            }

            // 2. Pre-allocate 64 Curved Low-Poly Gloo Wall 3D Energy Shields
            ArrayMesh curvedGlooMesh = LowPolyMeshFactory.GetCurvedGlooWallMesh();
            for (int g = 0; g < SnapshotBuffer.MaxGlooWalls; g++)
            {
                var inst = new MeshInstance3D
                {
                    Name = $"GlooWall_{g:00}",
                    Mesh = curvedGlooMesh,
                    MaterialOverride = _glooMaterial,
                    Visible = false
                };
                AddChild(inst);
                _glooMeshes[g] = inst;
            }

            // 3. Pre-allocate 8 Low-Poly Tactical SUV Vehicles
            ArrayMesh vehicleMesh = LowPolyMeshFactory.GetTacticalVehicleMesh();
            for (int v = 0; v < SnapshotBuffer.MaxVehicles; v++)
            {
                var inst = new MeshInstance3D
                {
                    Name = $"Vehicle_{v:00}",
                    Mesh = vehicleMesh,
                    MaterialOverride = _vehicleMaterial,
                    Visible = false
                };
                AddChild(inst);
                _vehicleMeshes[v] = inst;
                _lastVehicleSimPos[v] = Vec2.Zero;
                _vehicleYaw[v] = 0f;
            }

            // 4. Pre-allocate 96 Shot Tracer beams
            var beamMesh = new BoxMesh { Size = new Vector3(0.32f, 0.32f, 1.0f) };
            for (int t = 0; t < 96; t++)
            {
                var inst = new MeshInstance3D
                {
                    Name = $"ShotTracer_{t:00}",
                    Mesh = beamMesh,
                    MaterialOverride = _tracerMaterial,
                    Visible = false
                };
                AddChild(inst);
                _tracerPool.Push(inst);
            }

            // 5. Pre-allocate 16 Parabolic 3D Grenades + 16 Explosion Shockwave Domes
            ArrayMesh grenadeMesh = LowPolyMeshFactory.GetGrenadeMesh();
            var domeMesh = new SphereMesh
            {
                Radius = 1.0f,
                Height = 2.0f,
                RadialSegments = 20,
                Rings = 10
            };

            for (int i = 0; i < 16; i++)
            {
                var gInst = new MeshInstance3D
                {
                    Name = $"Grenade3D_{i:00}",
                    Mesh = grenadeMesh,
                    MaterialOverride = _vehicleMaterial,
                    Scale = new Vector3(1.6f, 1.6f, 1.6f),
                    Visible = false
                };
                AddChild(gInst);
                _grenadePool.Push(gInst);

                var expMat = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    AlbedoColor = new Color(1.0f, 0.55f, 0.12f, 0.75f)
                };
                var expInst = new MeshInstance3D
                {
                    Name = $"ExplosionDome_{i:00}",
                    Mesh = domeMesh,
                    MaterialOverride = expMat,
                    Visible = false
                };
                AddChild(expInst);
                _explosionPool.Push((expInst, expMat));
            }

            // 6. Pre-allocate 32 3D Expanding Pulse / Impact / Shockwave Rings
            var ringMesh = new CylinderMesh
            {
                TopRadius = 1.0f,
                BottomRadius = 1.0f,
                Height = 0.65f,
                RadialSegments = 28,
                CapTop = false,
                CapBottom = false
            };
            for (int i = 0; i < 32; i++)
            {
                var rMat = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    AlbedoColor = new Color(0.0f, 0.85f, 1.0f, 0.80f)
                };
                var rInst = new MeshInstance3D
                {
                    Name = $"PulseRing3D_{i:00}",
                    Mesh = ringMesh,
                    MaterialOverride = rMat,
                    Visible = false
                };
                AddChild(rInst);
                _pulseRingPool.Push((rInst, rMat));
            }

            // 7. Pre-allocate 40 3D Floating Combat Text Labels (Damage, Headshot, Gloo, Skill, Revive)
            for (int i = 0; i < 40; i++)
            {
                var lbl = new Label3D
                {
                    Name = $"CombatText3D_{i:00}",
                    Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                    NoDepthTest = true,
                    FontSize = 28,
                    OutlineSize = 8,
                    OutlineModulate = new Color(0.03f, 0.04f, 0.07f, 0.96f),
                    PixelSize = 0.105f,
                    Visible = false
                };
                AddChild(lbl);
                _floatingTextPool.Push(lbl);
            }

            // 8. Player Squad 3D Tactical Waypoint Beacon
            BuildPlayerCommandBeacon();

            // 9. Transport Aircraft & Drop Corridor 3D Visuals
            BuildTransportAircraft3D();

            _initialized = true;
        }

        private void BuildTransportAircraft3D()
        {
            _transportAircraftNode = new Node3D
            {
                Name = "TransportAircraft3D",
                Visible = false
            };
            AddChild(_transportAircraftNode);

            var bodyMesh = new MeshInstance3D
            {
                Name = "AircraftFuselage",
                Mesh = LowPolyMeshFactory.GetTransportAircraftMesh(),
                MaterialOverride = LowPolyMeshFactory.GetSharedVertexMaterial(metallic: 0.25f, roughness: 0.45f)
            };
            _transportAircraftNode.AddChild(bodyMesh);

            var propMat = LowPolyMeshFactory.GetSharedVertexMaterial(metallic: 0.4f, roughness: 0.3f);
            _leftPropellerMesh = new MeshInstance3D
            {
                Name = "LeftPropeller",
                Mesh = LowPolyMeshFactory.GetAircraftPropellerMesh(),
                MaterialOverride = propMat,
                Position = new Vector3(-3.8f, 0.35f, -3.2f)
            };
            _transportAircraftNode.AddChild(_leftPropellerMesh);

            _rightPropellerMesh = new MeshInstance3D
            {
                Name = "RightPropeller",
                Mesh = LowPolyMeshFactory.GetAircraftPropellerMesh(),
                MaterialOverride = propMat,
                Position = new Vector3(3.8f, 0.35f, -3.2f)
            };
            _transportAircraftNode.AddChild(_rightPropellerMesh);

            _aircraftLabel = new Label3D
            {
                Name = "AircraftLabel",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 26,
                OutlineSize = 7,
                OutlineModulate = new Color(0.03f, 0.05f, 0.09f, 0.95f),
                Modulate = new Color(0.25f, 0.92f, 1.0f, 0.96f),
                Position = new Vector3(0f, 4.8f, 0f),
                PixelSize = 0.13f,
                Text = "✈️ C-130 DROP SHIP · 350m"
            };
            _transportAircraftNode.AddChild(_aircraftLabel);

            _flightCorridorBeam = new MeshInstance3D
            {
                Name = "FlightCorridorBeam",
                Mesh = LowPolyMeshFactory.GetTracerBeamMesh(),
                MaterialOverride = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    NoDepthTest = true,
                    AlbedoColor = new Color(0.15f, 0.88f, 1.0f, 0.32f)
                },
                Visible = false
            };
            AddChild(_flightCorridorBeam);

            _playerDropTrajectoryBeam = new MeshInstance3D
            {
                Name = "PlayerDropTrajectoryBeam",
                Mesh = LowPolyMeshFactory.GetTracerBeamMesh(),
                MaterialOverride = new StandardMaterial3D
                {
                    Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                    CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                    NoDepthTest = true,
                    AlbedoColor = new Color(1.0f, 0.78f, 0.15f, 0.55f)
                },
                Visible = false
            };
            AddChild(_playerDropTrajectoryBeam);
        }

        private void UpdateTransportAircraftAndDropCorridor(InterpolatedFrameState frame, float dt)
        {
            if (_transportAircraftNode == null || _map == null) return;

            bool showFlight = frame.FlightPath.IsActive || frame.Phase == Phase.Drop;
            if (!showFlight)
            {
                _transportAircraftNode.Visible = false;
                if (_flightCorridorBeam != null) _flightCorridorBeam.Visible = false;
                if (_playerDropTrajectoryBeam != null) _playerDropTrajectoryBeam.Visible = false;
                return;
            }

            float planeAlt = frame.FlightPath.AltitudeMeters > 1f ? frame.FlightPath.AltitudeMeters : 350f;
            float planeExtraY = planeAlt * SnapshotInterpolator.AltitudeVisualScale;
            Vector3 planePos3D = SnapshotInterpolator.SimToWorld3D(frame.FlightPath.PlaneCurrentPos, _map, planeExtraY);
            Vector3 startPos3D = SnapshotInterpolator.SimToWorld3D(frame.FlightPath.StartPos, _map, planeExtraY * 0.5f);
            Vector3 endPos3D = SnapshotInterpolator.SimToWorld3D(frame.FlightPath.EndPos, _map, planeExtraY * 0.5f);

            _transportAircraftNode.Visible = frame.FlightPath.IsActive;
            if (frame.FlightPath.IsActive)
            {
                _transportAircraftNode.Position = planePos3D;
                Vec2 dir2 = frame.FlightPath.PlaneForwardDir;
                if (dir2.LengthSquared > 0.001f)
                {
                    float yaw = MathF.Atan2(-dir2.X, -dir2.Y);
                    _transportAircraftNode.Rotation = new Vector3(0f, yaw, 0f);
                }

                _propellerSpinAngle = (_propellerSpinAngle + dt * 28f) % (MathF.PI * 2f);
                if (_leftPropellerMesh != null) _leftPropellerMesh.Rotation = new Vector3(0f, 0f, _propellerSpinAngle);
                if (_rightPropellerMesh != null) _rightPropellerMesh.Rotation = new Vector3(0f, 0f, -_propellerSpinAngle);

                int aboardSquads = 0;
                for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
                {
                    int u0 = s * 4;
                    if (u0 < SnapshotBuffer.MaxUnits && frame.Units[u0].DropState == UnitDropState.InPlane)
                    {
                        aboardSquads++;
                    }
                }
                if (_aircraftLabel != null)
                {
                    _aircraftLabel.Text = aboardSquads > 0
                        ? $"✈️ C-130 DROP SHIP · {aboardSquads} SQUADS ABOARD"
                        : "✈️ C-130 DROP SHIP · ALL LAUNCHED";
                }
            }

            // Flight corridor beam across the island
            if (_flightCorridorBeam != null)
            {
                Vector3 diff = endPos3D - startPos3D;
                float len = diff.Length();
                if (len > 5f)
                {
                    _flightCorridorBeam.Visible = true;
                    _flightCorridorBeam.Position = (startPos3D + endPos3D) * 0.5f;
                    _flightCorridorBeam.Scale = new Vector3(6.5f, 1.0f, len);
                    _flightCorridorBeam.Rotation = new Vector3(0f, MathF.Atan2(-diff.X, -diff.Z), 0f);
                }
                else
                {
                    _flightCorridorBeam.Visible = false;
                }
            }

            // Player squad drop trajectory beam from launch point to target POI
            if (_playerDropTrajectoryBeam != null && _playerSquadId >= 0 && _playerSquadId < SnapshotBuffer.MaxSquads)
            {
                ref readonly SquadSnapshot pSq = ref frame.Squads[_playerSquadId];
                int poiId = pSq.DropPoiId;
                bool anyPlayerInPlaneOrAir = false;
                for (int m = 0; m < 4; m++)
                {
                    int u = _playerSquadId * 4 + m;
                    if (u < SnapshotBuffer.MaxUnits && frame.Units[u].DropState != UnitDropState.Grounded)
                    {
                        anyPlayerInPlaneOrAir = true;
                        break;
                    }
                }

                if (anyPlayerInPlaneOrAir && _map.Pois != null && poiId >= 0 && poiId < _map.Pois.Count)
                {
                    Vector3 launch3D = SnapshotInterpolator.SimToWorld3D(pSq.DropLaunchPos, _map, planeExtraY * 0.45f);
                    var poi = _map.Pois[poiId];
                    Vector3 poi3D = SnapshotInterpolator.SimToWorld3D(new Vec2(poi.CenterX, poi.CenterY), _map, 4.0f);
                    Vector3 diff = poi3D - launch3D;
                    float len = diff.Length();
                    if (len > 2f)
                    {
                        _playerDropTrajectoryBeam.Visible = true;
                        _playerDropTrajectoryBeam.Position = (launch3D + poi3D) * 0.5f;
                        _playerDropTrajectoryBeam.Scale = new Vector3(4.2f, 1.0f, len);
                        float pitch = MathF.Asin(Math.Clamp(diff.Y / len, -0.99f, 0.99f));
                        float yaw = MathF.Atan2(-diff.X, -diff.Z);
                        _playerDropTrajectoryBeam.Rotation = new Vector3(pitch, yaw, 0f);
                    }
                    else
                    {
                        _playerDropTrajectoryBeam.Visible = false;
                    }
                }
                else
                {
                    _playerDropTrajectoryBeam.Visible = false;
                }
            }
        }

        private void BuildPlayerCommandBeacon()
        {
            _playerCommandBeacon = new Node3D
            {
                Name = "PlayerCommandBeacon",
                Visible = false
            };
            AddChild(_playerCommandBeacon);

            _beaconMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(1.0f, 0.72f, 0.0f, 0.45f)
            };

            _beaconRing = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = 7.5f,
                    BottomRadius = 7.5f,
                    Height = 0.9f,
                    RadialSegments = 28,
                    CapTop = false,
                    CapBottom = false
                },
                MaterialOverride = _beaconMat,
                Position = new Vector3(0f, 0.6f, 0f)
            };
            _playerCommandBeacon.AddChild(_beaconRing);

            _beaconPillar = new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = 0.65f,
                    BottomRadius = 1.45f,
                    Height = 28f,
                    RadialSegments = 16,
                    CapTop = false,
                    CapBottom = false
                },
                MaterialOverride = _beaconMat,
                Position = new Vector3(0f, 14f, 0f)
            };
            _playerCommandBeacon.AddChild(_beaconPillar);

            _beaconLabel = new Label3D
            {
                Name = "BeaconOrderLabel",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 28,
                OutlineSize = 8,
                OutlineModulate = new Color(0.03f, 0.05f, 0.09f, 0.95f),
                Position = new Vector3(0f, 26.5f, 0f),
                PixelSize = 0.125f,
                Text = "📍 TACTICAL WAYPOINT"
            };
            _playerCommandBeacon.AddChild(_beaconLabel);
        }

        public void UpdateBattlefield(InterpolatedFrameState frame, float cameraDistance, float dt)
        {
            if (!_initialized || _map == null) return;
            _lastFrame = frame;

            UnitLodLevel lod = LodController.Evaluate(cameraDistance);

            // 0a. Update Transport Aircraft & Flight Corridor
            UpdateTransportAircraftAndDropCorridor(frame, dt);

            // 0b. Compute Squad Tactical Banners & Screen-Space Anti-Overlap / Body-Occlusion Offsets
            UpdateSquadBannersAndDeClutterUnitHud(frame, cameraDistance, lod, dt);

            // 1. Update 48 Units & trigger doorway breach shockwave
            for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
            {
                ref readonly InterpolatedUnitState st = ref frame.Units[u];
                _unitViews[u]?.ApplyState(in st, lod, dt);

                if (_unitBreachCooldownSec[u] > 0f)
                {
                    _unitBreachCooldownSec[u] = Math.Max(0f, _unitBreachCooldownSec[u] - dt);
                }

                bool isDoorBreach = st.State != UnitState.Eliminated && st.State != UnitState.Knocked
                    && (st.IsBreaching || (st.AtDoorway && st.CurrentBehavior == Behavior.Fire));
                if (isDoorBreach && _unitBreachCooldownSec[u] <= 0.01f && lod != UnitLodLevel.Lod2Macro)
                {
                    _unitBreachCooldownSec[u] = 5.0f;
                    Vector3 breachPos = st.WorldPos3D + new Vector3(0f, 1.0f, 0f);
                    SpawnPulseRing(breachPos, new Color(1.0f, 0.58f, 0.14f, 0.88f), 0.9f, 5.4f, 0.34f);
                    if (st.SquadId == _playerSquadId || lod == UnitLodLevel.Lod0Close)
                    {
                        SpawnFloatingText(
                            breachPos + new Vector3(0f, 6.2f, 0f),
                            "🚪⚡ BREACH!",
                            new Color(1.0f, 0.72f, 0.18f),
                            durationSec: 1.05f,
                            baseScale: 1.08f);
                    }
                }
            }

            // 2. Update Curved Energy Gloo Walls & detect newly shattered walls
            var currentGlooIds = new HashSet<int>();
            for (int g = 0; g < SnapshotBuffer.MaxGlooWalls; g++)
            {
                var mesh = _glooMeshes[g];
                if (g < frame.ActiveGlooWallCount)
                {
                    GlooWallSnapshot gw = frame.GlooWalls[g];
                    Vector3 pos3D = SnapshotInterpolator.SimToWorld3D(gw.Position, _map, 0f);
                    currentGlooIds.Add(gw.Id);
                    _lastGlooPosById[gw.Id] = pos3D;

                    float yaw = (gw.Id * 0.785398f) % MathF.Tau;
                    if (gw.OwnerSquadId >= 0 && gw.OwnerSquadId < SnapshotBuffer.MaxSquads)
                    {
                        Vec2 sqCenter = frame.Squads[gw.OwnerSquadId].CenterPos;
                        Vec2 dir = gw.Position - sqCenter;
                        if (dir.LengthSquared > 0.25f)
                        {
                            yaw = MathF.Atan2(dir.Y, dir.X);
                        }
                    }

                    mesh.Visible = true;
                    mesh.Position = pos3D;
                    mesh.Rotation = new Vector3(0f, -yaw, 0f);

                    float hpRatio = Math.Clamp(gw.Hp / Math.Max(1f, gw.MaxHp), 0.1f, 1.0f);
                    Color sqCol = gw.OwnerSquadId == _playerSquadId
                        ? new Color(1.0f, 0.78f, 0.12f)
                        : new Color(0.0f, 0.85f, 1.0f);
                    mesh.SetInstanceShaderParameter("shield_color", sqCol);
                    mesh.SetInstanceShaderParameter("hp_ratio", hpRatio);
                }
                else
                {
                    mesh.Visible = false;
                }
            }

            foreach (int prevId in _activeGlooIds)
            {
                if (!currentGlooIds.Contains(prevId) && _lastGlooPosById.TryGetValue(prevId, out Vector3 shatterPos))
                {
                    SpawnPulseRing(shatterPos + new Vector3(0f, 1.2f, 0f), new Color(0.0f, 0.90f, 1.0f, 0.88f), 1.4f, 6.8f, 0.35f);
                    _lastGlooPosById.Remove(prevId);
                }
            }
            _activeGlooIds.Clear();
            foreach (int id in currentGlooIds) _activeGlooIds.Add(id);

            // 3. Update Low-Poly Tactical SUV Vehicles with heading tracking
            for (int v = 0; v < SnapshotBuffer.MaxVehicles; v++)
            {
                var mesh = _vehicleMeshes[v];
                if (v < frame.ActiveVehicleCount && frame.Vehicles[v].Hp > 0f)
                {
                    VehicleSnapshot vs = frame.Vehicles[v];
                    Vec2 prev = _lastVehicleSimPos[v];
                    Vec2 deltaPos = vs.Position - prev;
                    if (prev.LengthSquared > 1f && deltaPos.LengthSquared > 0.04f)
                    {
                        _vehicleYaw[v] = MathF.Atan2(deltaPos.Y, deltaPos.X);
                    }
                    _lastVehicleSimPos[v] = vs.Position;

                    mesh.Visible = true;
                    mesh.Position = SnapshotInterpolator.SimToWorld3D(vs.Position, _map, 0f);
                    mesh.Rotation = new Vector3(0f, -_vehicleYaw[v], 0f);
                }
                else
                {
                    mesh.Visible = false;
                }
            }

            // 4. Update Player Squad 3D Tactical Waypoint Beacon
            UpdatePlayerBeacon(frame);

            // 5. Tick active shot tracers
            for (int i = _activeTracers.Count - 1; i >= 0; i--)
            {
                ActiveTracer tr = _activeTracers[i];
                tr.RemainingSec -= dt;
                if (tr.RemainingSec <= 0f)
                {
                    tr.Instance.Visible = false;
                    _tracerPool.Push(tr.Instance);
                    _activeTracers.RemoveAt(i);
                }
                else
                {
                    _activeTracers[i] = tr;
                }
            }

            // 6. Tick parabolic 3D grenades (with 2-stage window/door bounce into indoor rooms)
            for (int i = _activeGrenades.Count - 1; i >= 0; i--)
            {
                ActiveGrenadeArc gr = _activeGrenades[i];
                gr.ElapsedSec += dt;
                float t = Math.Clamp(gr.ElapsedSec / Math.Max(0.05f, gr.DurationSec), 0f, 1f);
                if (t >= 1f)
                {
                    gr.Instance.Visible = false;
                    _grenadePool.Push(gr.Instance);
                    SpawnExplosionDome(gr.EndPos, gr.HasIndoorBounce ? 0.52f : 0.42f);
                    SpawnPulseRing(
                        gr.EndPos,
                        gr.HasIndoorBounce ? new Color(1.0f, 0.32f, 0.12f, 0.95f) : new Color(1.0f, 0.48f, 0.14f, 0.90f),
                        1.8f,
                        gr.HasIndoorBounce ? 14.5f : 11.5f,
                        gr.HasIndoorBounce ? 0.50f : 0.42f);
                    _activeGrenades.RemoveAt(i);
                }
                else if (gr.HasIndoorBounce)
                {
                    const float splitT = 0.70f;
                    if (t < splitT)
                    {
                        float segT = t / splitT;
                        Vector3 linear = gr.StartPos.Lerp(gr.BouncePos, segT);
                        float arcHeight = 7.5f * 4f * segT * (1f - segT);
                        gr.Instance.Position = new Vector3(linear.X, linear.Y + arcHeight, linear.Z);
                    }
                    else
                    {
                        if (!gr.BounceTriggered)
                        {
                            gr.BounceTriggered = true;
                            SpawnPulseRing(gr.BouncePos, new Color(0.0f, 0.92f, 1.0f, 0.88f), 0.5f, 3.2f, 0.22f);
                            SpawnFloatingText(
                                gr.BouncePos + new Vector3(0f, 3.5f, 0f),
                                "🪟 NADE BOUNCE!",
                                new Color(0.25f, 0.95f, 1.0f),
                                durationSec: 0.78f,
                                baseScale: 0.92f);
                        }
                        float segT = (t - splitT) / (1f - splitT);
                        Vector3 linear = gr.BouncePos.Lerp(gr.EndPos, segT);
                        float bounceArc = 2.4f * 4f * segT * (1f - segT);
                        gr.Instance.Position = new Vector3(linear.X, linear.Y + bounceArc, linear.Z);
                    }
                    gr.Instance.Rotation = new Vector3(t * 16f, t * 11f, t * 18f);
                    _activeGrenades[i] = gr;
                }
                else
                {
                    Vector3 linear = gr.StartPos.Lerp(gr.EndPos, t);
                    float arcHeight = 9.5f * 4f * t * (1f - t);
                    gr.Instance.Position = new Vector3(linear.X, linear.Y + arcHeight, linear.Z);
                    gr.Instance.Rotation = new Vector3(t * 12f, t * 8f, t * 15f);
                    _activeGrenades[i] = gr;
                }
            }

            // 7. Tick 3D explosion shockwave domes
            for (int i = _activeExplosions.Count - 1; i >= 0; i--)
            {
                ActiveExplosionDome ex = _activeExplosions[i];
                ex.ElapsedSec += dt;
                float t = Math.Clamp(ex.ElapsedSec / Math.Max(0.05f, ex.DurationSec), 0f, 1f);
                if (t >= 1f)
                {
                    ex.Instance.Visible = false;
                    _explosionPool.Push((ex.Instance, ex.Material));
                    _activeExplosions.RemoveAt(i);
                }
                else
                {
                    float radius = 1.5f + t * 11.5f;
                    ex.Instance.Scale = new Vector3(radius, radius * 0.78f, radius);
                    Color c = ex.Material.AlbedoColor;
                    c.A = (1f - t) * 0.68f;
                    ex.Material.AlbedoColor = c;
                    _activeExplosions[i] = ex;
                }
            }

            // 8. Tick 3D expanding pulse / impact rings
            for (int i = _activePulseRings.Count - 1; i >= 0; i--)
            {
                ActivePulseRing pr = _activePulseRings[i];
                pr.ElapsedSec += dt;
                float t = Math.Clamp(pr.ElapsedSec / Math.Max(0.04f, pr.DurationSec), 0f, 1f);
                if (t >= 1f)
                {
                    pr.Instance.Visible = false;
                    _pulseRingPool.Push((pr.Instance, pr.Material));
                    _activePulseRings.RemoveAt(i);
                }
                else
                {
                    float r = Mathf.Lerp(pr.StartRadius, pr.MaxRadius, 1f - (1f - t) * (1f - t));
                    pr.Instance.Scale = new Vector3(r, 1.0f + (1f - t) * 0.8f, r);
                    Color c = pr.BaseColor;
                    c.A = pr.BaseColor.A * (1f - t);
                    pr.Material.AlbedoColor = c;
                    _activePulseRings[i] = pr;
                }
            }

            // 9. Tick 3D floating combat text labels
            for (int i = _activeFloatingTexts.Count - 1; i >= 0; i--)
            {
                ActiveFloatingText ft = _activeFloatingTexts[i];
                ft.ElapsedSec += dt;
                float t = Math.Clamp(ft.ElapsedSec / Math.Max(0.05f, ft.DurationSec), 0f, 1f);
                if (t >= 1f)
                {
                    ft.Label.Visible = false;
                    _floatingTextPool.Push(ft.Label);
                    _activeFloatingTexts.RemoveAt(i);
                }
                else
                {
                    float pop = t < 0.16f ? Mathf.Lerp(0.55f, 1.25f, t / 0.16f) : Mathf.Lerp(1.25f, 1.0f, Math.Clamp((t - 0.16f) / 0.25f, 0f, 1f));
                    ft.Label.Scale = Vector3.One * (ft.BaseScale * pop);
                    ft.Label.Position = ft.StartPos + new Vector3(0f, t * 7.8f, 0f);
                    float alpha = t > 0.65f ? (1f - (t - 0.65f) / 0.35f) : 1f;
                    Color mod = ft.BaseColor;
                    mod.A = alpha;
                    ft.Label.Modulate = mod;
                    _activeFloatingTexts[i] = ft;
                }
            }
        }

        private void UpdateSquadBannersAndDeClutterUnitHud(InterpolatedFrameState frame, float cameraDistance, UnitLodLevel lod, float dt)
        {
            float distT = Math.Clamp((cameraDistance - 150f) / 1100f, 0f, 1f);
            float pitchDeg = Mathf.Lerp(54f, 78f, distT);
            float pitchRad = Mathf.DegToRad(pitchDeg);
            float cosP = MathF.Cos(pitchRad);
            float sinP = MathF.Sin(pitchRad);
            float bannerSmoothT = 1f - MathF.Exp(-12f * Math.Max(0.001f, dt));

            // 1. Update Elevated Squad Tactical Banners (1 per squad at centroid Y + 17.5m)
            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                Label3D banner = _squadBanners[s];
                if (banner == null) continue;

                int aliveOrKnocked = 0;
                int aliveStanding = 0;
                Vector3 sumPos = Vector3.Zero;
                bool anyFreefall = false;
                bool anyGliding = false;
                bool anyBreaching = false;
                bool anyFiring = false;
                bool anyReviving = false;
                bool anyInVehicle = false;
                bool anyMoving = false;
                bool anyIndoor = false;

                int baseUnit = s * 4;
                for (int m = 0; m < 4; m++)
                {
                    int u = baseUnit + m;
                    if (u >= SnapshotBuffer.MaxUnits) break;
                    ref readonly InterpolatedUnitState st = ref frame.Units[u];
                    if (st.State == UnitState.Eliminated || st.DropState == UnitDropState.InPlane || st.WorldPos3D.LengthSquared() < 1f) continue;

                    aliveOrKnocked++;
                    if (st.State != UnitState.Knocked) aliveStanding++;
                    sumPos += st.WorldPos3D;

                    if (st.DropState == UnitDropState.Freefall) anyFreefall = true;
                    if (st.DropState == UnitDropState.Gliding) anyGliding = true;
                    if (st.IsBreaching) anyBreaching = true;
                    if (st.CurrentBehavior == Behavior.Fire) anyFiring = true;
                    if (st.CurrentBehavior == Behavior.Revive) anyReviving = true;
                    if (st.VehicleId >= 0) anyInVehicle = true;
                    if (st.CurrentBehavior != Behavior.TakeCover) anyMoving = true;
                    if (st.IsIndoors) anyIndoor = true;
                }

                if (aliveOrKnocked == 0)
                {
                    banner.Visible = false;
                    continue;
                }

                bool isPlayerSquad = s == _playerSquadId;
                Vector3 centroid3D = sumPos / aliveOrKnocked;
                Vector3 targetPos = centroid3D + new Vector3(0f, isPlayerSquad ? 18.2f : 16.8f, 0f);

                if (_smoothedSquadBannerPos[s].LengthSquared() < 1f)
                {
                    _smoothedSquadBannerPos[s] = targetPos;
                }
                else
                {
                    _smoothedSquadBannerPos[s] = _smoothedSquadBannerPos[s].Lerp(targetPos, bannerSmoothT);
                }

                string tag = _squadTags[s] ?? $"T{s + 1:00}";
                string prefix = isPlayerSquad ? "★ " : "";
                if (lod == UnitLodLevel.Lod2Macro && !isPlayerSquad && !anyFreefall && !anyGliding)
                {
                    banner.Text = $"{prefix}[{tag}] ×{aliveStanding}";
                    banner.Scale = Vector3.One * 0.96f;
                }
                else
                {
                    string actionTag = anyFreefall ? "🏄 SKYDIVE"
                        : anyGliding ? "🪂 GLIDE"
                        : anyBreaching ? "BREACH"
                        : anyReviving ? "REVIVE"
                        : anyFiring ? "ENGAGE"
                        : anyInVehicle ? "DRIVE"
                        : anyIndoor ? "GARRISON"
                        : anyMoving ? "ROTATE"
                        : "HOLD";
                    banner.Text = $"{prefix}[{tag}] ×{aliveStanding} · {actionTag}";
                    banner.Scale = Vector3.One * (isPlayerSquad ? 1.06f : 0.88f);
                }

                Color baseCol = isPlayerSquad
                    ? new Color(1.0f, 0.88f, 0.28f, 0.96f)
                    : _squadColors[s].Lightened(0.32f) with { A = lod == UnitLodLevel.Lod2Macro ? 0.86f : 0.78f };
                banner.Modulate = baseCol;
                banner.Visible = true;
            }

            // Anti-overlap pass between Squad Tactical Banners so colliding squads never overlap headers
            for (int s1 = 0; s1 < SnapshotBuffer.MaxSquads; s1++)
            {
                if (_squadBanners[s1] == null || !_squadBanners[s1].Visible) continue;
                for (int s2 = s1 + 1; s2 < SnapshotBuffer.MaxSquads; s2++)
                {
                    if (_squadBanners[s2] == null || !_squadBanners[s2].Visible) continue;
                    Vector3 p1 = _smoothedSquadBannerPos[s1];
                    Vector3 p2 = _smoothedSquadBannerPos[s2];
                    float dx = p2.X - p1.X;
                    float sy1 = p1.Y * cosP - p1.Z * sinP;
                    float sy2 = p2.Y * cosP - p2.Z * sinP;
                    float dy = sy2 - sy1;
                    if (MathF.Abs(dx) < 18.0f && MathF.Abs(dy) < 3.6f)
                    {
                        float lift = (3.6f - MathF.Abs(dy)) / Math.Max(0.35f, cosP);
                        if (s2 == _playerSquadId || (s1 != _playerSquadId && sy2 >= sy1))
                        {
                            _smoothedSquadBannerPos[s2].Y += lift * 0.65f;
                        }
                        else
                        {
                            _smoothedSquadBannerPos[s1].Y += lift * 0.65f;
                        }
                    }
                }
                _squadBanners[s1].Position = _smoothedSquadBannerPos[s1];
            }

            // 2. Screen-Space Body-Occlusion & Pairwise Label Anti-Overlap Solver for 48 Units
            if (lod == UnitLodLevel.Lod2Macro)
            {
                return;
            }

            for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
            {
                ref readonly InterpolatedUnitState st = ref frame.Units[u];
                if (st.State == UnitState.Eliminated || st.WorldPos3D.LengthSquared() < 1f)
                {
                    _hudOffX[u] = 0f;
                    _hudOffY[u] = 0f;
                    _unitClustered[u] = false;
                    continue;
                }

                // 3D Character Body visual center (torso/head silhouette at Y + 2.2m)
                Vector3 bodyCenter = st.WorldPos3D + new Vector3(0f, st.State == UnitState.Knocked ? 1.0f : 2.2f, 0f);
                _bodyScreenX[u] = bodyCenter.X;
                _bodyScreenY[u] = bodyCenter.Y * cosP - bodyCenter.Z * sinP;

                // Default un-displaced HUD anchor in world space (Y + 6.85m standing, Y + 4.2m knocked)
                float baseHudY = st.WorldPos3D.Y + (st.State == UnitState.Knocked ? 4.2f : 6.85f);
                _baseHudScreenX[u] = st.WorldPos3D.X;
                _baseHudScreenY[u] = baseHudY * cosP - st.WorldPos3D.Z * sinP;

                // Detect nearby squad/combat cluster & seed subtle horizontal fan-out by slot
                int nearbyCount = 0;
                for (int other = 0; other < SnapshotBuffer.MaxUnits; other++)
                {
                    if (other == u) continue;
                    ref readonly InterpolatedUnitState ost = ref frame.Units[other];
                    if (ost.State == UnitState.Eliminated || ost.WorldPos3D.LengthSquared() < 1f) continue;
                    float distSq = st.WorldPos3D.DistanceSquaredTo(ost.WorldPos3D);
                    if (distSq < 196f) // within 14m
                    {
                        nearbyCount++;
                    }
                }

                _unitClustered[u] = nearbyCount > 0;
                int slot = u % 4;
                _hudOffX[u] = _unitClustered[u] ? ((slot - 1.5f) * 0.85f) : 0f;
                _hudOffY[u] = 0f;
            }

            const float bodyHalfW = 4.6f;
            const float bodyHalfH = 3.2f;
            const float labelSepX = 7.4f;
            const float labelSepY = 2.40f;

            for (int iter = 0; iter < 4; iter++)
            {
                // Pass A: Evict HUD labels out of ANY other unit's 3D character body silhouette
                for (int i = 0; i < SnapshotBuffer.MaxUnits; i++)
                {
                    if (frame.Units[i].State == UnitState.Eliminated || frame.Units[i].WorldPos3D.LengthSquared() < 1f) continue;
                    float curX = _baseHudScreenX[i] + _hudOffX[i];
                    float curY = _baseHudScreenY[i] + _hudOffY[i];

                    for (int j = 0; j < SnapshotBuffer.MaxUnits; j++)
                    {
                        if (i == j) continue;
                        if (frame.Units[j].State == UnitState.Eliminated || frame.Units[j].WorldPos3D.LengthSquared() < 1f) continue;

                        float dx = curX - _bodyScreenX[j];
                        float dy = curY - _bodyScreenY[j];
                        if (MathF.Abs(dx) < bodyHalfW && MathF.Abs(dy) < bodyHalfH)
                        {
                            float signX = MathF.Abs(dx) > 0.1f ? MathF.Sign(dx) : ((i % 2 == 0) ? 1f : -1f);
                            _hudOffX[i] += signX * (bodyHalfW - MathF.Abs(dx)) * 0.62f;
                            _hudOffY[i] += Math.Max(0.25f, (bodyHalfH - dy) * 0.52f);
                            curX = _baseHudScreenX[i] + _hudOffX[i];
                            curY = _baseHudScreenY[i] + _hudOffY[i];
                        }
                    }
                }

                // Pass B: Pairwise HUD-to-HUD anti-overlap repulsion
                for (int i = 0; i < SnapshotBuffer.MaxUnits; i++)
                {
                    if (frame.Units[i].State == UnitState.Eliminated || frame.Units[i].WorldPos3D.LengthSquared() < 1f) continue;
                    for (int j = i + 1; j < SnapshotBuffer.MaxUnits; j++)
                    {
                        if (frame.Units[j].State == UnitState.Eliminated || frame.Units[j].WorldPos3D.LengthSquared() < 1f) continue;

                        float xi = _baseHudScreenX[i] + _hudOffX[i];
                        float yi = _baseHudScreenY[i] + _hudOffY[i];
                        float xj = _baseHudScreenX[j] + _hudOffX[j];
                        float yj = _baseHudScreenY[j] + _hudOffY[j];

                        float dx = xj - xi;
                        float dy = yj - yi;
                        if (MathF.Abs(dx) < labelSepX && MathF.Abs(dy) < labelSepY)
                        {
                            float penX = labelSepX - MathF.Abs(dx);
                            float penY = labelSepY - MathF.Abs(dy);
                            float dirX = MathF.Abs(dx) > 0.08f ? MathF.Sign(dx) : ((j % 2 == 0) ? 1f : -1f);
                            float dirY = MathF.Abs(dy) > 0.08f ? MathF.Sign(dy) : 1f;

                            if (penX * 0.38f < penY)
                            {
                                float pushX = dirX * penX * 0.52f;
                                float pushY = dirY * penY * 0.24f;
                                _hudOffX[i] -= pushX;
                                _hudOffX[j] += pushX;
                                _hudOffY[i] -= pushY;
                                _hudOffY[j] += pushY;
                            }
                            else
                            {
                                float pushX = dirX * penX * 0.30f;
                                float pushY = dirY * penY * 0.55f;
                                _hudOffX[i] -= pushX;
                                _hudOffX[j] += pushX;
                                _hudOffY[i] -= pushY;
                                _hudOffY[j] += pushY;
                            }
                        }
                    }
                }

                // Pass C: Clamp offsets & strictly prevent downward drop onto unit's own 3D model
                for (int i = 0; i < SnapshotBuffer.MaxUnits; i++)
                {
                    _hudOffX[i] = Math.Clamp(_hudOffX[i], -8.2f, 8.2f);
                    _hudOffY[i] = Math.Clamp(_hudOffY[i], 0.0f, 7.2f);
                }
            }

            // 3. Apply solved offsets to each UnitView
            for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
            {
                _unitViews[u]?.SetHudDeClutterState(
                    new Vector2(_hudOffX[u], _hudOffY[u]),
                    pitchRad,
                    _unitClustered[u]);
            }
        }

        private void UpdatePlayerBeacon(InterpolatedFrameState frame)
        {
            if (_playerCommandBeacon == null || _beaconMat == null || _beaconRing == null || _map == null) return;
            if (_playerSquadId < 0 || _playerSquadId >= SnapshotBuffer.MaxSquads) return;

            ref readonly SquadSnapshot sq = ref frame.Squads[_playerSquadId];
            if (sq.AliveCount <= 0)
            {
                _playerCommandBeacon.Visible = false;
                return;
            }

            Vec2 target2D = sq.HasActiveCommand && sq.ActiveCommandTarget.LengthSquared > 1f
                ? sq.ActiveCommandTarget
                : sq.MacroGoal;

            if (target2D.LengthSquared < 1f)
            {
                _playerCommandBeacon.Visible = false;
                return;
            }

            _playerCommandBeacon.Visible = true;
            _playerCommandBeacon.Position = SnapshotInterpolator.SimToWorld3D(target2D, _map, 0.2f);

            float pulse = 1.0f + 0.15f * MathF.Sin(frame.TimeSec * 4.8f);
            _beaconRing.Scale = new Vector3(pulse, 1f, pulse);

            if (sq.HasActiveCommand)
            {
                (Color cmdCol, string cmdTitle) = sq.ActiveCommand switch
                {
                    CommandType.RotateNow => (new Color(0.0f, 0.85f, 1.0f, 0.56f), "🏃 ORDER: ROTATE NOW"),
                    CommandType.HoldPosition => (new Color(1.0f, 0.75f, 0.08f, 0.56f), "🛡️ ORDER: HOLD POSITION"),
                    CommandType.ContestTarget => (new Color(1.0f, 0.28f, 0.34f, 0.58f), "⚔️ ORDER: PRESS FIGHT"),
                    CommandType.EndgameFormation => (new Color(0.68f, 0.38f, 0.98f, 0.56f), "🧊 ORDER: GLOO FORT"),
                    CommandType.AvoidFight => (new Color(0.18f, 0.86f, 0.48f, 0.56f), "💨 ORDER: DISENGAGE"),
                    CommandType.PopSkill => (new Color(0.0f, 0.95f, 0.85f, 0.58f), "⚡ ORDER: POP SKILLS"),
                    _ => (new Color(0.0f, 0.85f, 1.0f, 0.52f), "📻 COACH ORDER ACTIVE")
                };
                _beaconMat.AlbedoColor = cmdCol;
                if (_beaconLabel != null)
                {
                    _beaconLabel.Text = cmdTitle;
                    _beaconLabel.Modulate = cmdCol.Lightened(0.35f) with { A = 0.95f };
                }
            }
            else
            {
                _beaconMat.AlbedoColor = new Color(1.0f, 0.72f, 0.0f, 0.32f);
                if (_beaconLabel != null)
                {
                    _beaconLabel.Text = $"📍 IGL GOAL ({sq.CurrentMode.ToString().ToUpperInvariant()})";
                    _beaconLabel.Modulate = new Color(1.0f, 0.82f, 0.25f, 0.85f);
                }
            }
        }

        public void OnSimEvent(SimEvent ev)
        {
            if (!_initialized || _map == null) return;

            switch (ev.Type)
            {
                case SimEventType.ShotFired:
                {
                    bool isHeadshot = (ev.ExtraCode & 16) != 0;
                    bool isCoverBlocked = (ev.ExtraCode & 32) != 0;
                    bool isFlankHit = (ev.ExtraCode & 64) != 0;
                    bool isCqcBreach = (ev.ExtraCode & 128) != 0;
                    bool isWindowPeek = (ev.ExtraCode & 256) != 0;

                    Vector3 from = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 4.1f);
                    Vector3 to = SnapshotInterpolator.SimToWorld3D(ev.Pos2, _map, 3.8f);

                    WeaponCategory wCat = WeaponCategory.AR;
                    if (ev.ActorUnitIdx >= 0 && ev.ActorUnitIdx < _unitViews.Length && _unitViews[ev.ActorUnitIdx] != null)
                    {
                        wCat = _unitViews[ev.ActorUnitIdx].EquippedWeaponCategory;
                        _unitViews[ev.ActorUnitIdx].TriggerMuzzleFlash();
                        if (isHeadshot)
                        {
                            _unitViews[ev.ActorUnitIdx].TriggerHeadshotCallout();
                        }
                    }

                    if (isWindowPeek)
                    {
                        SpawnPulseRing(from, new Color(0.0f, 0.90f, 1.0f, 0.74f), 0.35f, 1.9f, 0.16f);
                    }

                    SpawnWeaponBallistics(from, to, wCat, isHeadshot);

                    if (ev.TargetUnitIdx >= 0 && ev.TargetUnitIdx < _unitViews.Length && ev.Value > 1f)
                    {
                        _unitViews[ev.TargetUnitIdx]?.TriggerHitFlash();
                        bool isPlayerInvolved = ev.SquadIdx == _playerSquadId || ev.TargetSquadIdx == _playerSquadId;

                        if (isHeadshot)
                        {
                            SpawnPulseRing(to, new Color(1.0f, 0.82f, 0.12f, 0.92f), 0.7f, 4.6f, 0.28f);
                            SpawnFloatingText(
                                to + new Vector3(0f, 5.5f, 0f),
                                $"🎯 -{(int)MathF.Round(ev.Value)} HEADSHOT!",
                                new Color(1.0f, 0.86f, 0.15f),
                                durationSec: 1.25f,
                                baseScale: 1.28f);
                        }
                        else if (isCqcBreach)
                        {
                            SpawnPulseRing(to, new Color(1.0f, 0.62f, 0.12f, 0.92f), 0.65f, 3.8f, 0.25f);
                            if (isPlayerInvolved || ev.Value >= 18f)
                            {
                                SpawnFloatingText(
                                    to + new Vector3(0f, 5.2f, 0f),
                                    $"💥 -{(int)MathF.Round(ev.Value)} CQC BREACH!",
                                    new Color(1.0f, 0.76f, 0.18f),
                                    durationSec: 1.08f,
                                    baseScale: 1.14f);
                            }
                        }
                        else if (isFlankHit)
                        {
                            SpawnPulseRing(to, new Color(1.0f, 0.25f, 0.30f, 0.90f), 0.65f, 4.0f, 0.26f);
                            if (isPlayerInvolved || ev.Value >= 15f)
                            {
                                SpawnFloatingText(
                                    to + new Vector3(0f, 5.1f, 0f),
                                    $"⚠️ -{(int)MathF.Round(ev.Value)} FLANKED!",
                                    new Color(1.0f, 0.38f, 0.26f),
                                    durationSec: 1.05f,
                                    baseScale: 1.10f);
                            }
                        }
                        else if (isCoverBlocked)
                        {
                            // Directional Cover Ricochet Spark & Cyan Shield Flash
                            Vector3 shotDir = (to - from).Normalized();
                            Vector3 shieldPoint = to - shotDir * 1.9f;
                            Vector3 ricochetEnd = shieldPoint + new Vector3(-shotDir.Z * 2.2f, 2.6f, shotDir.X * 2.2f);
                            SpawnTracerBeam(shieldPoint, ricochetEnd, 0.14f, 0.85f, _sniperTracerMaterial);
                            SpawnPulseRing(shieldPoint, new Color(0.15f, 0.92f, 1.0f, 0.85f), 0.45f, 2.6f, 0.20f);
                            if (isPlayerInvolved || ev.Value >= 14f)
                            {
                                SpawnFloatingText(
                                    to + new Vector3(0f, 4.8f, 0f),
                                    $"🛡️ -{(int)MathF.Round(ev.Value)} COVER",
                                    new Color(0.28f, 0.92f, 1.0f),
                                    durationSec: 0.92f,
                                    baseScale: 0.96f);
                            }
                        }
                        else if (ev.Value >= 16f || isPlayerInvolved)
                        {
                            SpawnPulseRing(to, new Color(1.0f, 0.45f, 0.20f, 0.72f), 0.5f, 2.8f, 0.18f);
                            SpawnFloatingText(
                                to + new Vector3(0f, 4.6f, 0f),
                                $"-{(int)MathF.Round(ev.Value)}",
                                ev.SquadIdx == _playerSquadId ? new Color(1.0f, 0.92f, 0.50f) : new Color(0.95f, 0.96f, 0.98f),
                                durationSec: 0.85f,
                                baseScale: 0.92f);
                        }
                    }
                    else if (ev.TargetUnitIdx < 0 && ev.Value > 1f)
                    {
                        // Shot hit a Gloo Wall or M82B pierced cover
                        SpawnPulseRing(to, new Color(0.0f, 0.88f, 1.0f, 0.82f), 0.6f, 3.4f, 0.22f);
                        string glooHitText = wCat == WeaponCategory.Sniper
                            ? $"⚡ -{(int)MathF.Round(ev.Value)} PIERCE!"
                            : $"🧊 -{(int)MathF.Round(ev.Value)}";
                        SpawnFloatingText(
                            to + new Vector3(0f, 4.2f, 0f),
                            glooHitText,
                            new Color(0.20f, 0.92f, 1.0f),
                            durationSec: 0.90f,
                            baseScale: 0.95f);
                    }
                    break;
                }
                case SimEventType.GrenadeThrown:
                {
                    Vector3 from = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 4.4f);
                    Vector3 to = SnapshotInterpolator.SimToWorld3D(ev.Pos2, _map, 0.6f);
                    bool targetIndoor = ev.ExtraCode == 1 || _map.GetCellAtWorld(ev.Pos2).IsBuilding;
                    SpawnGrenadeArc(from, to, targetIndoor ? 0.46f : 0.38f, targetIndoor);

                    if (ev.TargetUnitIdx >= 0 && ev.TargetUnitIdx < _unitViews.Length && ev.Value > 1f)
                    {
                        _unitViews[ev.TargetUnitIdx]?.TriggerHitFlash();
                    }

                    if (targetIndoor)
                    {
                        SpawnExplosionDome(to, 0.54f);
                        SpawnPulseRing(to, new Color(1.0f, 0.28f, 0.12f, 0.95f), 2.0f, 15.5f, 0.52f);
                        SpawnPulseRing(to + new Vector3(0f, 2.2f, 0f), new Color(1.0f, 0.76f, 0.16f, 0.90f), 1.2f, 10.5f, 0.42f);
                        SpawnFloatingText(
                            to + new Vector3(0f, 7.2f, 0f),
                            ev.Value > 1f ? $"💥🏠 INDOOR BLAST! -{(int)MathF.Round(ev.Value)}" : "💥🏠 INDOOR ROOM BLAST!",
                            new Color(1.0f, 0.44f, 0.16f),
                            durationSec: 1.35f,
                            baseScale: 1.26f);
                    }
                    else if (ev.Value > 1f)
                    {
                        SpawnFloatingText(
                            to + new Vector3(0f, 5.2f, 0f),
                            $"💣 -{(int)MathF.Round(ev.Value)} BLAST!",
                            new Color(1.0f, 0.52f, 0.18f),
                            durationSec: 1.15f,
                            baseScale: 1.15f);
                    }
                    break;
                }
                case SimEventType.GlooPlaced:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 1.0f);
                    SpawnPulseRing(pos, new Color(0.0f, 0.88f, 1.0f, 0.85f), 1.2f, 6.5f, 0.38f);
                    SpawnFloatingText(
                        pos + new Vector3(0f, 5.8f, 0f),
                        "🧊 GLOO WALL",
                        new Color(0.15f, 0.92f, 1.0f),
                        durationSec: 1.05f,
                        baseScale: 1.05f);
                    break;
                }
                case SimEventType.SkillTriggered:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 1.2f);
                    (string skillLabel, Color skillCol) = ResolveSkillCallout(ev.ExtraCode);
                    SpawnPulseRing(pos, skillCol with { A = 0.88f }, 1.8f, 11.5f, 0.52f);
                    SpawnFloatingText(
                        pos + new Vector3(0f, 7.2f, 0f),
                        skillLabel,
                        skillCol,
                        durationSec: 1.35f,
                        baseScale: 1.18f);
                    break;
                }
                case SimEventType.Knocked:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 1.0f);
                    SpawnPulseRing(pos, new Color(1.0f, 0.28f, 0.34f, 0.88f), 1.2f, 7.2f, 0.42f);
                    SpawnFloatingText(
                        pos + new Vector3(0f, 6.8f, 0f),
                        "💀 KNOCKED!",
                        new Color(1.0f, 0.35f, 0.38f),
                        durationSec: 1.30f,
                        baseScale: 1.20f);
                    break;
                }
                case SimEventType.Revived:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 1.0f);
                    SpawnPulseRing(pos, new Color(0.18f, 0.92f, 0.50f, 0.90f), 1.4f, 8.5f, 0.48f);
                    SpawnFloatingText(
                        pos + new Vector3(0f, 7.0f, 0f),
                        "💚 REVIVED!",
                        new Color(0.22f, 0.96f, 0.55f),
                        durationSec: 1.35f,
                        baseScale: 1.22f);
                    break;
                }
                case SimEventType.Eliminated:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 1.0f);
                    SpawnFloatingText(
                        pos + new Vector3(0f, 6.5f, 0f),
                        "☠️ ELIMINATED",
                        new Color(1.0f, 0.25f, 0.32f),
                        durationSec: 1.25f,
                        baseScale: 1.15f);
                    break;
                }
                case SimEventType.CommandExecuted:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 1.2f);
                    bool complied = ev.Value >= 0.5f;
                    Color c = complied ? new Color(0.18f, 0.92f, 0.50f) : new Color(1.0f, 0.72f, 0.0f);
                    SpawnPulseRing(pos, c with { A = 0.88f }, 2.0f, 12.5f, 0.55f);
                    SpawnFloatingText(
                        pos + new Vector3(0f, 8.5f, 0f),
                        complied ? "📻 IGL COMPLIED!" : "⚠️ IGL OVERRODE!",
                        c,
                        durationSec: 1.45f,
                        baseScale: 1.25f);
                    break;
                }
                case SimEventType.DropLaunched:
                {
                    float planeExtraY = 350f * SnapshotInterpolator.AltitudeVisualScale;
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, planeExtraY);
                    int sqId = ev.ActorId;
                    bool isPlayer = sqId == _playerSquadId;
                    Color c = isPlayer ? new Color(1.0f, 0.85f, 0.2f) : new Color(0.25f, 0.92f, 1.0f);
                    SpawnPulseRing(pos, c with { A = 0.82f }, 2.2f, 14.0f, 0.55f);
                    if (isPlayer)
                    {
                        SpawnFloatingText(
                            pos + new Vector3(0f, 5.5f, 0f),
                            "🏄 SQUAD LAUNCHED!",
                            c,
                            durationSec: 1.35f,
                            baseScale: 1.2f);
                    }
                    break;
                }
                case SimEventType.ParachuteOpened:
                {
                    float glideExtraY = 120f * SnapshotInterpolator.AltitudeVisualScale;
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, glideExtraY);
                    int sqId = ev.ActorId;
                    bool isPlayer = sqId == _playerSquadId;
                    if (isPlayer)
                    {
                        SpawnFloatingText(
                            pos + new Vector3(0f, 4.5f, 0f),
                            "🪂 CHUTES DEPLOYED!",
                            new Color(0.35f, 0.95f, 1.0f),
                            durationSec: 1.25f,
                            baseScale: 1.15f);
                    }
                    break;
                }
                case SimEventType.DropVeerOff:
                {
                    float glideExtraY = 120f * SnapshotInterpolator.AltitudeVisualScale;
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, glideExtraY);
                    int sqId = ev.ActorId;
                    string tag = sqId >= 0 && sqId < _squadTags.Length ? _squadTags[sqId] : $"SQ{sqId + 1}";
                    SpawnFloatingText(
                        pos + new Vector3(0f, 5.5f, 0f),
                        $"↪ [{tag}] VEERED OFF!",
                        new Color(1.0f, 0.65f, 0.2f),
                        durationSec: 1.35f,
                        baseScale: 1.15f);
                    break;
                }
                case SimEventType.Landed:
                {
                    Vector3 pos = SnapshotInterpolator.SimToWorld3D(ev.Pos, _map, 0.8f);
                    int sqId = ev.SquadIdx;
                    bool isPlayer = sqId == _playerSquadId;
                    Color dustCol = isPlayer ? new Color(1.0f, 0.82f, 0.18f, 0.88f) : new Color(0.75f, 0.82f, 0.90f, 0.68f);
                    SpawnPulseRing(pos, dustCol, 1.6f, isPlayer ? 11.5f : 8.5f, 0.48f);
                    if (isPlayer)
                    {
                        SpawnFloatingText(
                            pos + new Vector3(0f, 5.2f, 0f),
                            "⚡ TOUCHDOWN · LOOT PHASE!",
                            new Color(1.0f, 0.86f, 0.22f),
                            durationSec: 1.35f,
                            baseScale: 1.18f);
                    }
                    break;
                }
            }
        }

        public void TriggerCommandFeedback3D(CommandType cmdType)
        {
            if (!_initialized || _map == null || _lastFrame == null) return;
            ref readonly SquadSnapshot sq = ref _lastFrame.Squads[_playerSquadId];
            if (sq.AliveCount <= 0) return;

            Vector3 squadPos = SnapshotInterpolator.SimToWorld3D(sq.CenterPos, _map, 1.0f);
            Color col = cmdType switch
            {
                CommandType.ContestTarget => new Color(1.0f, 0.28f, 0.34f),
                CommandType.HoldPosition => new Color(1.0f, 0.75f, 0.08f),
                CommandType.RotateNow => new Color(0.0f, 0.85f, 1.0f),
                CommandType.EndgameFormation => new Color(0.68f, 0.38f, 0.98f),
                CommandType.AvoidFight => new Color(0.18f, 0.88f, 0.48f),
                CommandType.PopSkill => new Color(0.0f, 0.95f, 0.85f),
                _ => new Color(0.0f, 0.85f, 1.0f)
            };

            SpawnPulseRing(squadPos, col with { A = 0.92f }, 2.2f, 14.5f, 0.55f);
            SpawnFloatingText(
                squadPos + new Vector3(0f, 9.2f, 0f),
                $"📻 ORDER SENT: {cmdType.ToString().ToUpperInvariant()}",
                col.Lightened(0.25f),
                durationSec: 1.35f,
                baseScale: 1.22f);
            PulseSquadSelection(_playerSquadId);
        }

        public Vec2 TriggerGroundPin3D(Vector3 worldClickPos)
        {
            if (_map == null) return Vec2.Zero;
            Vec2 simPos = new(
                Math.Clamp(worldClickPos.X, 20f, MapData.MapSizeMeters - 20f),
                Math.Clamp(worldClickPos.Z, 20f, MapData.MapSizeMeters - 20f));
            simPos = _map.ClampToWalkableWorld(simPos);

            Vector3 snapped3D = SnapshotInterpolator.SimToWorld3D(simPos, _map, 0.8f);
            SpawnPulseRing(snapped3D, new Color(0.0f, 0.92f, 1.0f, 0.95f), 1.5f, 13.5f, 0.52f);
            SpawnPulseRing(snapped3D, new Color(1.0f, 0.76f, 0.08f, 0.85f), 0.8f, 7.5f, 0.38f);
            SpawnFloatingText(
                snapped3D + new Vector3(0f, 6.5f, 0f),
                $"📍 TARGET PINNED ({simPos.X:0}, {simPos.Y:0})",
                new Color(0.15f, 0.95f, 1.0f),
                durationSec: 1.40f,
                baseScale: 1.20f);
            return simPos;
        }

        public void PulseSquadSelection(int squadId)
        {
            if (squadId < 0 || squadId >= SnapshotBuffer.MaxSquads) return;
            for (int m = 0; m < 4; m++)
            {
                int uIdx = squadId * 4 + m;
                _unitViews[uIdx]?.TriggerSelectionPulse();
            }

            if (_lastFrame != null && _map != null && _lastFrame.Squads[squadId].AliveCount > 0)
            {
                Vector3 sqPos = SnapshotInterpolator.SimToWorld3D(_lastFrame.Squads[squadId].CenterPos, _map, 0.8f);
                SpawnPulseRing(sqPos, new Color(0.0f, 0.88f, 1.0f, 0.85f), 2.0f, 12.0f, 0.45f);
            }
        }

        public int TryPickSquadAtWorldPos(Vector3 worldPos, float maxRadiusMeters = 28f)
        {
            if (_lastFrame == null) return -1;
            float bestDistSq = maxRadiusMeters * maxRadiusMeters;
            int bestSquadId = -1;

            for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
            {
                ref readonly InterpolatedUnitState st = ref _lastFrame.Units[u];
                if (st.State == UnitState.Eliminated) continue;
                float dx = st.WorldPos3D.X - worldPos.X;
                float dz = st.WorldPos3D.Z - worldPos.Z;
                float dSq = dx * dx + dz * dz;
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    bestSquadId = st.SquadId;
                }
            }

            if (bestSquadId >= 0)
            {
                PulseSquadSelection(bestSquadId);
            }
            return bestSquadId;
        }

        private (string Label, Color Color) ResolveSkillCallout(int skillIndex)
        {
            SkillCategory cat = SkillCategory.Heal;
            if (_skillsCatalog != null && skillIndex >= 0 && skillIndex < _skillsCatalog.Count)
            {
                cat = _skillsCatalog[skillIndex].GetCategoryEnum();
            }

            return cat switch
            {
                SkillCategory.Shield => ("🛡️ CHRONO SHIELD!", new Color(0.68f, 0.42f, 1.0f)),
                SkillCategory.Heal => ("🎵 ALOK HEAL AURA!", new Color(0.22f, 0.94f, 0.52f)),
                SkillCategory.Deploy => ("🌊 SKYLER RIPTIDE!", new Color(1.0f, 0.36f, 0.40f)),
                SkillCategory.Dash => ("⚡ TATSUYA DASH!", new Color(0.10f, 0.90f, 1.0f)),
                SkillCategory.Scan => ("👁️ MOCO SCAN!", new Color(1.0f, 0.80f, 0.12f)),
                _ => ("⚡ SKILL ACTIVE!", new Color(0.20f, 0.92f, 1.0f))
            };
        }

        private void SpawnWeaponBallistics(Vector3 from, Vector3 to, WeaponCategory category, bool isHeadshot)
        {
            switch (category)
            {
                case WeaponCategory.Sniper:
                case WeaponCategory.DMR:
                {
                    SpawnTracerBeam(
                        from,
                        to,
                        durationSec: isHeadshot ? 0.26f : 0.20f,
                        thickness: isHeadshot ? 1.85f : 1.45f,
                        matOverride: _sniperTracerMaterial);
                    break;
                }
                case WeaponCategory.Shotgun:
                {
                    Vector3 dir = (to - from);
                    Vector3 right = dir.Cross(Vector3.Up).Normalized() * 1.15f;
                    SpawnTracerBeam(from, to, 0.12f, 1.10f, _shotgunTracerMaterial);
                    SpawnTracerBeam(from, to + right + new Vector3(0f, 0.4f, 0f), 0.11f, 0.85f, _shotgunTracerMaterial);
                    SpawnTracerBeam(from, to - right - new Vector3(0f, 0.3f, 0f), 0.11f, 0.85f, _shotgunTracerMaterial);
                    break;
                }
                default:
                {
                    SpawnTracerBeam(
                        from,
                        to,
                        durationSec: isHeadshot ? 0.18f : 0.11f,
                        thickness: isHeadshot ? 1.35f : 1.0f,
                        matOverride: isHeadshot ? _sniperTracerMaterial : _tracerMaterial);
                    break;
                }
            }
        }

        private void SpawnTracerBeam(Vector3 from, Vector3 to, float durationSec, float thickness = 1.0f, StandardMaterial3D? matOverride = null)
        {
            if (_tracerPool.Count == 0) return;
            float dist = from.DistanceTo(to);
            if (dist < 1.0f) return;

            MeshInstance3D beam = _tracerPool.Pop();
            Vector3 mid = (from + to) * 0.5f;
            beam.Position = mid;
            beam.LookAtFromPosition(mid, to, Vector3.Up);
            beam.Scale = new Vector3(thickness, thickness, dist);
            beam.MaterialOverride = matOverride ?? _tracerMaterial;
            beam.Visible = true;

            _activeTracers.Add(new ActiveTracer
            {
                Instance = beam,
                RemainingSec = durationSec
            });
        }

        private void SpawnPulseRing(Vector3 pos, Color color, float startRadius, float maxRadius, float durationSec)
        {
            if (_pulseRingPool.Count == 0) return;
            var (inst, mat) = _pulseRingPool.Pop();
            inst.Position = pos;
            inst.Scale = new Vector3(startRadius, 1.2f, startRadius);
            mat.AlbedoColor = color;
            inst.Visible = true;

            _activePulseRings.Add(new ActivePulseRing
            {
                Instance = inst,
                Material = mat,
                StartRadius = startRadius,
                MaxRadius = maxRadius,
                BaseColor = color,
                ElapsedSec = 0f,
                DurationSec = durationSec
            });
        }

        private void SpawnFloatingText(Vector3 worldPos, string text, Color color, float durationSec = 1.10f, float baseScale = 1.0f)
        {
            if (_floatingTextPool.Count == 0) return;
            Label3D lbl = _floatingTextPool.Pop();

            // Stagger horizontally & lift above unit HUD layer so combat popups never cover nameplates or 3D models
            int seq = _floatingTextSequence++;
            float jitterX = ((seq % 5) - 2) * 1.35f;
            Vector3 elevatedStart = worldPos + new Vector3(jitterX, 3.8f, 0f);

            lbl.Text = text;
            lbl.Position = elevatedStart;
            lbl.Modulate = color;
            lbl.Scale = Vector3.One * (baseScale * 0.65f);
            lbl.Visible = true;

            _activeFloatingTexts.Add(new ActiveFloatingText
            {
                Label = lbl,
                StartPos = elevatedStart,
                BaseColor = color,
                BaseScale = baseScale,
                ElapsedSec = 0f,
                DurationSec = durationSec
            });
        }

        private void SpawnGrenadeArc(Vector3 from, Vector3 to, float durationSec, bool targetIndoor = false)
        {
            if (_grenadePool.Count == 0)
            {
                SpawnExplosionDome(to, 0.40f);
                return;
            }

            MeshInstance3D nade = _grenadePool.Pop();
            nade.Position = from;
            nade.Visible = true;

            Vector3 bouncePos = from.Lerp(to, 0.72f) + new Vector3(0f, 3.1f, 0f);
            _activeGrenades.Add(new ActiveGrenadeArc
            {
                Instance = nade,
                StartPos = from,
                BouncePos = bouncePos,
                EndPos = to,
                HasIndoorBounce = targetIndoor,
                BounceTriggered = false,
                ElapsedSec = 0f,
                DurationSec = durationSec
            });
        }

        private void SpawnExplosionDome(Vector3 pos, float durationSec)
        {
            if (_explosionPool.Count == 0) return;
            var (inst, mat) = _explosionPool.Pop();
            inst.Position = pos;
            inst.Scale = new Vector3(1.5f, 1.2f, 1.5f);
            mat.AlbedoColor = new Color(1.0f, 0.55f, 0.12f, 0.72f);
            inst.Visible = true;

            _activeExplosions.Add(new ActiveExplosionDome
            {
                Instance = inst,
                Material = mat,
                ElapsedSec = 0f,
                DurationSec = durationSec
            });
        }

        private void ClearPoolChildren()
        {
            _activeTracers.Clear();
            _tracerPool.Clear();
            _activeGrenades.Clear();
            _grenadePool.Clear();
            _activeExplosions.Clear();
            _explosionPool.Clear();
            _activePulseRings.Clear();
            _pulseRingPool.Clear();
            _activeFloatingTexts.Clear();
            _floatingTextPool.Clear();
            foreach (Node child in GetChildren())
            {
                child.QueueFree();
            }
        }
    }
}
