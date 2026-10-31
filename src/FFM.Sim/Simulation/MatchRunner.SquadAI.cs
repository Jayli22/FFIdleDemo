using System;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Entities;
using FFM.Sim.Map;

namespace FFM.Sim.Simulation
{
    public sealed partial class MatchRunner
    {
        private readonly int[] _poiClaimCountScratch = new int[32];

        private const float PlaneCruiseAltitudeMeters = 420f;
        private const float ParachuteOpenAltitudeMeters = 140f;
        private const float PlaneSpeedMetersPerTick = 32.0f; // 320 m/s
        private const float FreefallHorizontalSpeedPerTick = 9.5f; // 95 m/s
        private const float FreefallVerticalDescentPerTick = 11.0f; // 110 m/s
        private const float GlidingHorizontalSpeedPerTick = 4.2f; // 42 m/s
        private const float GlidingVerticalDescentPerTick = 3.8f; // 38 m/s
        private const float MaxReachableDropCorridorMeters = 900f;

        /// <summary>
        /// Deterministic transport aircraft flight path generator for a given match seed.
        /// Passes within 280m of map center (1000, 1000) across a 1960m chord.
        /// </summary>
        public static void ComputeFlightPathForSeed(ulong matchSeed, out Vec2 startPos, out Vec2 endPos)
        {
            Rng flightRng = new Rng((matchSeed == 0 ? 1UL : matchSeed) ^ 0x9E3779B97F4A7C15UL);
            float theta = flightRng.NextFloat01() * MathF.PI * 2f;
            float offset = (flightRng.NextFloat01() - 0.5f) * 520f; // [-260m, +260m]

            Vec2 center = new Vec2(1000f, 1000f);
            Vec2 dir = Vec2.FromAngle(theta);
            Vec2 normal = new Vec2(-dir.Y, dir.X);
            Vec2 anchor = center + normal * offset;

            startPos = anchor - dir * 980f;
            endPos = anchor + dir * 980f;
        }

        private static float DistancePointToLineSegment(Vec2 pt, Vec2 a, Vec2 b, out float projT, out Vec2 closestPt)
        {
            Vec2 ab = b - a;
            float lenSq = ab.LengthSquared;
            if (lenSq < 1e-4f)
            {
                projT = 0f;
                closestPt = a;
                return Vec2.Distance(pt, a);
            }
            projT = Math.Clamp(Vec2.Dot(pt - a, ab) / lenSq, 0f, 1f);
            closestPt = a + ab * projT;
            return Vec2.Distance(pt, closestPt);
        }

        private void ComputeLaunchPointAndTickForTarget(Vec2 targetPos, out Vec2 launchPos, out int launchTick)
        {
            DistancePointToLineSegment(targetPos, _flightStartPos, _flightEndPos, out float projT, out Vec2 closestPt);
            // Launch slightly before perpendicular closest point so diagonal freefall approaches the POI smoothly
            float leadMeters = Math.Min(110f, Vec2.Distance(targetPos, closestPt) * 0.22f);
            float distAlongFlight = Math.Max(15f, projT * _flightLength - leadMeters);
            launchPos = _flightStartPos + _flightDir * distAlongFlight;
            launchTick = Math.Clamp((int)MathF.Round(distAlongFlight / PlaneSpeedMetersPerTick), 1, 56);
        }

        private void AssignSquadDropTarget(int squadIndex, int poiIdx, int claimsBefore)
        {
            ref SquadRuntime sq = ref _squads[squadIndex];
            PoiDef chosenPoi = _map.Pois[poiIdx];
            sq.DropPoiId = chosenPoi.PoiId;

            float dropAngle = claimsBefore * 2.1f + squadIndex * 0.45f;
            float dropOffsetDist = claimsBefore == 0 ? 14f : 42f;
            Vec2 squadDropPos = _map.ClampToWalkableWorld(chosenPoi.Center + Vec2.FromAngle(dropAngle) * dropOffsetDist);

            ComputeLaunchPointAndTickForTarget(squadDropPos, out Vec2 launchPos, out int launchTick);
            sq.DropLaunchPos = launchPos;
            sq.DropLaunchTick = launchTick;
            sq.HasVeeredOff = false;

            sq.MacroGoal = squadDropPos;
            sq.WaypointCount = 1;
            sq.CurrentWaypointIndex = 0;
            _squadWaypoints[squadIndex, 0] = squadDropPos;

            for (int m = 0; m < UnitsPerSquad; m++)
            {
                int uId = sq.FirstUnitId + m;
                RoleDef rDef = _cfg.Roles.GetRoleDef(_units[uId].Role);
                Vec2 memberOffset = new Vec2(rDef.ForwardOffsetMeters, rDef.SideOffsetMeters);
                Vec2 unitTargetPos = _map.ClampToWalkableWorld(squadDropPos + memberOffset);

                _units[uId].LandingTargetPos = unitTargetPos;
                _units[uId].DesiredPosition = unitTargetPos;
                if (_units[uId].DropState == UnitDropState.InPlane)
                {
                    _units[uId].Position = _flightCurrentPos;
                    _units[uId].AltitudeMeters = PlaneCruiseAltitudeMeters;
                    _units[uId].FacingRadians = _flightFacingRadians;
                }
            }
        }

        private void ExecuteDropPhaseSelection()
        {
            ComputeFlightPathForSeed(_matchCfg.Seed, out _flightStartPos, out _flightEndPos);
            Vec2 flightVec = _flightEndPos - _flightStartPos;
            _flightLength = Math.Max(1f, flightVec.Length);
            _flightDir = flightVec * (1f / _flightLength);
            _flightFacingRadians = MathF.Atan2(_flightDir.Y, _flightDir.X);
            _flightActive = true;
            _flightCurrentPos = _flightStartPos;
            _flightAltitudeMeters = PlaneCruiseAltitudeMeters;

            Array.Clear(_poiClaimCountScratch, 0, _poiClaimCountScratch.Length);
            int poiCount = _map.Pois.Count;

            for (int s = 0; s < SquadCount; s++)
            {
                ref SquadRuntime sq = ref _squads[s];
                float iglDecision = _units[sq.IglUnitId].Eff.Decision;
                float noiseStd = Math.Max(0.15f, (20f - iglDecision) / _cfg.SquadAi.DecisionNoiseDivisor);

                int bestPoiIdx = 0;
                float bestScore = float.MinValue;

                for (int p = 0; p < poiCount; p++)
                {
                    PoiDef poi = _map.Pois[p];
                    float dPerp = DistancePointToLineSegment(poi.Center, _flightStartPos, _flightEndPos, out _, out _);
                    if (dPerp > MaxReachableDropCorridorMeters)
                    {
                        continue; // Outside reachable glide corridor
                    }

                    float lootVal = poi.LootTierWeight * 4.0f;
                    float hotspotFactor = poi.IsHotspot ? sq.HotspotWeightMod : (2.0f - sq.HotspotWeightMod * 0.5f);
                    float glidePenalty = (dPerp / 300f) * 1.15f;

                    int existingClaims = _poiClaimCountScratch[p];
                    float contestPenalty = existingClaims * (3.2f / Math.Max(0.5f, sq.AggressionMod)) * (0.6f + iglDecision * 0.03f);
                    float score = lootVal * hotspotFactor - glidePenalty - contestPenalty + _rng.NextGaussian(0f, noiseStd);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPoiIdx = p;
                    }
                }

                int claimsBefore = _poiClaimCountScratch[bestPoiIdx]++;
                AssignSquadDropTarget(s, bestPoiIdx, claimsBefore);

                _events.Record(new SimEvent(_currentTick, SimEventType.DropSelected, squadIdx: s, pos: sq.MacroGoal, extraCode: sq.DropPoiId));
            }
        }

        public bool OverridePlayerDropPoi(int squadIndex, int poiId)
        {
            if (squadIndex < 0 || squadIndex >= SquadCount) return false;
            if (_currentPhase != Phase.Drop) return false;

            int poiIdx = -1;
            for (int i = 0; i < _map.Pois.Count; i++)
            {
                if (_map.Pois[i].PoiId == poiId)
                {
                    poiIdx = i;
                    break;
                }
            }
            if (poiIdx < 0) return false;

            ref SquadRuntime sq = ref _squads[squadIndex];
            // Only allow override while squad is still in the aircraft or in early high-altitude freefall (>220m)
            ref readonly UnitRuntime igl = ref _units[sq.IglUnitId];
            if (igl.DropState == UnitDropState.Grounded || igl.DropState == UnitDropState.Gliding || igl.AltitudeMeters < 220f)
            {
                return false;
            }

            AssignSquadDropTarget(squadIndex, poiIdx, 0);
            // If squad has Not yet jumped and plane already passed the new launch tick, jump immediately on next tick
            if (igl.DropState == UnitDropState.InPlane && sq.DropLaunchTick <= _currentTick)
            {
                sq.DropLaunchTick = _currentTick + 1;
            }

            _events.Record(new SimEvent(_currentTick, SimEventType.DropSelected, squadIdx: squadIndex, pos: sq.MacroGoal, extraCode: poiId));
            return true;
        }

        private void StepDropPhaseAndParachutes()
        {
            int maxPlaneTicks = (int)MathF.Ceiling(_flightLength / PlaneSpeedMetersPerTick) + 4;
            if (_currentTick <= maxPlaneTicks)
            {
                _flightActive = true;
                float dist = Math.Min(_flightLength, _currentTick * PlaneSpeedMetersPerTick);
                _flightCurrentPos = _flightStartPos + _flightDir * dist;
                _flightAltitudeMeters = PlaneCruiseAltitudeMeters;
            }
            else
            {
                _flightActive = false;
            }

            for (int s = 0; s < SquadCount; s++)
            {
                ref SquadRuntime sq = ref _squads[s];
                bool squadJumpedThisTick = false;
                bool squadOpenedChuteThisTick = false;
                bool squadLandedThisTick = false;

                for (int m = 0; m < UnitsPerSquad; m++)
                {
                    int uId = sq.FirstUnitId + m;
                    ref UnitRuntime u = ref _units[uId];
                    if (u.State == UnitState.Eliminated || u.DropState == UnitDropState.Grounded)
                    {
                        continue;
                    }

                    float speedMult = Math.Max(1.0f, u.DropSpeedMultiplier);

                    if (u.DropState == UnitDropState.InPlane)
                    {
                        u.Position = _flightCurrentPos;
                        u.AltitudeMeters = PlaneCruiseAltitudeMeters;
                        u.FacingRadians = _flightFacingRadians;

                        if (_currentTick >= sq.DropLaunchTick)
                        {
                            u.DropState = UnitDropState.Freefall;
                            Vec2 toTarget = u.LandingTargetPos - u.Position;
                            if (toTarget.LengthSquared > 0.1f)
                            {
                                u.FacingRadians = MathF.Atan2(toTarget.Y, toTarget.X);
                            }
                            squadJumpedThisTick = true;
                        }
                    }
                    else if (u.DropState == UnitDropState.Freefall)
                    {
                        Vec2 toTarget = u.LandingTargetPos - u.Position;
                        if (toTarget.LengthSquared > 0.04f)
                        {
                            u.FacingRadians = MathF.Atan2(toTarget.Y, toTarget.X);
                            u.Position = Vec2.MoveTowards(u.Position, u.LandingTargetPos, FreefallHorizontalSpeedPerTick * speedMult);
                        }

                        u.AltitudeMeters = Math.Max(ParachuteOpenAltitudeMeters, u.AltitudeMeters - FreefallVerticalDescentPerTick * speedMult);
                        if (u.AltitudeMeters <= ParachuteOpenAltitudeMeters + 0.01f)
                        {
                            u.DropState = UnitDropState.Gliding;
                            squadOpenedChuteThisTick = true;
                        }
                    }
                    else if (u.DropState == UnitDropState.Gliding)
                    {
                        Vec2 toTarget = u.LandingTargetPos - u.Position;
                        if (toTarget.LengthSquared > 0.04f)
                        {
                            u.FacingRadians = MathF.Atan2(toTarget.Y, toTarget.X);
                            u.Position = Vec2.MoveTowards(u.Position, u.LandingTargetPos, GlidingHorizontalSpeedPerTick * speedMult);
                        }

                        u.AltitudeMeters = Math.Max(0f, u.AltitudeMeters - GlidingVerticalDescentPerTick * speedMult);
                        if (u.AltitudeMeters <= 0.01f)
                        {
                            u.AltitudeMeters = 0f;
                            u.DropState = UnitDropState.Grounded;
                            u.Position = _map.ClampToWalkableWorld(u.LandingTargetPos);
                            u.DesiredPosition = u.Position;
                            u.HeightLevel = _map.GetCellAtWorld(u.Position).HeightLevel;
                            squadLandedThisTick = true;
                        }
                    }
                }

                if (squadJumpedThisTick)
                {
                    _events.Record(new SimEvent(_currentTick, SimEventType.DropLaunched, actorUnitIdx: sq.IglUnitId, squadIdx: s, pos: _flightCurrentPos, pos2: sq.MacroGoal, extraCode: sq.DropPoiId));
                }

                if (squadOpenedChuteThisTick)
                {
                    _events.Record(new SimEvent(_currentTick, SimEventType.ParachuteOpened, actorUnitIdx: sq.IglUnitId, squadIdx: s, pos: GetSquadCentroid(s), extraCode: sq.DropPoiId));
                    TryTriggerMidAirVeerOff(ref sq);
                }

                if (squadLandedThisTick)
                {
                    Vec2 landPos = GetSquadCentroid(s);
                    sq.MacroGoal = landPos;
                    _squadWaypoints[s, 0] = landPos;
                    _events.Record(new SimEvent(_currentTick, SimEventType.Landed, actorUnitIdx: sq.IglUnitId, squadIdx: s, pos: landPos, extraCode: sq.DropPoiId));
                }
            }
        }

        /// <summary>
        /// Mid-air Scouting & Veer-Off ("空中侦察偏航") when parachute opens at 140m:
        /// Cautious squads that detect >=2 enemy squads descending lower onto the same POI veer off to an uncontested peripheral compound.
        /// </summary>
        private void TryTriggerMidAirVeerOff(ref SquadRuntime sq)
        {
            if (sq.HasVeeredOff || sq.AggressionMod >= 1.05f) return;

            ref readonly UnitRuntime igl = ref _units[sq.IglUnitId];
            int rivalSquadsLowerOnSamePoi = 0;
            for (int otherS = 0; otherS < SquadCount; otherS++)
            {
                if (otherS == sq.SquadId) continue;
                if (_squads[otherS].DropPoiId != sq.DropPoiId) continue;

                ref readonly UnitRuntime otherIgl = ref _units[_squads[otherS].IglUnitId];
                if (otherIgl.AltitudeMeters <= igl.AltitudeMeters + 15f)
                {
                    rivalSquadsLowerOnSamePoi++;
                }
            }

            if (rivalSquadsLowerOnSamePoi < 2) return;

            float veerProb = Math.Clamp(0.28f + (igl.Eff.Awareness + igl.Eff.Decision) * 0.022f, 0.25f, 0.88f);
            if (!_rng.Chance(veerProb)) return;

            sq.HasVeeredOff = true;
            Vec2 awayDir = (GetSquadCentroid(sq.SquadId) - sq.MacroGoal).Normalized();
            if (awayDir.LengthSquared < 0.01f)
            {
                awayDir = Vec2.FromAngle(sq.SquadId * 1.15f);
            }

            Vec2 veerGoal = _map.ClampToWalkableWorld(sq.MacroGoal + awayDir * 95f);
            sq.MacroGoal = veerGoal;
            _squadWaypoints[sq.SquadId, 0] = veerGoal;

            for (int m = 0; m < UnitsPerSquad; m++)
            {
                int uId = sq.FirstUnitId + m;
                RoleDef rDef = _cfg.Roles.GetRoleDef(_units[uId].Role);
                Vec2 memberOffset = new Vec2(rDef.ForwardOffsetMeters, rDef.SideOffsetMeters);
                _units[uId].LandingTargetPos = _map.ClampToWalkableWorld(veerGoal + memberOffset);
                _units[uId].DesiredPosition = _units[uId].LandingTargetPos;
            }

            _events.Record(new SimEvent(_currentTick, SimEventType.DropVeerOff, actorUnitIdx: sq.IglUnitId, squadIdx: sq.SquadId, pos: veerGoal, extraCode: sq.DropPoiId));
        }

        private void StepSquadAi()
        {
            var aiCfg = _cfg.SquadAi;

            for (int s = 0; s < SquadCount; s++)
            {
                ref SquadRuntime sq = ref _squads[s];
                if (sq.AliveCount <= 0) continue;

                // Commit any pending rotation goal after Comms reaction delay expires
                if (sq.PendingRotateTargetReadyTick >= 0 && _currentTick >= sq.PendingRotateTargetReadyTick)
                {
                    sq.PendingRotateTargetReadyTick = -1;
                    SetSquadMacroGoalAndPath(ref sq, sq.PendingMacroGoal);
                    _events.Record(new SimEvent(_currentTick, SimEventType.RotationStarted, squadIdx: s, pos: sq.MacroGoal));
                }

                if (_currentTick < sq.NextAiEvalTick) continue;
                sq.NextAiEvalTick = _currentTick + aiCfg.EvalIntervalTicks;

                Vec2 centroid = GetSquadCentroid(s);
                float iglDecision = GetActiveIglDecision(ref sq);
                float avgComms = GetSquadAverageComms(ref sq);

                // If an active compliant Coach Command overrides macro goal
                if (sq.HasActiveCommand && sq.ActiveCommandCompliant && _currentTick < sq.ActiveCommandExpireTick)
                {
                    if (sq.ActiveCommand.Type == CommandType.HoldPosition)
                    {
                        continue;
                    }
                    if (sq.ActiveCommand.Type == CommandType.RotateNow &&
                        Vec2.DistanceSquared(centroid, sq.MacroGoal) > 18f * 18f)
                    {
                        continue;
                    }
                    if (sq.ActiveCommand.Type == CommandType.ContestTarget)
                    {
                        if (TryFindNearestEnemySquadCentroid(s, centroid, 220f, out Vec2 enemyPos))
                        {
                            Vec2 engageGoal = _map.ClampToWalkableWorld(Vec2.MoveTowards(enemyPos, centroid, 24f));
                            if (Vec2.DistanceSquared(engageGoal, sq.MacroGoal) > 16f * 16f)
                            {
                                SetSquadMacroGoalAndPath(ref sq, engageGoal);
                            }
                            continue;
                        }
                        if (Vec2.DistanceSquared(centroid, sq.MacroGoal) > 18f * 18f)
                        {
                            continue;
                        }
                    }
                }

                // Determine if squad should rotate toward Next Safe Circle or converge in late game
                float leadSec = aiCfg.EarlyRotateLeadBaseSec + sq.EarlyRotateSecOffset + (iglDecision - 10f) * aiCfg.DecisionRotateBonusSecPerPoint;
                bool needZoneRotate = _zone.IsShrinking
                    || _zone.SecondsUntilShrink <= leadSec
                    || Vec2.Distance(centroid, _zone.NextCenter) > _zone.NextRadius * 0.92f;

                if (_currentPhase == Phase.Drop)
                {
                    continue;
                }

                if (_currentPhase == Phase.Loot && !needZoneRotate)
                {
                    // During early loot phase: contest hot-drop rivals or roam building-to-building within the POI
                    if (sq.AggressionMod >= 0.95f && TryFindNearestEnemySquadCentroid(s, centroid, 115f, out Vec2 rivalPos))
                    {
                        Vec2 contestGoal = _map.ClampToWalkableWorld(Vec2.MoveTowards(rivalPos, centroid, 26f));
                        if (Vec2.DistanceSquared(contestGoal, sq.MacroGoal) > 14f * 14f)
                        {
                            SetSquadMacroGoalAndPath(ref sq, contestGoal);
                        }
                    }
                    else
                    {
                        PoiDef dropPoi = _map.Pois[Math.Clamp(sq.DropPoiId - 1, 0, _map.Pois.Count - 1)];
                        Vec2 roamTarget = dropPoi.Center;
                        // Roam from building to building inside the drop POI
                        int targetBldOrdinal = ((_currentTick / 30) + s) % 5;
                        int matched = 0;
                        for (int b = 0; b < _map.Buildings.Count; b++)
                        {
                            var bld = _map.Buildings[b];
                            if (bld.PoiId == dropPoi.PoiId)
                            {
                                if (matched == targetBldOrdinal)
                                {
                                    roamTarget = MapData.FineCellToWorldCenter(bld.MinCellX + 1, bld.MinCellY + 1);
                                    break;
                                }
                                matched++;
                                roamTarget = MapData.FineCellToWorldCenter(bld.MinCellX + 1, bld.MinCellY + 1);
                            }
                        }
                        roamTarget = _map.ClampToWalkableWorld(roamTarget);
                        if (Vec2.DistanceSquared(roamTarget, sq.MacroGoal) > 12f * 12f)
                        {
                            SetSquadMacroGoalAndPath(ref sq, roamTarget);
                        }
                    }
                    continue;
                }

                Vec2 bestCandidate = EvaluateBestTacticalGoal(ref sq, centroid, iglDecision);

                if (Vec2.DistanceSquared(bestCandidate, sq.MacroGoal) > 25f * 25f)
                {
                    // Apply Comms delay: higher Comms => faster squad-wide rotation response
                    int commsDelay = Math.Max(0, (int)MathF.Round((18f - avgComms) * aiCfg.CommsDelayMultiplierTicks * 0.5f));
                    if (commsDelay <= 1)
                    {
                        SetSquadMacroGoalAndPath(ref sq, bestCandidate);
                        _events.Record(new SimEvent(_currentTick, SimEventType.RotationStarted, squadIdx: s, pos: bestCandidate));
                    }
                    else if (sq.PendingRotateTargetReadyTick < 0)
                    {
                        sq.PendingMacroGoal = bestCandidate;
                        sq.PendingRotateTargetReadyTick = _currentTick + commsDelay;
                    }
                }
            }
        }

        private bool TryFindNearestEnemySquadCentroid(int selfSquadId, Vec2 origin, float maxRange, out Vec2 nearestCentroid)
        {
            nearestCentroid = origin;
            float bestDistSq = maxRange * maxRange;
            bool found = false;

            for (int other = 0; other < SquadCount; other++)
            {
                if (other == selfSquadId || _squads[other].AliveCount <= 0) continue;
                Vec2 c = GetSquadCentroid(other);
                if (Vec2.Distance(c, _zone.CurrentCenter) > _zone.CurrentRadius * 0.98f) continue;

                float dSq = Vec2.DistanceSquared(origin, c);
                if (dSq < bestDistSq)
                {
                    bestDistSq = dSq;
                    nearestCentroid = c;
                    found = true;
                }
            }

            return found;
        }

        private Vec2 EvaluateBestTacticalGoal(ref SquadRuntime sq, Vec2 centroid, float iglDecision)
        {
            var aiCfg = _cfg.SquadAi;
            float noiseStd = Math.Max(0.15f, (20f - iglDecision) / aiCfg.DecisionNoiseDivisor);

            Vec2 zoneCenter = _zone.NextCenter;
            float safeRadius = Math.Max(14f, _zone.NextRadius * 0.85f);

            // In Stage 5-6 EndGame or when circle is tight, squads must converge into the final zone
            bool isLateCircle = _zone.CurrentStageNumber >= 4 || _aliveSquadCount <= 4;

            Vec2 bestPos = _map.ClampToWalkableWorld(zoneCenter);
            float bestUtility = float.MinValue;

            // Sample 16 candidate positions inside the next safe zone + POI building compounds
            for (int i = 0; i < 16; i++)
            {
                Vec2 candidate;
                if (i < 10)
                {
                    float angle = (i / 10f) * MathF.PI * 2f + (_currentTick * 0.017f) + sq.SquadId * 0.5f;
                    float radialFrac = isLateCircle ? (0.15f + (i % 3) * 0.25f) : (0.25f + (i % 3) * 0.30f);
                    candidate = zoneCenter + Vec2.FromAngle(angle) * (safeRadius * radialFrac);
                }
                else if (i < 14 && _map.Buildings.Count > 0)
                {
                    var bld = _map.Buildings[(i * 7 + sq.SquadId * 3 + (_currentTick / 40)) % _map.Buildings.Count];
                    Vec2 bldPos = MapData.FineCellToWorldCenter(bld.MinCellX + 1, bld.MinCellY + 1);
                    candidate = Vec2.Distance(bldPos, zoneCenter) <= safeRadius
                        ? bldPos
                        : Vec2.MoveTowards(zoneCenter, bldPos, safeRadius * 0.82f);
                }
                else
                {
                    PoiDef poi = _map.Pois[(i + sq.SquadId) % _map.Pois.Count];
                    if (Vec2.Distance(poi.Center, zoneCenter) > safeRadius)
                    {
                        candidate = Vec2.MoveTowards(zoneCenter, poi.Center, safeRadius * 0.8f);
                    }
                    else
                    {
                        candidate = poi.Center;
                    }
                }

                candidate = _map.ClampToWalkableWorld(candidate);
                MapCell cell = _map.GetCellAtWorld(candidate);

                float distFromSquad = Vec2.Distance(centroid, candidate);
                float travelCost = distFromSquad / 220f;

                float hgScore = cell.HeightLevel * aiCfg.HighGroundWeight * sq.HighGroundBias;
                float coverScore = cell.CoverNormalized * aiCfg.CoverWeight * sq.CoverBias;
                if (cell.IsBuilding)
                {
                    coverScore += 0.45f * sq.CoverBias; // Compound garrison bonus
                }
                else if (cell.IsTacticalLowCover)
                {
                    coverScore += 0.28f * sq.CoverBias;
                }

                // Evaluate nearby enemy threat or hunt opportunity
                float enemyPresence = EstimateEnemyDensityAt(candidate, sq.SquadId, 95f);
                float threatTerm;
                if (sq.HasActiveCommand && sq.ActiveCommandCompliant && sq.ActiveCommand.Type == CommandType.AvoidFight)
                {
                    threatTerm = -enemyPresence * 3.5f;
                }
                else if (sq.HasActiveCommand && sq.ActiveCommandCompliant && sq.ActiveCommand.Type == CommandType.ContestTarget)
                {
                    threatTerm = enemyPresence * 2.0f;
                }
                else if (isLateCircle || sq.AggressionMod >= 1.15f)
                {
                    threatTerm = enemyPresence * 0.6f * sq.AggressionMod;
                }
                else
                {
                    threatTerm = -enemyPresence * aiCfg.ThreatAvoidanceWeight / Math.Max(0.5f, sq.AggressionMod);
                }

                float noise = _rng.NextGaussian(0f, noiseStd);
                float utility = hgScore + coverScore + threatTerm - travelCost + noise;

                if (utility > bestUtility)
                {
                    bestUtility = utility;
                    bestPos = candidate;
                }
            }

            return bestPos;
        }

        private float EstimateEnemyDensityAt(Vec2 pos, int selfSquadId, float radius)
        {
            Span<int> nearby = stackalloc int[48];
            int count = _spatialGrid.QueryRadius(pos, radius, nearby);
            int enemies = 0;
            for (int i = 0; i < count; i++)
            {
                int u = nearby[i];
                if (_units[u].SquadId != selfSquadId && _units[u].State == UnitState.Alive)
                {
                    enemies++;
                }
            }
            return enemies * 0.5f;
        }

        private void SetSquadMacroGoalAndPath(ref SquadRuntime sq, Vec2 goalWorld)
        {
            sq.MacroGoal = _map.ClampToWalkableWorld(goalWorld);
            Vec2 startPos = GetSquadCentroid(sq.SquadId);
            int wpCount = _astar.FindPath(_map, startPos, sq.MacroGoal, _pathScratch);
            sq.WaypointCount = wpCount;
            sq.CurrentWaypointIndex = 0;
            for (int i = 0; i < wpCount; i++)
            {
                _squadWaypoints[sq.SquadId, i] = _pathScratch[i];
            }
        }

        private Vec2 GetSquadCentroid(int squadId)
        {
            int first = squadId * UnitsPerSquad;
            float sumX = 0f, sumY = 0f;
            int count = 0;
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                ref readonly UnitRuntime u = ref _units[first + m];
                if (u.State != UnitState.Eliminated)
                {
                    sumX += u.Position.X;
                    sumY += u.Position.Y;
                    count++;
                }
            }
            if (count == 0) return _squads[squadId].MacroGoal;
            return new Vec2(sumX / count, sumY / count);
        }

        private float GetActiveIglDecision(ref SquadRuntime sq)
        {
            ref readonly UnitRuntime igl = ref _units[sq.IglUnitId];
            if (igl.State == UnitState.Alive)
            {
                return igl.Eff.Decision;
            }
            // Fallback to highest Decision surviving member, with a -1.5 penalty for losing primary IGL
            float best = 6f;
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                ref readonly UnitRuntime u = ref _units[sq.FirstUnitId + m];
                if (u.State == UnitState.Alive && u.Eff.Decision > best)
                {
                    best = u.Eff.Decision;
                }
            }
            return Math.Max(1f, best - 1.5f);
        }

        private float GetSquadAverageComms(ref SquadRuntime sq)
        {
            float sum = 0f;
            int count = 0;
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                ref readonly UnitRuntime u = ref _units[sq.FirstUnitId + m];
                if (u.State == UnitState.Alive)
                {
                    sum += u.Eff.Comms;
                    count++;
                }
            }
            return count > 0 ? sum / count : 8f;
        }
    }
}
