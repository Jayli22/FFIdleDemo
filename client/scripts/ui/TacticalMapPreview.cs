using System;
using FFM.Client.Art;
using FFM.Sim.Data;
using FFM.Sim.Map;
using Godot;

namespace FFM.Client.UI
{
    /// <summary>
    /// Interactive 2D Bermuda-Lite Tactical Map Preview Control for the Tactical Playbook screen.
    /// Renders the satellite map texture, 14 POIs, DropStyle target zones, RotationStyle path vectors,
    /// EndgameStyle posture rings, and interactive POI hover/click inspection.
    /// </summary>
    public partial class TacticalMapPreview : Control
    {
        private MapData? _map;
        private PlaybookDef? _playbook;
        private Texture2D? _mapTexture;
        private int _selectedPoiId = -1;
        private int _hoveredPoiId = -1;

        public event Action<PoiDef>? PoiSelected;

        public TacticalMapPreview()
        {
            CustomMinimumSize = new Vector2(460f, 460f);
            MouseFilter = MouseFilterEnum.Stop;
        }

        public void SetData(MapData? map, PlaybookDef? playbook)
        {
            _map = map;
            _playbook = playbook;
            _mapTexture = VisualAssetRegistry.GetBermudaMapTexture(map);

            if (_selectedPoiId < 0 && _map != null && _map.Pois.Count > 0)
            {
                _selectedPoiId = ChooseRecommendedPoiId(_map, _playbook?.GetDropStyle() ?? DropStyle.Flex);
            }
            QueueRedraw();
        }

        public void RefreshPlaybook(PlaybookDef playbook)
        {
            _playbook = playbook;
            if (_map != null)
            {
                _selectedPoiId = ChooseRecommendedPoiId(_map, playbook.GetDropStyle());
            }
            QueueRedraw();
        }

        private static int ChooseRecommendedPoiId(MapData map, DropStyle style)
        {
            foreach (var poi in map.Pois)
            {
                if (style == DropStyle.Hot && poi.IsHotspot) return poi.PoiId;
                if (style == DropStyle.Cold && !poi.IsHotspot && IsEdgePoi(poi)) return poi.PoiId;
                if (style == DropStyle.Flex && !poi.IsHotspot && !IsEdgePoi(poi)) return poi.PoiId;
            }
            return map.Pois.Count > 0 ? map.Pois[0].PoiId : -1;
        }

        private static bool IsEdgePoi(PoiDef poi)
        {
            float dx = poi.CenterX - 1000f;
            float dy = poi.CenterY - 1000f;
            return MathF.Sqrt(dx * dx + dy * dy) > 520f;
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (_map == null) return;

            if (@event is InputEventMouseMotion mm)
            {
                int hit = FindPoiAtLocalPos(mm.Position);
                if (hit != _hoveredPoiId)
                {
                    _hoveredPoiId = hit;
                    QueueRedraw();
                }
            }
            else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            {
                int hit = FindPoiAtLocalPos(mb.Position);
                if (hit >= 0)
                {
                    _selectedPoiId = hit;
                    foreach (var p in _map.Pois)
                    {
                        if (p.PoiId == hit)
                        {
                            PoiSelected?.Invoke(p);
                            break;
                        }
                    }
                    QueueRedraw();
                }
            }
        }

        private int FindPoiAtLocalPos(Vector2 localPos)
        {
            if (_map == null) return -1;
            Rect2 mapRect = GetSquareMapRect();
            if (!mapRect.HasPoint(localPos)) return -1;

            int bestId = -1;
            float bestDist = 26f;
            foreach (var poi in _map.Pois)
            {
                Vector2 pScreen = WorldToMapLocal(poi.CenterX, poi.CenterY, mapRect);
                float d = pScreen.DistanceTo(localPos);
                if (d < bestDist)
                {
                    bestDist = d;
                    bestId = poi.PoiId;
                }
            }
            return bestId;
        }

        private Rect2 GetSquareMapRect()
        {
            float side = MathF.Min(Size.X, Size.Y) - 8f;
            side = MathF.Max(200f, side);
            float ox = (Size.X - side) * 0.5f;
            float oy = (Size.Y - side) * 0.5f;
            return new Rect2(ox, oy, side, side);
        }

        private static Vector2 WorldToMapLocal(float worldX, float worldY, Rect2 rect)
        {
            float nx = Math.Clamp(worldX / MapData.MapSizeMeters, 0f, 1f);
            float ny = Math.Clamp(worldY / MapData.MapSizeMeters, 0f, 1f);
            return new Vector2(rect.Position.X + nx * rect.Size.X, rect.Position.Y + ny * rect.Size.Y);
        }

        public override void _Draw()
        {
            Rect2 rect = GetSquareMapRect();

            // Outer frame
            DrawRect(rect.Grow(3f), EsportsUiTheme.NavyBorder, true);
            if (_mapTexture != null)
            {
                DrawTextureRect(_mapTexture, rect, false);
            }
            else
            {
                DrawRect(rect, new Color(0.06f, 0.11f, 0.18f), true);
            }

            // Tactical grid overlay
            Color gridCol = new(0.0f, 0.83f, 1.0f, 0.08f);
            for (int i = 1; i < 8; i++)
            {
                float gx = rect.Position.X + rect.Size.X * (i / 8f);
                float gy = rect.Position.Y + rect.Size.Y * (i / 8f);
                DrawLine(new Vector2(gx, rect.Position.Y), new Vector2(gx, rect.End.Y), gridCol, 1f);
                DrawLine(new Vector2(rect.Position.X, gy), new Vector2(rect.End.X, gy), gridCol, 1f);
            }

            if (_map == null) return;

            DropStyle dropStyle = _playbook?.GetDropStyle() ?? DropStyle.Flex;
            RotationStyle rotStyle = _playbook?.GetRotationStyle() ?? RotationStyle.Early;
            EndgameStyle endgame = _playbook?.GetEndgameStyle() ?? EndgameStyle.HighGround;

            Vector2 mapCenter = WorldToMapLocal(1000f, 1000f, rect);

            // 1. Draw Expected Safe Zone Stage 2 & Stage 4 Reference Rings
            float rStage2 = (540f / MapData.MapSizeMeters) * rect.Size.X;
            float rStage4 = (230f / MapData.MapSizeMeters) * rect.Size.X;
            DrawArc(mapCenter, rStage2, 0f, MathF.Tau, 64, new Color(1f, 1f, 1f, 0.25f), 1.5f);
            DrawArc(mapCenter, rStage4, 0f, MathF.Tau, 48, new Color(0f, 0.83f, 1f, 0.45f), 2.0f);

            // 2. Find Selected or Primary Drop POI for Rotation Vector Preview
            PoiDef? activePoi = null;
            foreach (var p in _map.Pois)
            {
                if (p.PoiId == _selectedPoiId)
                {
                    activePoi = p;
                    break;
                }
            }
            if (activePoi == null && _map.Pois.Count > 0)
            {
                activePoi = _map.Pois[0];
            }

            // 2b. Simulated C-130 Flight Corridor & Perpendicular Drop Launch Preview
            Vector2 flightStart = WorldToMapLocal(160f, 380f, rect);
            Vector2 flightEnd = WorldToMapLocal(1840f, 1620f, rect);
            DrawDashedLine(flightStart, flightEnd, new Color(0.20f, 0.90f, 1.0f, 0.55f), 2.0f, 7.0f);

            if (activePoi != null)
            {
                Vector2 startPos = WorldToMapLocal(activePoi.CenterX, activePoi.CenterY, rect);
                Vector2 ab = flightEnd - flightStart;
                float tProj = ab.LengthSquared() > 1f
                    ? Math.Clamp((startPos - flightStart).Dot(ab) / ab.LengthSquared(), 0.08f, 0.92f)
                    : 0.5f;
                Vector2 launchPos = flightStart + ab * tProj;
                DrawDashedLine(launchPos, startPos, new Color(1.0f, 0.82f, 0.18f, 0.85f), 2.2f, 5.0f);
                DrawCircle(launchPos, 5.0f, EsportsUiTheme.CyanElectric);

                DrawRotationPathOverlay(startPos, mapCenter, rStage2, rStage4, rotStyle, endgame);
            }

            // 3. Draw all 14 POIs with DropStyle suitability highlight
            Font font = ThemeDB.FallbackFont;
            foreach (var poi in _map.Pois)
            {
                Vector2 pPos = WorldToMapLocal(poi.CenterX, poi.CenterY, rect);
                float pRadius = Math.Max(12f, (poi.RadiusMeters / MapData.MapSizeMeters) * rect.Size.X);

                bool isRecommended = IsPoiRecommendedForDrop(poi, dropStyle);
                bool isSelected = poi.PoiId == _selectedPoiId;
                bool isHovered = poi.PoiId == _hoveredPoiId;

                Color ringCol = poi.IsHotspot
                    ? EsportsUiTheme.CrimsonDanger
                    : (isRecommended ? EsportsUiTheme.CyanElectric : EsportsUiTheme.TextSecondary);

                if (isRecommended)
                {
                    DrawCircle(pPos, pRadius, new Color(ringCol.R, ringCol.G, ringCol.B, isSelected ? 0.26f : 0.14f));
                    DrawArc(pPos, pRadius, 0f, MathF.Tau, 32, new Color(ringCol.R, ringCol.G, ringCol.B, 0.85f), isSelected ? 2.5f : 1.5f);
                }
                else
                {
                    DrawArc(pPos, pRadius, 0f, MathF.Tau, 24, new Color(0.65f, 0.72f, 0.82f, 0.30f), 1.0f);
                }

                if (isSelected || isHovered)
                {
                    DrawArc(pPos, pRadius + 4f, 0f, MathF.Tau, 32, EsportsUiTheme.GoldPrimary, 2.2f);
                }

                // Center dot
                DrawCircle(pPos, isSelected ? 5.5f : 4.0f, isSelected ? EsportsUiTheme.GoldPrimary : ringCol);

                // Label
                string labelText = poi.IsHotspot ? $"★ {poi.Name}" : poi.Name;
                Color textCol = isSelected
                    ? EsportsUiTheme.GoldBright
                    : (isRecommended ? EsportsUiTheme.TextPrimary : EsportsUiTheme.TextSecondary);
                DrawStringOutline(font, pPos + new Vector2(-labelText.Length * 3.1f, -pRadius - 4f), labelText, HorizontalAlignment.Left, -1f, 11, 4, new Color(0.03f, 0.05f, 0.09f, 0.95f));
                DrawString(font, pPos + new Vector2(-labelText.Length * 3.1f, -pRadius - 4f), labelText, HorizontalAlignment.Left, -1f, 11, textCol);
            }

            // 4. Draw Bottom Legend & Selected POI Info Banner
            DrawOverlayLegend(rect, activePoi, dropStyle, rotStyle, endgame, font);
        }

        private void DrawRotationPathOverlay(
            Vector2 startPos,
            Vector2 mapCenter,
            float rStage2,
            float rStage4,
            RotationStyle rotStyle,
            EndgameStyle endgame)
        {
            Vector2 toCenter = mapCenter - startPos;
            float dist = toCenter.Length();
            Vector2 dir = dist > 1f ? toCenter / dist : Vector2.Right;
            Vector2 perp = new(-dir.Y, dir.X);

            if (rotStyle == RotationStyle.Early)
            {
                // Direct high-speed vector straight into Zone 4 center compound
                Vector2 wp1 = startPos.Lerp(mapCenter, 0.52f);
                Vector2 wp2 = mapCenter - dir * (rStage4 * 0.35f);
                DrawTacticalArrow(startPos, wp1, EsportsUiTheme.CyanElectric, 2.6f);
                DrawTacticalArrow(wp1, wp2, EsportsUiTheme.GoldPrimary, 2.6f);
            }
            else if (rotStyle == RotationStyle.Edge)
            {
                // Curved flanking path hugging Stage 2 boundary before entering Stage 4 edge
                Vector2 edgeEntry = mapCenter - dir * rStage2 + perp * (rStage2 * 0.45f);
                Vector2 edgeRotate = mapCenter - dir * (rStage4 * 0.95f) + perp * (rStage4 * 0.65f);
                DrawTacticalArrow(startPos, edgeEntry, EsportsUiTheme.EmeraldSuccess, 2.4f);
                DrawTacticalArrow(edgeEntry, edgeRotate, EsportsUiTheme.CyanElectric, 2.4f);
                DrawTacticalArrow(edgeRotate, mapCenter - dir * (rStage4 * 0.4f), EsportsUiTheme.GoldPrimary, 2.4f);
            }
            else
            {
                // Late rotation: full loot + gatekeep chokepoint on Stage 2 rim
                Vector2 chokePos = mapCenter - dir * (rStage2 * 0.85f);
                DrawTacticalArrow(startPos, chokePos, EsportsUiTheme.AmberWarning, 2.6f);
                DrawArc(chokePos, 18f, 0f, MathF.Tau, 24, EsportsUiTheme.CrimsonDanger, 2.2f);
                DrawTacticalArrow(chokePos, mapCenter - dir * (rStage4 * 0.45f), EsportsUiTheme.CrimsonDanger, 2.2f);
            }

            // Endgame posture icon at center
            Color endCol = endgame switch
            {
                EndgameStyle.HighGround => EsportsUiTheme.GoldPrimary,
                EndgameStyle.Building => EsportsUiTheme.EmeraldSuccess,
                _ => EsportsUiTheme.CyanElectric
            };
            DrawArc(mapCenter, 12f, 0f, MathF.Tau, 24, endCol, 2.2f);
        }

        private void DrawTacticalArrow(Vector2 from, Vector2 to, Color color, float width)
        {
            DrawLine(from, to, color, width, true);
            Vector2 d = (to - from);
            if (d.LengthSquared() < 16f) return;
            Vector2 dir = d.Normalized();
            Vector2 n = new(-dir.Y, dir.X);
            Vector2 p1 = to - dir * 10f + n * 5.5f;
            Vector2 p2 = to - dir * 10f - n * 5.5f;
            DrawPolygon(new[] { to, p1, p2 }, new[] { color, color, color });
        }

        private static bool IsPoiRecommendedForDrop(PoiDef poi, DropStyle style)
        {
            return style switch
            {
                DropStyle.Hot => poi.IsHotspot,
                DropStyle.Cold => !poi.IsHotspot && IsEdgePoi(poi),
                _ => !poi.IsHotspot && !IsEdgePoi(poi)
            };
        }

        private void DrawOverlayLegend(
            Rect2 rect,
            PoiDef? activePoi,
            DropStyle dropStyle,
            RotationStyle rotStyle,
            EndgameStyle endgame,
            Font font)
        {
            Rect2 hudRect = new(rect.Position.X + 8f, rect.End.Y - 64f, rect.Size.X - 16f, 56f);
            DrawRect(hudRect, new Color(0.04f, 0.06f, 0.11f, 0.88f), true);
            DrawRect(hudRect, EsportsUiTheme.NavyBorder, false, 1.2f);

            string poiDesc = activePoi != null
                ? $"焦点跳点: {activePoi.Name} ({(activePoi.IsHotspot ? "★高级热区 Hot" : "战术资源点")} · 海拔 L{activePoi.HeightLevel})"
                : "点击地图任意资源点查看跳点与进圈路线预览";
            string tacDesc = $"跳点策略: {dropStyle}  |  进圈节奏: {rotStyle}  |  决赛圈阵型: {endgame}";

            DrawString(font, hudRect.Position + new Vector2(10f, 22f), poiDesc, HorizontalAlignment.Left, -1f, 13, EsportsUiTheme.GoldPrimary);
            DrawString(font, hudRect.Position + new Vector2(10f, 44f), tacDesc, HorizontalAlignment.Left, -1f, 12, EsportsUiTheme.CyanElectric);
        }
    }
}
