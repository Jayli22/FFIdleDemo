using System;
using FFM.Client.Art;
using FFM.Client.Camera;
using FFM.Client.Sim;
using FFM.Client.View;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.UI
{
    /// <summary>
    /// Top-Right Interactive 2D Bermuda Radar Minimap Widget for the Live Broadcast HUD.
    /// Renders the satellite map texture, Safe Zone &amp; Next Zone rings, 48 unit markers,
    /// squad macro rotation vectors, active combat ripples, camera focus reticle,
    /// and supports click/drag-to-pan 3D camera control.
    /// </summary>
    public partial class MinimapWidget : Control
    {
        private MapData? _map;
        private MatchConfig? _matchCfg;
        private InterpolatedFrameState _frame = new();
        private bool _hasFrame;
        private int _playerSquadIdx;
        private int _focusedSquadIdx;
        private Vector3 _cameraFocusWorld = new(1000f, 0f, 1000f);
        private float _pulsePhase;
        private bool _isDraggingMap;
        private bool _hasPinnedTarget;
        private Vec2 _pinnedTargetWorld;

        public event Action<Vector3>? WorldPointClicked;
        public event Action<int>? SquadMarkerClicked;
        public event Action<Vec2>? TacticalTargetPinned;
        public event Action<int>? DropPoiClicked;

        public bool HasPinnedTarget => _hasPinnedTarget;
        public Vec2 PinnedTargetWorld => _pinnedTargetWorld;

        public void SetPinnedTarget(Vec2 worldPos)
        {
            _pinnedTargetWorld = worldPos;
            _hasPinnedTarget = true;
            QueueRedraw();
        }

        public void ClearPinnedTarget()
        {
            _hasPinnedTarget = false;
            QueueRedraw();
        }

        public override void _Ready()
        {
            CustomMinimumSize = new Vector2(228f, 228f);
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
        }

        public void Bind(MapData? map, MatchConfig? matchCfg, int playerSquadIdx)
        {
            _map = map;
            _matchCfg = matchCfg;
            _playerSquadIdx = playerSquadIdx;
            _hasPinnedTarget = false;
            QueueRedraw();
        }

        public void UpdateMinimap(
            InterpolatedFrameState frame,
            int focusedSquadIdx,
            Vector3 cameraFocusWorld,
            float dt)
        {
            _frame = frame;
            _hasFrame = true;
            _focusedSquadIdx = focusedSquadIdx;
            _cameraFocusWorld = cameraFocusWorld;
            _pulsePhase = (_pulsePhase + dt * 4.2f) % (MathF.PI * 2f);
            QueueRedraw();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb)
            {
                if (mb.ButtonIndex == MouseButton.Left)
                {
                    _isDraggingMap = mb.Pressed;
                    if (mb.Pressed)
                    {
                        HandleMinimapClick(mb.Position, allowSquadSnap: true);
                    }
                }
                else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
                {
                    HandleTacticalPinClick(mb.Position);
                }
            }
            else if (@event is InputEventMouseMotion mm && _isDraggingMap)
            {
                HandleMinimapClick(mm.Position, allowSquadSnap: false);
            }
        }

        private void HandleTacticalPinClick(Vector2 localPos)
        {
            Vector2 size = Size;
            if (size.X <= 1f || size.Y <= 1f) return;

            // During Phase.Drop, right-clicking a POI overrides the player squad's drop target POI
            if (_hasFrame && _frame.Phase == Phase.Drop && _map != null && _map.Pois != null)
            {
                int bestPoi = -1;
                float bestDist = 18f;
                for (int i = 0; i < _map.Pois.Count; i++)
                {
                    var poi = _map.Pois[i];
                    Vector2 p = WorldToMap(poi.CenterX, poi.CenterY, size);
                    float d = p.DistanceTo(localPos);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestPoi = poi.PoiId;
                    }
                }
                if (bestPoi >= 0)
                {
                    DropPoiClicked?.Invoke(bestPoi);
                    QueueRedraw();
                    return;
                }
            }

            float normX = Math.Clamp(localPos.X / size.X, 0f, 1f);
            float normY = Math.Clamp(localPos.Y / size.Y, 0f, 1f);
            _pinnedTargetWorld = new Vec2(normX * MapData.MapSizeMeters, normY * MapData.MapSizeMeters);
            _hasPinnedTarget = true;
            TacticalTargetPinned?.Invoke(_pinnedTargetWorld);
            QueueRedraw();
        }

        private void HandleMinimapClick(Vector2 localPos, bool allowSquadSnap)
        {
            Vector2 size = Size;
            if (size.X <= 1f || size.Y <= 1f) return;

            float normX = Math.Clamp(localPos.X / size.X, 0f, 1f);
            float normY = Math.Clamp(localPos.Y / size.Y, 0f, 1f);
            float worldX = normX * MapData.MapSizeMeters;
            float worldZ = normY * MapData.MapSizeMeters;

            if (allowSquadSnap && _hasFrame && _frame.Units != null)
            {
                // If user clicked directly on a squad cluster (within 9px), snap to following that squad
                int bestSquad = -1;
                float bestDistSq = 9.5f * 9.5f;
                for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
                {
                    if (_frame.Squads[s].AliveCount <= 0) continue;
                    if (CameraRig.TryComputeSquadCentroid(_frame, s, out Vector3 centroid))
                    {
                        Vector2 sqScreen = WorldToMap(centroid.X, centroid.Z, size);
                        float dSq = sqScreen.DistanceSquaredTo(localPos);
                        if (dSq < bestDistSq)
                        {
                            bestDistSq = dSq;
                            bestSquad = s;
                        }
                    }
                }

                if (bestSquad >= 0)
                {
                    SquadMarkerClicked?.Invoke(bestSquad);
                    return;
                }
            }

            WorldPointClicked?.Invoke(new Vector3(worldX, 0f, worldZ));
        }

        public override void _Draw()
        {
            Vector2 size = Size;
            if (size.X <= 4f || size.Y <= 4f) return;

            Rect2 fullRect = new(Vector2.Zero, size);

            // 1. Satellite Map Background
            Texture2D mapTex = VisualAssetRegistry.GetBermudaMapTexture();
            DrawTextureRect(mapTex, fullRect, false);

            // Slight dark radar contrast glaze
            DrawRect(fullRect, new Color(0.03f, 0.05f, 0.09f, 0.22f), true);

            // 2. Tactical 4x4 Grid Lines
            Color gridCol = new(1.0f, 1.0f, 1.0f, 0.08f);
            for (int i = 1; i < 4; i++)
            {
                float t = i / 4f;
                DrawLine(new Vector2(size.X * t, 0f), new Vector2(size.X * t, size.Y), gridCol, 1f);
                DrawLine(new Vector2(0f, size.Y * t), new Vector2(size.X, size.Y * t), gridCol, 1f);
            }

            // 3. Subtle POI Dots & Short Labels
            if (_map != null && _map.Pois != null)
            {
                Font font = ThemeDB.FallbackFont;
                for (int i = 0; i < _map.Pois.Count; i++)
                {
                    var poi = _map.Pois[i];
                    Vector2 p = WorldToMap(poi.CenterX, poi.CenterY, size);
                    DrawCircle(p, 2.0f, new Color(0.85f, 0.90f, 1.0f, 0.40f));
                    if ((poi.IsHotspot || poi.LootTierWeight >= 1.15f) && font != null)
                    {
                        string shortName = poi.Name.Length > 8 ? poi.Name[..8] : poi.Name;
                        DrawString(
                            font,
                            p + new Vector2(-18f, -4f),
                            shortName.ToUpperInvariant(),
                            HorizontalAlignment.Left,
                            -1f,
                            8,
                            new Color(0.88f, 0.93f, 1.0f, 0.52f));
                    }
                }
            }

            if (!_hasFrame || _frame.Units == null || _frame.Squads == null)
            {
                DrawRect(fullRect, EsportsUiTheme.BorderSubtle, false, 1.5f);
                return;
            }

            float mapScale = size.X / MapData.MapSizeMeters;

            // 3b. Compound Building Footprints & Live Garrison / Breach Outlines
            DrawCompoundGarrisonFootprints(size);

            // 4. Next Safe Zone (dashed white/gold preview circle)
            if (_frame.NextZoneRadius > 5f && _frame.NextZoneRadius < 1900f)
            {
                Vector2 nextCenter = WorldToMap(_frame.NextZoneCenter3D.X, _frame.NextZoneCenter3D.Z, size);
                float nextRadPx = _frame.NextZoneRadius * mapScale;
                DrawDashedCircle(nextCenter, nextRadPx, new Color(1.0f, 0.88f, 0.45f, 0.65f), 1.3f);
            }

            // 5. Current Safe Zone Ring
            if (_frame.ZoneRadius > 5f && _frame.ZoneRadius < 1950f)
            {
                Vector2 curCenter = WorldToMap(_frame.ZoneCenter3D.X, _frame.ZoneCenter3D.Z, size);
                float curRadPx = _frame.ZoneRadius * mapScale;
                Color zoneCol = _frame.ZoneIsShrinking
                    ? new Color(1.0f, 0.35f, 0.42f, 0.92f)
                    : new Color(0.0f, 0.84f, 1.0f, 0.88f);
                DrawArc(curCenter, curRadPx, 0f, MathF.Tau, 56, zoneCol, 1.8f);
            }

            // 5b. Flight Path Corridor & Transport Aircraft Icon during Drop / Early Loot
            if (_frame.FlightPath.IsActive || _frame.Phase == Phase.Drop)
            {
                Vector2 fpStart = WorldToMap(_frame.FlightPath.StartPos.X, _frame.FlightPath.StartPos.Y, size);
                Vector2 fpEnd = WorldToMap(_frame.FlightPath.EndPos.X, _frame.FlightPath.EndPos.Y, size);
                if (fpStart.DistanceSquaredTo(fpEnd) > 16f)
                {
                    DrawDashedLine(fpStart, fpEnd, new Color(0.20f, 0.90f, 1.0f, 0.72f), 1.8f, 6.0f);
                }

                // Player squad drop vector from launch point to target POI
                if ((uint)_playerSquadIdx < SnapshotBuffer.MaxSquads && _map?.Pois != null)
                {
                    ref readonly SquadSnapshot pSq = ref _frame.Squads[_playerSquadIdx];
                    if (pSq.DropPoiId >= 0 && pSq.DropPoiId < _map.Pois.Count)
                    {
                        var tPoi = _map.Pois[pSq.DropPoiId];
                        Vector2 launchPt = WorldToMap(pSq.DropLaunchPos.X, pSq.DropLaunchPos.Y, size);
                        Vector2 targetPt = WorldToMap(tPoi.CenterX, tPoi.CenterY, size);
                        DrawDashedLine(launchPt, targetPt, new Color(1.0f, 0.82f, 0.18f, 0.85f), 1.6f, 4.0f);
                        DrawArc(targetPt, 6.5f, 0f, MathF.Tau, 18, EsportsUiTheme.AccentGold, 1.8f);
                    }
                }

                if (_frame.FlightPath.IsActive)
                {
                    Vector2 planePt = WorldToMap(_frame.FlightPath.PlaneCurrentPos.X, _frame.FlightPath.PlaneCurrentPos.Y, size);
                    Vec2 fDir = _frame.FlightPath.PlaneForwardDir;
                    Vector2 dir = fDir.LengthSquared > 0.001f
                        ? new Vector2(fDir.X, fDir.Y).Normalized()
                        : Vector2.Right;
                    Vector2 perp = new(-dir.Y, dir.X);
                    Vector2 nose = planePt + dir * 7.5f;
                    Vector2 wingL = planePt - dir * 2.5f + perp * 6.5f;
                    Vector2 wingR = planePt - dir * 2.5f - perp * 6.5f;
                    Vector2 tail = planePt - dir * 6.0f;
                    Color planeCol = new(0.25f, 0.95f, 1.0f, 0.96f);
                    DrawPolygon(new[] { nose, wingL, tail, wingR }, new[] { planeCol, planeCol, planeCol, planeCol });
                    DrawArc(planePt, 8.5f, 0f, MathF.Tau, 18, new Color(1.0f, 0.85f, 0.25f, 0.65f), 1.2f);
                }
            }

            // 6. Focused & Player Squad Macro Goal Vector
            DrawSquadMacroGoalVector(_playerSquadIdx, size, new Color(1.0f, 0.72f, 0.0f, 0.72f));
            if (_focusedSquadIdx != _playerSquadIdx && (uint)_focusedSquadIdx < SnapshotBuffer.MaxSquads)
            {
                DrawSquadMacroGoalVector(_focusedSquadIdx, size, new Color(0.0f, 0.84f, 1.0f, 0.65f));
            }

            // 7. Active Gloo Walls (tiny cyan shields)
            if (_frame.GlooWalls != null)
            {
                for (int g = 0; g < _frame.ActiveGlooWallCount; g++)
                {
                    ref readonly GlooWallSnapshot gw = ref _frame.GlooWalls[g];
                    Vector2 gp = WorldToMap(gw.Position.X, gw.Position.Y, size);
                    DrawRect(new Rect2(gp.X - 2f, gp.Y - 1f, 4f, 2f), new Color(0.0f, 0.92f, 1.0f, 0.85f), true);
                }
            }

            // 8. All 48 Unit Dots + Combat Ripples
            float pulse = 0.5f + 0.5f * MathF.Sin(_pulsePhase);
            for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
            {
                ref readonly InterpolatedUnitState unit = ref _frame.Units[u];
                if (unit.State == UnitState.Eliminated || unit.DropState == UnitDropState.InPlane) continue;

                int sqIdx = unit.SquadId;
                Vector2 up = WorldToMap(unit.WorldPos3D.X, unit.WorldPos3D.Z, size);
                Color sqCol = GetSquadColor(sqIdx);

                // Combat ripple when actively fighting
                if (unit.CurrentBehavior == Behavior.Fire && unit.State == UnitState.Alive)
                {
                    float rippleR = 4.5f + pulse * 4.0f;
                    DrawArc(up, rippleR, 0f, MathF.Tau, 16, new Color(1.0f, 0.42f, 0.20f, 0.55f * (1f - pulse * 0.5f)), 1.2f);
                }

                // Airborne parachute/surfboard halo ring
                if (unit.DropState == UnitDropState.Freefall || unit.DropState == UnitDropState.Gliding)
                {
                    Color chuteCol = unit.DropState == UnitDropState.Gliding
                        ? new Color(0.25f, 0.95f, 1.0f, 0.88f)
                        : new Color(1.0f, 0.82f, 0.22f, 0.85f);
                    DrawArc(up, 5.2f, MathF.PI, MathF.Tau, 12, chuteCol, 1.5f);
                }

                if (unit.State == UnitState.Knocked)
                {
                    DrawCircle(up, 3.2f, new Color(0.15f, 0.08f, 0.02f, 0.9f));
                    DrawArc(up, 3.2f, 0f, MathF.Tau, 12, EsportsUiTheme.AccentAmber, 1.3f);
                }
                else
                {
                    bool isFocusOrPlayer = (sqIdx == _focusedSquadIdx || sqIdx == _playerSquadIdx);
                    float radius = isFocusOrPlayer ? 3.8f : 2.7f;

                    DrawCircle(up, radius + 1.0f, new Color(0.02f, 0.04f, 0.08f, 0.92f));
                    DrawCircle(up, radius, sqCol);

                    if (sqIdx == _playerSquadIdx)
                    {
                        DrawArc(up, radius + 1.6f, 0f, MathF.Tau, 14, EsportsUiTheme.AccentGold, 1.1f);
                    }
                    else if (sqIdx == _focusedSquadIdx)
                    {
                        DrawArc(up, radius + 1.6f, 0f, MathF.Tau, 14, EsportsUiTheme.AccentCyan, 1.1f);
                    }
                }
            }

            // 9. Squad Short Tag Badges at Centroid of Focused & Player Squads
            Font tagFont = ThemeDB.FallbackFont;
            if (tagFont != null)
            {
                for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
                {
                    if (_frame.Squads[s].AliveCount <= 0) continue;
                    bool highlight = (s == _focusedSquadIdx || s == _playerSquadIdx);
                    if (!highlight && _frame.AliveSquads > 6) continue;

                    if (CameraRig.TryComputeSquadCentroid(_frame, s, out Vector3 centroid))
                    {
                        Vector2 cp = WorldToMap(centroid.X, centroid.Z, size);
                        string tag = GetSquadShortTag(s);
                        Color col = s == _playerSquadIdx
                            ? EsportsUiTheme.AccentGold
                            : (s == _focusedSquadIdx ? EsportsUiTheme.AccentCyan : GetSquadColor(s).Lightened(0.25f));
                        DrawString(
                            tagFont,
                            cp + new Vector2(-11f, -6f),
                            tag,
                            HorizontalAlignment.Left,
                            -1f,
                            9,
                            col);
                    }
                }
            }

            // 9b. Coach Tactical Pin Reticle (Right-Click Radar Target)
            if (_hasPinnedTarget)
            {
                Vector2 pinPt = WorldToMap(_pinnedTargetWorld.X, _pinnedTargetWorld.Y, size);
                float pinPulse = 5.5f + MathF.Sin(_pulsePhase * 1.6f) * 2.2f;
                Color pinCol = EsportsUiTheme.AccentGold;
                DrawArc(pinPt, pinPulse, 0f, MathF.Tau, 20, pinCol, 1.8f);
                DrawLine(pinPt + new Vector2(-8f, 0f), pinPt + new Vector2(8f, 0f), pinCol, 1.4f);
                DrawLine(pinPt + new Vector2(0f, -8f), pinPt + new Vector2(0f, 8f), pinCol, 1.4f);
            }

            // 10. Camera Focus Reticle Bracket
            Vector2 camPt = WorldToMap(_cameraFocusWorld.X, _cameraFocusWorld.Z, size);
            DrawCameraReticle(camPt, size);

            // Outer Border
            DrawRect(fullRect, EsportsUiTheme.BorderHighlight, false, 1.5f);
        }

        private void DrawCompoundGarrisonFootprints(Vector2 size)
        {
            if (_map == null || _map.Buildings == null || _frame.Units == null) return;

            for (int b = 0; b < _map.Buildings.Count; b++)
            {
                var bld = _map.Buildings[b];
                float minWorldX = bld.MinCellX * MapData.FineCellMeters;
                float minWorldZ = bld.MinCellY * MapData.FineCellMeters;
                float maxWorldX = (bld.MinCellX + bld.WidthCells) * MapData.FineCellMeters;
                float maxWorldZ = (bld.MinCellY + bld.HeightCells) * MapData.FineCellMeters;

                Vector2 p0 = WorldToMap(minWorldX, minWorldZ, size);
                Vector2 p1 = WorldToMap(maxWorldX, maxWorldZ, size);
                Rect2 rect = new(p0, new Vector2(Math.Max(3.5f, p1.X - p0.X), Math.Max(3.0f, p1.Y - p0.Y)));

                int occupyingSquad = -1;
                int unitCount = 0;
                bool multiTeamBreach = false;

                for (int u = 0; u < SnapshotBuffer.MaxUnits; u++)
                {
                    ref readonly InterpolatedUnitState st = ref _frame.Units[u];
                    if (st.State == UnitState.Eliminated) continue;
                    if (st.WorldPos3D.X >= minWorldX - 1.5f && st.WorldPos3D.X <= maxWorldX + 1.5f &&
                        st.WorldPos3D.Z >= minWorldZ - 1.5f && st.WorldPos3D.Z <= maxWorldZ + 1.5f)
                    {
                        unitCount++;
                        if (occupyingSquad < 0) occupyingSquad = st.SquadId;
                        else if (occupyingSquad != st.SquadId) multiTeamBreach = true;
                    }
                }

                if (unitCount > 0)
                {
                    Color garCol = multiTeamBreach
                        ? new Color(1.0f, 0.25f, 0.30f, 0.92f)
                        : GetSquadColor(occupyingSquad) with { A = 0.88f };
                    DrawRect(rect.Grow(1.2f), garCol with { A = 0.24f }, true);
                    DrawRect(rect.Grow(1.2f), garCol, false, 1.4f);
                }
                else
                {
                    DrawRect(rect, new Color(0.55f, 0.68f, 0.85f, 0.26f), false, 0.9f);
                }
            }
        }

        private void DrawSquadMacroGoalVector(int squadIdx, Vector2 size, Color color)
        {
            if ((uint)squadIdx >= SnapshotBuffer.MaxSquads) return;
            SquadSnapshot sq = _frame.Squads[squadIdx];
            if (sq.AliveCount <= 0) return;
            if (sq.MacroGoal.X <= 1f && sq.MacroGoal.Y <= 1f) return;

            if (!CameraRig.TryComputeSquadCentroid(_frame, squadIdx, out Vector3 centroid)) return;

            Vector2 from = WorldToMap(centroid.X, centroid.Z, size);
            Vector2 to = WorldToMap(sq.MacroGoal.X, sq.MacroGoal.Y, size);
            if (from.DistanceSquaredTo(to) < 25f) return;

            DrawDashedLine(from, to, color, 1.2f, 4.0f);
            DrawArc(to, 3.5f, 0f, MathF.Tau, 12, color, 1.2f);
        }

        private void DrawCameraReticle(Vector2 center, Vector2 mapSize)
        {
            const float half = 14f;
            const float corner = 5f;
            float x0 = Math.Clamp(center.X - half, 2f, mapSize.X - 2f);
            float y0 = Math.Clamp(center.Y - half, 2f, mapSize.Y - 2f);
            float x1 = Math.Clamp(center.X + half, 2f, mapSize.X - 2f);
            float y1 = Math.Clamp(center.Y + half, 2f, mapSize.Y - 2f);

            Color c = new(1.0f, 1.0f, 1.0f, 0.72f);
            // Top-left
            DrawLine(new Vector2(x0, y0), new Vector2(x0 + corner, y0), c, 1.4f);
            DrawLine(new Vector2(x0, y0), new Vector2(x0, y0 + corner), c, 1.4f);
            // Top-right
            DrawLine(new Vector2(x1, y0), new Vector2(x1 - corner, y0), c, 1.4f);
            DrawLine(new Vector2(x1, y0), new Vector2(x1, y0 + corner), c, 1.4f);
            // Bottom-left
            DrawLine(new Vector2(x0, y1), new Vector2(x0 + corner, y1), c, 1.4f);
            DrawLine(new Vector2(x0, y1), new Vector2(x0, y1 - corner), c, 1.4f);
            // Bottom-right
            DrawLine(new Vector2(x1, y1), new Vector2(x1 - corner, y1), c, 1.4f);
            DrawLine(new Vector2(x1, y1), new Vector2(x1, y1 - corner), c, 1.4f);
        }

        private void DrawDashedCircle(Vector2 center, float radius, Color color, float width)
        {
            const int segments = 32;
            float step = MathF.Tau / segments;
            for (int i = 0; i < segments; i += 2)
            {
                DrawArc(center, radius, i * step, (i + 1) * step, 4, color, width);
            }
        }

        private Color GetSquadColor(int squadIdx)
        {
            if (squadIdx == _playerSquadIdx) return EsportsUiTheme.AccentGold;
            if (_matchCfg != null && (uint)squadIdx < _matchCfg.Squads.Count)
            {
                var sq = _matchCfg.Squads[squadIdx];
                return BattlefieldView.GetSquadColor(squadIdx, sq.ColorHex);
            }
            return BattlefieldView.GetSquadColor(squadIdx);
        }

        private string GetSquadShortTag(int squadIdx)
        {
            if (_matchCfg != null && (uint)squadIdx < _matchCfg.Squads.Count)
            {
                return BattlefieldView.GetShortTag(_matchCfg.Squads[squadIdx].Name, squadIdx);
            }
            return $"T{squadIdx + 1:00}";
        }

        private static Vector2 WorldToMap(float worldX, float worldZ, Vector2 widgetSize)
        {
            return new Vector2(
                Math.Clamp(worldX / MapData.MapSizeMeters, 0f, 1f) * widgetSize.X,
                Math.Clamp(worldZ / MapData.MapSizeMeters, 0f, 1f) * widgetSize.Y);
        }
    }
}
