using System;
using Godot;

namespace FFM.Client.UI
{
    /// <summary>
    /// Unified Esports Broadcast &amp; Franchise Management UI Theme and Component Factory.
    /// Provides glassmorphism Bento cards, pill badges, progress bars, and broadcast typography helpers.
    /// </summary>
    public static class EsportsUiTheme
    {
        private static Theme? _cachedTheme;

        // Core Palette
        public static readonly Color BgObsidian = new(0.043f, 0.059f, 0.098f, 1.0f);       // #0B0F19
        public static readonly Color CardSurface = new(0.075f, 0.106f, 0.180f, 0.92f);     // #131B2E
        public static readonly Color CardElevated = new(0.106f, 0.149f, 0.243f, 0.95f);    // #1B263E
        public static readonly Color CardDarkInset = new(0.051f, 0.071f, 0.122f, 0.90f);   // #0D121F
        public static readonly Color BorderSubtle = new(0.20f, 0.27f, 0.39f, 0.72f);

        // Brand Accents
        public static readonly Color AccentGold = new(1.00f, 0.72f, 0.00f, 1.0f);          // #FFB800
        public static readonly Color AccentCyan = new(0.00f, 0.83f, 1.00f, 1.0f);          // #00D4FF
        public static readonly Color AccentEmerald = new(0.18f, 0.84f, 0.45f, 1.0f);       // #2ED573
        public static readonly Color AccentCrimson = new(1.00f, 0.28f, 0.34f, 1.0f);       // #FF4757
        public static readonly Color AccentOrange = new(1.00f, 0.56f, 0.14f, 1.0f);        // #FF8F24
        public static readonly Color AccentPurple = new(0.68f, 0.38f, 0.98f, 1.0f);        // #AD61FA

        // Semantic Color Aliases used across MetaScreens, TacticalMapPreview, MinimapWidget, BroadcastHud, CommandPanel
        public static readonly Color NavyBgDark = BgObsidian;
        public static readonly Color NavySurface = CardSurface;
        public static readonly Color NavySurfaceElevated = CardElevated;
        public static readonly Color NavyBorder = BorderSubtle;
        public static readonly Color BgPanel = CardSurface;
        public static readonly Color BgCard = CardElevated;
        public static readonly Color BorderHighlight = AccentCyan;

        public static readonly Color GoldPrimary = AccentGold;
        public static readonly Color GoldBright = new(1.00f, 0.84f, 0.30f, 1.0f);
        public static readonly Color CyanElectric = AccentCyan;
        public static readonly Color EmeraldSuccess = AccentEmerald;
        public static readonly Color AccentGreen = AccentEmerald;
        public static readonly Color AmberWarning = AccentOrange;
        public static readonly Color AccentAmber = AccentOrange;
        public static readonly Color CrimsonDanger = AccentCrimson;

        // Text Hierarchy
        public static readonly Color TextPrimary = new(0.95f, 0.97f, 1.00f, 1.0f);
        public static readonly Color TextSecondary = new(0.74f, 0.80f, 0.90f, 1.0f);
        public static readonly Color TextMuted = new(0.50f, 0.57f, 0.68f, 1.0f);

        public static Theme GetOrCreateTheme()
        {
            if (_cachedTheme != null) return _cachedTheme;

            var theme = new Theme();
            var tabSelected = new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.18f, 0.29f, 0.98f),
                BorderColor = AccentGold,
                BorderWidthTop = 3,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            };
            var tabUnselected = new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.09f, 0.15f, 0.88f),
                BorderColor = BorderSubtle,
                BorderWidthTop = 1,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            };
            var tabPanel = new StyleBoxFlat
            {
                BgColor = new Color(0.05f, 0.075f, 0.13f, 0.94f),
                BorderColor = BorderSubtle,
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                BorderWidthTop = 1,
                CornerRadiusBottomLeft = 8,
                CornerRadiusBottomRight = 8,
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 12,
                ContentMarginBottom = 12
            };

            theme.SetStylebox("tab_selected", "TabContainer", tabSelected);
            theme.SetStylebox("tab_hovered", "TabContainer", tabSelected);
            theme.SetStylebox("tab_unselected", "TabContainer", tabUnselected);
            theme.SetStylebox("panel", "TabContainer", tabPanel);
            theme.SetColor("font_selected_color", "TabContainer", AccentGold);
            theme.SetColor("font_unselected_color", "TabContainer", TextSecondary);

            _cachedTheme = theme;
            return _cachedTheme;
        }

        public static StyleBoxFlat CreateHeaderBarStyle()
        {
            return new StyleBoxFlat
            {
                BgColor = new Color(0.045f, 0.065f, 0.115f, 0.94f),
                BorderColor = AccentGold,
                BorderWidthBottom = 2,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 6,
                ContentMarginBottom = 6,
                ShadowColor = new Color(0f, 0f, 0f, 0.35f),
                ShadowSize = 4
            };
        }

        public static StyleBoxFlat CreateCardStyle(
            Color? bgColor = null,
            Color? borderColor = null,
            int leftAccentWidth = 0,
            int topAccentWidth = 0,
            int cornerRadius = 8,
            int marginH = 14,
            int marginV = 12)
        {
            Color bg = bgColor ?? CardSurface;
            Color border = borderColor ?? BorderSubtle;

            return new StyleBoxFlat
            {
                BgColor = bg,
                BorderColor = border,
                BorderWidthLeft = leftAccentWidth > 0 ? leftAccentWidth : 1,
                BorderWidthTop = topAccentWidth > 0 ? topAccentWidth : 1,
                BorderWidthRight = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = cornerRadius,
                CornerRadiusTopRight = cornerRadius,
                CornerRadiusBottomLeft = cornerRadius,
                CornerRadiusBottomRight = cornerRadius,
                ContentMarginLeft = marginH,
                ContentMarginRight = marginH,
                ContentMarginTop = marginV,
                ContentMarginBottom = marginV,
                ShadowColor = new Color(0f, 0f, 0f, 0.28f),
                ShadowSize = 4
            };
        }

        public static StyleBoxFlat CreateBentoCardStyle(
            Color? bgColor = null,
            Color? borderColor = null,
            int cornerRadius = 8,
            int borderTop = 1,
            int padding = 10)
        {
            return CreateCardStyle(
                bgColor: bgColor,
                borderColor: borderColor,
                leftAccentWidth: 1,
                topAccentWidth: borderTop,
                cornerRadius: cornerRadius,
                marginH: padding,
                marginV: padding);
        }

        public static PanelContainer CreateBentoCard(
            Color? accentBorder = null,
            Color? bgOverride = null,
            int minWidth = 0,
            int minHeight = 0)
        {
            Color border = accentBorder ?? BorderSubtle;
            Color bg = bgOverride ?? CardSurface;
            var panel = new PanelContainer
            {
                CustomMinimumSize = new Vector2(minWidth, minHeight)
            };
            panel.AddThemeStyleboxOverride(
                "panel",
                CreateCardStyle(bg, border, leftAccentWidth: accentBorder.HasValue ? 3 : 1));
            return panel;
        }

        public static PanelContainer CreateBentoCard(
            out VBoxContainer contentBox,
            string? title = null,
            Color? accentColor = null,
            int minWidth = 0,
            int minHeight = 0)
        {
            Color accent = accentColor ?? AccentGold;
            var panel = new PanelContainer
            {
                CustomMinimumSize = new Vector2(minWidth, minHeight),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            panel.AddThemeStyleboxOverride("panel", CreateCardStyle(CardSurface, accent, leftAccentWidth: 3));

            contentBox = new VBoxContainer
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            contentBox.AddThemeConstantOverride("separation", 8);
            panel.AddChild(contentBox);

            if (!string.IsNullOrWhiteSpace(title))
            {
                contentBox.AddChild(CreateSectionTitle(title, accent));
            }

            return panel;
        }

        public static Label CreateSectionHeader(string text, Color? accentColor = null, int fontSize = 13)
        {
            var lbl = new Label
            {
                Text = text.ToUpperInvariant(),
                Modulate = accentColor ?? AccentGold
            };
            lbl.AddThemeFontSizeOverride("font_size", fontSize);
            return lbl;
        }

        public static HBoxContainer CreateSectionTitle(string text, Color? accentColor = null, int fontSize = 14)
        {
            Color accent = accentColor ?? AccentGold;
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var pip = new ColorRect
            {
                Color = accent,
                CustomMinimumSize = new Vector2(4f, 16f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            row.AddChild(pip);

            var lbl = new Label
            {
                Text = text,
                Modulate = accent
            };
            lbl.AddThemeFontSizeOverride("font_size", fontSize);
            row.AddChild(lbl);
            return row;
        }

        public static PanelContainer CreatePillBadge(string text, Color accentColor, int fontSize = 11)
        {
            var badge = new PanelContainer
            {
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            badge.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(accentColor.R * 0.22f, accentColor.G * 0.22f, accentColor.B * 0.22f, 0.92f),
                BorderColor = new Color(accentColor.R, accentColor.G, accentColor.B, 0.85f),
                BorderWidthLeft = 1,
                BorderWidthRight = 1,
                BorderWidthTop = 1,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 7,
                ContentMarginRight = 7,
                ContentMarginTop = 2,
                ContentMarginBottom = 2
            });

            var lbl = new Label
            {
                Text = text,
                Modulate = accentColor.Lightened(0.22f),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            lbl.AddThemeFontSizeOverride("font_size", fontSize);
            badge.AddChild(lbl);
            return badge;
        }

        public static ProgressBar CreateStyledProgressBar(
            Color fillColor,
            float minVal = 0f,
            float maxVal = 100f,
            float value = 0f,
            Vector2? minSize = null)
        {
            var bar = new ProgressBar
            {
                MinValue = minVal,
                MaxValue = Math.Max(minVal + 0.01f, maxVal),
                Value = Math.Clamp(value, minVal, maxVal),
                ShowPercentage = false,
                CustomMinimumSize = minSize ?? new Vector2(120f, 10f),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };

            bar.AddThemeStyleboxOverride("background", new StyleBoxFlat
            {
                BgColor = new Color(0.04f, 0.06f, 0.10f, 0.90f),
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3
            });

            bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat
            {
                BgColor = fillColor,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3
            });

            return bar;
        }

        public static ProgressBar CreateStatBar(
            float value,
            float maxValue,
            Color fillColor,
            float width = 120f,
            float height = 10f)
        {
            return CreateStyledProgressBar(fillColor, 0f, maxValue, value, new Vector2(width, height));
        }

        public static HBoxContainer CreateStatBar(
            string labelText,
            float value,
            float maxValue = 20f,
            Color? colorOverride = null)
        {
            Color fillCol = colorOverride ?? (Math.Abs(maxValue - 20f) < 0.1f
                ? GetAttributeTierColor(value)
                : AccentCyan);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var nameLbl = new Label
            {
                Text = labelText,
                CustomMinimumSize = new Vector2(126f, 0f),
                Modulate = TextSecondary,
                ClipText = true
            };
            nameLbl.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(nameLbl);

            var bar = CreateStyledProgressBar(fillCol, 0f, maxValue, value, new Vector2(88f, 9f));
            bar.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(bar);

            var valLbl = new Label
            {
                Text = maxValue <= 20.1f ? $"{value:0.0}" : $"{value:0}",
                CustomMinimumSize = new Vector2(36f, 0f),
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = fillCol.Lightened(0.18f)
            };
            valLbl.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(valLbl);

            return row;
        }

        public static Button CreateActionButton(
            string text,
            bool isPrimaryGold = false,
            Vector2? minSize = null,
            string? tooltip = null)
        {
            return CreateStyledButton(
                text,
                accentColor: isPrimaryGold ? AccentGold : AccentCyan,
                isPrimary: isPrimaryGold,
                minSize: minSize,
                tooltip: tooltip);
        }

        public static Button CreateStyledButton(
            string text,
            bool isPrimaryGold,
            float minWidth = 110f,
            float minHeight = 32f,
            string? tooltip = null)
        {
            return CreateStyledButton(
                text,
                accentColor: isPrimaryGold ? AccentGold : AccentCyan,
                isPrimary: isPrimaryGold,
                minSize: new Vector2(minWidth, minHeight),
                tooltip: tooltip);
        }

        public static Button CreateStyledButton(
            string text,
            Color? accentColor = null,
            bool isPrimary = false,
            Vector2? minSize = null,
            string? tooltip = null)
        {
            Color accent = accentColor ?? (isPrimary ? AccentGold : AccentCyan);
            var btn = new Button
            {
                Text = text,
                CustomMinimumSize = minSize ?? new Vector2(110f, 32f),
                TooltipText = tooltip ?? string.Empty
            };

            Color normalBg = isPrimary
                ? new Color(accent.R * 0.30f, accent.G * 0.25f, 0.05f, 0.95f)
                : new Color(0.10f, 0.14f, 0.23f, 0.94f);

            var normalStyle = new StyleBoxFlat
            {
                BgColor = normalBg,
                BorderColor = new Color(accent.R, accent.G, accent.B, isPrimary ? 0.95f : 0.55f),
                BorderWidthLeft = isPrimary ? 2 : 1,
                BorderWidthRight = isPrimary ? 2 : 1,
                BorderWidthTop = isPrimary ? 2 : 1,
                BorderWidthBottom = isPrimary ? 2 : 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = 10,
                ContentMarginRight = 10,
                ContentMarginTop = 5,
                ContentMarginBottom = 5
            };

            var hoverStyle = (StyleBoxFlat)normalStyle.Duplicate();
            hoverStyle.BgColor = normalBg.Lightened(0.16f);
            hoverStyle.BorderColor = accent.Lightened(0.2f);

            var pressedStyle = (StyleBoxFlat)normalStyle.Duplicate();
            pressedStyle.BgColor = accent.Darkened(0.45f);

            var disabledStyle = (StyleBoxFlat)normalStyle.Duplicate();
            disabledStyle.BgColor = new Color(0.07f, 0.09f, 0.14f, 0.6f);
            disabledStyle.BorderColor = new Color(0.22f, 0.26f, 0.34f, 0.4f);

            btn.AddThemeStyleboxOverride("normal", normalStyle);
            btn.AddThemeStyleboxOverride("hover", hoverStyle);
            btn.AddThemeStyleboxOverride("pressed", pressedStyle);
            btn.AddThemeStyleboxOverride("disabled", disabledStyle);
            btn.AddThemeColorOverride("font_color", isPrimary ? accent.Lightened(0.3f) : TextPrimary);
            btn.AddThemeColorOverride("font_hover_color", Colors.White);

            return btn;
        }

        public static Color GetAttributeTierColor(float val20)
        {
            if (val20 >= 15.5f) return AccentGold;
            if (val20 >= 12.5f) return AccentCyan;
            if (val20 >= 9.5f) return AccentEmerald;
            return TextSecondary;
        }

        public static Color GetRoleColor(string roleName)
        {
            return roleName.ToLowerInvariant() switch
            {
                "rusher" => AccentCrimson,
                "secondrusher" => AccentOrange,
                "sniper" => AccentCyan,
                "support" => AccentEmerald,
                "grenadier" => AccentGold,
                _ => AccentCyan
            };
        }

        /// <summary>
        /// Safely anchors a Control to fill its entire parent/viewport without Godot 4's
        /// SetAnchorsPreset(FullRect, keepOffsets: false) zero-size offset recalculation pitfall.
        /// </summary>
        public static void SetFullRect(Control ctrl, float minWidth = 1920f, float minHeight = 1080f)
        {
            ctrl.AnchorLeft = 0f;
            ctrl.AnchorTop = 0f;
            ctrl.AnchorRight = 1f;
            ctrl.AnchorBottom = 1f;
            ctrl.OffsetLeft = 0f;
            ctrl.OffsetTop = 0f;
            ctrl.OffsetRight = 0f;
            ctrl.OffsetBottom = 0f;
            ctrl.GrowHorizontal = Control.GrowDirection.Both;
            ctrl.GrowVertical = Control.GrowDirection.Both;
            ctrl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            ctrl.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
            if (minWidth > 0f || minHeight > 0f)
            {
                ctrl.CustomMinimumSize = new Vector2(minWidth, minHeight);
            }
        }

        public static void SetTopWide(Control ctrl, float height)
        {
            ctrl.AnchorLeft = 0f;
            ctrl.AnchorTop = 0f;
            ctrl.AnchorRight = 1f;
            ctrl.AnchorBottom = 0f;
            ctrl.OffsetLeft = 0f;
            ctrl.OffsetTop = 0f;
            ctrl.OffsetRight = 0f;
            ctrl.OffsetBottom = height;
            ctrl.CustomMinimumSize = new Vector2(0f, height);
        }
    }
}
