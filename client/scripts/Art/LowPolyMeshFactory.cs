using System;
using System.Collections.Generic;
using FFM.Sim.Data;
using Godot;

namespace FFM.Client.Art
{
    /// <summary>
    /// Procedural Low-Poly 3D Mesh Factory for characters, role headgear, 3D weapons,
    /// curved Gloo Walls, Tactical SUVs, 6 island building styles, and MultiMesh foliage/cover props.
    /// All character meshes use local +X as Forward, +Y as Up, and ±Z as Right/Left to match Sim FacingRadians.
    /// </summary>
    public static class LowPolyMeshFactory
    {
        public enum PropKind
        {
            PalmTree = 0,
            BroadleafTree = 1,
            BushClump = 2,
            RockCluster = 3,
            CargoContainer = 4,
            SandbagWall = 5,
            ConcreteBarrier = 6,
            TacticalCrateCover = 7
        }

        public static ArrayMesh GetPropMesh(PropKind kind)
        {
            return kind switch
            {
                PropKind.PalmTree => GetPalmTreeMesh(),
                PropKind.BroadleafTree => GetBroadleafTreeMesh(),
                PropKind.BushClump => GetBushClumpMesh(),
                PropKind.RockCluster => GetRockClusterMesh(),
                PropKind.SandbagWall => GetTacticalSandbagWallMesh(),
                PropKind.ConcreteBarrier => GetConcreteBarrierMesh(),
                PropKind.TacticalCrateCover => GetTacticalCrateCoverMesh(),
                _ => GetCargoContainerMesh()
            };
        }

        public static ArrayMesh GetTorsoMesh() => GetCharacterTorsoMesh();
        public static ArrayMesh GetHeadMesh(Role role, bool isIgl) => GetCharacterHeadMesh(role, isIgl);
        public static ArrayMesh GetArmMesh(bool isLeft) => GetCharacterArmMesh(!isLeft);
        public static ArrayMesh GetLegMesh() => GetCharacterLegMesh();
        public static ArrayMesh GetDeathLootCrateMesh() => GetLootCrateMesh();
        public static ArrayMesh GetGrenadeMesh() => GetGrenadeProjectileMesh();

        private static BoxMesh? _tracerBeamMesh;

        public static StandardMaterial3D GetSharedVertexMaterial(float metallic = 0.25f, float roughness = 0.45f)
        {
            return new StandardMaterial3D
            {
                VertexColorUseAsAlbedo = true,
                Metallic = metallic,
                Roughness = roughness,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled
            };
        }

        public static Mesh GetTracerBeamMesh()
        {
            return _tracerBeamMesh ??= new BoxMesh
            {
                Size = new Vector3(1.0f, 1.0f, 1.0f)
            };
        }

        private static ArrayMesh? _torsoMesh;
        private static ArrayMesh? _leftArmMesh;
        private static ArrayMesh? _rightArmMesh;
        private static ArrayMesh? _legMesh;
        private static ArrayMesh? _lootCrateMesh;
        private static ArrayMesh? _glooWallMesh;
        private static ArrayMesh? _vehicleMesh;
        private static ArrayMesh? _grenadeMesh;
        private static ArrayMesh? _palmTreeMesh;
        private static ArrayMesh? _broadleafTreeMesh;
        private static ArrayMesh? _bushMesh;
        private static ArrayMesh? _rockMesh;
        private static ArrayMesh? _containerMesh;
        private static ArrayMesh? _sandbagWallMesh;
        private static ArrayMesh? _concreteBarrierMesh;
        private static ArrayMesh? _tacticalCrateCoverMesh;
        private static ArrayMesh? _coverShieldArcMesh;
        private static ArrayMesh? _transportAircraftMesh;
        private static ArrayMesh? _aircraftPropellerMesh;
        private static ArrayMesh? _skySurfboardMesh;
        private static ArrayMesh? _parachuteCanopyMesh;

        private static readonly Dictionary<string, ArrayMesh> HeadMeshCache = new();
        private static readonly Dictionary<string, ArrayMesh> WeaponMeshCache = new();

        // Vertex color helper: alpha = 1.0 means tint by per-instance team_color in unit_team_color.gdshader;
        // alpha = 0.0 means preserve fixed vertex color (skin, tactical vest, boots, visor, gunmetal).
        private static Color TeamTint(float brightness = 1.0f) => new(brightness, brightness, brightness, 1.0f);
        private static Color FixedColor(float r, float g, float b) => new(r, g, b, 0.0f);

        public static ArrayMesh GetCharacterTorsoMesh()
        {
            if (_torsoMesh != null) return _torsoMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            // Pivot at waist/hips (0, 0, 0). Extends up to Y = 3.1.
            // Forward is +X (thickness 1.5), Lateral is ±Z (width 2.3).
            Color darkArmor = FixedColor(0.14f, 0.17f, 0.23f);
            Color beltCol = FixedColor(0.10f, 0.12f, 0.16f);
            Color packCol = FixedColor(0.18f, 0.21f, 0.27f);
            Color teamJersey = TeamTint(1.0f);
            Color teamTrim = TeamTint(1.15f);

            // 1. Pelvis / Tactical Belt (Y: 0.0 .. 0.65)
            AddTaperedBox(st, new Vector3(0f, 0.32f, 0f),
                new Vector3(1.25f, 0.65f, 1.85f),
                new Vector3(1.30f, 0.65f, 1.95f), beltCol);

            // 2. Lower Torso Jersey (Y: 0.65 .. 1.65) - Team colored
            AddTaperedBox(st, new Vector3(0f, 1.15f, 0f),
                new Vector3(1.28f, 1.0f, 1.90f),
                new Vector3(1.50f, 1.0f, 2.35f), teamJersey);

            // 3. Upper Chest Tactical Vest (Y: 1.65 .. 2.85) - Dark armor with team collar
            AddTaperedBox(st, new Vector3(0f, 2.25f, 0f),
                new Vector3(1.52f, 1.2f, 2.35f),
                new Vector3(1.42f, 1.2f, 2.55f), darkArmor);

            // Front Chest Team Emblem Plate (+X face)
            AddBox(st, new Vector3(0.76f, 2.28f, 0f), new Vector3(0.14f, 0.72f, 1.35f), teamTrim);

            // Left & Right Shoulder Pauldrons (Z = -1.45, +1.45)
            AddBox(st, new Vector3(0f, 2.65f, -1.42f), new Vector3(1.25f, 0.52f, 0.55f), teamTrim);
            AddBox(st, new Vector3(0f, 2.65f, 1.42f), new Vector3(1.25f, 0.52f, 0.55f), teamTrim);

            // Back Tactical Backpack (-X face)
            AddBox(st, new Vector3(-0.95f, 2.05f, 0f), new Vector3(0.58f, 1.45f, 1.45f), packCol);
            AddBox(st, new Vector3(-1.26f, 2.25f, 0f), new Vector3(0.12f, 0.55f, 0.95f), teamJersey);

            st.GenerateNormals();
            _torsoMesh = st.Commit();
            return _torsoMesh;
        }

        public static ArrayMesh GetCharacterHeadMesh(Role role, bool isIgl)
        {
            string key = $"{role}_{isIgl}";
            if (HeadMeshCache.TryGetValue(key, out ArrayMesh? cached))
            {
                return cached;
            }

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color skin = FixedColor(0.87f, 0.71f, 0.57f);
            Color darkHelmet = FixedColor(0.15f, 0.18f, 0.24f);
            Color cyanVisor = FixedColor(0.0f, 0.90f, 1.0f);
            Color goldVisor = FixedColor(1.0f, 0.78f, 0.14f);
            Color emeraldVisor = FixedColor(0.18f, 0.92f, 0.50f);
            Color teamAccent = TeamTint(1.1f);

            // Neck & Head core (pivot at neck base Y = 0)
            AddBox(st, new Vector3(0f, 0.22f, 0f), new Vector3(0.75f, 0.45f, 0.75f), skin);
            AddTaperedBox(st, new Vector3(0f, 0.95f, 0f),
                new Vector3(1.28f, 1.25f, 1.28f),
                new Vector3(1.36f, 1.25f, 1.36f), skin);

            // Role-distinctive Helmet & Visor (+X is front face)
            switch (role)
            {
                case Role.Rusher:
                    // Aggressive assault helmet + glowing gold/team visor + top fin
                    AddTaperedBox(st, new Vector3(-0.04f, 1.35f, 0f),
                        new Vector3(1.50f, 0.85f, 1.52f),
                        new Vector3(1.32f, 0.85f, 1.32f), darkHelmet);
                    AddBox(st, new Vector3(0.68f, 1.05f, 0f), new Vector3(0.22f, 0.36f, 1.28f), goldVisor);
                    AddBox(st, new Vector3(0.0f, 1.82f, 0f), new Vector3(1.35f, 0.24f, 0.36f), teamAccent);
                    break;

                case Role.SecondRusher:
                    // Pro gaming headset + sleek cyber visor + backwards cap
                    AddBox(st, new Vector3(-0.12f, 1.45f, 0f), new Vector3(1.48f, 0.58f, 1.44f), teamAccent);
                    AddBox(st, new Vector3(-0.85f, 1.28f, 0f), new Vector3(0.42f, 0.18f, 0.95f), darkHelmet); // Backward brim
                    AddBox(st, new Vector3(0.68f, 1.02f, 0f), new Vector3(0.18f, 0.30f, 1.22f), cyanVisor);
                    // Headset earcups
                    AddBox(st, new Vector3(0f, 0.98f, -0.76f), new Vector3(0.55f, 0.58f, 0.24f), darkHelmet);
                    AddBox(st, new Vector3(0f, 0.98f, 0.76f), new Vector3(0.55f, 0.58f, 0.24f), darkHelmet);
                    break;

                case Role.Sniper:
                    // Recon cowl hood + dual cyan sniper optics
                    AddTaperedBox(st, new Vector3(-0.06f, 1.22f, 0f),
                        new Vector3(1.56f, 1.15f, 1.58f),
                        new Vector3(1.18f, 1.15f, 1.20f), darkHelmet);
                    AddBox(st, new Vector3(0.72f, 1.05f, -0.32f), new Vector3(0.28f, 0.32f, 0.42f), cyanVisor);
                    AddBox(st, new Vector3(0.72f, 1.05f, 0.32f), new Vector3(0.28f, 0.32f, 0.42f), cyanVisor);
                    AddBox(st, new Vector3(0f, 1.72f, 0f), new Vector3(1.15f, 0.16f, 1.20f), teamAccent);
                    break;

                case Role.Support:
                    // Tactical comms helmet + emerald medical visor + right side radio antenna
                    AddTaperedBox(st, new Vector3(0f, 1.36f, 0f),
                        new Vector3(1.48f, 0.82f, 1.50f),
                        new Vector3(1.30f, 0.82f, 1.32f), teamAccent);
                    AddBox(st, new Vector3(0.68f, 1.02f, 0f), new Vector3(0.20f, 0.32f, 1.24f), emeraldVisor);
                    AddBox(st, new Vector3(-0.25f, 1.85f, 0.72f), new Vector3(0.14f, 1.15f, 0.14f), goldVisor); // Comms antenna
                    break;

                default: // Grenadier
                    // Heavy EOD ballistic helmet + blast shield
                    AddTaperedBox(st, new Vector3(0f, 1.32f, 0f),
                        new Vector3(1.58f, 0.92f, 1.62f),
                        new Vector3(1.42f, 0.92f, 1.46f), darkHelmet);
                    AddBox(st, new Vector3(0.72f, 0.96f, 0f), new Vector3(0.22f, 0.52f, 1.36f), goldVisor);
                    AddBox(st, new Vector3(0f, 1.78f, 0f), new Vector3(1.32f, 0.18f, 0.82f), teamAccent);
                    break;
            }

            // IGL Golden Captain Crown / Crest on top of helmet
            if (isIgl)
            {
                AddTaperedBox(st, new Vector3(0.15f, 2.02f, 0f),
                    new Vector3(0.65f, 0.42f, 0.95f),
                    new Vector3(0.35f, 0.42f, 1.15f), goldVisor);
            }

            st.GenerateNormals();
            ArrayMesh mesh = st.Commit();
            HeadMeshCache[key] = mesh;
            return mesh;
        }

        public static ArrayMesh GetCharacterArmMesh(bool isRight)
        {
            if (isRight && _rightArmMesh != null) return _rightArmMesh;
            if (!isRight && _leftArmMesh != null) return _leftArmMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color teamSleeve = TeamTint(0.95f);
            Color skin = FixedColor(0.86f, 0.70f, 0.56f);
            Color glove = FixedColor(0.13f, 0.16f, 0.22f);

            // Pivot at shoulder joint (0, 0, 0), hanging down to Y = -2.4
            AddTaperedBox(st, new Vector3(0f, -0.55f, 0f),
                new Vector3(0.68f, 1.1f, 0.68f),
                new Vector3(0.76f, 1.1f, 0.76f), teamSleeve);

            AddTaperedBox(st, new Vector3(0.12f, -1.45f, 0f),
                new Vector3(0.58f, 0.95f, 0.58f),
                new Vector3(0.64f, 0.95f, 0.64f), skin);

            // Tactical glove at hand
            AddBox(st, new Vector3(0.22f, -2.05f, 0f), new Vector3(0.62f, 0.48f, 0.58f), glove);

            st.GenerateNormals();
            ArrayMesh mesh = st.Commit();
            if (isRight) _rightArmMesh = mesh;
            else _leftArmMesh = mesh;
            return mesh;
        }

        public static ArrayMesh GetCharacterLegMesh()
        {
            if (_legMesh != null) return _legMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color pants = FixedColor(0.15f, 0.19f, 0.26f);
            Color boot = FixedColor(0.09f, 0.11f, 0.15f);
            Color teamTrim = TeamTint(0.9f);

            // Pivot at hip joint (0, 0, 0), extending down to Y = -2.8
            // Thigh
            AddTaperedBox(st, new Vector3(0f, -0.72f, 0f),
                new Vector3(0.76f, 1.45f, 0.76f),
                new Vector3(0.88f, 1.45f, 0.88f), pants);

            // Knee guard with team accent
            AddBox(st, new Vector3(0.42f, -1.35f, 0f), new Vector3(0.18f, 0.42f, 0.68f), teamTrim);

            // Shin & Combat Boot (pointing +X forward)
            AddTaperedBox(st, new Vector3(0f, -2.05f, 0f),
                new Vector3(0.72f, 1.25f, 0.72f),
                new Vector3(0.76f, 1.25f, 0.76f), boot);
            AddBox(st, new Vector3(0.22f, -2.55f, 0f), new Vector3(1.12f, 0.42f, 0.76f), boot);

            st.GenerateNormals();
            _legMesh = st.Commit();
            return _legMesh;
        }

        public static ArrayMesh GetWeaponMesh(WeaponCategory category, int tier)
        {
            string key = $"{category}_{tier}";
            if (WeaponMeshCache.TryGetValue(key, out ArrayMesh? cached))
            {
                return cached;
            }

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color gunmetal = new(0.18f, 0.21f, 0.27f);
            Color darkGrip = new(0.10f, 0.12f, 0.16f);
            Color tierTrim = tier switch
            {
                3 => new Color(1.0f, 0.76f, 0.14f), // Tier 3 Gold skin
                2 => new Color(0.0f, 0.85f, 1.0f),  // Tier 2 Electric Cyan skin
                _ => new Color(0.55f, 0.62f, 0.72f) // Tier 1 Steel
            };

            // Weapon oriented along +X (forward), held at hand origin (0,0,0)
            switch (category)
            {
                case WeaponCategory.Sniper:
                    // Long sniper rifle (AWM / M82B / Kar98k)
                    AddBox(st, new Vector3(1.35f, 0.15f, 0f), new Vector3(2.55f, 0.36f, 0.30f), gunmetal);
                    AddBox(st, new Vector3(3.15f, 0.18f, 0f), new Vector3(1.85f, 0.18f, 0.18f), tierTrim); // Fluted long barrel
                    AddBox(st, new Vector3(4.10f, 0.18f, 0f), new Vector3(0.35f, 0.26f, 0.28f), darkGrip); // Muzzle brake
                    AddBox(st, new Vector3(1.30f, 0.52f, 0f), new Vector3(1.15f, 0.28f, 0.28f), tierTrim); // High-power scope
                    AddBox(st, new Vector3(-0.25f, 0.02f, 0f), new Vector3(0.95f, 0.42f, 0.26f), darkGrip); // Stock
                    break;

                case WeaponCategory.DMR:
                    // Marksman rifle (Woodpecker / AC80 / SKS)
                    AddBox(st, new Vector3(1.15f, 0.15f, 0f), new Vector3(2.20f, 0.38f, 0.30f), gunmetal);
                    AddBox(st, new Vector3(2.65f, 0.18f, 0f), new Vector3(1.25f, 0.18f, 0.18f), tierTrim);
                    AddBox(st, new Vector3(1.15f, 0.48f, 0f), new Vector3(0.85f, 0.24f, 0.24f), tierTrim);
                    AddBox(st, new Vector3(1.25f, -0.25f, 0f), new Vector3(0.32f, 0.55f, 0.22f), darkGrip);
                    break;

                case WeaponCategory.Shotgun:
                    // Heavy double-barrel / pump shotgun (M1887 / M1014 / MAG7)
                    AddBox(st, new Vector3(1.10f, 0.16f, 0f), new Vector3(2.15f, 0.44f, 0.38f), gunmetal);
                    AddBox(st, new Vector3(2.45f, 0.22f, 0f), new Vector3(0.95f, 0.24f, 0.30f), tierTrim);
                    AddBox(st, new Vector3(1.95f, -0.04f, 0f), new Vector3(0.95f, 0.22f, 0.32f), darkGrip); // Pump tube
                    break;

                case WeaponCategory.SMG:
                    // Compact high-fire-rate SMG (MP40 / MAC10 / MP5)
                    AddBox(st, new Vector3(0.85f, 0.16f, 0f), new Vector3(1.55f, 0.42f, 0.32f), tierTrim);
                    AddBox(st, new Vector3(1.85f, 0.18f, 0f), new Vector3(0.65f, 0.22f, 0.22f), gunmetal);
                    AddBox(st, new Vector3(0.95f, -0.38f, 0f), new Vector3(0.28f, 0.82f, 0.22f), darkGrip); // Extended mag
                    break;

                default: // AR (M4A1 / AK47 / Groza)
                    AddBox(st, new Vector3(1.05f, 0.16f, 0f), new Vector3(2.05f, 0.40f, 0.32f), gunmetal);
                    AddBox(st, new Vector3(1.15f, 0.22f, 0f), new Vector3(1.45f, 0.18f, 0.36f), tierTrim); // Tier skin stripe
                    AddBox(st, new Vector3(2.45f, 0.18f, 0f), new Vector3(0.95f, 0.18f, 0.18f), gunmetal); // Barrel
                    AddBox(st, new Vector3(0.95f, 0.46f, 0f), new Vector3(0.52f, 0.24f, 0.26f), tierTrim); // Holo sight
                    AddBox(st, new Vector3(1.25f, -0.32f, 0f), new Vector3(0.36f, 0.68f, 0.24f), darkGrip); // Curved mag
                    break;
            }

            st.GenerateNormals();
            ArrayMesh mesh = st.Commit();
            WeaponMeshCache[key] = mesh;
            return mesh;
        }

        public static ArrayMesh GetLootCrateMesh()
        {
            if (_lootCrateMesh != null) return _lootCrateMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color crateBase = new(0.16f, 0.20f, 0.28f);
            Color goldTrim = new(1.0f, 0.76f, 0.12f);
            Color cyanBeacon = new(0.0f, 0.88f, 1.0f);

            AddBox(st, new Vector3(0f, 0.9f, 0f), new Vector3(3.2f, 1.8f, 2.2f), crateBase);
            AddBox(st, new Vector3(0f, 1.85f, 0f), new Vector3(3.4f, 0.35f, 2.4f), goldTrim);
            AddBox(st, new Vector3(0f, 2.15f, 0f), new Vector3(1.2f, 0.25f, 0.8f), cyanBeacon);

            st.GenerateNormals();
            _lootCrateMesh = st.Commit();
            return _lootCrateMesh;
        }

        public static ArrayMesh GetCurvedGlooWallMesh()
        {
            if (_glooWallMesh != null) return _glooWallMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            // Free Fire signature curved C-arc Gloo Wall shield:
            // Faces outward along +X, spanning Z in [-5.2, +5.2], Height Y in [-2.6, +2.8]
            const int segments = 7;
            const float radius = 6.2f;
            const float halfAngle = 0.72f; // ~82 deg arc
            const float thickness = 0.85f;
            const float halfH = 2.7f;

            for (int i = 0; i < segments; i++)
            {
                float u0 = (float)i / segments;
                float u1 = (float)(i + 1) / segments;
                float a0 = Mathf.Lerp(-halfAngle, halfAngle, u0);
                float a1 = Mathf.Lerp(-halfAngle, halfAngle, u1);

                // Taper height slightly at outer wings for organic Gloo silhouette
                float h0 = halfH * (1.0f - 0.18f * MathF.Abs(u0 * 2f - 1f));
                float h1 = halfH * (1.0f - 0.18f * MathF.Abs(u1 * 2f - 1f));

                Vector3 f0B = new(MathF.Cos(a0) * radius - radius * 0.82f, -halfH, MathF.Sin(a0) * radius);
                Vector3 f1B = new(MathF.Cos(a1) * radius - radius * 0.82f, -halfH, MathF.Sin(a1) * radius);
                Vector3 f0T = new(f0B.X, h0, f0B.Z);
                Vector3 f1T = new(f1B.X, h1, f1B.Z);

                Vector3 b0B = f0B - new Vector3(thickness, 0f, 0f);
                Vector3 b1B = f1B - new Vector3(thickness, 0f, 0f);
                Vector3 b0T = f0T - new Vector3(thickness, 0f, 0f);
                Vector3 b1T = f1T - new Vector3(thickness, 0f, 0f);

                Color c = new(0.15f, 0.90f, 1.0f, 1.0f);
                AddQuadWithUv(st, f0B, f1B, f1T, f0T, new Vector2(u0, 0f), new Vector2(u1, 1f), c);
                AddQuadWithUv(st, b1B, b0B, b0T, b1T, new Vector2(u0, 0f), new Vector2(u1, 1f), c);
                AddQuadWithUv(st, b0T, b1T, f1T, f0T, new Vector2(u0, 0.9f), new Vector2(u1, 1f), c.Lightened(0.25f));
            }

            st.GenerateNormals();
            _glooWallMesh = st.Commit();
            return _glooWallMesh;
        }

        public static ArrayMesh GetTacticalVehicleMesh()
        {
            if (_vehicleMesh != null) return _vehicleMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color bodyPaint = new(0.92f, 0.60f, 0.12f);   // Tactical Amber/Gold SUV
            Color darkChassis = new(0.14f, 0.16f, 0.20f);
            Color glassWindshield = new(0.12f, 0.68f, 0.88f);
            Color tireBlack = new(0.08f, 0.09f, 0.11f);
            Color headlight = new(1.0f, 0.95f, 0.72f);

            // Oriented along +X (length ~7.6m, width ~3.8m, height ~3.4m)
            // Lower Chassis
            AddBox(st, new Vector3(0f, 0.45f, 0f), new Vector3(7.4f, 0.95f, 3.6f), darkChassis);
            // Main Body Lower
            AddTaperedBox(st, new Vector3(0.1f, 1.25f, 0f),
                new Vector3(7.5f, 1.15f, 3.8f),
                new Vector3(7.1f, 1.15f, 3.6f), bodyPaint);
            // Cabin / Roof
            AddTaperedBox(st, new Vector3(-0.35f, 2.35f, 0f),
                new Vector3(4.2f, 1.15f, 3.35f),
                new Vector3(3.2f, 1.15f, 2.95f), bodyPaint.Darkened(0.12f));
            // Sloped Windshield (+X front of cabin)
            AddBox(st, new Vector3(1.48f, 2.32f, 0f), new Vector3(0.55f, 0.85f, 2.85f), glassWindshield);
            // Side Windows
            AddBox(st, new Vector3(-0.3f, 2.35f, 0f), new Vector3(2.8f, 0.72f, 3.28f), glassWindshield);
            // Front Bullbar & Headlights
            AddBox(st, new Vector3(3.82f, 1.05f, 0f), new Vector3(0.35f, 0.75f, 3.4f), darkChassis);
            AddBox(st, new Vector3(3.85f, 1.32f, -1.25f), new Vector3(0.22f, 0.35f, 0.65f), headlight);
            AddBox(st, new Vector3(3.85f, 1.32f, 1.25f), new Vector3(0.22f, 0.35f, 0.65f), headlight);

            // 4 Wheels
            Vector3 wheelSize = new(1.65f, 1.65f, 0.75f);
            AddBox(st, new Vector3(2.25f, 0.05f, -1.85f), wheelSize, tireBlack);
            AddBox(st, new Vector3(2.25f, 0.05f, 1.85f), wheelSize, tireBlack);
            AddBox(st, new Vector3(-2.25f, 0.05f, -1.85f), wheelSize, tireBlack);
            AddBox(st, new Vector3(-2.25f, 0.05f, 1.85f), wheelSize, tireBlack);

            st.GenerateNormals();
            _vehicleMesh = st.Commit();
            return _vehicleMesh;
        }

        public static ArrayMesh GetGrenadeProjectileMesh()
        {
            if (_grenadeMesh != null) return _grenadeMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color body = new(1.0f, 0.45f, 0.12f);
            Color core = new(1.0f, 0.88f, 0.25f);
            AddBox(st, Vector3.Zero, new Vector3(1.1f, 1.1f, 1.1f), body);
            AddBox(st, Vector3.Zero, new Vector3(0.75f, 1.45f, 0.75f), core);

            st.GenerateNormals();
            _grenadeMesh = st.Commit();
            return _grenadeMesh;
        }

        /// <summary>
        /// Builds a two-part Low-Poly hollow tactical compound (Cutaway Walkable Body + Smart-Fadeable Roof)
        /// aligned 1:1 with the 4x3 Sim cell topology (North/South Doorways, 4 Window Bays, 4 Corner Pillars,
        /// Dark Slate Tactical Floor, and Interior Cover Props).
        /// </summary>
        public static (ArrayMesh bodyMesh, ArrayMesh roofMesh) CreateBuildingMeshes(
            float width,
            float height,
            float depth,
            int styleIndex)
        {
            int style = Math.Abs(styleIndex) % 6;
            float bodyH = Math.Max(5.2f, height * 0.76f);
            float roofH = Math.Max(1.8f, height * 0.24f);

            var stBody = new SurfaceTool();
            stBody.Begin(Mesh.PrimitiveType.Triangles);

            var stRoof = new SurfaceTool();
            stRoof.Begin(Mesh.PrimitiveType.Triangles);

            // Low-saturation architectural palette (§6.7)
            Color wallMain = style switch
            {
                0 => new Color(0.36f, 0.38f, 0.42f), // Tropical Concrete Villa
                1 => new Color(0.30f, 0.34f, 0.39f), // Industrial Hangar / Warehouse
                2 => new Color(0.34f, 0.33f, 0.30f), // Watchtower / Bunker Stone
                3 => new Color(0.28f, 0.34f, 0.42f), // Tech Compound (Mars Electric / Obs)
                4 => new Color(0.32f, 0.35f, 0.34f), // Reinforced Stronghold
                _ => new Color(0.38f, 0.35f, 0.31f)  // Coastal Island Stilt House
            };
            Color wallFoundation = wallMain.Darkened(0.28f);
            Color interiorFloor = new(0.11f, 0.14f, 0.20f);
            Color interiorGrid = new(0.16f, 0.22f, 0.31f);
            Color doorThreshold = new(0.95f, 0.68f, 0.14f);
            Color windowSillCol = new(0.18f, 0.62f, 0.78f);
            Color trimBand = wallMain.Lightened(0.14f);

            float hx = width * 0.5f;
            float hz = depth * 0.5f;
            float cellW = width / 4f;
            float cellD = depth / 3f;
            float wallT = 0.88f;

            // 1. Exterior Foundation Plinth & High-Contrast Dark Slate Interior Floor
            AddBox(stBody, new Vector3(0f, 0.16f, 0f), new Vector3(width + 0.5f, 0.32f, depth + 0.5f), wallFoundation);
            AddBox(stBody, new Vector3(0f, 0.35f, 0f), new Vector3(width - wallT * 1.4f, 0.14f, depth - wallT * 1.4f), interiorFloor);

            // Tactical interior floor zone markings (center room pad + grid strips)
            AddBox(stBody, new Vector3(0f, 0.43f, 0f), new Vector3(cellW * 1.9f, 0.05f, cellD * 1.05f), interiorGrid);
            AddBox(stBody, new Vector3(0f, 0.44f, 0f), new Vector3(width - 2.2f, 0.04f, 0.28f), interiorGrid.Lightened(0.12f));
            AddBox(stBody, new Vector3(0f, 0.44f, 0f), new Vector3(0.28f, 0.04f, depth - 2.2f), interiorGrid.Lightened(0.12f));

            // 2. Glowing Amber Doorway Thresholds aligned with Sim Doorways:
            // North Doorway: (dx=2, dy=0) -> X = +0.5 * cellW, Z = -hz
            // South Doorway: (dx=1, dy=2) -> X = -0.5 * cellW, Z = +hz
            float northDoorX = +0.5f * cellW;
            float southDoorX = -0.5f * cellW;
            float doorSpanW = cellW * 0.76f;
            AddBox(stBody, new Vector3(northDoorX, 0.42f, -hz + 0.45f), new Vector3(doorSpanW, 0.16f, 1.35f), doorThreshold);
            AddBox(stBody, new Vector3(southDoorX, 0.42f, hz - 0.45f), new Vector3(doorSpanW, 0.16f, 1.35f), doorThreshold);

            // 3. Four Solid Corner Pillars & Corner Walls (dx=0/3, dy=0/2 - BlocksLos)
            float pillarSize = 1.65f;
            float cornerWallH = bodyH * 0.74f;
            AddBox(stBody, new Vector3(-hx + pillarSize * 0.4f, bodyH * 0.5f, -hz + pillarSize * 0.4f), new Vector3(pillarSize, bodyH, pillarSize), wallFoundation);
            AddBox(stBody, new Vector3(hx - pillarSize * 0.4f, bodyH * 0.5f, -hz + pillarSize * 0.4f), new Vector3(pillarSize, bodyH, pillarSize), wallFoundation);
            AddBox(stBody, new Vector3(-hx + pillarSize * 0.4f, bodyH * 0.5f, hz - pillarSize * 0.4f), new Vector3(pillarSize, bodyH, pillarSize), wallFoundation);
            AddBox(stBody, new Vector3(hx - pillarSize * 0.4f, bodyH * 0.5f, hz - pillarSize * 0.4f), new Vector3(pillarSize, bodyH, pillarSize), wallFoundation);

            // Solid corner wing walls on North & South facades (dx=0 and dx=3)
            AddBox(stBody, new Vector3(-1.5f * cellW, cornerWallH * 0.5f, -hz + wallT * 0.5f), new Vector3(cellW, cornerWallH, wallT), wallMain);
            AddBox(stBody, new Vector3(1.5f * cellW, cornerWallH * 0.5f, -hz + wallT * 0.5f), new Vector3(cellW, cornerWallH, wallT), wallMain);
            AddBox(stBody, new Vector3(-1.5f * cellW, cornerWallH * 0.5f, hz - wallT * 0.5f), new Vector3(cellW, cornerWallH, wallT), wallMain);
            AddBox(stBody, new Vector3(1.5f * cellW, cornerWallH * 0.5f, hz - wallT * 0.5f), new Vector3(cellW, cornerWallH, wallT), wallMain);

            // Solid corner wing walls on West & East facades (dy=0 and dy=2)
            AddBox(stBody, new Vector3(-hx + wallT * 0.5f, cornerWallH * 0.5f, -cellD), new Vector3(wallT, cornerWallH, cellD), wallMain);
            AddBox(stBody, new Vector3(-hx + wallT * 0.5f, cornerWallH * 0.5f, cellD), new Vector3(wallT, cornerWallH, cellD), wallMain);
            AddBox(stBody, new Vector3(hx - wallT * 0.5f, cornerWallH * 0.5f, -cellD), new Vector3(wallT, cornerWallH, cellD), wallMain);
            AddBox(stBody, new Vector3(hx - wallT * 0.5f, cornerWallH * 0.5f, cellD), new Vector3(wallT, cornerWallH, cellD), wallMain);

            // 4. Four Tactical Firing Window Bays with Waist-High Cover Parapet + Cyan Sill:
            // North Window: (dx=1, dy=0) -> X = -0.5 * cellW, Z = -hz
            // South Window: (dx=2, dy=2) -> X = +0.5 * cellW, Z = +hz
            // West Window:  (dx=0, dy=1) -> X = -hx, Z = 0
            // East Window:  (dx=3, dy=1) -> X = +hx, Z = 0
            float sillH = 1.48f;
            float northWinX = -0.5f * cellW;
            float southWinX = +0.5f * cellW;

            // North & South window parapets + cyan firing sills
            AddBox(stBody, new Vector3(northWinX, sillH * 0.5f, -hz + wallT * 0.5f), new Vector3(cellW, sillH, wallT), wallMain);
            AddBox(stBody, new Vector3(northWinX, sillH + 0.08f, -hz + wallT * 0.5f), new Vector3(cellW * 0.88f, 0.18f, wallT + 0.24f), windowSillCol);

            AddBox(stBody, new Vector3(southWinX, sillH * 0.5f, hz - wallT * 0.5f), new Vector3(cellW, sillH, wallT), wallMain);
            AddBox(stBody, new Vector3(southWinX, sillH + 0.08f, hz - wallT * 0.5f), new Vector3(cellW * 0.88f, 0.18f, wallT + 0.24f), windowSillCol);

            // West & East window parapets + cyan firing sills
            AddBox(stBody, new Vector3(-hx + wallT * 0.5f, sillH * 0.5f, 0f), new Vector3(wallT, sillH, cellD), wallMain);
            AddBox(stBody, new Vector3(-hx + wallT * 0.5f, sillH + 0.08f, 0f), new Vector3(wallT + 0.24f, 0.18f, cellD * 0.86f), windowSillCol);

            AddBox(stBody, new Vector3(hx - wallT * 0.5f, sillH * 0.5f, 0f), new Vector3(wallT, sillH, cellD), wallMain);
            AddBox(stBody, new Vector3(hx - wallT * 0.5f, sillH + 0.08f, 0f), new Vector3(wallT + 0.24f, 0.18f, cellD * 0.86f), windowSillCol);

            // Doorway side jamb pillars & Upper Ring Lintel Beam (frames doorways and windows cleanly)
            float jambH = bodyH * 0.82f;
            AddBox(stBody, new Vector3(0f, jambH * 0.5f, -hz + wallT * 0.5f), new Vector3(0.95f, jambH, wallT + 0.18f), wallFoundation);
            AddBox(stBody, new Vector3(0f, jambH * 0.5f, hz - wallT * 0.5f), new Vector3(0.95f, jambH, wallT + 0.18f), wallFoundation);

            // Upper structural ring beam at Y = bodyH - 0.38f
            float ringY = bodyH - 0.38f;
            AddBox(stBody, new Vector3(0f, ringY, -hz + wallT * 0.5f), new Vector3(width + 0.3f, 0.72f, wallT + 0.15f), trimBand);
            AddBox(stBody, new Vector3(0f, ringY, hz - wallT * 0.5f), new Vector3(width + 0.3f, 0.72f, wallT + 0.15f), trimBand);
            AddBox(stBody, new Vector3(-hx + wallT * 0.5f, ringY, 0f), new Vector3(wallT + 0.15f, 0.72f, depth + 0.3f), trimBand);
            AddBox(stBody, new Vector3(hx - wallT * 0.5f, ringY, 0f), new Vector3(wallT + 0.15f, 0.72f, depth + 0.3f), trimBand);

            // 5. Low-Profile Interior Tactical Furnishings (console table + supply crates inside room)
            Color crateCol = new(0.22f, 0.28f, 0.36f);
            Color crateAccent = new(0.92f, 0.66f, 0.14f);
            AddBox(stBody, new Vector3(-cellW * 1.15f, 0.92f, -cellD * 0.65f), new Vector3(2.4f, 1.05f, 1.6f), crateCol);
            AddBox(stBody, new Vector3(-cellW * 1.15f, 1.48f, -cellD * 0.65f), new Vector3(2.5f, 0.14f, 1.7f), crateAccent);
            AddBox(stBody, new Vector3(cellW * 1.15f, 0.85f, cellD * 0.65f), new Vector3(2.2f, 0.95f, 1.5f), crateCol.Darkened(0.08f));

            // 6. Roof Geometry positioned on top of bodyH (fades out when units occupy/approach building)
            Color roofCol = style switch
            {
                0 => new Color(0.48f, 0.34f, 0.28f), // Terracotta island roof
                1 => new Color(0.36f, 0.42f, 0.48f), // Industrial steel roof
                3 => new Color(0.28f, 0.44f, 0.52f), // Tech slate-cyan roof
                _ => new Color(0.40f, 0.43f, 0.48f)  // Tactical concrete roof
            };

            float overW = width + 1.4f;
            float overD = depth + 1.4f;

            if (style == 0 || style == 5)
            {
                // Pitched Hip / Gable Island Roof sitting at Y = bodyH
                AddTaperedBox(stRoof, new Vector3(0f, bodyH + roofH * 0.48f, 0f),
                    new Vector3(overW, roofH, overD),
                    new Vector3(overW * 0.52f, roofH, overD * 0.35f), roofCol);
            }
            else if (style == 1)
            {
                // Industrial Sawtooth / Stepped Hangar Roof + Skylight ridge
                AddTaperedBox(stRoof, new Vector3(0f, bodyH + roofH * 0.35f, 0f),
                    new Vector3(overW, roofH * 0.7f, overD),
                    new Vector3(overW * 0.86f, roofH * 0.7f, overD * 0.86f), roofCol);
                AddBox(stRoof, new Vector3(0f, bodyH + roofH * 0.82f, 0f), new Vector3(overW * 0.65f, roofH * 0.35f, overD * 0.32f), roofCol.Lightened(0.15f));
            }
            else
            {
                // Parapet Tactical Flat Roof + HVAC / Comms Box on top
                AddBox(stRoof, new Vector3(0f, bodyH + roofH * 0.25f, 0f), new Vector3(overW, roofH * 0.5f, overD), roofCol);
                AddBox(stRoof, new Vector3(overW * 0.18f, bodyH + roofH * 0.68f, -overD * 0.15f),
                    new Vector3(overW * 0.28f, roofH * 0.55f, overD * 0.28f), roofCol.Darkened(0.18f));
            }

            stBody.GenerateNormals();
            stRoof.GenerateNormals();
            return (stBody.Commit(), stRoof.Commit());
        }

        public static ArrayMesh GetPalmTreeMesh()
        {
            if (_palmTreeMesh != null) return _palmTreeMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color bark = new(0.32f, 0.25f, 0.18f);
            Color frond = new(0.16f, 0.32f, 0.20f);
            Color frondTip = new(0.20f, 0.36f, 0.22f);

            // Segmented curved trunk (height ~9.5m)
            AddTaperedBox(st, new Vector3(0f, 1.6f, 0f), new Vector3(1.1f, 3.2f, 1.1f), new Vector3(0.85f, 3.2f, 0.85f), bark);
            AddTaperedBox(st, new Vector3(0.25f, 4.6f, 0.1f), new Vector3(0.85f, 3.2f, 0.85f), new Vector3(0.65f, 3.2f, 0.65f), bark.Lightened(0.06f));
            AddTaperedBox(st, new Vector3(0.55f, 7.4f, 0.2f), new Vector3(0.65f, 2.8f, 0.65f), new Vector3(0.50f, 2.8f, 0.50f), bark);

            // 6 Low-Poly Palm Fronds radiating from crown (0.6, 8.8, 0.2)
            Vector3 crown = new(0.6f, 8.8f, 0.2f);
            for (int i = 0; i < 6; i++)
            {
                float ang = i * MathF.Tau / 6f;
                Vector3 dir = new(MathF.Cos(ang), 0f, MathF.Sin(ang));
                Vector3 side = new(-dir.Z * 0.85f, 0f, dir.X * 0.85f);
                Vector3 mid = crown + dir * 2.2f + new Vector3(0f, 0.45f, 0f);
                Vector3 tip = crown + dir * 4.4f + new Vector3(0f, -0.95f, 0f);

                AddTriangle(st, crown - side * 0.4f, crown + side * 0.4f, mid + side, frond);
                AddTriangle(st, crown - side * 0.4f, mid + side, mid - side, frond);
                AddTriangle(st, mid - side, mid + side, tip, frondTip);
            }

            st.GenerateNormals();
            _palmTreeMesh = st.Commit();
            return _palmTreeMesh;
        }

        public static ArrayMesh GetBroadleafTreeMesh()
        {
            if (_broadleafTreeMesh != null) return _broadleafTreeMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color trunk = new(0.28f, 0.22f, 0.16f);
            Color canopyDark = new(0.14f, 0.27f, 0.18f);
            Color canopyMid = new(0.18f, 0.32f, 0.21f);
            Color canopyTop = new(0.22f, 0.36f, 0.23f);

            AddTaperedBox(st, new Vector3(0f, 1.8f, 0f), new Vector3(1.2f, 3.6f, 1.2f), new Vector3(0.8f, 3.6f, 0.8f), trunk);
            // 3 stacked faceted foliage tiers
            AddTaperedBox(st, new Vector3(0f, 4.6f, 0f), new Vector3(4.8f, 2.8f, 4.8f), new Vector3(2.8f, 2.8f, 2.8f), canopyDark);
            AddTaperedBox(st, new Vector3(0.2f, 6.6f, -0.1f), new Vector3(3.8f, 2.5f, 3.8f), new Vector3(1.8f, 2.5f, 1.8f), canopyMid);
            AddTaperedBox(st, new Vector3(-0.1f, 8.3f, 0.1f), new Vector3(2.4f, 1.9f, 2.4f), new Vector3(0.6f, 1.9f, 0.6f), canopyTop);

            st.GenerateNormals();
            _broadleafTreeMesh = st.Commit();
            return _broadleafTreeMesh;
        }

        public static ArrayMesh GetBushClumpMesh()
        {
            if (_bushMesh != null) return _bushMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color c1 = new(0.15f, 0.28f, 0.18f);
            Color c2 = new(0.19f, 0.33f, 0.21f);

            AddTaperedBox(st, new Vector3(0f, 0.9f, 0f), new Vector3(2.8f, 1.8f, 2.6f), new Vector3(1.8f, 1.8f, 1.6f), c1);
            AddTaperedBox(st, new Vector3(1.1f, 0.7f, 0.6f), new Vector3(2.1f, 1.4f, 2.0f), new Vector3(1.2f, 1.4f, 1.2f), c2);
            AddTaperedBox(st, new Vector3(-0.9f, 0.65f, -0.5f), new Vector3(1.9f, 1.3f, 1.9f), new Vector3(1.1f, 1.3f, 1.1f), c2);

            st.GenerateNormals();
            _bushMesh = st.Commit();
            return _bushMesh;
        }

        public static ArrayMesh GetRockClusterMesh()
        {
            if (_rockMesh != null) return _rockMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color r1 = new(0.28f, 0.31f, 0.35f);
            Color r2 = new(0.23f, 0.26f, 0.30f);

            AddTaperedBox(st, new Vector3(0f, 1.2f, 0f), new Vector3(3.4f, 2.4f, 2.9f), new Vector3(1.9f, 2.4f, 1.5f), r1);
            AddTaperedBox(st, new Vector3(1.6f, 0.8f, 0.7f), new Vector3(2.2f, 1.6f, 2.0f), new Vector3(1.1f, 1.6f, 1.0f), r2);
            AddTaperedBox(st, new Vector3(-1.3f, 0.7f, -0.6f), new Vector3(2.0f, 1.4f, 1.8f), new Vector3(1.2f, 1.4f, 0.9f), r2);

            st.GenerateNormals();
            _rockMesh = st.Commit();
            return _rockMesh;
        }

        public static ArrayMesh GetCargoContainerMesh()
        {
            if (_containerMesh != null) return _containerMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color steelBlue = new(0.22f, 0.32f, 0.44f);
            Color sandbag = new(0.38f, 0.35f, 0.27f);

            AddBox(st, new Vector3(0f, 1.6f, 0f), new Vector3(6.2f, 3.2f, 2.8f), steelBlue);
            AddBox(st, new Vector3(0f, 0.65f, 2.1f), new Vector3(3.6f, 1.3f, 1.1f), sandbag);

            st.GenerateNormals();
            _containerMesh = st.Commit();
            return _containerMesh;
        }

        public static ArrayMesh GetTacticalSandbagWallMesh()
        {
            if (_sandbagWallMesh != null) return _sandbagWallMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color bagBase = new(0.42f, 0.38f, 0.28f);
            Color bagTop = new(0.48f, 0.44f, 0.32f);
            Color strap = new(0.24f, 0.22f, 0.18f);

            // Curved/angled 2-course tactical sandbag fortification (faces outward along +X)
            AddTaperedBox(st, new Vector3(0f, 0.48f, 0f), new Vector3(1.65f, 0.95f, 4.6f), new Vector3(1.35f, 0.95f, 4.4f), bagBase);
            AddTaperedBox(st, new Vector3(-0.08f, 1.22f, -1.05f), new Vector3(1.25f, 0.68f, 1.95f), new Vector3(0.98f, 0.68f, 1.75f), bagTop);
            AddTaperedBox(st, new Vector3(-0.08f, 1.22f, 1.05f), new Vector3(1.25f, 0.68f, 1.95f), new Vector3(0.98f, 0.68f, 1.75f), bagTop);
            // Reinforcement stakes
            AddBox(st, new Vector3(0.75f, 0.65f, -1.5f), new Vector3(0.22f, 1.3f, 0.22f), strap);
            AddBox(st, new Vector3(0.75f, 0.65f, 0f), new Vector3(0.22f, 1.3f, 0.22f), strap);
            AddBox(st, new Vector3(0.75f, 0.65f, 1.5f), new Vector3(0.22f, 1.3f, 0.22f), strap);

            st.GenerateNormals();
            _sandbagWallMesh = st.Commit();
            return _sandbagWallMesh;
        }

        public static ArrayMesh GetConcreteBarrierMesh()
        {
            if (_concreteBarrierMesh != null) return _concreteBarrierMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color concrete = new(0.36f, 0.39f, 0.44f);
            Color darkBase = new(0.22f, 0.25f, 0.30f);
            Color hazardGold = new(0.96f, 0.72f, 0.12f);

            // Jersey-style sloped concrete barrier (faces outward along +X, width along Z = 4.8m)
            AddBox(st, new Vector3(0f, 0.22f, 0f), new Vector3(1.75f, 0.44f, 4.8f), darkBase);
            AddTaperedBox(st, new Vector3(0f, 0.98f, 0f), new Vector3(1.45f, 1.15f, 4.7f), new Vector3(0.68f, 1.15f, 4.5f), concrete);
            // High-visibility tactical hazard stripe on top ridge
            AddBox(st, new Vector3(0.18f, 1.48f, -1.35f), new Vector3(0.55f, 0.20f, 1.15f), hazardGold);
            AddBox(st, new Vector3(0.18f, 1.48f, 1.35f), new Vector3(0.55f, 0.20f, 1.15f), hazardGold);

            st.GenerateNormals();
            _concreteBarrierMesh = st.Commit();
            return _concreteBarrierMesh;
        }

        public static ArrayMesh GetTacticalCrateCoverMesh()
        {
            if (_tacticalCrateCoverMesh != null) return _tacticalCrateCoverMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color polymerCase = new(0.20f, 0.27f, 0.36f);
            Color oliveCase = new(0.26f, 0.33f, 0.27f);
            Color cyanTrim = new(0.0f, 0.82f, 0.98f);

            AddBox(st, new Vector3(0f, 0.75f, -0.85f), new Vector3(2.1f, 1.5f, 2.3f), polymerCase);
            AddBox(st, new Vector3(0f, 1.52f, -0.85f), new Vector3(2.2f, 0.14f, 2.4f), cyanTrim);
            AddBox(st, new Vector3(0.25f, 0.58f, 1.15f), new Vector3(1.8f, 1.16f, 1.75f), oliveCase);

            st.GenerateNormals();
            _tacticalCrateCoverMesh = st.Commit();
            return _tacticalCrateCoverMesh;
        }

        /// <summary>
        /// Builds a 120-degree curved tactical directional cover shield arc at the unit's feet,
        /// centered on +X (local forward of cover direction).
        /// </summary>
        public static ArrayMesh GetDirectionalCoverShieldArcMesh()
        {
            if (_coverShieldArcMesh != null) return _coverShieldArcMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            const int segments = 8;
            const float innerR = 2.05f;
            const float outerR = 2.65f;
            const float wallH = 0.85f;
            const float halfArc = 1.0472f; // 60 deg -> 120 deg total arc

            Color baseCol = new(1f, 1f, 1f, 0.92f);
            Color rimCol = new(1f, 1f, 1f, 1.0f);

            for (int i = 0; i < segments; i++)
            {
                float u0 = (float)i / segments;
                float u1 = (float)(i + 1) / segments;
                float a0 = Mathf.Lerp(-halfArc, halfArc, u0);
                float a1 = Mathf.Lerp(-halfArc, halfArc, u1);

                float c0 = MathF.Cos(a0), s0 = MathF.Sin(a0);
                float c1 = MathF.Cos(a1), s1 = MathF.Sin(a1);

                // Taper height at arc tips for sleek broadcast look
                float h0 = wallH * (1f - 0.35f * MathF.Abs(u0 * 2f - 1f));
                float h1 = wallH * (1f - 0.35f * MathF.Abs(u1 * 2f - 1f));

                Vector3 p0In = new(c0 * innerR, 0.08f, s0 * innerR);
                Vector3 p1In = new(c1 * innerR, 0.08f, s1 * innerR);
                Vector3 p0Out = new(c0 * outerR, 0.08f, s0 * outerR);
                Vector3 p1Out = new(c1 * outerR, 0.08f, s1 * outerR);

                Vector3 p0Top = new(c0 * outerR, 0.08f + h0, s0 * outerR);
                Vector3 p1Top = new(c1 * outerR, 0.08f + h1, s1 * outerR);

                // Ground arc ribbon
                AddQuad(st, p0In, p1In, p1Out, p0Out, baseCol);
                // Low holographic energy shield lip (double-sided)
                AddQuad(st, p0Out, p1Out, p1Top, p0Top, rimCol);
                AddQuad(st, p1Out, p0Out, p0Top, p1Top, rimCol);
            }

            st.GenerateNormals();
            _coverShieldArcMesh = st.Commit();
            return _coverShieldArcMesh;
        }

        /// <summary>
        /// Builds the Free Fire 3D Tactical Transport Aircraft mesh (+X forward, +Y up, ±Z wingspan ~44m).
        /// Styled in dark tactical navy, Championship Gold livery stripes, and cyan cockpit/engine accents.
        /// </summary>
        public static ArrayMesh GetTransportAircraftMesh()
        {
            if (_transportAircraftMesh != null) return _transportAircraftMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color hullCol = new(0.14f, 0.18f, 0.25f);
            Color bellyCol = new(0.09f, 0.12f, 0.17f);
            Color goldStripe = new(1.0f, 0.72f, 0.0f);
            Color cyanGlow = new(0.0f, 0.85f, 1.0f);
            Color wingCol = new(0.18f, 0.22f, 0.30f);
            Color rampCol = new(0.22f, 0.26f, 0.34f);

            // 1. Main Cargo Fuselage (X: -14 .. +12, Y: -2.2 .. +2.6, Z: ±3.2)
            AddTaperedBox(st, new Vector3(-1.0f, 0.2f, 0f),
                new Vector3(26.0f, 4.8f, 5.8f),
                new Vector3(25.0f, 4.8f, 6.4f), hullCol);

            // Belly armor plate
            AddBox(st, new Vector3(-1.0f, -2.35f, 0f), new Vector3(24.0f, 0.55f, 5.2f), bellyCol);

            // Championship Gold fuselage livery stripe
            AddBox(st, new Vector3(-0.5f, 0.55f, 0f), new Vector3(25.4f, 0.65f, 6.55f), goldStripe);

            // 2. Aerodynamic Nose Cone & Cockpit Visor (X: +12 .. +19.5)
            AddTaperedBox(st, new Vector3(15.2f, -0.35f, 0f),
                new Vector3(6.8f, 3.6f, 5.4f),
                new Vector3(5.8f, 2.4f, 3.2f), hullCol.Lightened(0.05f));
            AddTaperedBox(st, new Vector3(14.4f, 1.45f, 0f),
                new Vector3(4.6f, 1.35f, 4.4f),
                new Vector3(3.2f, 1.35f, 2.8f), cyanGlow);

            // 3. Rear Cargo Drop Ramp & Upswept Tail Cone (X: -14 .. -21.5)
            AddTaperedBox(st, new Vector3(-17.4f, 0.95f, 0f),
                new Vector3(7.2f, 3.5f, 5.2f),
                new Vector3(6.8f, 2.2f, 2.6f), hullCol);
            // Open jump bay glowing interior indicator
            AddBox(st, new Vector3(-15.5f, -1.65f, 0f), new Vector3(4.8f, 0.38f, 4.2f), rampCol);
            AddBox(st, new Vector3(-14.2f, -1.35f, 0f), new Vector3(1.8f, 0.45f, 3.6f), cyanGlow.Darkened(0.15f));

            // 4. High Swept Main Wings (Z: -22.5 .. +22.5)
            AddTaperedBox(st, new Vector3(0.5f, 2.35f, 0f),
                new Vector3(7.4f, 0.85f, 44.0f),
                new Vector3(5.6f, 0.85f, 45.2f), wingCol);
            // Gold wingtip chevrons & navigation beacons
            AddBox(st, new Vector3(0.2f, 2.45f, -21.2f), new Vector3(5.8f, 0.92f, 2.4f), goldStripe);
            AddBox(st, new Vector3(0.2f, 2.45f, 21.2f), new Vector3(5.8f, 0.92f, 2.4f), goldStripe);
            AddBox(st, new Vector3(0.2f, 2.55f, -22.6f), new Vector3(2.2f, 0.65f, 0.55f), new Color(1.0f, 0.22f, 0.26f));
            AddBox(st, new Vector3(0.2f, 2.55f, 22.6f), new Vector3(2.2f, 0.65f, 0.55f), new Color(0.18f, 0.95f, 0.52f));

            // 5. Twin Turboprop Engine Nacelles (Z = ±10.5, X = +4.2)
            foreach (float side in new[] { -1f, 1f })
            {
                float z = side * 10.5f;
                AddTaperedBox(st, new Vector3(3.8f, 1.35f, z),
                    new Vector3(7.2f, 2.1f, 2.4f),
                    new Vector3(6.8f, 2.1f, 2.2f), bellyCol.Lightened(0.08f));
                AddBox(st, new Vector3(7.5f, 1.35f, z), new Vector3(0.8f, 1.5f, 1.6f), goldStripe);
            }

            // 6. Tall T-Tail Vertical Fin & Horizontal Stabilizer
            AddTaperedBox(st, new Vector3(-18.8f, 5.2f, 0f),
                new Vector3(5.6f, 6.8f, 0.95f),
                new Vector3(3.4f, 6.8f, 0.70f), wingCol);
            AddBox(st, new Vector3(-18.9f, 5.6f, 0f), new Vector3(3.8f, 1.6f, 1.05f), goldStripe);
            AddBox(st, new Vector3(-19.6f, 8.5f, 0f), new Vector3(3.6f, 0.65f, 14.5f), wingCol.Lightened(0.06f));

            st.GenerateNormals();
            _transportAircraftMesh = st.Commit();
            return _transportAircraftMesh;
        }

        /// <summary>
        /// 4-blade turboprop rotor centered at origin, spinning around +X axis.
        /// </summary>
        public static ArrayMesh GetAircraftPropellerMesh()
        {
            if (_aircraftPropellerMesh != null) return _aircraftPropellerMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color hubCol = new(1.0f, 0.72f, 0.0f);
            Color bladeCol = new(0.82f, 0.88f, 0.95f);
            Color tipCol = new(0.0f, 0.85f, 1.0f);

            AddBox(st, Vector3.Zero, new Vector3(0.9f, 0.75f, 0.75f), hubCol);
            // Vertical blades (±Y)
            AddBox(st, new Vector3(0f, 0f, 0f), new Vector3(0.18f, 5.2f, 0.48f), bladeCol);
            AddBox(st, new Vector3(0f, 2.45f, 0f), new Vector3(0.20f, 0.45f, 0.50f), tipCol);
            AddBox(st, new Vector3(0f, -2.45f, 0f), new Vector3(0.20f, 0.45f, 0.50f), tipCol);
            // Horizontal blades (±Z)
            AddBox(st, new Vector3(0f, 0f, 0f), new Vector3(0.18f, 0.48f, 5.2f), bladeCol);
            AddBox(st, new Vector3(0f, 0f, 2.45f), new Vector3(0.20f, 0.50f, 0.45f), tipCol);
            AddBox(st, new Vector3(0f, 0f, -2.45f), new Vector3(0.20f, 0.50f, 0.45f), tipCol);

            st.GenerateNormals();
            _aircraftPropellerMesh = st.Commit();
            return _aircraftPropellerMesh;
        }

        /// <summary>
        /// Free Fire Sky Surfboard ridden under the player's feet during Freefall dive (+X forward).
        /// Uses TeamTint so the surfboard trim matches the squad's team color via unit_team_color.gdshader.
        /// </summary>
        public static ArrayMesh GetSkySurfboardMesh()
        {
            if (_skySurfboardMesh != null) return _skySurfboardMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color deckDark = FixedColor(0.12f, 0.15f, 0.22f);
            Color teamTrim = TeamTint(1.10f);
            Color thrusterGlow = FixedColor(0.0f, 0.90f, 1.0f);

            // Center deck (X: -1.8 .. +1.8, Y: 0.15, Z: ±0.85)
            AddTaperedBox(st, new Vector3(0f, 0.18f, 0f),
                new Vector3(3.6f, 0.24f, 1.65f),
                new Vector3(3.8f, 0.24f, 1.75f), deckDark);

            // Pointed nose cone (+X: +1.8 .. +2.9)
            AddTaperedBox(st, new Vector3(2.30f, 0.22f, 0f),
                new Vector3(1.15f, 0.20f, 1.45f),
                new Vector3(1.15f, 0.16f, 0.55f), teamTrim);

            // Team-colored lateral rails
            AddBox(st, new Vector3(0.1f, 0.22f, -0.86f), new Vector3(3.5f, 0.26f, 0.22f), teamTrim);
            AddBox(st, new Vector3(0.1f, 0.22f, 0.86f), new Vector3(3.5f, 0.26f, 0.22f), teamTrim);

            // Rear twin jet thrusters (-X: -2.05)
            AddBox(st, new Vector3(-2.05f, 0.20f, -0.46f), new Vector3(0.65f, 0.28f, 0.42f), thrusterGlow);
            AddBox(st, new Vector3(-2.05f, 0.20f, 0.46f), new Vector3(0.65f, 0.28f, 0.42f), thrusterGlow);

            st.GenerateNormals();
            _skySurfboardMesh = st.Commit();
            return _skySurfboardMesh;
        }

        /// <summary>
        /// Curved 7-cell ram-air rectangular parachute wing canopy + 4 suspension rigging lines.
        /// Origin is at the character's shoulder harness (Y = 0), with the canopy arched at Y = +4.4 .. +5.3.
        /// Uses TeamTint so each squad's parachute canopy is vividly colored in their team color.
        /// </summary>
        public static ArrayMesh GetParachuteCanopyMesh()
        {
            if (_parachuteCanopyMesh != null) return _parachuteCanopyMesh;

            var st = new SurfaceTool();
            st.Begin(Mesh.PrimitiveType.Triangles);

            Color teamMain = TeamTint(1.08f);
            Color teamAccent = TeamTint(0.78f);
            Color darkRib = FixedColor(0.10f, 0.13f, 0.18f);
            Color cordCol = FixedColor(0.70f, 0.76f, 0.84f);

            // 7 arched ram-air cells across span Z ∈ [-4.55, +4.55], chord X ∈ [-2.15, +2.15]
            const int cells = 7;
            float spanZ = 9.1f;
            float cellW = spanZ / cells;
            float halfChord = 2.15f;
            float thickY = 0.52f;

            for (int i = 0; i < cells; i++)
            {
                float z0 = -spanZ * 0.5f + i * cellW;
                float z1 = z0 + cellW;
                float u0 = (float)i / cells - 0.5f;
                float u1 = (float)(i + 1) / cells - 0.5f;

                // Parabolic arch: highest at center (Y = 5.25), curving down at wingtips (Y = 3.95)
                float y0 = 5.25f - 5.2f * (u0 * u0);
                float y1 = 5.25f - 5.2f * (u1 * u1);

                Color cellCol = (i % 2 == 0) ? teamMain : teamAccent;
                if (i == 3) cellCol = FixedColor(1.0f, 0.78f, 0.12f); // Center championship gold cell

                Vector3 b00 = new(-halfChord, y0, z0);
                Vector3 b10 = new(halfChord, y0 + 0.18f, z0);
                Vector3 b11 = new(halfChord, y1 + 0.18f, z1);
                Vector3 b01 = new(-halfChord, y1, z1);

                Vector3 t00 = b00 + new Vector3(0f, thickY, 0f);
                Vector3 t10 = b10 + new Vector3(0f, thickY, 0f);
                Vector3 t11 = b11 + new Vector3(0f, thickY, 0f);
                Vector3 t01 = b01 + new Vector3(0f, thickY, 0f);

                // Top & Bottom airfoil surfaces
                AddQuad(st, t00, t10, t11, t01, cellCol);
                AddQuad(st, b01, b11, b10, b00, cellCol.Darkened(0.18f));
                // Leading edge ram-air intake (+X) & trailing edge (-X)
                AddQuad(st, b10, b11, t11, t10, darkRib);
                AddQuad(st, b01, b00, t00, t01, cellCol.Darkened(0.10f));
                // Side ribs
                AddQuad(st, b11, b01, t01, t11, darkRib);
                AddQuad(st, b00, b10, t10, t00, darkRib);
            }

            // 4 Suspension Rigging Cords from shoulder harness (Y = 0, Z = ±0.75) up to canopy underside
            Vector3[,] cords =
            {
                { new Vector3(0.45f, 0f, -0.75f), new Vector3(1.15f, 4.35f, -3.15f) },
                { new Vector3(-0.35f, 0f, -0.75f), new Vector3(-1.05f, 4.20f, -3.15f) },
                { new Vector3(0.45f, 0f, 0.75f), new Vector3(1.15f, 4.35f, 3.15f) },
                { new Vector3(-0.35f, 0f, 0.75f), new Vector3(-1.05f, 4.20f, 3.15f) }
            };
            for (int c = 0; c < 4; c++)
            {
                Vector3 p0 = cords[c, 0];
                Vector3 p1 = cords[c, 1];
                Vector3 mid = (p0 + p1) * 0.5f;
                float len = p0.DistanceTo(p1);
                // Build thin angled strut quad pair
                Vector3 d = (p1 - p0).Normalized();
                Vector3 side = d.Cross(Vector3.Right).Normalized() * 0.055f;
                Vector3 fwd = new(0.055f, 0f, 0f);
                AddQuad(st, p0 - side, p0 + side, p1 + side, p1 - side, cordCol);
                AddQuad(st, p0 + side, p0 - side, p1 - side, p1 + side, cordCol);
                AddQuad(st, p0 - fwd, p0 + fwd, p1 + fwd, p1 - fwd, cordCol);
            }

            st.GenerateNormals();
            _parachuteCanopyMesh = st.Commit();
            return _parachuteCanopyMesh;
        }

        private static void AddBox(SurfaceTool st, Vector3 center, Vector3 size, Color col)
        {
            AddTaperedBox(st, center, size, size, col);
        }

        private static void AddTaperedBox(SurfaceTool st, Vector3 center, Vector3 bottomSize, Vector3 topSize, Color col)
        {
            float bx = bottomSize.X * 0.5f;
            float bz = bottomSize.Z * 0.5f;
            float tx = topSize.X * 0.5f;
            float tz = topSize.Z * 0.5f;
            float hy = bottomSize.Y * 0.5f;

            Vector3 b00 = center + new Vector3(-bx, -hy, -bz);
            Vector3 b10 = center + new Vector3(bx, -hy, -bz);
            Vector3 b11 = center + new Vector3(bx, -hy, bz);
            Vector3 b01 = center + new Vector3(-bx, -hy, bz);

            Vector3 t00 = center + new Vector3(-tx, hy, -tz);
            Vector3 t10 = center + new Vector3(tx, hy, -tz);
            Vector3 t11 = center + new Vector3(tx, hy, tz);
            Vector3 t01 = center + new Vector3(-tx, hy, tz);

            // Top (+Y) & Bottom (-Y)
            AddQuad(st, t00, t10, t11, t01, col.Lightened(0.08f));
            AddQuad(st, b01, b11, b10, b00, col.Darkened(0.15f));

            // Front (+X) & Back (-X)
            AddQuad(st, b10, b11, t11, t10, col);
            AddQuad(st, b01, b00, t00, t01, col.Darkened(0.06f));

            // Right (+Z) & Left (-Z)
            AddQuad(st, b11, b01, t01, t11, col.Darkened(0.03f));
            AddQuad(st, b00, b10, t10, t00, col.Darkened(0.03f));
        }

        private static void AddQuad(SurfaceTool st, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Color col)
        {
            st.SetColor(col);
            st.AddVertex(v0);
            st.AddVertex(v1);
            st.AddVertex(v2);

            st.AddVertex(v0);
            st.AddVertex(v2);
            st.AddVertex(v3);
        }

        private static void AddQuadWithUv(SurfaceTool st, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uvMin, Vector2 uvMax, Color col)
        {
            st.SetColor(col);
            st.SetUV(new Vector2(uvMin.X, uvMin.Y));
            st.AddVertex(v0);
            st.SetUV(new Vector2(uvMax.X, uvMin.Y));
            st.AddVertex(v1);
            st.SetUV(new Vector2(uvMax.X, uvMax.Y));
            st.AddVertex(v2);

            st.SetUV(new Vector2(uvMin.X, uvMin.Y));
            st.AddVertex(v0);
            st.SetUV(new Vector2(uvMax.X, uvMax.Y));
            st.AddVertex(v2);
            st.SetUV(new Vector2(uvMin.X, uvMax.Y));
            st.AddVertex(v3);
        }

        private static void AddTriangle(SurfaceTool st, Vector3 v0, Vector3 v1, Vector3 v2, Color col)
        {
            st.SetColor(col);
            st.AddVertex(v0);
            st.AddVertex(v1);
            st.AddVertex(v2);

            // Double-sided for palm fronds
            st.AddVertex(v0);
            st.AddVertex(v2);
            st.AddVertex(v1);
        }
    }
}
