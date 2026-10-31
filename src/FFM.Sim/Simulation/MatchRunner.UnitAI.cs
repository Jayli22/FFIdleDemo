using System;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Entities;
using FFM.Sim.Map;

namespace FFM.Sim.Simulation
{
    public sealed partial class MatchRunner
    {
        private void StepUnitAi()
        {
            var uCfg = _cfg.UnitAi;
            var eCfg = _cfg.Engagement;

            // Advance squad waypoint indices if centroid reached current waypoint
            for (int s = 0; s < SquadCount; s++)
            {
                ref SquadRuntime sq = ref _squads[s];
                if (sq.AliveCount <= 0 || sq.WaypointCount <= 0) continue;

                Vec2 centroid = GetSquadCentroid(s);
                Vec2 curWp = _squadWaypoints[s, sq.CurrentWaypointIndex];
                if (Vec2.Distance(centroid, curWp) <= 22f && sq.CurrentWaypointIndex + 1 < sq.WaypointCount)
                {
                    sq.CurrentWaypointIndex++;
                }
            }

            for (int u = 0; u < TotalUnits; u++)
            {
                ref UnitRuntime unit = ref _units[u];
                if (unit.State == UnitState.Eliminated || unit.DropState != UnitDropState.Grounded) continue;

                // Tick skill cooldowns and durations
                if (unit.ActiveSkillCooldownTicks > 0) unit.ActiveSkillCooldownTicks--;
                if (unit.ActiveSkillDurationTicks > 0)
                {
                    unit.ActiveSkillDurationTicks--;
                    // If Alok HealAura is active, heal nearby squadmates every 5 ticks
                    if (unit.ActiveSkillCategory == SkillCategory.Heal && (_currentTick % 5 == 0))
                    {
                        ApplySquadHealAura(ref unit, 8f);
                    }
                }

                if (unit.State == UnitState.Knocked)
                {
                    // Knocked units crawl slowly toward nearest alive squadmate
                    MoveKnockedUnitTowardTeammate(ref unit);
                    continue;
                }

                // 1. Check if a knocked squadmate needs revive (CMB-07)
                if (TryStepReviveTeammate(ref unit, eCfg))
                {
                    continue;
                }

                // 2. Check self-healing if HP is low and unit has medkit & cover
                MapCell currentCell = _map.GetCellAtWorld(unit.Position);
                bool hasGoodCover = currentCell.CoverValue >= 38 || HasNearbyFriendlyGloo(unit.Position, 4.5f);
                if (unit.Hp < unit.MaxHp * uCfg.HealHpThresholdRatio && unit.MedkitCount > 0 && (hasGoodCover || unit.TargetUnitId == -1))
                {
                    unit.HealProgressTicks++;
                    int requiredHealTicks = (int)MathF.Round(uCfg.HealDurationSec * 10f);
                    if (unit.HealProgressTicks >= requiredHealTicks)
                    {
                        unit.HealProgressTicks = 0;
                        unit.MedkitCount--;
                        unit.Hp = Math.Min(unit.MaxHp, unit.Hp + uCfg.HealAmount);
                    }
                    continue;
                }
                unit.HealProgressTicks = 0;

                // 3. Step Looting (governed by Looting attribute)
                if (_currentPhase == Phase.Loot || _currentPhase == Phase.Rotate || currentCell.PoiId > 0)
                {
                    StepUnitLooting(ref unit, currentCell.PoiId, uCfg);
                }

                // 3b. Free Fire Offensive Advancing Gloo Wall ("推进冰墙") when frontliners push across open ground (never inside buildings)
                if (unit.TargetUnitId >= 0 && !hasGoodCover && !currentCell.IsBuilding && unit.GlooCount >= 2 &&
                    (unit.Role == Role.Rusher || unit.Role == Role.SecondRusher || unit.Role == Role.Grenadier) &&
                    (_currentTick + u * 7) % 45 == 0)
                {
                    ref readonly UnitRuntime tgt = ref _units[unit.TargetUnitId];
                    float distToTgt = Vec2.Distance(unit.Position, tgt.Position);
                    if (distToTgt >= 18f && distToTgt <= 55f && _rng.Chance(0.24f + unit.Eff.Utility * 0.025f))
                    {
                        Vec2 dirToTgt = (tgt.Position - unit.Position).Normalized();
                        PlaceGlooWall(ref unit, unit.Position + dirToTgt * 2.3f);
                    }
                }

                // 4. Compute tactical desired position (Formation + Positioning micro-cover)
                ref SquadRuntime sq = ref _squads[unit.SquadId];
                Vec2 waypoint = sq.WaypointCount > 0
                    ? _squadWaypoints[unit.SquadId, Math.Clamp(sq.CurrentWaypointIndex, 0, sq.WaypointCount - 1)]
                    : sq.MacroGoal;

                // Check vehicle mount / dismount for long rotations (WP-08)
                StepUnitVehicleLogic(ref unit, waypoint);

                if ((_currentTick + u) % 8 == 0)
                {
                    unit.DesiredPosition = SelectMicroPosition(ref unit, ref sq, waypoint);
                }

                // 5. Move toward DesiredPosition (routing building entry/exit through BuildingDoorway)
                float moveSpeed = uCfg.BaseMoveSpeedMetersPerSec * (1f + unit.Eff.Movement * uCfg.MovementSpeedScalePerPoint);
                if (unit.ActiveSkillDurationTicks > 0 && unit.ActiveSkillCategory == SkillCategory.Dash)
                {
                    moveSpeed *= 1.35f; // Tatsuya Rush dash speed boost
                }
                if (unit.VehicleId >= 0 && unit.VehicleId < _vehicleCount && _vehicles[unit.VehicleId].Hp > 0f)
                {
                    moveSpeed *= _vehicles[unit.VehicleId].SpeedMultiplier;
                }

                Vec2 effectiveMoveGoal = unit.DesiredPosition;
                if (unit.VehicleId < 0)
                {
                    int curBld = _map.FindBuildingAtWorld(unit.Position);
                    int dstBld = _map.FindBuildingAtWorld(unit.DesiredPosition);
                    if (curBld != dstBld)
                    {
                        if (curBld < 0 && dstBld >= 0)
                        {
                            // Entering a building from outside: route through nearest doorway first
                            Vec2 doorPos = _map.GetNearestDoorwayWorld(dstBld, unit.Position);
                            if (!currentCell.IsDoorway && Vec2.DistanceSquared(unit.Position, doorPos) > 5.0f * 5.0f)
                            {
                                effectiveMoveGoal = doorPos;
                            }
                            if (unit.TargetUnitId >= 0 && Vec2.DistanceSquared(unit.Position, doorPos) <= 10.5f * 10.5f)
                            {
                                unit.LastBreachTick = _currentTick;
                            }
                        }
                        else if (curBld >= 0 && !currentCell.IsDoorway)
                        {
                            // Exiting a building from inside: route to nearest doorway first
                            Vec2 exitDoorPos = _map.GetNearestDoorwayWorld(curBld, unit.DesiredPosition);
                            if (Vec2.DistanceSquared(unit.Position, exitDoorPos) > 4.2f * 4.2f)
                            {
                                effectiveMoveGoal = exitDoorPos;
                            }
                        }
                    }
                    else if (currentCell.IsDoorway && unit.TargetUnitId >= 0 &&
                             (unit.Role == Role.Rusher || unit.Role == Role.SecondRusher))
                    {
                        unit.LastBreachTick = _currentTick;
                    }
                }

                float stepMeters = moveSpeed * 0.1f;
                Vec2 delta = effectiveMoveGoal - unit.Position;
                if (delta.LengthSquared > 0.04f)
                {
                    unit.FacingRadians = MathF.Atan2(delta.Y, delta.X);
                    Vec2 nextPos = Vec2.MoveTowards(unit.Position, effectiveMoveGoal, stepMeters);
                    MapCell nextCell = _map.GetCellAtWorld(nextPos);
                    if (nextCell.IsWalkable)
                    {
                        // Prevent walking straight across a window wall without using a doorway
                        if (unit.VehicleId < 0 && currentCell.IsBuilding != nextCell.IsBuilding &&
                            !currentCell.IsDoorway && !nextCell.IsDoorway)
                        {
                            int bIdx = currentCell.IsBuilding
                                ? _map.FindBuildingAtWorld(unit.Position)
                                : _map.FindBuildingAtWorld(nextPos);
                            if (bIdx >= 0)
                            {
                                Vec2 doorWorld = _map.GetNearestDoorwayWorld(bIdx, unit.Position);
                                nextPos = Vec2.MoveTowards(unit.Position, doorWorld, stepMeters);
                            }
                        }
                        if (_map.GetCellAtWorld(nextPos).IsWalkable)
                        {
                            unit.Position = nextPos;
                        }
                    }
                    else
                    {
                        unit.Position = _map.ClampToWalkableWorld(nextPos);
                    }
                    unit.HeightLevel = _map.GetCellAtWorld(unit.Position).HeightLevel;

                    if (unit.VehicleId >= 0 && unit.VehicleId < _vehicleCount)
                    {
                        _vehicles[unit.VehicleId].Position = unit.Position;
                    }
                }
            }
        }

        private void ApplySquadHealAura(ref UnitRuntime caster, float healAmount)
        {
            int first = caster.SquadId * UnitsPerSquad;
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                ref UnitRuntime ally = ref _units[first + m];
                if (ally.State == UnitState.Alive && Vec2.DistanceSquared(caster.Position, ally.Position) <= 15f * 15f)
                {
                    ally.Hp = Math.Min(ally.MaxHp, ally.Hp + healAmount);
                }
            }
        }

        private void MoveKnockedUnitTowardTeammate(ref UnitRuntime knockedUnit)
        {
            int first = knockedUnit.SquadId * UnitsPerSquad;
            int nearestAlly = -1;
            float bestDistSq = float.MaxValue;
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                int allyId = first + m;
                if (allyId == knockedUnit.UnitId) continue;
                if (_units[allyId].State == UnitState.Alive)
                {
                    float dSq = Vec2.DistanceSquared(knockedUnit.Position, _units[allyId].Position);
                    if (dSq < bestDistSq)
                    {
                        bestDistSq = dSq;
                        nearestAlly = allyId;
                    }
                }
            }

            if (nearestAlly != -1 && bestDistSq > 2.0f * 2.0f)
            {
                Vec2 next = Vec2.MoveTowards(knockedUnit.Position, _units[nearestAlly].Position, 0.18f);
                if (_map.GetCellAtWorld(next).IsWalkable)
                {
                    knockedUnit.Position = next;
                }
            }
        }

        /// <summary>
        /// Formula CMB-07: Teammate Revive governed by Teamwork and Support role bonus.
        /// </summary>
        private bool TryStepReviveTeammate(ref UnitRuntime unit, EngagementBalanceConfig eCfg)
        {
            // Do not attempt open-field revive if critically low HP or actively taking fire without cover/Gloo
            if (unit.Hp < 65f || (_currentTick - unit.LastDamagedTick < 18 && !HasNearbyFriendlyGloo(unit.Position, 4.0f)))
            {
                unit.ReviveProgressTicks = 0;
                return false;
            }

            int first = unit.SquadId * UnitsPerSquad;
            int knockedAllyId = -1;
            float bestDist = 20f;

            for (int m = 0; m < UnitsPerSquad; m++)
            {
                int candidateId = first + m;
                if (candidateId == unit.UnitId) continue;
                ref UnitRuntime ally = ref _units[candidateId];
                if (ally.State == UnitState.Knocked)
                {
                    if (ally.ReviverUnitId != -1 && ally.ReviverUnitId != unit.UnitId && _units[ally.ReviverUnitId].State == UnitState.Alive)
                    {
                        continue; // Already being revived by another squadmate
                    }
                    float dist = Vec2.Distance(unit.Position, ally.Position);
                    if (dist < bestDist)
                    {
                        bestDist = dist;
                        knockedAllyId = candidateId;
                    }
                }
            }

            if (knockedAllyId == -1)
            {
                unit.ReviveProgressTicks = 0;
                return false;
            }

            ref UnitRuntime targetKnocked = ref _units[knockedAllyId];
            targetKnocked.ReviverUnitId = unit.UnitId;

            if (bestDist > eCfg.ReviveRangeMeters)
            {
                // Move toward knocked teammate to revive
                unit.DesiredPosition = targetKnocked.Position;
                Vec2 nextPos = Vec2.MoveTowards(unit.Position, targetKnocked.Position, 0.58f);
                if (_map.GetCellAtWorld(nextPos).IsWalkable)
                {
                    unit.Position = nextPos;
                }
                return true;
            }

            // If Support has Gloo Wall and no friendly Gloo nearby, place a protective revive Gloo Wall
            if (unit.GlooCount > 0 && !HasNearbyFriendlyGloo(targetKnocked.Position, 4.0f))
            {
                PlaceGlooWall(ref unit, targetKnocked.Position + new Vec2(1.8f, 0f));
            }

            // CMB-07: ReviveDurationSec = max(2.0, BaseReviveSec - Teamwork * 0.12 - (Role == Support ? 1.0 : 0.0))
            float supportBonus = unit.Role == Role.Support ? eCfg.ReviveSupportBonusSec : 0f;
            float reviveSec = Math.Max(2.0f, (eCfg.BaseReviveSec - unit.Eff.Teamwork * eCfg.ReviveTeamworkScale - supportBonus) / Math.Max(1.0f, unit.Eff.ReviveSpeedMultiplier));
            int neededTicks = Math.Max(20, (int)MathF.Round(reviveSec * 10f));

            targetKnocked.ReviveProgressTicks++;
            if (targetKnocked.ReviveProgressTicks >= neededTicks)
            {
                targetKnocked.State = UnitState.Alive;
                targetKnocked.Hp = Math.Min(targetKnocked.MaxHp, 75f + unit.Eff.Teamwork * 2.0f + unit.Eff.ReviveBonusHp);
                targetKnocked.KnockedRemainTicks = 0;
                targetKnocked.ReviveProgressTicks = 0;
                targetKnocked.ReviverUnitId = -1;
                unit.RevivesCompleted++;

                _events.Record(new SimEvent(
                    _currentTick,
                    SimEventType.Revived,
                    actorUnitIdx: unit.UnitId,
                    targetUnitIdx: targetKnocked.UnitId,
                    squadIdx: unit.SquadId,
                    pos: targetKnocked.Position));
            }

            return true;
        }

        private void StepUnitLooting(ref UnitRuntime unit, byte poiId, UnitAiBalanceConfig uCfg)
        {
            if (unit.LootCooldownTicks > 0)
            {
                unit.LootCooldownTicks--;
                return;
            }

            int interval = Math.Max(32, 90 - (int)MathF.Round(unit.Eff.Looting * 2.8f));
            unit.LootCooldownTicks = interval;

            float poiRichness = 1.0f;
            if (poiId > 0 && poiId <= _map.Pois.Count)
            {
                poiRichness = _map.Pois[poiId - 1].LootTierWeight;
            }

            float qualityRoll = _rng.NextFloat01() * 0.90f + unit.Eff.Looting * 0.032f + (poiRichness - 1f) * 0.20f;
            int targetTier = qualityRoll >= 1.26f ? 3 : (qualityRoll >= 0.85f ? 2 : 1);

            WeaponDef curWeapon = _cfg.Weapons.Weapons[unit.PrimaryWeaponIndex];
            if (targetTier > curWeapon.Tier)
            {
                int upgradedIdx = FindWeaponIndexByCategoryAndTier(curWeapon.Category, targetTier);
                if (upgradedIdx >= 0)
                {
                    unit.PrimaryWeaponIndex = upgradedIdx;
                }
            }

            if (unit.SecondaryWeaponIndex >= 0 && unit.SecondaryWeaponIndex < _cfg.Weapons.Weapons.Count)
            {
                WeaponDef curSecondary = _cfg.Weapons.Weapons[unit.SecondaryWeaponIndex];
                if (targetTier > curSecondary.Tier)
                {
                    int upSecIdx = FindWeaponIndexByCategoryAndTier(curSecondary.Category, targetTier);
                    if (upSecIdx >= 0)
                    {
                        unit.SecondaryWeaponIndex = upSecIdx;
                    }
                }
            }

            if (targetTier > unit.ArmorTier)
            {
                unit.ArmorTier = targetTier;
                unit.ArmorDurability = 100f;
            }
            if (targetTier > unit.HelmetTier)
            {
                unit.HelmetTier = targetTier;
            }

            int maxGloo = unit.Role == Role.Support ? 6 : 4;
            if (unit.GlooCount < maxGloo && _rng.Chance(0.28f + unit.Eff.Looting * 0.025f))
            {
                unit.GlooCount++;
            }

            int maxNades = unit.Role == Role.Grenadier ? 4 : 2;
            if (unit.GrenadeCount < maxNades && _rng.Chance(0.22f + unit.Eff.Looting * 0.025f))
            {
                unit.GrenadeCount++;
            }

            if (unit.MedkitCount < 3 && _rng.Chance(0.28f + unit.Eff.Looting * 0.020f))
            {
                unit.MedkitCount++;
            }

            _events.Record(new SimEvent(_currentTick, SimEventType.Looted, actorUnitIdx: unit.UnitId, squadIdx: unit.SquadId, pos: unit.Position, extraCode: targetTier));
        }

        private int FindWeaponIndexByCategoryAndTier(string category, int tier)
        {
            for (int i = 0; i < _cfg.Weapons.Weapons.Count; i++)
            {
                var w = _cfg.Weapons.Weapons[i];
                if (w.Tier == tier && string.Equals(w.Category, category, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return -1;
        }

        private void StepUnitVehicleLogic(ref UnitRuntime unit, Vec2 waypoint)
        {
            float distToGoal = Vec2.Distance(unit.Position, waypoint);

            // Dismount vehicle if reached goal (< 65m), or in EndGame (Stage >= 4), or under heavy close fire
            if (unit.VehicleId >= 0)
            {
                int vIdx = unit.VehicleId;
                if (distToGoal < 65f || _zone.CurrentStageNumber >= 4 || _vehicles[vIdx].Hp <= 40f)
                {
                    _vehicles[vIdx].DriverUnitId = -1;
                    _vehicles[vIdx].SquadId = -1;
                    unit.VehicleId = -1;
                }
                return;
            }

            // Mount an available nearby vehicle if rotating long distance (> 240m) before Stage 4
            if (distToGoal > 240f && _zone.CurrentStageNumber <= 3)
            {
                for (int v = 0; v < _vehicleCount; v++)
                {
                    if (_vehicles[v].DriverUnitId == -1 && _vehicles[v].Hp > 80f)
                    {
                        if (Vec2.DistanceSquared(unit.Position, _vehicles[v].Position) <= 22f * 22f)
                        {
                            _vehicles[v].DriverUnitId = unit.UnitId;
                            _vehicles[v].SquadId = unit.SquadId;
                            unit.VehicleId = v;
                            _events.Record(new SimEvent(_currentTick, SimEventType.VehicleMounted, actorUnitIdx: unit.UnitId, squadIdx: unit.SquadId, pos: unit.Position, extraCode: _vehicles[v].Id));
                            break;
                        }
                    }
                }
            }
        }

        private Vec2 SelectMicroPosition(ref UnitRuntime unit, ref SquadRuntime sq, Vec2 squadWaypoint)
        {
            RoleDef rDef = _cfg.Roles.GetRoleDef(unit.Role);
            Vec2 formationAnchor;

            int activeTargetId = unit.TargetUnitId >= 0 ? unit.TargetUnitId : sq.FocusTargetUnitId;
            bool inActiveCombat = activeTargetId >= 0 &&
                                  activeTargetId < TotalUnits &&
                                  _units[activeTargetId].State != UnitState.Eliminated &&
                                  !_zone.IsOutsideZone(unit.Position) &&
                                  !(sq.HasActiveCommand && sq.ActiveCommandCompliant && sq.ActiveCommand.Type == CommandType.RotateNow);

            int myBldgIdx = _map.FindBuildingAtWorld(unit.Position);
            bool inMyBldg = myBldgIdx >= 0;
            Vec2 enemyPos = inActiveCombat ? _units[activeTargetId].Position : sq.MacroGoal;

            if (inActiveCombat)
            {
                float distToEnemy = Vec2.Distance(unit.Position, enemyPos);
                int enemyBldgIdx = _map.FindBuildingAtWorld(enemyPos);
                bool enemyInBldg = enemyBldgIdx >= 0;
                bool sameBldg = inMyBldg && enemyInBldg && myBldgIdx == enemyBldgIdx;

                if (inMyBldg && !sameBldg)
                {
                    // Garrison Mode (守楼): hold inside compound; low HP retreats to deep interior, others hold windows/doorways facing enemy
                    var myBldg = _map.Buildings[myBldgIdx];
                    float minX = myBldg.MinCellX * MapData.FineCellMeters;
                    float maxX = (myBldg.MinCellX + myBldg.WidthCells) * MapData.FineCellMeters;
                    float minY = myBldg.MinCellY * MapData.FineCellMeters;
                    float maxY = (myBldg.MinCellY + myBldg.HeightCells) * MapData.FineCellMeters;
                    Vec2 bldgCenter = new Vec2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

                    if (unit.Hp < 110f)
                    {
                        formationAnchor = bldgCenter;
                    }
                    else if (unit.Role == Role.Rusher || unit.Role == Role.SecondRusher)
                    {
                        formationAnchor = _map.GetNearestDoorwayWorld(myBldgIdx, enemyPos);
                    }
                    else
                    {
                        Vec2 dirToEnemy = (enemyPos - bldgCenter).Normalized();
                        float halfW = Math.Max(2f, (maxX - minX) * 0.5f - 2f);
                        float halfH = Math.Max(2f, (maxY - minY) * 0.5f - 2f);
                        formationAnchor = new Vec2(
                            Math.Clamp(bldgCenter.X + dirToEnemy.X * halfW, minX + 2f, maxX - 2f),
                            Math.Clamp(bldgCenter.Y + dirToEnemy.Y * halfH, minY + 2f, maxY - 2f));
                    }
                }
                else if (enemyInBldg && !sameBldg && distToEnemy <= 55f)
                {
                    // Breach Mode (攻楼): Rushers converge on enemy doorway; Grenadier holds 20m throw angle outside doorway/window
                    Vec2 breachDoor = _map.GetNearestDoorwayWorld(enemyBldgIdx, unit.Position);
                    Vec2 dirFromDoor = (unit.Position - breachDoor).Normalized();
                    if (dirFromDoor.LengthSquared < 0.01f) dirFromDoor = new Vec2(1f, 0f);
                    Vec2 perp = new Vec2(-dirFromDoor.Y, dirFromDoor.X);
                    float sideSign = (unit.UnitId & 1) == 0 ? 1f : -1f;

                    switch (unit.Role)
                    {
                        case Role.Rusher:
                        case Role.SecondRusher:
                            formationAnchor = Vec2.MoveTowards(unit.Position, enemyPos, 18f);
                            break;
                        case Role.Grenadier:
                            formationAnchor = breachDoor + dirFromDoor * 20f + perp * (4f * sideSign);
                            break;
                        case Role.Sniper:
                            formationAnchor = breachDoor + dirFromDoor * 46f + perp * (8f * sideSign);
                            break;
                        default:
                            formationAnchor = breachDoor + dirFromDoor * 12f;
                            break;
                    }
                }
                else if (distToEnemy <= 110f)
                {
                    Vec2 dirFromEnemy = (unit.Position - enemyPos).Normalized();
                    if (dirFromEnemy.LengthSquared < 0.01f) dirFromEnemy = new Vec2(1f, 0f);
                    Vec2 perp = new Vec2(-dirFromEnemy.Y, dirFromEnemy.X);
                    float sideSign = (unit.UnitId & 1) == 0 ? 1f : -1f;

                    switch (unit.Role)
                    {
                        case Role.Rusher:
                            // Free Fire 1st Rusher ("一号位突击手"): aggressively close distance to 14m CQC shotgun/SMG range
                            formationAnchor = Vec2.MoveTowards(unit.Position, enemyPos + dirFromEnemy * 14f + perp * (3.5f * sideSign), 18f);
                            break;
                        case Role.SecondRusher:
                            // Free Fire 2nd Rusher ("二号位拉枪线"): wide flank crossfire angle at ~22m
                            formationAnchor = Vec2.MoveTowards(unit.Position, enemyPos + dirFromEnemy * 22f + perp * (14f * sideSign), 18f);
                            break;
                        case Role.Grenadier:
                            // Free Fire Grenadier ("雷神"): hold 25m throwable sweet-spot range
                            formationAnchor = Vec2.MoveTowards(unit.Position, enemyPos + dirFromEnemy * 25f - perp * (6f * sideSign), 16f);
                            break;
                        case Role.Sniper:
                            // Free Fire Sniper ("架枪狙击"): hold 58m..72m high-ground overwatch
                            formationAnchor = Vec2.MoveTowards(unit.Position, enemyPos + dirFromEnemy * 62f, 14f);
                            break;
                        case Role.Support:
                        default:
                            // Free Fire IGL/Support ("辅助/指挥"): stay 10m behind Rusher for Alok aura, trade fire & fast revive
                            ref readonly UnitRuntime frontliner = ref _units[sq.FirstUnitId];
                            Vec2 supportGoal = frontliner.State == UnitState.Alive
                                ? frontliner.Position + dirFromEnemy * 10f
                                : enemyPos + dirFromEnemy * 34f;
                            formationAnchor = Vec2.MoveTowards(unit.Position, supportGoal, 16f);
                            break;
                    }
                }
                else
                {
                    Vec2 toGoal = (sq.MacroGoal - unit.Position).Normalized();
                    if (toGoal.LengthSquared < 0.01f) toGoal = new Vec2(1f, 0f);
                    Vec2 right = new Vec2(-toGoal.Y, toGoal.X);
                    formationAnchor = squadWaypoint + toGoal * rDef.ForwardOffsetMeters + right * rDef.SideOffsetMeters;
                }
            }
            else
            {
                Vec2 toGoal = (sq.MacroGoal - unit.Position).Normalized();
                if (toGoal.LengthSquared < 0.01f) toGoal = new Vec2(1f, 0f);
                Vec2 right = new Vec2(-toGoal.Y, toGoal.X);
                formationAnchor = squadWaypoint + toGoal * rDef.ForwardOffsetMeters + right * rDef.SideOffsetMeters;
            }

            formationAnchor = _map.ClampToWalkableWorld(formationAnchor);

            // Search local cells around formationAnchor for best cover & elevation governed by Positioning attribute
            MapData.WorldToFineCell(formationAnchor, out int baseCx, out int baseCy);
            int radius = _cfg.UnitAi.CoverSearchRadiusCells;
            float posNoiseScale = Math.Max(0.02f, (20f - unit.Eff.Positioning) * 0.035f);

            int bestCx = baseCx;
            int bestCy = baseCy;
            float bestScore = float.MinValue;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int cx = baseCx + dx;
                    int cy = baseCy + dy;
                    if ((uint)cx >= MapData.FineGridSize || (uint)cy >= MapData.FineGridSize) continue;

                    MapCell cell = _map.GetCell(cx, cy);
                    if (!cell.IsWalkable) continue;

                    float coverScore = cell.CoverNormalized * 1.8f;
                    if (cell.IsTacticalLowCover && inActiveCombat)
                    {
                        coverScore += 0.38f;
                    }

                    float indoorBonus = 0f;
                    if (inMyBldg)
                    {
                        if (unit.Hp < 110f && cell.IsBuilding && !cell.IsWindow && !cell.IsDoorway)
                        {
                            indoorBonus += 0.95f;
                        }
                        else if (inActiveCombat && cell.IsWindow && (unit.Role == Role.Sniper || unit.Role == Role.Support || unit.Role == Role.Grenadier))
                        {
                            indoorBonus += 0.78f;
                        }
                        else if (inActiveCombat && (cell.IsDoorway || cell.IsWindow) && (unit.Role == Role.Rusher || unit.Role == Role.SecondRusher))
                        {
                            indoorBonus += 0.68f;
                        }
                    }

                    float heightScore = cell.HeightLevel * rDef.HighGroundPreference * 0.8f;
                    float distPenalty = MathF.Sqrt(dx * dx + dy * dy) * 0.12f;
                    float noise = _rng.NextGaussian(0f, posNoiseScale);

                    float score = coverScore + indoorBonus + heightScore - distPenalty + noise;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestCx = cx;
                        bestCy = cy;
                    }
                }
            }

            return MapData.FineCellToWorldCenter(bestCx, bestCy);
        }

        private bool HasNearbyFriendlyGloo(Vec2 pos, float radius)
        {
            float rSq = radius * radius;
            for (int i = 0; i < _glooWallCount; i++)
            {
                if (_glooWalls[i].Hp > 0f && Vec2.DistanceSquared(pos, _glooWalls[i].Position) <= rSq)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
