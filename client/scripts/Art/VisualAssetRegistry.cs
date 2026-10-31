using System;
using System.Collections.Generic;
using System.IO;
using FFM.Sim.Data;
using FFM.Sim.Map;
using Godot;

namespace FFM.Client.Art
{
    /// <summary>
    /// Centralized 2D Visual Asset Registry for both the Esports Franchise Hub and 3D Broadcast HUD.
    /// Automatically loads external PNG assets from res://assets/art/ui/ when present, and provides
    /// deterministic, high-resolution procedural vector/pixel texture generation for all 18 teams,
    /// 11 skills, 15 weapons, 5 roles, equipment icons, player portraits, and the Bermuda tactical map.
    /// </summary>
    public static class VisualAssetRegistry
    {
        private static readonly Dictionary<string, Texture2D> TeamLogoCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> SkillIconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> WeaponIconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> RoleIconCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> PortraitCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Texture2D> EquipIconCache = new(StringComparer.OrdinalIgnoreCase);
        private static Texture2D? _cachedMapTexture;

        private static readonly (string id, string cat, int tier, string shortName)[] WeaponCatalog =
        {
            ("wp_ar_t1", "AR", 1, "M4A1-I"),
            ("wp_ar_t2", "AR", 2, "AK47-II"),
            ("wp_ar_t3", "AR", 3, "GROZA-III"),
            ("wp_smg_t1", "SMG", 1, "MP5-I"),
            ("wp_smg_t2", "SMG", 2, "MP40-II"),
            ("wp_smg_t3", "SMG", 3, "MAC10-III"),
            ("wp_sg_t1", "SG", 1, "M1014-I"),
            ("wp_sg_t2", "SG", 2, "M1887-II"),
            ("wp_sg_t3", "SG", 3, "MAG7-III"),
            ("wp_dmr_t1", "DMR", 1, "SKS-I"),
            ("wp_dmr_t2", "DMR", 2, "WDPK-II"),
            ("wp_dmr_t3", "DMR", 3, "AC80-III"),
            ("wp_sr_t1", "SR", 1, "KAR98-I"),
            ("wp_sr_t2", "SR", 2, "M82B-II"),
            ("wp_sr_t3", "SR", 3, "AWM-III")
        };

        public static string GetWeaponDisplayName(int weaponIndex)
        {
            if ((uint)weaponIndex < (uint)WeaponCatalog.Length)
            {
                return WeaponCatalog[weaponIndex].shortName;
            }
            return "M4A1-I";
        }

        public static int GetWeaponTier(int weaponIndex)
        {
            if ((uint)weaponIndex < (uint)WeaponCatalog.Length)
            {
                return WeaponCatalog[weaponIndex].tier;
            }
            return 1;
        }

        public static string GetWeaponCategoryCode(int weaponIndex)
        {
            if ((uint)weaponIndex < (uint)WeaponCatalog.Length)
            {
                return WeaponCatalog[weaponIndex].cat;
            }
            return "AR";
        }

        public static Texture2D GetTeamLogo(string teamId, string? colorHex = null)
        {
            string key = string.IsNullOrWhiteSpace(teamId) ? "t_default" : teamId.Trim();
            if (TeamLogoCache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }

            Texture2D? external = TryLoadExternalTexture($"res://assets/art/ui/teams/{key}.png");
            if (external != null)
            {
                TeamLogoCache[key] = external;
                return external;
            }

            Color primary = ParseTeamColor(key, colorHex);
            Texture2D generated = GenerateTeamCrestTexture(key, primary);
            TeamLogoCache[key] = generated;
            return generated;
        }

        public static Texture2D GetTeamLogo(string teamId, string? teamName, string? colorHex)
        {
            return GetTeamLogo(teamId, colorHex);
        }

        public static Texture2D GetTeamLogo(string teamId, string? teamName, Color teamColor)
        {
            return GetTeamLogo(teamId, "#" + teamColor.ToHtml(false));
        }

        public static Texture2D GetRoleIcon(Role role)
        {
            return GetRoleIcon(role.ToString());
        }

        public static Texture2D GetRoleIcon(string roleName)
        {
            string key = string.IsNullOrWhiteSpace(roleName) ? "Rusher" : roleName.Trim();
            if (RoleIconCache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }

            Texture2D? external = TryLoadExternalTexture($"res://assets/art/ui/roles/{key.ToLowerInvariant()}.png");
            if (external != null)
            {
                RoleIconCache[key] = external;
                return external;
            }

            Texture2D generated = GenerateRoleBadgeTexture(key);
            RoleIconCache[key] = generated;
            return generated;
        }

        public static Texture2D GetSkillIcon(string skillId, SkillCategory category, bool isActive = true)
        {
            return GetSkillIcon(skillId, isActive, category.ToString());
        }

        public static Texture2D GetSkillIcon(string skillId, string? category, bool isActive = true)
        {
            return GetSkillIcon(skillId, isActive, category);
        }

        public static Texture2D GetSkillIcon(string skillId, bool isActive, SkillCategory category)
        {
            return GetSkillIcon(skillId, isActive, category.ToString());
        }

        public static Texture2D GetSkillIcon(string skillId, bool isActive = true, string? category = null)
        {
            string key = string.IsNullOrWhiteSpace(skillId) ? "sk_dash" : skillId.Trim();
            if (SkillIconCache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }

            Texture2D? external = TryLoadExternalTexture($"res://assets/art/ui/skills/{key}.png");
            if (external != null)
            {
                SkillIconCache[key] = external;
                return external;
            }

            Texture2D generated = GenerateSkillBadgeTexture(key, isActive, category);
            SkillIconCache[key] = generated;
            return generated;
        }

        public static Texture2D GetWeaponIcon(WeaponCategory weaponCategory, int tier = 1)
        {
            string cat = weaponCategory switch
            {
                WeaponCategory.Shotgun => "SG",
                WeaponCategory.Sniper => "SR",
                _ => weaponCategory.ToString()
            };
            int clampedTier = Math.Clamp(tier, 1, 3);
            string id = $"wp_{cat.ToLowerInvariant()}_t{clampedTier}";
            return GetWeaponIcon(id, cat, clampedTier);
        }

        public static Texture2D GetWeaponIcon(int weaponIndex)
        {
            if ((uint)weaponIndex >= (uint)WeaponCatalog.Length)
            {
                weaponIndex = 0;
            }
            var info = WeaponCatalog[weaponIndex];
            return GetWeaponIcon(info.id, info.cat, info.tier);
        }

        public static Texture2D GetWeaponIcon(string weaponId, string category, int tier)
        {
            string key = $"{weaponId}_{category}_{tier}";
            if (WeaponIconCache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }

            Texture2D? external = TryLoadExternalTexture($"res://assets/art/ui/weapons/{weaponId}.png");
            if (external != null)
            {
                WeaponIconCache[key] = external;
                return external;
            }

            Texture2D generated = GenerateWeaponSilhouetteTexture(category, tier);
            WeaponIconCache[key] = generated;
            return generated;
        }

        public static Texture2D GetEquipmentIcon(string equipType, int tier = 1)
        {
            string key = $"{equipType.ToLowerInvariant()}_{tier}";
            if (EquipIconCache.TryGetValue(key, out Texture2D? cached))
            {
                return cached;
            }

            Texture2D? external = TryLoadExternalTexture($"res://assets/art/ui/equip/{key}.png");
            if (external != null)
            {
                EquipIconCache[key] = external;
                return external;
            }

            Texture2D generated = GenerateEquipmentIconTexture(equipType.ToLowerInvariant(), tier);
            EquipIconCache[key] = generated;
            return generated;
        }

        public static Texture2D GetPlayerPortrait(string playerId, Role role, Color? teamColor = null, bool isIgl = false)
        {
            return GetPlayerPortrait(playerId, role.ToString(), teamColor, isIgl);
        }

        public static Texture2D GetPlayerPortrait(string playerId, string roleName = "Rusher", Color? teamColor = null, bool isIgl = false)
        {
            Color jerseyCol = teamColor ?? new Color(1.0f, 0.72f, 0.0f);
            string cacheKey = $"{playerId}_{roleName}_{jerseyCol.ToHtml(false)}_{isIgl}";
            if (PortraitCache.TryGetValue(cacheKey, out Texture2D? cached))
            {
                return cached;
            }

            // Check player-specific portrait first, then role archetype portrait
            string archetype = isIgl ? "igl" : roleName.ToLowerInvariant() switch
            {
                "sniper" => "sniper",
                "secondrusher" => "second_rusher",
                "grenadier" => "grenadier",
                "support" => "support",
                _ => "rusher"
            };

            Texture2D? external = TryLoadExternalTexture($"res://assets/art/ui/portraits/{playerId}.png")
                                  ?? TryLoadExternalTexture($"res://assets/art/ui/portraits/portrait_{archetype}.png");
            if (external != null)
            {
                // If loaded from archetype, still cache per key
                PortraitCache[cacheKey] = external;
                return external;
            }

            Texture2D generated = GeneratePlayerPortraitTexture(playerId, roleName, jerseyCol, isIgl);
            PortraitCache[cacheKey] = generated;
            return generated;
        }

        public static Texture2D GetBermudaMapTexture(MapData? map = null)
        {
            return GetBermudaTacticalMapTexture(map ?? MapData.GenerateBermudaLite());
        }

        public static Texture2D GetBermudaTacticalMapTexture(MapData map)
        {
            if (_cachedMapTexture != null)
            {
                return _cachedMapTexture;
            }

            Image? externalBg = TryLoadExternalImage("res://assets/art/ui/maps/bermuda_satellite_map.png");
            _cachedMapTexture = GenerateBermudaMapTexture(map, externalBg);
            return _cachedMapTexture;
        }

        private static Texture2D? TryLoadExternalTexture(string resPath)
        {
            Image? img = TryLoadExternalImage(resPath);
            if (img != null)
            {
                return ImageTexture.CreateFromImage(img);
            }
            return null;
        }

        private static Image? TryLoadExternalImage(string resPath)
        {
            try
            {
                string absPath = ProjectSettings.GlobalizePath(resPath);
                if (File.Exists(absPath))
                {
                    var img = new Image();
                    if (img.Load(absPath) == Error.Ok && !img.IsEmpty())
                    {
                        return img;
                    }
                }
            }
            catch
            {
                // Ignore and fall back to procedural rendering
            }
            return null;
        }

        public static Color ParseTeamColor(string teamId, string? colorHex = null)
        {
            if (!string.IsNullOrWhiteSpace(colorHex))
            {
                return Color.FromHtml(colorHex);
            }

            return teamId.ToLowerInvariant() switch
            {
                "t_evos" => Color.FromHtml("#2274A5"),
                "t_buriram" => Color.FromHtml("#003F88"),
                "t_fluxo" => Color.FromHtml("#7B2CBF"),
                "t_loud" => Color.FromHtml("#00CC66"),
                "t_falcons" => Color.FromHtml("#2EC4B6"),
                "t_rrq" => Color.FromHtml("#FF9F1C"),
                "t_onic" => Color.FromHtml("#F7B801"),
                "t_cggg" => Color.FromHtml("#E71D36"),
                "t_pain" => Color.FromHtml("#D90429"),
                "t_corinthians" => Color.FromHtml("#8D99AE"),
                "t_wag" => Color.FromHtml("#FB5607"),
                "t_pbox" => Color.FromHtml("#FF006E"),
                "t_indostars" => Color.FromHtml("#3A86FF"),
                "t_attack" => Color.FromHtml("#8338EC"),
                "t_magic" => Color.FromHtml("#06D6A0"),
                "t_allglory" => Color.FromHtml("#118AB2"),
                "t_ignite" => Color.FromHtml("#EF476F"),
                "t_alpha" => Color.FromHtml("#8AC926"),
                _ => new Color(1.0f, 0.72f, 0.0f)
            };
        }

        private static Texture2D GenerateTeamCrestTexture(string teamId, Color primary)
        {
            const int size = 72;
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;

            Color darkCore = primary.Darkened(0.62f);
            Color brightRim = primary.Lightened(0.35f);
            Color goldAccent = new(1.0f, 0.78f, 0.18f);

            int motif = Math.Abs(StableHash(teamId)) % 5;

            for (int y = 0; y < size; y++)
            {
                float ny = (y - cy) / (size * 0.44f);
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - cx) / (size * 0.42f);
                    float ax = MathF.Abs(nx);

                    // Heraldic esports shield SDF
                    float shieldWidth = ny < 0.1f ? 0.92f : Math.Max(0.0f, 0.92f - (ny - 0.1f) * 0.88f);
                    float dist = Math.Max(ax - shieldWidth, MathF.Abs(ny) - 0.95f);

                    if (dist > 0.04f)
                    {
                        img.SetPixel(x, y, Colors.Transparent);
                        continue;
                    }

                    float edgeAlpha = Math.Clamp((0.04f - dist) / 0.06f, 0f, 1f);
                    bool isOuterRim = dist > -0.10f;
                    bool isInnerRim = dist <= -0.10f && dist > -0.16f;

                    Color col;
                    if (isOuterRim)
                    {
                        col = brightRim.Lerp(goldAccent, 0.35f);
                    }
                    else if (isInnerRim)
                    {
                        col = new Color(0.06f, 0.08f, 0.13f);
                    }
                    else
                    {
                        // Diagonal metallic gradient inside shield
                        float grad = Math.Clamp((nx + ny + 1.2f) * 0.42f, 0f, 1f);
                        col = darkCore.Lerp(primary, grad);

                        // Inner geometric emblem motif
                        bool emblemHit = motif switch
                        {
                            0 => MathF.Abs(ny + ax * 0.9f - 0.1f) < 0.13f || (ax < 0.12f && MathF.Abs(ny) < 0.55f), // Chevron + sword
                            1 => MathF.Abs(nx * 1.4f + ny * 0.7f) < 0.14f && MathF.Abs(ny) < 0.62f, // Lightning slash
                            2 => MathF.Abs(MathF.Sqrt(nx * nx + ny * ny) - 0.42f) < 0.10f || (ax < 0.16f && ny < 0.0f && ny > -0.55f), // Crown ring
                            3 => MathF.Abs(ny - MathF.Abs(nx * 1.2f) + 0.25f) < 0.13f || MathF.Abs(ny + 0.25f) < 0.09f, // Winged V
                            _ => (ax + MathF.Abs(ny)) < 0.46f && (ax + MathF.Abs(ny)) > 0.24f // Diamond core
                        };

                        if (emblemHit)
                        {
                            col = Colors.White.Lerp(brightRim, 0.25f);
                        }
                    }

                    col.A = edgeAlpha;
                    img.SetPixel(x, y, col);
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D GenerateRoleBadgeTexture(string roleName)
        {
            const int size = 56;
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;

            string norm = roleName.ToLowerInvariant();
            Color accent = norm switch
            {
                "rusher" => new Color(1.0f, 0.32f, 0.36f),
                "secondrusher" => new Color(1.0f, 0.62f, 0.16f),
                "sniper" => new Color(0.0f, 0.85f, 1.0f),
                "support" => new Color(0.18f, 0.88f, 0.48f),
                "grenadier" => new Color(1.0f, 0.76f, 0.12f),
                _ => new Color(1.0f, 0.72f, 0.0f)
            };

            for (int y = 0; y < size; y++)
            {
                float ny = (y - cy) / (size * 0.45f);
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - cx) / (size * 0.45f);
                    float ax = MathF.Abs(nx);
                    float ay = MathF.Abs(ny);

                    // Octagonal tactical badge
                    float oct = Math.Max(Math.Max(ax, ay), (ax + ay) * 0.73f);
                    if (oct > 1.0f)
                    {
                        img.SetPixel(x, y, Colors.Transparent);
                        continue;
                    }

                    float alpha = Math.Clamp((1.0f - oct) / 0.07f, 0f, 1f);
                    Color col = new Color(0.07f, 0.10f, 0.16f).Lerp(accent.Darkened(0.65f), 0.5f);

                    if (oct > 0.84f)
                    {
                        col = accent;
                    }
                    else
                    {
                        bool symbol = norm switch
                        {
                            // Rusher: Double upward tactical chevrons
                            "rusher" => (MathF.Abs(ny + 0.22f - ax * 0.85f) < 0.12f && ax < 0.56f) ||
                                        (MathF.Abs(ny - 0.18f - ax * 0.85f) < 0.12f && ax < 0.56f),
                            // SecondRusher: Dual wing arrows
                            "secondrusher" => (MathF.Abs(ny - ax * 0.7f + 0.15f) < 0.11f && ax < 0.62f) ||
                                              (ax < 0.14f && ay < 0.52f),
                            // Sniper: Crosshair ring + reticles
                            "sniper" => MathF.Abs(MathF.Sqrt(nx * nx + ny * ny) - 0.46f) < 0.09f ||
                                        (ax < 0.07f && ay < 0.70f) || (ay < 0.07f && ax < 0.70f),
                            // Support: Bold medical/tactical cross
                            "support" => (ax < 0.18f && ay < 0.56f) || (ay < 0.18f && ax < 0.56f),
                            // Grenadier: Explosion starburst + core
                            _ => MathF.Sqrt(nx * nx + ny * ny) < 0.34f ||
                                 (MathF.Abs(ax - ay) < 0.10f && ax < 0.58f)
                        };

                        if (symbol)
                        {
                            col = Colors.White.Lerp(accent.Lightened(0.35f), 0.35f);
                        }
                    }

                    col.A = alpha;
                    img.SetPixel(x, y, col);
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D GenerateSkillBadgeTexture(string skillId, bool isActive, string? category)
        {
            const int size = 64;
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;

            string idLower = skillId.ToLowerInvariant();
            string catLower = (category ?? string.Empty).ToLowerInvariant();

            Color themeCol = idLower switch
            {
                "sk_dash" => new Color(1.0f, 0.58f, 0.12f),      // Tatsuya Rush - Blaze Orange
                "sk_shield" => new Color(0.0f, 0.84f, 1.0f),     // Chrono Force Field - Electric Cyan
                "sk_scan" => new Color(0.98f, 0.80f, 0.15f),     // Clu Tracing Steps - Radar Gold
                "sk_heal" => new Color(0.18f, 0.90f, 0.48f),     // Alok Drop the Beat - Emerald
                "sk_deploy" => new Color(0.68f, 0.38f, 0.98f),   // Skyler Riptide - Sonic Purple
                "sk_p_aim" => new Color(1.0f, 0.36f, 0.42f),     // Sharp Eye - Crimson
                "sk_p_move" => new Color(1.0f, 0.76f, 0.18f),    // Kelly Dash - Sprint Yellow
                "sk_p_armor" => new Color(0.32f, 0.68f, 1.0f),   // Andrew Wolf Pack - Steel Blue
                "sk_p_gloo" => new Color(0.15f, 0.92f, 0.92f),   // Nairi Ice Iron - Frost Cyan
                "sk_p_heal" => new Color(0.28f, 0.92f, 0.58f),   // Kapella Healing Song - Mint Green
                "sk_p_loot" => new Color(0.92f, 0.58f, 0.98f),   // Paloma Arms - Violet
                _ => isActive ? new Color(1.0f, 0.72f, 0.0f) : new Color(0.0f, 0.83f, 1.0f)
            };

            if (!string.IsNullOrEmpty(catLower) && idLower == "sk_default")
            {
                themeCol = catLower.Contains("heal") ? new Color(0.18f, 0.90f, 0.48f) : themeCol;
            }

            Color goldRim = new(1.0f, 0.76f, 0.16f);

            for (int y = 0; y < size; y++)
            {
                float ny = (y - cy) / (size * 0.46f);
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - cx) / (size * 0.46f);
                    float ax = MathF.Abs(nx);
                    float ay = MathF.Abs(ny);

                    // Active skill = Hexagon; Passive skill = Rotated Diamond / Octagon
                    float shapeDist = isActive
                        ? Math.Max(ax * 0.866f + ay * 0.5f, ay)
                        : Math.Max((ax + ay) * 0.76f, Math.Max(ax, ay) * 0.92f);

                    if (shapeDist > 1.0f)
                    {
                        img.SetPixel(x, y, Colors.Transparent);
                        continue;
                    }

                    float alpha = Math.Clamp((1.0f - shapeDist) / 0.06f, 0f, 1f);
                    Color col;

                    if (shapeDist > 0.83f)
                    {
                        col = isActive ? goldRim.Lerp(themeCol, 0.35f) : themeCol.Lightened(0.22f);
                    }
                    else if (shapeDist > 0.76f)
                    {
                        col = new Color(0.05f, 0.07f, 0.12f);
                    }
                    else
                    {
                        float r = MathF.Sqrt(nx * nx + ny * ny);
                        col = new Color(0.08f, 0.11f, 0.18f).Lerp(themeCol.Darkened(0.45f), 1.0f - r * 0.8f);

                        bool glyph = idLower switch
                        {
                            "sk_dash" or "sk_p_move" =>
                                (MathF.Abs(ny - nx * 0.8f + 0.15f) < 0.11f && r < 0.58f) ||
                                (MathF.Abs(ny - nx * 0.8f - 0.18f) < 0.11f && r < 0.58f),
                            "sk_shield" or "sk_p_gloo" =>
                                MathF.Abs(r - 0.46f) < 0.09f || (MathF.Abs(ax * 0.86f + ay * 0.5f - 0.32f) < 0.08f),
                            "sk_scan" or "sk_p_aim" =>
                                MathF.Abs(r - 0.48f) < 0.07f || MathF.Abs(r - 0.24f) < 0.07f || (ax < 0.06f && ay < 0.62f),
                            "sk_heal" or "sk_p_heal" =>
                                (ax < 0.16f && ay < 0.50f) || (ay < 0.16f && ax < 0.50f) || MathF.Abs(r - 0.56f) < 0.05f,
                            "sk_deploy" =>
                                (MathF.Abs(ny - MathF.Sin(nx * 6f) * 0.22f) < 0.12f && ax < 0.58f) ||
                                (MathF.Abs(ny + 0.28f - MathF.Sin(nx * 6f) * 0.22f) < 0.10f && ax < 0.52f),
                            "sk_p_armor" =>
                                ax < 0.42f && ay < 0.46f && (ax > 0.26f || ny > 0.15f),
                            _ => r < 0.36f || (ax < 0.12f && ay < 0.55f)
                        };

                        if (glyph)
                        {
                            col = Colors.White.Lerp(themeCol.Lightened(0.4f), 0.3f);
                        }
                    }

                    col.A = alpha;
                    img.SetPixel(x, y, col);
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D GenerateWeaponSilhouetteTexture(string category, int tier)
        {
            const int w = 92;
            const int h = 36;
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

            Color tierCol = tier switch
            {
                3 => new Color(1.0f, 0.76f, 0.14f), // Tier 3 Gold
                2 => new Color(0.0f, 0.84f, 1.0f),  // Tier 2 Electric Cyan
                _ => new Color(0.72f, 0.78f, 0.88f) // Tier 1 Steel Silver
            };

            Color bgCol = new(0.07f, 0.10f, 0.16f, 0.92f);
            string cat = category.ToUpperInvariant();

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / (h - 1);
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / (w - 1);

                    // Rounded card frame
                    bool isBorder = x == 0 || x == w - 1 || y == 0 || y == h - 1 || (x < 3 && tier >= 2);
                    if (isBorder)
                    {
                        img.SetPixel(x, y, new Color(tierCol.R, tierCol.G, tierCol.B, x < 3 ? 0.95f : 0.45f));
                        continue;
                    }

                    // Side-profile weapon silhouette check
                    bool inGun = false;
                    switch (cat)
                    {
                        case "SR": // Long sniper barrel + scope + stock
                            inGun = (ny >= 0.44f && ny <= 0.54f && nx >= 0.10f && nx <= 0.90f) || // Barrel + receiver
                                    (ny >= 0.26f && ny <= 0.40f && nx >= 0.34f && nx <= 0.62f) || // Scope
                                    (ny >= 0.48f && ny <= 0.72f && nx >= 0.10f && nx <= 0.28f) || // Stock
                                    (ny >= 0.54f && ny <= 0.76f && nx >= 0.38f && nx <= 0.45f);   // Grip/Mag
                            break;
                        case "DMR": // Marksman rifle
                            inGun = (ny >= 0.42f && ny <= 0.55f && nx >= 0.12f && nx <= 0.86f) ||
                                    (ny >= 0.28f && ny <= 0.40f && nx >= 0.36f && nx <= 0.56f) ||
                                    (ny >= 0.46f && ny <= 0.70f && nx >= 0.12f && nx <= 0.26f) ||
                                    (ny >= 0.55f && ny <= 0.78f && nx >= 0.46f && nx <= 0.54f);
                            break;
                        case "SG": // Heavy shotgun receiver + pump
                            inGun = (ny >= 0.40f && ny <= 0.56f && nx >= 0.16f && nx <= 0.82f) ||
                                    (ny >= 0.56f && ny <= 0.64f && nx >= 0.48f && nx <= 0.72f) || // Pump tube
                                    (ny >= 0.48f && ny <= 0.74f && nx >= 0.14f && nx <= 0.30f);   // Stock
                            break;
                        case "SMG": // Compact submachine gun + extended mag
                            inGun = (ny >= 0.38f && ny <= 0.56f && nx >= 0.24f && nx <= 0.76f) ||
                                    (ny >= 0.56f && ny <= 0.84f && nx >= 0.48f && nx <= 0.56f) || // Long mag
                                    (ny >= 0.56f && ny <= 0.78f && nx >= 0.30f && nx <= 0.38f);   // Grip
                            break;
                        default: // AR: Assault rifle receiver + curved mag + stock + sight
                            inGun = (ny >= 0.40f && ny <= 0.56f && nx >= 0.14f && nx <= 0.84f) ||
                                    (ny >= 0.28f && ny <= 0.40f && nx >= 0.38f && nx <= 0.50f) || // Holo sight
                                    (ny >= 0.46f && ny <= 0.70f && nx >= 0.14f && nx <= 0.28f) || // Stock
                                    (ny >= 0.56f && ny <= 0.82f && nx >= 0.48f && nx <= 0.57f) || // Mag
                                    (ny >= 0.56f && ny <= 0.76f && nx >= 0.33f && nx <= 0.40f);   // Grip
                            break;
                    }

                    if (inGun)
                    {
                        Color gunCol = Colors.White.Lerp(tierCol, 0.45f);
                        img.SetPixel(x, y, gunCol);
                    }
                    else
                    {
                        img.SetPixel(x, y, bgCol);
                    }
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D GenerateEquipmentIconTexture(string equipType, int tier)
        {
            const int size = 40;
            var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
            float cx = (size - 1) * 0.5f;
            float cy = (size - 1) * 0.5f;

            Color col = equipType switch
            {
                "gloo" => new Color(0.0f, 0.86f, 1.0f),
                "grenade" => new Color(1.0f, 0.56f, 0.16f),
                "medkit" => new Color(0.18f, 0.88f, 0.46f),
                "igl_crown" or "booyah_trophy" => new Color(1.0f, 0.78f, 0.12f),
                _ => tier switch
                {
                    3 => new Color(1.0f, 0.76f, 0.14f),
                    2 => new Color(0.0f, 0.84f, 1.0f),
                    1 => new Color(0.75f, 0.82f, 0.92f),
                    _ => new Color(0.35f, 0.40f, 0.48f)
                }
            };

            for (int y = 0; y < size; y++)
            {
                float ny = (y - cy) / (size * 0.45f);
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - cx) / (size * 0.45f);
                    float ax = MathF.Abs(nx);
                    float ay = MathF.Abs(ny);
                    float r = MathF.Sqrt(nx * nx + ny * ny);

                    if (r > 0.96f)
                    {
                        img.SetPixel(x, y, Colors.Transparent);
                        continue;
                    }

                    bool hit = equipType switch
                    {
                        "armor" => ax < 0.56f && ay < 0.62f && !(ay < -0.25f && ax < 0.24f),
                        "helmet" => r < 0.62f && ny < 0.42f && !(ny > 0.05f && ax < 0.35f),
                        "gloo" => ax < 0.68f && ay < 0.48f && MathF.Abs(ny + ax * 0.35f) < 0.42f,
                        "grenade" => r < 0.48f || (ax < 0.18f && ny < -0.35f && ny > -0.72f),
                        "medkit" => (ax < 0.18f && ay < 0.55f) || (ay < 0.18f && ax < 0.55f),
                        _ => ny > -0.45f && ny < 0.45f && ax < 0.62f
                    };

                    if (hit)
                    {
                        img.SetPixel(x, y, col.Lightened(0.18f));
                    }
                    else if (r > 0.82f)
                    {
                        img.SetPixel(x, y, new Color(col.R, col.G, col.B, 0.55f));
                    }
                    else
                    {
                        img.SetPixel(x, y, new Color(0.06f, 0.09f, 0.15f, 0.85f));
                    }
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D GeneratePlayerPortraitTexture(string playerId, string roleName, Color teamColor, bool isIgl)
        {
            const int w = 96;
            const int h = 112;
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);

            int hash = Math.Abs(StableHash(playerId));
            int hairStyle = hash % 4;
            int visorType = (hash / 4) % 3;

            Color skinTone = (hash % 3) switch
            {
                0 => new Color(0.88f, 0.72f, 0.58f),
                1 => new Color(0.78f, 0.60f, 0.46f),
                _ => new Color(0.92f, 0.78f, 0.66f)
            };
            Color hairCol = ((hash / 3) % 4) switch
            {
                0 => new Color(0.12f, 0.14f, 0.18f),
                1 => new Color(0.26f, 0.18f, 0.12f),
                2 => new Color(0.85f, 0.88f, 0.94f), // Silver cyber hair
                _ => teamColor.Darkened(0.35f)
            };

            Color bgTop = teamColor.Darkened(0.72f);
            Color bgBot = new(0.05f, 0.07f, 0.12f);
            Color visorCol = roleName.Equals("Sniper", StringComparison.OrdinalIgnoreCase)
                ? new Color(0.0f, 0.88f, 1.0f)
                : new Color(1.0f, 0.74f, 0.12f);

            for (int y = 0; y < h; y++)
            {
                float ny = (float)y / (h - 1); // 0 top .. 1 bottom
                for (int x = 0; x < w; x++)
                {
                    float nx = (float)x / (w - 1) * 2f - 1f; // -1 left .. +1 right
                    float ax = MathF.Abs(nx);

                    // Frame border
                    if (x < 2 || x >= w - 2 || y < 2 || y >= h - 2)
                    {
                        Color border = isIgl ? new Color(1.0f, 0.78f, 0.12f) : teamColor.Lightened(0.15f);
                        img.SetPixel(x, y, border);
                        continue;
                    }

                    // Background with subtle radial glow
                    Color px = bgTop.Lerp(bgBot, ny);
                    float glow = Math.Max(0f, 1.0f - MathF.Sqrt(nx * nx + (ny - 0.42f) * (ny - 0.42f) * 2.2f));
                    px = px.Lerp(teamColor.Darkened(0.35f), glow * 0.45f);

                    // 1. Pro Jersey & Shoulders (ny: 0.62 .. 1.0)
                    if (ny >= 0.62f)
                    {
                        float shoulderW = 0.32f + (ny - 0.62f) * 1.45f;
                        if (ax <= shoulderW)
                        {
                            px = new Color(0.11f, 0.14f, 0.22f);
                            // Team color shoulder stripes & V-neck collar
                            if (ax > shoulderW - 0.16f || MathF.Abs(ax - (ny - 0.62f) * 0.65f) < 0.07f)
                            {
                                px = teamColor.Lightened(0.12f);
                            }
                            else if (ny > 0.78f && ax < 0.18f)
                            {
                                px = teamColor.Darkened(0.25f);
                            }
                        }
                    }

                    // 2. Neck (ny: 0.53 .. 0.66)
                    if (ny >= 0.53f && ny <= 0.66f && ax < 0.17f)
                    {
                        px = skinTone.Darkened(0.14f);
                    }

                    // 3. Head / Face oval (center around ny = 0.38)
                    float hy = (ny - 0.38f) / 0.19f;
                    float hx = nx / 0.31f;
                    float headDist = hx * hx + hy * hy;
                    if (headDist <= 1.0f)
                    {
                        px = skinTone.Lerp(skinTone.Darkened(0.12f), Math.Clamp(nx * 0.4f + 0.3f, 0f, 1f));

                        // Tactical Visor / Cyber Glasses
                        if (ny >= 0.34f && ny <= 0.41f && ax < 0.25f)
                        {
                            if (visorType == 0 || ax > 0.04f)
                            {
                                px = visorCol;
                            }
                        }
                    }

                    // 4. Hair / Cap (top of head ny: 0.15 .. 0.33)
                    float hairY = (ny - 0.25f) / 0.13f;
                    float hairX = (nx - (hairStyle == 1 ? 0.05f : 0f)) / 0.34f;
                    if (hairX * hairX + hairY * hairY <= 1.0f && ny < 0.35f)
                    {
                        px = hairCol;
                    }

                    // 5. Esports Headset Earcups & Band
                    bool earCup = ny >= 0.32f && ny <= 0.46f && ax >= 0.27f && ax <= 0.38f;
                    bool headBand = ny >= 0.14f && ny <= 0.34f &&
                                    MathF.Abs(MathF.Sqrt(nx * nx * 2.2f + (ny - 0.34f) * (ny - 0.34f) * 6.5f) - 0.52f) < 0.06f;
                    if (earCup || headBand)
                    {
                        px = earCup && ax > 0.33f ? teamColor.Lightened(0.25f) : new Color(0.16f, 0.20f, 0.28f);
                    }

                    img.SetPixel(x, y, px);
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static Texture2D GenerateBermudaMapTexture(MapData map, Image? externalSatellite)
        {
            const int texSize = 512;
            var img = Image.CreateEmpty(texSize, texSize, false, Image.Format.Rgba8);

            bool hasExternal = externalSatellite != null && !externalSatellite.IsEmpty();
            if (hasExternal && (externalSatellite!.GetWidth() != texSize || externalSatellite.GetHeight() != texSize))
            {
                externalSatellite.Resize(texSize, texSize, Image.Interpolation.Bilinear);
            }

            for (int y = 0; y < texSize; y++)
            {
                int cy = Math.Clamp((y * MapData.FineGridSize) / texSize, 0, MapData.FineGridSize - 1);
                for (int x = 0; x < texSize; x++)
                {
                    int cx = Math.Clamp((x * MapData.FineGridSize) / texSize, 0, MapData.FineGridSize - 1);
                    MapCell cell = map.GetCell(cx, cy);

                    Color col;
                    if (cell.IsWater)
                    {
                        float wave = 0.02f * MathF.Sin(x * 0.18f + y * 0.14f);
                        col = new Color(0.07f + wave, 0.14f + wave, 0.24f + wave);
                    }
                    else if (cell.IsRoad)
                    {
                        col = new Color(0.32f, 0.35f, 0.40f);
                    }
                    else if (cell.IsBuilding)
                    {
                        col = new Color(0.46f, 0.50f, 0.56f);
                    }
                    else
                    {
                        col = cell.HeightLevel switch
                        {
                            0 => new Color(0.14f, 0.21f, 0.17f),
                            2 => new Color(0.25f, 0.31f, 0.22f),
                            _ => new Color(0.18f, 0.25f, 0.19f)
                        };

                        if (cell.CoverValue > 0.35f)
                        {
                            col = col.Darkened(0.12f);
                        }
                    }

                    if (hasExternal && !cell.IsRoad && !cell.IsBuilding)
                    {
                        Color sat = externalSatellite!.GetPixel(x, y);
                        col = col.Lerp(sat, 0.38f);
                    }

                    // Subtle 250m tactical grid overlay (every 64 pixels)
                    if (x % 64 == 0 || y % 64 == 0)
                    {
                        col = col.Lerp(new Color(0.0f, 0.83f, 1.0f), 0.12f);
                    }

                    img.SetPixel(x, y, col);
                }
            }

            return ImageTexture.CreateFromImage(img);
        }

        private static int StableHash(string text)
        {
            unchecked
            {
                int hash = 23;
                for (int i = 0; i < text.Length; i++)
                {
                    hash = hash * 31 + text[i];
                }
                return hash;
            }
        }
    }
}
