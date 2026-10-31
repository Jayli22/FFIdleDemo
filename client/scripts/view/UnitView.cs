using System;
using System.Collections.Generic;
using FFM.Client.Art;
using FFM.Client.Sim;
using FFM.Sim.Data;
using Godot;

namespace FFM.Client.View
{
    /// <summary>
    /// Visual representation of a single esports player unit on the 3D battlefield (§6.3, WP-13).
    /// Upgraded with a 6-part articulated Low-Poly character rig, role-specific helmet/headgear,
    /// dynamic 3D weapon attachment, muzzle flash, death loot crate, and 8 procedural animations.
    /// </summary>
    public partial class UnitView : Node3D
    {
        private Node3D? _characterRig;
        private MeshInstance3D? _torsoMesh;
        private MeshInstance3D? _headMesh;
        private Node3D? _leftArmPivot;
        private MeshInstance3D? _leftArmMesh;
        private Node3D? _rightArmPivot;
        private MeshInstance3D? _rightArmMesh;
        private MeshInstance3D? _weaponMesh;
        private MeshInstance3D? _muzzleFlashMesh;
        private Node3D? _leftLegPivot;
        private MeshInstance3D? _leftLegMesh;
        private Node3D? _rightLegPivot;
        private MeshInstance3D? _rightLegMesh;

        private MeshInstance3D? _deathCrateMesh;
        private MeshInstance3D? _facingPointer;
        private MeshInstance3D? _macroBeacon;
        private MeshInstance3D? _skillAuraRing;
        private StandardMaterial3D? _skillAuraMat;
        private MeshInstance3D? _chronoDomeMesh;
        private StandardMaterial3D? _chronoDomeMat;
        private MeshInstance3D? _microActionRing;
        private StandardMaterial3D? _microActionMat;
        private MeshInstance3D? _coverShieldArcMesh;
        private StandardMaterial3D? _coverShieldArcMat;
        private MeshInstance3D? _skySurfboardMesh;
        private MeshInstance3D? _parachuteCanopyMesh;

        // World-aligned Overhead Esports Nameplate & 3D Micro Health/Armor Bar Assembly
        private Node3D? _worldAlignedHudRoot;
        private Node3D? _hudAnchor;
        private Node3D? _hpBarPitchNode;
        private MeshInstance3D? _hpBarBgMesh;
        private MeshInstance3D? _hpBarFillMesh;
        private StandardMaterial3D? _hpBarFillMat;
        private MeshInstance3D? _armorBarFillMesh;
        private StandardMaterial3D? _armorBarFillMat;
        private MeshInstance3D? _leaderLineMesh;
        private StandardMaterial3D? _leaderLineMat;
        private Label3D? _overheadLabel;

        private readonly List<MeshInstance3D> _teamShadedMeshes = new(10);

        private Color _teamColor = new(1.0f, 0.72f, 0.0f);
        private string _displayName = "P1";
        private string _teamTag = "T01";
        private string _shortCallsign = "P1";
        private Role _currentRole = Role.Rusher;
        private bool _currentIsIgl;
        private int _currentWeaponIdx = -999;
        private IReadOnlyList<WeaponDef>? _weaponsCatalog;

        private Vector2 _targetScreenOffset;
        private Vector2 _smoothedScreenOffset;
        private float _currentCameraPitchRad = 1.02f;
        private bool _isCompactClusterMode;

        private float _hitFlash;
        private float _lastAppliedHitFlash = -1f;
        private bool _lastAppliedKnocked;
        private float _muzzleFlashTimer;
        private float _headshotCalloutTimer;
        private float _selectionPulseTimer;
        private float _touchdownRingTimer;
        private UnitDropState _lastDropState = UnitDropState.Grounded;
        private float _lastHp = 200f;
        private float _animTime;
        private Vector3 _lastWorldPos;
        private bool _hasLastPos;

        public int UnitId { get; private set; }
        public int SquadId { get; private set; }
        public string TeamTag => _teamTag;
        public string ShortCallsign => _shortCallsign;
        public WeaponCategory EquippedWeaponCategory { get; private set; } = WeaponCategory.AR;

        public void Initialize(
            int unitId,
            int squadId,
            string displayName,
            Color teamColor,
            ShaderMaterial sharedUnitMaterial,
            Role initialRole = Role.Rusher,
            bool isIgl = false,
            IReadOnlyList<WeaponDef>? weaponsCatalog = null)
        {
            UnitId = unitId;
            SquadId = squadId;
            _displayName = displayName;
            int dotIdx = displayName.IndexOf('·');
            if (dotIdx > 0 && dotIdx < displayName.Length - 1)
            {
                _teamTag = displayName[..dotIdx].Trim();
                _shortCallsign = displayName[(dotIdx + 1)..].Trim();
            }
            else
            {
                _teamTag = $"T{squadId + 1:00}";
                _shortCallsign = displayName.Trim();
            }
            if (_shortCallsign.Length > 9)
            {
                _shortCallsign = _shortCallsign[..9];
            }

            _teamColor = teamColor;
            _currentRole = initialRole;
            _currentIsIgl = isIgl;
            _weaponsCatalog = weaponsCatalog;
            _animTime = (unitId * 0.43f) % 6.28f;
            _targetScreenOffset = Vector2.Zero;
            _smoothedScreenOffset = Vector2.Zero;

            _teamShadedMeshes.Clear();

            var vertexColorMat = new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Roughness = 0.55f,
                Metallic = 0.25f
            };

            // 1. Articulated 6-Part Low-Poly Character Rig
            _characterRig = new Node3D { Name = "CharacterRig" };
            AddChild(_characterRig);

            _torsoMesh = new MeshInstance3D
            {
                Name = "Torso",
                Mesh = LowPolyMeshFactory.GetTorsoMesh(),
                MaterialOverride = sharedUnitMaterial,
                Position = new Vector3(0f, 3.25f, 0f)
            };
            _characterRig.AddChild(_torsoMesh);
            _teamShadedMeshes.Add(_torsoMesh);

            _headMesh = new MeshInstance3D
            {
                Name = "Head",
                Mesh = LowPolyMeshFactory.GetHeadMesh(initialRole, isIgl),
                MaterialOverride = sharedUnitMaterial,
                Position = new Vector3(0f, 5.15f, 0f)
            };
            _characterRig.AddChild(_headMesh);
            _teamShadedMeshes.Add(_headMesh);

            _leftArmPivot = new Node3D
            {
                Name = "LeftArmPivot",
                Position = new Vector3(0f, 4.95f, -1.65f)
            };
            _characterRig.AddChild(_leftArmPivot);

            _leftArmMesh = new MeshInstance3D
            {
                Name = "LeftArm",
                Mesh = LowPolyMeshFactory.GetArmMesh(true),
                MaterialOverride = sharedUnitMaterial
            };
            _leftArmPivot.AddChild(_leftArmMesh);
            _teamShadedMeshes.Add(_leftArmMesh);

            _rightArmPivot = new Node3D
            {
                Name = "RightArmPivot",
                Position = new Vector3(0f, 4.95f, 1.65f)
            };
            _characterRig.AddChild(_rightArmPivot);

            _rightArmMesh = new MeshInstance3D
            {
                Name = "RightArm",
                Mesh = LowPolyMeshFactory.GetArmMesh(false),
                MaterialOverride = sharedUnitMaterial
            };
            _rightArmPivot.AddChild(_rightArmMesh);
            _teamShadedMeshes.Add(_rightArmMesh);

            // 3D Weapon attached to Right Hand
            _weaponMesh = new MeshInstance3D
            {
                Name = "EquippedWeapon3D",
                Mesh = LowPolyMeshFactory.GetWeaponMesh(WeaponCategory.AR, 1),
                MaterialOverride = vertexColorMat,
                Position = new Vector3(0.55f, -2.05f, -0.25f)
            };
            _rightArmPivot.AddChild(_weaponMesh);

            // Muzzle Flash at barrel tip
            var flashMesh = new PrismMesh { Size = new Vector3(1.4f, 1.8f, 1.4f) };
            var flashMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1.0f, 0.90f, 0.35f, 0.92f)
            };
            _muzzleFlashMesh = new MeshInstance3D
            {
                Name = "MuzzleFlash",
                Mesh = flashMesh,
                MaterialOverride = flashMat,
                Position = new Vector3(3.2f, -1.95f, -0.25f),
                RotationDegrees = new Vector3(0f, 0f, -90f),
                Visible = false
            };
            _rightArmPivot.AddChild(_muzzleFlashMesh);

            _leftLegPivot = new Node3D
            {
                Name = "LeftLegPivot",
                Position = new Vector3(0f, 3.25f, -0.68f)
            };
            _characterRig.AddChild(_leftLegPivot);

            _leftLegMesh = new MeshInstance3D
            {
                Name = "LeftLeg",
                Mesh = LowPolyMeshFactory.GetLegMesh(),
                MaterialOverride = sharedUnitMaterial
            };
            _leftLegPivot.AddChild(_leftLegMesh);
            _teamShadedMeshes.Add(_leftLegMesh);

            _rightLegPivot = new Node3D
            {
                Name = "RightLegPivot",
                Position = new Vector3(0f, 3.25f, 0.68f)
            };
            _characterRig.AddChild(_rightLegPivot);

            _rightLegMesh = new MeshInstance3D
            {
                Name = "RightLeg",
                Mesh = LowPolyMeshFactory.GetLegMesh(),
                MaterialOverride = sharedUnitMaterial
            };
            _rightLegPivot.AddChild(_rightLegMesh);
            _teamShadedMeshes.Add(_rightLegMesh);

            // 1b. Free Fire Sky Surfboard (visible during Freefall dive)
            _skySurfboardMesh = new MeshInstance3D
            {
                Name = "SkySurfboard",
                Mesh = LowPolyMeshFactory.GetSkySurfboardMesh(),
                MaterialOverride = sharedUnitMaterial,
                Position = new Vector3(0f, 0.05f, 0f),
                Visible = false
            };
            _characterRig.AddChild(_skySurfboardMesh);
            _teamShadedMeshes.Add(_skySurfboardMesh);

            // 1c. Parachute Ram-Air Canopy + Rigging Cords (visible during Gliding descent)
            _parachuteCanopyMesh = new MeshInstance3D
            {
                Name = "ParachuteCanopy",
                Mesh = LowPolyMeshFactory.GetParachuteCanopyMesh(),
                MaterialOverride = sharedUnitMaterial,
                Position = new Vector3(0f, 4.95f, 0f),
                Visible = false
            };
            _characterRig.AddChild(_parachuteCanopyMesh);
            _teamShadedMeshes.Add(_parachuteCanopyMesh);

            // 2. Facing Direction Cone on ground
            var pointerPrism = new PrismMesh
            {
                Size = new Vector3(2.4f, 3.2f, 0.7f)
            };
            _facingPointer = new MeshInstance3D
            {
                Name = "FacingPointer",
                Mesh = pointerPrism,
                MaterialOverride = sharedUnitMaterial,
                Position = new Vector3(3.8f, 0.45f, 0f),
                RotationDegrees = new Vector3(0f, 0f, -90f)
            };
            AddChild(_facingPointer);
            _teamShadedMeshes.Add(_facingPointer);

            // 3. Macro Tactical Puck (LOD2 high-altitude visibility + ground ring)
            var puckMesh = new CylinderMesh
            {
                TopRadius = 7.8f,
                BottomRadius = 8.4f,
                Height = 1.6f,
                RadialSegments = 24
            };
            _macroBeacon = new MeshInstance3D
            {
                Name = "MacroBeacon",
                Mesh = puckMesh,
                MaterialOverride = sharedUnitMaterial,
                Position = new Vector3(0f, 0.9f, 0f)
            };
            AddChild(_macroBeacon);
            _teamShadedMeshes.Add(_macroBeacon);

            // Apply initial team color shader uniforms
            foreach (var m in _teamShadedMeshes)
            {
                m.SetInstanceShaderParameter("team_color", _teamColor);
                m.SetInstanceShaderParameter("hit_flash", 0.0f);
                m.SetInstanceShaderParameter("knocked_dim", 0.0f);
            }

            // 4. Death Loot Crate (shown when eliminated)
            _deathCrateMesh = new MeshInstance3D
            {
                Name = "DeathLootCrate",
                Mesh = LowPolyMeshFactory.GetDeathLootCrateMesh(),
                MaterialOverride = vertexColorMat,
                Visible = false
            };
            AddChild(_deathCrateMesh);

            // 5. Active Skill 3D Aura Ring + Chrono 3D Dome Shield
            var auraMesh = new CylinderMesh
            {
                TopRadius = 8.5f,
                BottomRadius = 8.5f,
                Height = 1.8f,
                RadialSegments = 28,
                CapTop = false,
                CapBottom = false
            };
            _skillAuraMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(0.18f, 0.84f, 0.45f, 0.55f)
            };
            _skillAuraRing = new MeshInstance3D
            {
                Name = "SkillAuraRing",
                Mesh = auraMesh,
                MaterialOverride = _skillAuraMat,
                Position = new Vector3(0f, 1.1f, 0f),
                Visible = false
            };
            AddChild(_skillAuraRing);

            var domeSphere = new SphereMesh
            {
                Radius = 5.2f,
                Height = 9.2f,
                RadialSegments = 24,
                Rings = 12
            };
            _chronoDomeMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(0.58f, 0.32f, 0.96f, 0.34f)
            };
            _chronoDomeMesh = new MeshInstance3D
            {
                Name = "ChronoForceFieldDome",
                Mesh = domeSphere,
                MaterialOverride = _chronoDomeMat,
                Position = new Vector3(0f, 3.8f, 0f),
                Visible = false
            };
            AddChild(_chronoDomeMesh);

            // 5b. Unit Micro-Action & Selection Ring (Heal / Revive / Knocked / Squad Select)
            var microMesh = new CylinderMesh
            {
                TopRadius = 4.6f,
                BottomRadius = 4.8f,
                Height = 1.1f,
                RadialSegments = 24,
                CapTop = false,
                CapBottom = false
            };
            _microActionMat = new StandardMaterial3D
            {
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(0.18f, 0.84f, 0.45f, 0.65f)
            };
            _microActionRing = new MeshInstance3D
            {
                Name = "MicroActionRing",
                Mesh = microMesh,
                MaterialOverride = _microActionMat,
                Position = new Vector3(0f, 0.7f, 0f),
                Visible = false
            };
            AddChild(_microActionRing);

            // 5c. 3D Directional Cover Shield Arc (120-degree holographic barrier toward CoverFacingRadians)
            _coverShieldArcMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                AlbedoColor = new Color(0.0f, 0.85f, 1.0f, 0.68f)
            };
            _coverShieldArcMesh = new MeshInstance3D
            {
                Name = "DirectionalCoverShieldArc",
                Mesh = LowPolyMeshFactory.GetDirectionalCoverShieldArcMesh(),
                MaterialOverride = _coverShieldArcMat,
                Position = Vector3.Zero,
                Visible = false
            };
            AddChild(_coverShieldArcMesh);

            // 6. World-Aligned Esports Overhead HUD (3D Health/Armor Strip + Leader Line + Compact Callsign)
            _worldAlignedHudRoot = new Node3D
            {
                Name = "WorldAlignedHudRoot",
                Position = Vector3.Zero
            };
            AddChild(_worldAlignedHudRoot);

            _leaderLineMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = _teamColor.Lightened(0.20f) with { A = 0.55f }
            };
            _leaderLineMesh = new MeshInstance3D
            {
                Name = "HudLeaderLine",
                Mesh = new BoxMesh { Size = new Vector3(0.08f, 1.0f, 0.08f) },
                MaterialOverride = _leaderLineMat,
                Visible = false
            };
            _worldAlignedHudRoot.AddChild(_leaderLineMesh);

            _hudAnchor = new Node3D
            {
                Name = "HudAnchor",
                Position = new Vector3(0f, 6.85f, 0f)
            };
            _worldAlignedHudRoot.AddChild(_hudAnchor);

            _hpBarPitchNode = new Node3D
            {
                Name = "HpBarPitchNode",
                Rotation = new Vector3(-1.02f, 0f, 0f)
            };
            _hudAnchor.AddChild(_hpBarPitchNode);

            var hpBgMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(0.04f, 0.06f, 0.10f, 0.86f)
            };
            _hpBarBgMesh = new MeshInstance3D
            {
                Name = "HpBarBg",
                Mesh = new BoxMesh { Size = new Vector3(2.86f, 0.42f, 0.06f) },
                MaterialOverride = hpBgMat,
                Position = Vector3.Zero
            };
            _hpBarPitchNode.AddChild(_hpBarBgMesh);

            _hpBarFillMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = _teamColor.Lightened(0.18f)
            };
            _hpBarFillMesh = new MeshInstance3D
            {
                Name = "HpBarFill",
                Mesh = new BoxMesh { Size = new Vector3(2.70f, 0.22f, 0.08f) },
                MaterialOverride = _hpBarFillMat,
                Position = new Vector3(0f, 0.06f, 0.02f)
            };
            _hpBarPitchNode.AddChild(_hpBarFillMesh);

            _armorBarFillMat = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(0.35f, 0.90f, 1.0f)
            };
            _armorBarFillMesh = new MeshInstance3D
            {
                Name = "ArmorBarFill",
                Mesh = new BoxMesh { Size = new Vector3(2.70f, 0.08f, 0.08f) },
                MaterialOverride = _armorBarFillMat,
                Position = new Vector3(0f, -0.11f, 0.02f)
            };
            _hpBarPitchNode.AddChild(_armorBarFillMesh);

            _overheadLabel = new Label3D
            {
                Name = "OverheadLabel",
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                FontSize = 23,
                OutlineSize = 6,
                Modulate = _teamColor.Lightened(0.28f),
                OutlineModulate = new Color(0.03f, 0.05f, 0.09f, 0.94f),
                Position = new Vector3(0f, 0.78f, 0f),
                PixelSize = 0.078f
            };
            _hudAnchor.AddChild(_overheadLabel);
        }

        public void SetHudDeClutterState(Vector2 screenOffsetXY, float cameraPitchRad, bool compactClusterMode)
        {
            _targetScreenOffset = screenOffsetXY;
            _currentCameraPitchRad = cameraPitchRad;
            _isCompactClusterMode = compactClusterMode;
        }

        public void TriggerHitFlash()
        {
            _hitFlash = 1.0f;
        }

        public void TriggerMuzzleFlash()
        {
            _muzzleFlashTimer = 0.11f;
            if (_muzzleFlashMesh != null)
            {
                _muzzleFlashMesh.Visible = true;
            }
        }

        public void TriggerHeadshotCallout()
        {
            _headshotCalloutTimer = 1.35f;
        }

        public void TriggerSelectionPulse()
        {
            _selectionPulseTimer = 1.65f;
        }

        public void ApplyState(in InterpolatedUnitState state, UnitLodLevel lod, float dt)
        {
            if (state.WorldPos3D.LengthSquared() < 1f || state.DropState == UnitDropState.InPlane)
            {
                Visible = false;
                _lastDropState = state.DropState;
                return;
            }

            if (_lastDropState != UnitDropState.Grounded && state.DropState == UnitDropState.Grounded)
            {
                _touchdownRingTimer = 1.15f;
            }
            _lastDropState = state.DropState;

            if (_touchdownRingTimer > 0f)
            {
                _touchdownRingTimer = Math.Max(0f, _touchdownRingTimer - dt);
            }

            Visible = true;
            Position = state.WorldPos3D;

            // Eliminated units leave behind a Low-Poly Death Loot Crate
            if (state.State == UnitState.Eliminated)
            {
                if (_characterRig != null) _characterRig.Visible = false;
                if (_facingPointer != null) _facingPointer.Visible = false;
                if (_macroBeacon != null) _macroBeacon.Visible = false;
                if (_skillAuraRing != null) _skillAuraRing.Visible = false;
                if (_chronoDomeMesh != null) _chronoDomeMesh.Visible = false;
                if (_microActionRing != null) _microActionRing.Visible = false;
                if (_coverShieldArcMesh != null) _coverShieldArcMesh.Visible = false;
                if (_worldAlignedHudRoot != null) _worldAlignedHudRoot.Visible = false;
                if (_deathCrateMesh != null)
                {
                    _deathCrateMesh.Visible = lod != UnitLodLevel.Lod2Macro;
                }
                return;
            }

            if (_deathCrateMesh != null) _deathCrateMesh.Visible = false;
            Rotation = new Vector3(0f, -state.FacingRadians, 0f);

            bool isAirborne = state.IsAirborne;
            if (_skySurfboardMesh != null)
            {
                _skySurfboardMesh.Visible = state.DropState == UnitDropState.Freefall;
            }
            if (_parachuteCanopyMesh != null)
            {
                bool showChute = state.DropState == UnitDropState.Gliding;
                _parachuteCanopyMesh.Visible = showChute;
                if (showChute)
                {
                    _parachuteCanopyMesh.Rotation = new Vector3(
                        MathF.Sin(_animTime * 2.8f) * 0.08f,
                        0f,
                        MathF.Cos(_animTime * 2.1f) * 0.06f);
                }
            }
            if (_weaponMesh != null)
            {
                _weaponMesh.Visible = !isAirborne;
            }

            // Update role headgear if role/IGL changed
            if ((state.Role != _currentRole || state.IsIgl != _currentIsIgl) && _headMesh != null)
            {
                _currentRole = state.Role;
                _currentIsIgl = state.IsIgl;
                _headMesh.Mesh = LowPolyMeshFactory.GetHeadMesh(_currentRole, _currentIsIgl);
            }

            // Update 3D weapon mesh if PrimaryWeaponIndex changed
            if (state.PrimaryWeaponIndex != _currentWeaponIdx && _weaponMesh != null)
            {
                _currentWeaponIdx = state.PrimaryWeaponIndex;
                WeaponCategory cat = WeaponCategory.AR;
                int tier = 1;
                if (_weaponsCatalog != null && _currentWeaponIdx >= 0 && _currentWeaponIdx < _weaponsCatalog.Count)
                {
                    cat = _weaponsCatalog[_currentWeaponIdx].GetCategoryEnum();
                    tier = _weaponsCatalog[_currentWeaponIdx].Tier;
                }
                EquippedWeaponCategory = cat;
                _weaponMesh.Mesh = LowPolyMeshFactory.GetWeaponMesh(cat, tier);
            }

            // Detect damage for visual hit flash
            if (state.Hp < _lastHp - 1.5f)
            {
                _hitFlash = 1.0f;
            }
            _lastHp = state.Hp;

            if (_hitFlash > 0f)
            {
                _hitFlash = Math.Max(0f, _hitFlash - dt * 5.5f);
            }

            if (_muzzleFlashTimer > 0f)
            {
                _muzzleFlashTimer = Math.Max(0f, _muzzleFlashTimer - dt);
                if (_muzzleFlashMesh != null)
                {
                    _muzzleFlashMesh.Visible = _muzzleFlashTimer > 0f && lod != UnitLodLevel.Lod2Macro && !isAirborne;
                }
            }

            if (_headshotCalloutTimer > 0f)
            {
                _headshotCalloutTimer = Math.Max(0f, _headshotCalloutTimer - dt);
            }

            if (_selectionPulseTimer > 0f)
            {
                _selectionPulseTimer = Math.Max(0f, _selectionPulseTimer - dt);
            }

            bool isKnocked = state.State == UnitState.Knocked;
            if (MathF.Abs(_hitFlash - _lastAppliedHitFlash) > 0.01f || isKnocked != _lastAppliedKnocked)
            {
                _lastAppliedHitFlash = _hitFlash;
                _lastAppliedKnocked = isKnocked;
                float knockedVal = isKnocked ? 1.0f : 0.0f;
                for (int i = 0; i < _teamShadedMeshes.Count; i++)
                {
                    var m = _teamShadedMeshes[i];
                    m.SetInstanceShaderParameter("hit_flash", _hitFlash);
                    m.SetInstanceShaderParameter("knocked_dim", knockedVal);
                }
            }

            // Compute horizontal movement velocity for procedural animation
            float moveSpeed = 0f;
            if (_hasLastPos && dt > 0.0001f)
            {
                Vector2 delta2D = new(state.WorldPos3D.X - _lastWorldPos.X, state.WorldPos3D.Z - _lastWorldPos.Z);
                moveSpeed = delta2D.Length() / dt;
            }
            _lastWorldPos = state.WorldPos3D;
            _hasLastPos = true;

            _animTime += dt * (1.0f + Math.Clamp(moveSpeed * 0.12f, 0f, 1.8f));

            if (_characterRig != null)
            {
                _characterRig.Visible = lod != UnitLodLevel.Lod2Macro || isAirborne;
                if (_characterRig.Visible)
                {
                    AnimateCharacterRig(in state, moveSpeed, isKnocked);
                }
            }

            if (_facingPointer != null)
            {
                _facingPointer.Visible = lod == UnitLodLevel.Lod0Close && !isKnocked && !isAirborne;
            }

            if (_macroBeacon != null)
            {
                _macroBeacon.Visible = !isAirborne;
                float beaconScale = lod == UnitLodLevel.Lod2Macro ? 1.45f : (lod == UnitLodLevel.Lod1Medium ? 0.72f : 0.42f);
                _macroBeacon.Scale = new Vector3(beaconScale, 1f, beaconScale);
            }

            // 3D Directional Cover Shield Arc (orients toward CoverFacingRadians in world space)
            if (_coverShieldArcMesh != null && _coverShieldArcMat != null)
            {
                bool showCoverArc = !isAirborne && lod != UnitLodLevel.Lod2Macro && !isKnocked && (state.CoverPercent >= 25 || state.IsFlanked);
                _coverShieldArcMesh.Visible = showCoverArc;
                if (showCoverArc)
                {
                    float localShieldYaw = state.FacingRadians - state.CoverFacingRadians;
                    _coverShieldArcMesh.Rotation = new Vector3(0f, localShieldYaw, 0f);
                    if (state.IsFlanked)
                    {
                        float fPulse = 0.85f + 0.22f * MathF.Sin(_animTime * 14f);
                        _coverShieldArcMesh.Scale = new Vector3(1.08f, fPulse, 1.08f);
                        _coverShieldArcMat.AlbedoColor = new Color(1.0f, 0.20f, 0.26f, 0.78f);
                    }
                    else if (state.CoverPercent >= 65)
                    {
                        float cPulse = 0.96f + 0.08f * MathF.Sin(_animTime * 5.5f);
                        _coverShieldArcMesh.Scale = new Vector3(1.0f, cPulse, 1.0f);
                        _coverShieldArcMat.AlbedoColor = new Color(0.0f, 0.88f, 1.0f, 0.72f);
                    }
                    else
                    {
                        _coverShieldArcMesh.Scale = new Vector3(0.92f, 0.85f, 0.92f);
                        _coverShieldArcMat.AlbedoColor = new Color(0.18f, 0.92f, 0.62f, 0.60f);
                    }
                }
            }

            // Active Skill 3D Aura & Chrono Force Field Dome
            bool skillOn = !isAirborne && state.ActiveSkillDurationRemaining > 0.05f && !isKnocked;
            if (_skillAuraRing != null && _skillAuraMat != null)
            {
                _skillAuraRing.Visible = skillOn;
                if (skillOn)
                {
                    Color auraCol = GetSkillCategoryColor(state.ActiveSkillCategory);
                    _skillAuraMat.AlbedoColor = auraCol;
                    float wave = state.ActiveSkillCategory == SkillCategory.Heal
                        ? 1.0f + 0.25f * ((_animTime * 1.8f) % 1.0f)
                        : 1.0f + 0.14f * MathF.Sin(_animTime * 9f);
                    _skillAuraRing.Scale = new Vector3(wave, 1f, wave);
                }
            }

            if (_chronoDomeMesh != null && _chronoDomeMat != null)
            {
                bool showDome = skillOn && state.ActiveSkillCategory == SkillCategory.Shield && lod != UnitLodLevel.Lod2Macro;
                _chronoDomeMesh.Visible = showDome;
                if (showDome)
                {
                    float domePulse = 1.0f + 0.05f * MathF.Sin(_animTime * 8f);
                    _chronoDomeMesh.Scale = new Vector3(domePulse, domePulse, domePulse);
                    _chronoDomeMat.AlbedoColor = new Color(0.56f, 0.30f, 0.98f, 0.30f + 0.08f * MathF.Sin(_animTime * 10f));
                }
            }

            // Micro-Action & Selection 3D Ring (Heal / Revive / Knocked / Selected Squad / Touchdown)
            if (_microActionRing != null && _microActionMat != null)
            {
                if (_touchdownRingTimer > 0f)
                {
                    _microActionRing.Visible = true;
                    float tdProgress = 1f - (_touchdownRingTimer / 1.15f);
                    float s = 0.75f + tdProgress * 1.65f;
                    _microActionRing.Scale = new Vector3(s, 0.7f, s);
                    _microActionRing.Position = new Vector3(0f, 0.45f, 0f);
                    _microActionMat.AlbedoColor = new Color(1.0f, 0.80f, 0.18f, Math.Clamp(_touchdownRingTimer * 0.75f, 0.1f, 0.85f));
                }
                else if (_selectionPulseTimer > 0f)
                {
                    _microActionRing.Visible = true;
                    float p = 1.0f + (1.65f - _selectionPulseTimer) * 0.55f;
                    _microActionRing.Scale = new Vector3(p, 1.3f, p);
                    _microActionRing.Position = new Vector3(0f, 0.8f, 0f);
                    _microActionMat.AlbedoColor = new Color(0.0f, 0.90f, 1.0f, Math.Clamp(_selectionPulseTimer * 0.55f, 0.15f, 0.85f));
                }
                else if (isKnocked)
                {
                    _microActionRing.Visible = lod != UnitLodLevel.Lod2Macro;
                    float sosPulse = 0.82f + 0.18f * MathF.Sin(_animTime * 7.5f);
                    _microActionRing.Scale = new Vector3(sosPulse, 0.9f, sosPulse);
                    _microActionRing.Position = new Vector3(0f, 0.55f, 0f);
                    _microActionMat.AlbedoColor = new Color(1.0f, 0.22f, 0.28f, 0.72f);
                }
                else if (!isAirborne && (state.CurrentBehavior == Behavior.Revive || state.CurrentBehavior == Behavior.Heal))
                {
                    _microActionRing.Visible = lod != UnitLodLevel.Lod2Macro;
                    float rise = (_animTime * 1.6f) % 1.0f;
                    float s = 0.85f + rise * 0.35f;
                    _microActionRing.Scale = new Vector3(s, 1.1f, s);
                    _microActionRing.Position = new Vector3(0f, 0.6f + rise * 3.2f, 0f);
                    _microActionMat.AlbedoColor = state.CurrentBehavior == Behavior.Revive
                        ? new Color(0.18f, 0.95f, 0.52f, (1.0f - rise * 0.65f) * 0.80f)
                        : new Color(0.25f, 0.88f, 0.45f, (1.0f - rise * 0.65f) * 0.72f);
                }
                else
                {
                    _microActionRing.Visible = false;
                }
            }

            // World-Aligned Overhead 3D Health/Armor Strip + De-Cluttered Callsign Label
            if (_worldAlignedHudRoot != null && _hudAnchor != null)
            {
                bool showUnitHud = lod != UnitLodLevel.Lod2Macro || isAirborne;
                _worldAlignedHudRoot.Visible = showUnitHud;
                if (showUnitHud)
                {
                    // Counter-rotate UnitView's yaw so _worldAlignedHudRoot stays locked to World/Camera axes
                    _worldAlignedHudRoot.Rotation = new Vector3(0f, state.FacingRadians, 0f);

                    float smoothT = 1f - MathF.Exp(-14f * Math.Max(0.001f, dt));
                    _smoothedScreenOffset = _smoothedScreenOffset.Lerp(_targetScreenOffset, smoothT);

                    float cosPitch = Math.Max(0.42f, MathF.Cos(_currentCameraPitchRad));
                    float worldOffsetX = _smoothedScreenOffset.X;
                    float worldOffsetY = Math.Clamp(Math.Max(0f, _smoothedScreenOffset.Y) / cosPitch, 0f, 13.5f);

                    float baseHeadHudY = state.DropState == UnitDropState.Gliding
                        ? 11.4f
                        : (isKnocked ? 4.2f : 6.85f);
                    Vector3 anchorPos = new(worldOffsetX, baseHeadHudY + worldOffsetY, 0f);
                    _hudAnchor.Position = anchorPos;

                    // Update subtle leader line when anti-overlap pushes nameplate away from helmet
                    if (_leaderLineMesh != null && _leaderLineMat != null)
                    {
                        Vector3 headTop = new(0f, isKnocked ? 2.4f : 5.75f, 0f);
                        Vector3 barBottom = anchorPos + new Vector3(0f, -0.22f, 0f);
                        float dx = barBottom.X - headTop.X;
                        float dy = barBottom.Y - headTop.Y;
                        float stemLen = MathF.Sqrt(dx * dx + dy * dy);
                        bool showStem = !isAirborne && _smoothedScreenOffset.LengthSquared() > 1.8f && stemLen > 1.0f;
                        _leaderLineMesh.Visible = showStem;
                        if (showStem)
                        {
                            _leaderLineMesh.Position = (headTop + barBottom) * 0.5f;
                            _leaderLineMesh.Rotation = new Vector3(0f, 0f, -MathF.Atan2(dx, dy));
                            _leaderLineMesh.Scale = new Vector3(1f, stemLen, 1f);
                            _leaderLineMat.AlbedoColor = _teamColor.Lightened(0.22f) with { A = 0.48f };
                        }
                    }

                    // Update 3D Micro Health & Armor Bar
                    float hpRatio = Math.Clamp(state.Hp / Math.Max(1f, state.MaxHp), 0f, 1f);
                    if (_hpBarPitchNode != null && _hpBarFillMesh != null && _hpBarFillMat != null && _armorBarFillMesh != null && _armorBarFillMat != null)
                    {
                        _hpBarPitchNode.Visible = !isAirborne;
                        if (!isAirborne)
                        {
                            _hpBarPitchNode.Rotation = new Vector3(-_currentCameraPitchRad, 0f, 0f);
                            float fillScale = Math.Max(0.03f, hpRatio);
                            _hpBarFillMesh.Scale = new Vector3(fillScale, 1f, 1f);
                            _hpBarFillMesh.Position = new Vector3(-1.35f * (1f - fillScale), 0.06f, 0.02f);

                            _hpBarFillMat.AlbedoColor = isKnocked
                                ? new Color(1.0f, 0.22f, 0.28f)
                                : (hpRatio < 0.35f
                                    ? new Color(1.0f, 0.36f, 0.22f)
                                    : (hpRatio < 0.65f
                                        ? new Color(1.0f, 0.78f, 0.16f)
                                        : _teamColor.Lightened(0.18f)));

                            bool hasArmor = state.ArmorTier > 0 && !isKnocked;
                            _armorBarFillMesh.Visible = hasArmor;
                            if (hasArmor)
                            {
                                float armorRatio = Math.Clamp(state.ArmorTier / 3f, 0.25f, 1f);
                                _armorBarFillMesh.Scale = new Vector3(armorRatio, 1f, 1f);
                                _armorBarFillMesh.Position = new Vector3(-1.35f * (1f - armorRatio), -0.11f, 0.02f);
                                _armorBarFillMat.AlbedoColor = state.ArmorTier >= 3
                                    ? new Color(1.0f, 0.82f, 0.22f)
                                    : new Color(0.30f, 0.90f, 1.0f);
                            }
                        }
                    }

                    // Update concise, non-occluding Callsign Label
                    if (_overheadLabel != null)
                    {
                        string callsign = state.IsIgl ? $"★{_shortCallsign}" : _shortCallsign;
                        string statusIcon = GetCompactStatusIcon(in state, isKnocked, _headshotCalloutTimer > 0f);

                        if (lod == UnitLodLevel.Lod0Close && !_isCompactClusterMode && !isAirborne)
                        {
                            string roleShort = GetRoleShortTag(state.Role, false);
                            _overheadLabel.Text = $"{callsign}·{roleShort}{statusIcon}";
                            _overheadLabel.Scale = Vector3.One;
                        }
                        else
                        {
                            _overheadLabel.Text = $"{callsign}{statusIcon}";
                            _overheadLabel.Scale = new Vector3(0.94f, 0.94f, 0.94f);
                        }

                        Color baseColor = isKnocked
                            ? new Color(1.0f, 0.30f, 0.36f)
                            : (state.IsFlanked
                                ? new Color(1.0f, 0.46f, 0.26f)
                                : (_headshotCalloutTimer > 0f
                                    ? new Color(1.0f, 0.88f, 0.18f)
                                    : (state.CurrentBehavior == Behavior.Revive || state.CurrentBehavior == Behavior.Heal
                                        ? new Color(0.28f, 0.96f, 0.56f)
                                        : _teamColor.Lightened(0.32f))));

                        if (_isCompactClusterMode && !isKnocked && !state.IsIgl && _headshotCalloutTimer <= 0f)
                        {
                            baseColor.A = 0.86f;
                        }
                        _overheadLabel.Modulate = baseColor;
                    }
                }
            }
        }

        private static string GetCompactStatusIcon(in InterpolatedUnitState state, bool isKnocked, bool hasHeadshot)
        {
            if (state.DropState == UnitDropState.Freefall) return $" 🏄{MathF.Round(state.AltitudeMeters):0}m";
            if (state.DropState == UnitDropState.Gliding) return $" 🪂{MathF.Round(state.AltitudeMeters):0}m";
            if (hasHeadshot) return " 🎯";
            if (isKnocked) return " 🆘";
            if (state.CurrentBehavior == Behavior.Revive) return " 💚";
            if (state.CurrentBehavior == Behavior.Heal) return " 💊";
            if (state.IsFlanked) return " ⚠️";
            if (state.IsBreaching || state.AtDoorway) return " 🚪";
            if (state.AtWindow) return " 🪟";
            if (state.IsIndoors) return " 🏠";
            if (state.CoverPercent >= 40) return " 🛡️";
            return "";
        }

        private void AnimateCharacterRig(in InterpolatedUnitState state, float moveSpeed, bool isKnocked)
        {
            if (_characterRig == null || _torsoMesh == null || _headMesh == null ||
                _leftArmPivot == null || _rightArmPivot == null ||
                _leftLegPivot == null || _rightLegPivot == null)
            {
                return;
            }

            if (state.DropState == UnitDropState.Freefall)
            {
                // Free Fire Sky-Surfing Dive Pose on surfboard
                float surfSway = MathF.Sin(_animTime * 6.5f) * 0.12f;
                _characterRig.Position = new Vector3(0f, -0.35f, 0f);
                _characterRig.Rotation = new Vector3(surfSway, 0f, -0.32f);
                _torsoMesh.Rotation = new Vector3(0f, 0.25f, -0.18f);
                _headMesh.Rotation = new Vector3(0f, -0.20f, 0.15f);
                _leftArmPivot.Rotation = new Vector3(0.55f, 0f, 0.85f + surfSway);
                _rightArmPivot.Rotation = new Vector3(-0.55f, 0f, -0.45f - surfSway);
                _leftLegPivot.Rotation = new Vector3(0f, 0f, 0.42f);
                _rightLegPivot.Rotation = new Vector3(0f, 0f, -0.38f);
                return;
            }

            if (state.DropState == UnitDropState.Gliding)
            {
                // Parachute Canopy Gliding Harness Pose (both arms raised holding risers)
                float pendSway = MathF.Sin(_animTime * 2.8f) * 0.08f;
                _characterRig.Position = new Vector3(0f, 0.15f, 0f);
                _characterRig.Rotation = new Vector3(pendSway, 0f, -0.08f);
                _torsoMesh.Rotation = Vector3.Zero;
                _headMesh.Rotation = new Vector3(0f, pendSway * 1.5f, 0.08f);
                _leftArmPivot.Rotation = new Vector3(0.28f, 0f, 2.58f);
                _rightArmPivot.Rotation = new Vector3(-0.28f, 0f, 2.58f);
                _leftLegPivot.Rotation = new Vector3(0f, 0f, 0.22f + pendSway * 0.6f);
                _rightLegPivot.Rotation = new Vector3(0f, 0f, -0.18f - pendSway * 0.6f);
                return;
            }

            if (isKnocked)
            {
                // Knocked crawl pose
                float crawl = MathF.Sin(_animTime * 4.5f) * 0.35f;
                _characterRig.Position = new Vector3(0f, 1.1f, 0f);
                _characterRig.Rotation = new Vector3(0f, 0f, -1.22f);
                _leftArmPivot.Rotation = new Vector3(0f, 0f, 0.9f + crawl);
                _rightArmPivot.Rotation = new Vector3(0f, 0f, 0.9f - crawl);
                _leftLegPivot.Rotation = new Vector3(0f, 0f, -0.25f - crawl * 0.5f);
                _rightLegPivot.Rotation = new Vector3(0f, 0f, -0.25f + crawl * 0.5f);
                return;
            }

            // Reset prone rotation
            _characterRig.Rotation = Vector3.Zero;

            bool isMoving = moveSpeed > 1.25f;
            bool isWindowCell = state.AtWindow;
            bool isBreachingCell = state.IsBreaching || state.AtDoorway;
            bool isInCover = state.CoverPercent >= 35 && !isMoving;

            float legSwing = isMoving ? MathF.Sin(_animTime * 8.5f) * 0.62f : 0f;
            float bob = isMoving ? MathF.Abs(MathF.Cos(_animTime * 8.5f)) * 0.22f : MathF.Sin(_animTime * 2.2f) * 0.06f;

            switch (state.CurrentBehavior)
            {
                case Behavior.Fire:
                {
                    float recoil = MathF.Sin(_animTime * 26f) * 0.11f;
                    float crouchDrop = isWindowCell ? -0.38f : (isInCover ? -0.45f : 0f);
                    float peekTiltX = isWindowCell ? 0.16f : 0f;
                    _characterRig.Position = new Vector3(0f, crouchDrop + bob * 0.5f, 0f);
                    _torsoMesh.Rotation = new Vector3(peekTiltX, 0.18f, -0.10f);
                    _headMesh.Rotation = new Vector3(peekTiltX * 0.6f, 0f, 0.05f);
                    _rightArmPivot.Rotation = new Vector3(0f, 0.08f, 1.18f + recoil);
                    _leftArmPivot.Rotation = new Vector3(0.32f, -0.25f, 1.05f + recoil * 0.8f);
                    _leftLegPivot.Rotation = new Vector3(0f, 0f, isMoving ? legSwing * 0.6f : (isInCover || isWindowCell ? 0.34f : 0.18f));
                    _rightLegPivot.Rotation = new Vector3(0f, 0f, isMoving ? -legSwing * 0.6f : (isInCover || isWindowCell ? -0.30f : -0.18f));
                    break;
                }
                case Behavior.Reload:
                {
                    float magAction = MathF.Sin(_animTime * 10f) * 0.32f;
                    float crouchDrop = isInCover ? -0.70f : 0f;
                    _characterRig.Position = new Vector3(0f, crouchDrop + bob * 0.5f, 0f);
                    _torsoMesh.Rotation = new Vector3(0f, 0f, isInCover ? -0.16f : 0f);
                    _rightArmPivot.Rotation = new Vector3(0f, 0.25f, 0.85f);
                    _leftArmPivot.Rotation = new Vector3(0.45f, -0.35f, 0.85f + magAction);
                    _leftLegPivot.Rotation = new Vector3(0f, 0f, isInCover ? 0.40f : legSwing * 0.7f);
                    _rightLegPivot.Rotation = new Vector3(0f, 0f, isInCover ? -0.34f : -legSwing * 0.7f);
                    break;
                }
                case Behavior.Throw:
                case Behavior.PlaceGloo:
                {
                    float throwArc = MathF.Sin(_animTime * 9.0f) * 0.55f;
                    _characterRig.Position = new Vector3(0f, bob, 0f);
                    _torsoMesh.Rotation = new Vector3(0f, -0.2f, -0.10f);
                    _rightArmPivot.Rotation = new Vector3(0f, 0f, 1.55f + throwArc);
                    _leftArmPivot.Rotation = new Vector3(0f, 0f, 0.45f - throwArc * 0.4f);
                    _leftLegPivot.Rotation = new Vector3(0f, 0f, 0.25f);
                    _rightLegPivot.Rotation = new Vector3(0f, 0f, -0.25f);
                    break;
                }
                case Behavior.Heal:
                case Behavior.Revive:
                case Behavior.TakeCover:
                {
                    // Tactical crouch posture
                    float pulse = MathF.Sin(_animTime * 6.0f) * 0.18f;
                    _characterRig.Position = new Vector3(0f, -0.85f, 0f);
                    _torsoMesh.Rotation = new Vector3(0f, 0f, -0.22f);
                    _rightArmPivot.Rotation = new Vector3(0f, 0.15f, 0.75f + pulse);
                    _leftArmPivot.Rotation = new Vector3(0f, -0.15f, 0.75f - pulse);
                    _leftLegPivot.Rotation = new Vector3(0f, 0f, 0.45f);
                    _rightLegPivot.Rotation = new Vector3(0f, 0f, -0.35f);
                    break;
                }
                default:
                {
                    // Run / Rotate / Loot / Cover Crouch / Doorway Breach
                    if (isInCover)
                    {
                        float breathe = MathF.Sin(_animTime * 3.5f) * 0.08f;
                        _characterRig.Position = new Vector3(0f, -0.72f, 0f);
                        _torsoMesh.Rotation = new Vector3(isWindowCell ? 0.12f : 0f, 0f, -0.18f);
                        _headMesh.Rotation = new Vector3(0f, breathe, 0.08f);
                        _rightArmPivot.Rotation = new Vector3(0f, 0.12f, 0.72f);
                        _leftArmPivot.Rotation = new Vector3(0.22f, -0.18f, 0.68f);
                        _leftLegPivot.Rotation = new Vector3(0f, 0f, 0.42f);
                        _rightLegPivot.Rotation = new Vector3(0f, 0f, -0.34f);
                    }
                    else
                    {
                        float leanZ = isBreachingCell ? -0.26f : (isMoving ? -0.12f : 0f);
                        _characterRig.Position = new Vector3(0f, bob, 0f);
                        _torsoMesh.Rotation = new Vector3(0f, 0f, leanZ);
                        _headMesh.Rotation = Vector3.Zero;
                        _rightArmPivot.Rotation = new Vector3(0f, 0.12f, (isBreachingCell ? 0.85f : 0.52f) - legSwing * 0.55f);
                        _leftArmPivot.Rotation = new Vector3(0.22f, -0.18f, (isBreachingCell ? 0.78f : 0.48f) + legSwing * 0.55f);
                        _leftLegPivot.Rotation = new Vector3(0f, 0f, legSwing);
                        _rightLegPivot.Rotation = new Vector3(0f, 0f, -legSwing);
                    }
                    break;
                }
            }
        }

        private static string GetRoleShortTag(Role role, bool isIgl)
        {
            string baseTag = role switch
            {
                Role.Rusher => "RUS",
                Role.SecondRusher => "2ND",
                Role.Sniper => "SNI",
                Role.Support => "SUP",
                Role.Grenadier => "GRN",
                _ => "PRO"
            };
            return isIgl ? $"{baseTag}★" : baseTag;
        }

        private static Color GetSkillCategoryColor(SkillCategory category)
        {
            return category switch
            {
                SkillCategory.Heal => new Color(0.18f, 0.84f, 0.45f, 0.62f),
                SkillCategory.Dash => new Color(0.0f, 0.83f, 1.0f, 0.62f),
                SkillCategory.Shield => new Color(0.62f, 0.35f, 0.92f, 0.65f),
                SkillCategory.Scan => new Color(1.0f, 0.72f, 0.0f, 0.62f),
                SkillCategory.Deploy => new Color(1.0f, 0.28f, 0.34f, 0.62f),
                _ => new Color(0.18f, 0.84f, 0.45f, 0.55f)
            };
        }
    }
}
