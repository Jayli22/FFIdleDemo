using System;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Entities;
using FFM.Sim.Map;

namespace FFM.Sim.Simulation
{
    public sealed partial class MatchRunner
    {
        private void StepCombatAndDamage()
        {
            var eCfg = _cfg.Engagement;

            // 1. Apply Safe Zone (Blue Zone) Damage & Knocked Bleed-Out every tick
            for (int u = 0; u < TotalUnits; u++)
            {
                ref UnitRuntime unit = ref _units[u];
                if (unit.State == UnitState.Eliminated || unit.DropState != UnitDropState.Grounded) continue;

                // Blue Zone DPS
                if (_zone.IsOutsideZone(unit.Position))
                {
                    float zoneDmg = _zone.CurrentDps * 0.1f * (1f - Math.Clamp(unit.Eff.ZoneDpsReduction, 0f, 0.5f));
                    ApplyDamageToUnit(ref unit, -1, zoneDmg, isZoneDamage: true);
                    if (unit.State == UnitState.Eliminated) continue;
                }

                // Knocked Bleed-Out (CMB-07)
                if (unit.State == UnitState.Knocked)
                {
                    unit.KnockedRemainTicks--;
                    if (unit.KnockedRemainTicks <= 0)
                    {
                        EliminateUnit(ref unit, unit.LastAttackerUnitId);
                    }
                }
            }

            // Skip weapon engagements during the first 25 ticks of Drop phase while parachuting/landing
            if (_currentTick <= 25) return;

            // 2. Target Acquisition & Detection (CMB-01)
            for (int u = 0; u < TotalUnits; u++)
            {
                ref UnitRuntime unit = ref _units[u];
                if (unit.State != UnitState.Alive || unit.DropState != UnitDropState.Grounded) continue;

                AcquireOrValidateTarget(ref unit, eCfg);
            }

            // 3. Synchronize Squad Focus Fire governed by Comms & Teamwork
            for (int s = 0; s < SquadCount; s++)
            {
                ref SquadRuntime sq = ref _squads[s];
                if (sq.AliveCount <= 0) continue;
                SyncSquadFocusTarget(ref sq);
            }

            // 4. Execute Combat Rounds every RoundDurationTicks (default 5 ticks = 0.5s)
            if (_currentTick % eCfg.RoundDurationTicks != 0)
            {
                return;
            }

            for (int u = 0; u < TotalUnits; u++)
            {
                ref UnitRuntime attacker = ref _units[u];
                if (attacker.State != UnitState.Alive || attacker.DropState != UnitDropState.Grounded || attacker.TargetUnitId < 0) continue;

                ref UnitRuntime defender = ref _units[attacker.TargetUnitId];
                if (defender.State == UnitState.Eliminated || defender.DropState != UnitDropState.Grounded)
                {
                    attacker.TargetUnitId = -1;
                    attacker.ConsecutiveRoundsInEngagement = 0;
                    continue;
                }

                ResolveCombatRound(ref attacker, ref defender, eCfg);
            }
        }

        /// <summary>
        /// Formula CMB-01: Enemy detection probability governed by Awareness, distance, target cover, and Scan skill.
        /// Includes Free Fire Gloo Wall awareness: units maintain awareness of enemies behind Gloo Walls within 75m
        /// so they can throw grenades over Gloo, trigger Skyler Riptide, or focus-fire/pierce the Gloo Wall.
        /// </summary>
        private void AcquireOrValidateTarget(ref UnitRuntime unit, EngagementBalanceConfig eCfg)
        {
            WeaponDef primaryW = _cfg.Weapons.Weapons[unit.PrimaryWeaponIndex];
            float maxWeaponRange = primaryW.RangeMeters;
            if (unit.SecondaryWeaponIndex >= 0 && unit.SecondaryWeaponIndex < _cfg.Weapons.Weapons.Count)
            {
                maxWeaponRange = Math.Max(maxWeaponRange, _cfg.Weapons.Weapons[unit.SecondaryWeaponIndex].RangeMeters);
            }
            float maxDetectRange = Math.Max(maxWeaponRange, 115f);

            // Validate existing target
            if (unit.TargetUnitId >= 0)
            {
                ref readonly UnitRuntime curTarget = ref _units[unit.TargetUnitId];
                float dist = Vec2.Distance(unit.Position, curTarget.Position);
                bool hasDirectLos = LineOfSight.HasLineOfSight(_map, unit.Position, curTarget.Position, _glooWalls, _glooWallCount);
                bool hasGlooTacticalLos = !hasDirectLos && dist <= 75f && LineOfSight.HasTerrainLineOfSight(_map, unit.Position, curTarget.Position);

                if (curTarget.State == UnitState.Eliminated ||
                    curTarget.DropState != UnitDropState.Grounded ||
                    dist > maxDetectRange * 1.15f ||
                    (!hasDirectLos && !hasGlooTacticalLos))
                {
                    unit.TargetUnitId = -1;
                    unit.ConsecutiveRoundsInEngagement = 0;
                }
            }

            Span<int> candidates = stackalloc int[48];
            int count = _spatialGrid.QueryRadius(unit.Position, maxDetectRange, candidates);
            if (count <= 1) return;

            bool hasActiveScan = unit.ActiveSkillDurationTicks > 0 && unit.ActiveSkillCategory == SkillCategory.Scan;
            int bestTarget = unit.TargetUnitId;
            float bestScore = bestTarget >= 0 ? 50f : float.MinValue;

            for (int i = 0; i < count; i++)
            {
                int otherId = candidates[i];
                ref readonly UnitRuntime other = ref _units[otherId];
                if (other.SquadId == unit.SquadId || other.State == UnitState.Eliminated || other.DropState != UnitDropState.Grounded) continue;

                float dist = Vec2.Distance(unit.Position, other.Position);
                if (dist > maxDetectRange) continue;

                bool hasDirectLos = LineOfSight.HasLineOfSight(_map, unit.Position, other.Position, _glooWalls, _glooWallCount);
                bool blockedOnlyByGloo = !hasDirectLos && dist <= 75f && LineOfSight.HasTerrainLineOfSight(_map, unit.Position, other.Position);
                if (!hasDirectLos && !blockedOnlyByGloo)
                {
                    continue;
                }

                // CMB-01: P(detect) = clamp(Base + Awareness * Scale - dist/450 - targetCover*0.25, 0.08, 0.98)
                bool alreadySpotted = (unit.SpottedEnemyMask & (1 << other.SquadId)) != 0;
                if (!alreadySpotted && !hasActiveScan)
                {
                    float targetCover = _map.GetCellAtWorld(other.Position).CoverNormalized;
                    float pDetect = Math.Clamp(
                        eCfg.DetectionBasePerTick + unit.Eff.Awareness * eCfg.DetectionAwarenessScale - (dist / 450f) - targetCover * 0.22f,
                        0.08f,
                        0.98f);
                    if (!_rng.Chance(pDetect))
                    {
                        continue;
                    }
                    unit.SpottedEnemyMask |= (1 << other.SquadId);
                }

                // Prefer Alive enemies, or finish off nearby Knocked enemies (< 40m)
                float stateScore = other.State == UnitState.Alive ? 45f : (dist < 40f ? 36f : -15f);
                float distScore = (maxDetectRange - dist) * 0.4f;
                float hpScore = (200f - other.Hp) * (0.12f + unit.Eff.Awareness * 0.006f);
                float totalScore = stateScore + distScore + hpScore;
                if (blockedOnlyByGloo)
                {
                    // Prefer exposed enemies first, but still engage enemies behind Gloo Walls for Gloo-breaking/grenades
                    totalScore *= 0.78f;
                }

                if (totalScore > bestScore)
                {
                    bestScore = totalScore;
                    bestTarget = otherId;
                }
            }

            if (bestTarget != unit.TargetUnitId)
            {
                unit.TargetUnitId = bestTarget;
                unit.ConsecutiveRoundsInEngagement = 0;
                if (bestTarget >= 0)
                {
                    _events.Record(new SimEvent(
                        _currentTick,
                        SimEventType.EngagementStarted,
                        actorUnitIdx: unit.UnitId,
                        targetUnitIdx: bestTarget,
                        squadIdx: unit.SquadId,
                        targetSquadIdx: _units[bestTarget].SquadId,
                        pos: unit.Position,
                        pos2: _units[bestTarget].Position));
                }
            }
        }

        private void SyncSquadFocusTarget(ref SquadRuntime sq)
        {
            float avgComms = GetSquadAverageComms(ref sq);
            // Higher Comms & Teamwork increase probability that squad members lock onto the squad's focus target
            int bestTarget = -1;
            float lowestHp = float.MaxValue;

            for (int m = 0; m < UnitsPerSquad; m++)
            {
                ref readonly UnitRuntime u = ref _units[sq.FirstUnitId + m];
                if (u.State == UnitState.Alive && u.TargetUnitId >= 0)
                {
                    ref readonly UnitRuntime tgt = ref _units[u.TargetUnitId];
                    if (tgt.State == UnitState.Alive && tgt.Hp < lowestHp)
                    {
                        lowestHp = tgt.Hp;
                        bestTarget = u.TargetUnitId;
                    }
                }
            }

            sq.FocusTargetUnitId = bestTarget;
            if (bestTarget < 0) return;

            float syncProb = Math.Clamp(0.20f + avgComms * 0.032f + sq.Synergy * 0.002f, 0.25f, 0.92f);
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                ref UnitRuntime u = ref _units[sq.FirstUnitId + m];
                if (u.State == UnitState.Alive && u.TargetUnitId != bestTarget && _rng.Chance(syncProb))
                {
                    float dist = Vec2.Distance(u.Position, _units[bestTarget].Position);
                    WeaponDef w = _cfg.Weapons.Weapons[u.PrimaryWeaponIndex];
                    if (dist <= w.RangeMeters && LineOfSight.HasTerrainLineOfSight(_map, u.Position, _units[bestTarget].Position))
                    {
                        u.TargetUnitId = bestTarget;
                    }
                }
            }
        }

        private void ResolveCombatRound(ref UnitRuntime attacker, ref UnitRuntime defender, EngagementBalanceConfig eCfg)
        {
            float dist = Vec2.Distance(attacker.Position, defender.Position);
            attacker.FacingRadians = MathF.Atan2(defender.Position.Y - attacker.Position.Y, defender.Position.X - attacker.Position.X);

            // Dynamically switch between Primary and Secondary weapon based on engagement distance
            SelectActiveWeaponForDistance(ref attacker, dist);

            // 1. Check Active Skill Trigger (CMB-10) — e.g., Skyler Riptide destroys enemy Gloo Walls!
            TryTriggerActiveSkill(ref attacker, ref defender, dist);

            // 2. Check Throwable Grenade Trigger (CMB-09) — grenades arc over Gloo Walls in [12m, 48m]!
            TryThrowGrenade(ref attacker, ref defender, dist);
            if (defender.State == UnitState.Eliminated) return;

            // 2b. Check if an active Gloo Wall still blocks direct bullet Line-Of-Sight
            int blockingGlooIdx = LineOfSight.FindBlockingGlooWallIndex(attacker.Position, defender.Position, _glooWalls, _glooWallCount);
            if (blockingGlooIdx >= 0)
            {
                FireAtGlooWall(ref attacker, ref defender, blockingGlooIdx, dist, eCfg);
                return;
            }

            // 3. Check Initiative (CMB-02) on round 0 of engagement
            bool wonInitiative = false;
            if (attacker.ConsecutiveRoundsInEngagement == 0)
            {
                float posAdv = (attacker.HeightLevel - defender.HeightLevel) * 0.25f
                             + (_map.GetCellAtWorld(attacker.Position).CoverNormalized - _map.GetCellAtWorld(defender.Position).CoverNormalized) * 0.5f;
                float initAttacker = attacker.Eff.Awareness * eCfg.InitiativeAwarenessWeight
                                   + attacker.Eff.Reflex * eCfg.InitiativeReflexWeight
                                   + posAdv * eCfg.InitiativePosAdvWeight
                                   + _rng.NextGaussian(0f, eCfg.InitiativeNoiseStdDev);
                float initDefender = defender.Eff.Awareness * eCfg.InitiativeAwarenessWeight
                                   + defender.Eff.Reflex * eCfg.InitiativeReflexWeight
                                   + _rng.NextGaussian(0f, eCfg.InitiativeNoiseStdDev);

                if (initAttacker > initDefender + 1.5f)
                {
                    wonInitiative = true;
                }
            }

            attacker.ConsecutiveRoundsInEngagement++;

            // 4. Execute Weapon Burst (CMB-03, CMB-04, CMB-05, CMB-08)
            FireWeaponBurst(ref attacker, ref defender, dist, eCfg, isTradeShot: false, bonusInitiativeShot: wonInitiative);
        }

        private static float WrapAngle(float angle)
        {
            while (angle > MathF.PI) angle -= 2f * MathF.PI;
            while (angle < -MathF.PI) angle += 2f * MathF.PI;
            return angle;
        }

        /// <summary>
        /// Free Fire Dual-Weapon Switching: compares primary and secondary weapon effective DPS/accuracy at current distance
        /// and equips the superior weapon into PrimaryWeaponIndex. Inside buildings or CQC breach (<18m), Shotguns/SMGs get +35% priority.
        /// </summary>
        private void SelectActiveWeaponForDistance(ref UnitRuntime unit, float dist)
        {
            if (unit.SecondaryWeaponIndex < 0 || unit.SecondaryWeaponIndex >= _cfg.Weapons.Weapons.Count) return;
            if (unit.SecondaryWeaponIndex == unit.PrimaryWeaponIndex) return;

            WeaponDef wPrimary = _cfg.Weapons.Weapons[unit.PrimaryWeaponIndex];
            WeaponDef wSecondary = _cfg.Weapons.Weapons[unit.SecondaryWeaponIndex];

            float scorePrimary = (dist <= wPrimary.RangeMeters * 1.1f)
                ? wPrimary.GetBaseAccuracy(dist) * wPrimary.Damage * wPrimary.ShotsPerRound
                : 0.01f;
            float scoreSecondary = (dist <= wSecondary.RangeMeters * 1.1f)
                ? wSecondary.GetBaseAccuracy(dist) * wSecondary.Damage * wSecondary.ShotsPerRound
                : 0.01f;

            bool isIndoorOrBreach = dist <= 18f || _map.GetCellAtWorld(unit.Position).IsBuilding;
            if (isIndoorOrBreach)
            {
                if (string.Equals(wPrimary.Category, "Shotgun", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wPrimary.Category, "SMG", StringComparison.OrdinalIgnoreCase))
                {
                    scorePrimary *= 1.35f;
                }
                if (string.Equals(wSecondary.Category, "Shotgun", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(wSecondary.Category, "SMG", StringComparison.OrdinalIgnoreCase))
                {
                    scoreSecondary *= 1.35f;
                }
            }

            // Hysteresis (12% threshold) to avoid rapid weapon-swap jitter at boundary distances
            if (scoreSecondary > scorePrimary * 1.12f)
            {
                int tmp = unit.PrimaryWeaponIndex;
                unit.PrimaryWeaponIndex = unit.SecondaryWeaponIndex;
                unit.SecondaryWeaponIndex = tmp;
            }
        }

        /// <summary>
        /// Free Fire Gloo Wall Counterplay: Squads focus-fire enemy Gloo Walls to break cover,
        /// while Sniper rifles (M82B) pierce through Gloo Walls to deal reduced direct damage.
        /// </summary>
        private void FireAtGlooWall(ref UnitRuntime attacker, ref UnitRuntime defender, int glooIdx, float dist, EngagementBalanceConfig eCfg)
        {
            WeaponDef weapon = _cfg.Weapons.Weapons[attacker.PrimaryWeaponIndex];
            if (dist > weapon.RangeMeters * 1.15f) return;

            float glooDmg = weapon.Damage * weapon.ShotsPerRound * (0.58f + attacker.Eff.Utility * 0.025f);
            _glooWalls[glooIdx].Hp -= glooDmg;

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.ShotFired,
                actorUnitIdx: attacker.UnitId,
                targetUnitIdx: defender.UnitId,
                squadIdx: attacker.SquadId,
                targetSquadIdx: defender.SquadId,
                pos: attacker.Position,
                pos2: _glooWalls[glooIdx].Position,
                value: glooDmg,
                extraCode: weapon.ShotsPerRound));

            // Free Fire M82B / Sniper Gloo Wall Piercing ("巴雷特穿冰墙")
            if (string.Equals(weapon.Category, "Sniper", StringComparison.OrdinalIgnoreCase))
            {
                float pierceHitProb = Math.Clamp(weapon.GetBaseAccuracy(dist) * (0.55f + attacker.Eff.Aim * 0.022f), 0.15f, 0.78f);
                if (_rng.Chance(pierceHitProb))
                {
                    float pierceDmg = weapon.Damage * 0.42f * (1f - defender.ArmorTier * eCfg.ArmorMitigationPerTier * 0.6f);
                    ApplyDamageToUnit(ref defender, attacker.UnitId, pierceDmg, isZoneDamage: false);
                }
            }
        }

        /// <summary>
        /// Formulas CMB-03, CMB-04, CMB-05, CMB-08: Hit probability, directional cover/flank/CQC mitigation, composure, recoil, and armor damage.
        /// </summary>
        private void FireWeaponBurst(
            ref UnitRuntime attacker,
            ref UnitRuntime defender,
            float dist,
            EngagementBalanceConfig eCfg,
            bool isTradeShot,
            bool bonusInitiativeShot = false)
        {
            SelectActiveWeaponForDistance(ref attacker, dist);
            WeaponDef weapon = _cfg.Weapons.Weapons[attacker.PrimaryWeaponIndex];
            if (dist > weapon.RangeMeters * 1.15f) return;

            MapCell attCell = _map.GetCellAtWorld(attacker.Position);
            MapCell defCell = _map.GetCellAtWorld(defender.Position);
            bool isWindowPeek = attCell.IsWindow || attCell.IsDoorway;

            // Update directional cover facing for both attacker and defender
            attacker.CoverFacingRadians = _map.GetCellCoverFacingRadians(attacker.Position, defender.Position);
            Vec2 defCoverRefPos = (defender.TargetUnitId >= 0 && defender.TargetUnitId < TotalUnits && _units[defender.TargetUnitId].State != UnitState.Eliminated)
                ? _units[defender.TargetUnitId].Position
                : attacker.Position;
            defender.CoverFacingRadians = _map.GetCellCoverFacingRadians(defender.Position, defCoverRefPos);

            // CMB-03: Base accuracy from weapon curve * Aim * range-specific attribute (Cqc / LongRange) * RoleProficiency (CMB-08)
            float weaponAcc = weapon.GetBaseAccuracy(dist);
            float aimMod = 0.69f + attacker.Eff.Aim * 0.0265f;

            float rangeAttrMod;
            if (dist <= 42f)
            {
                // Close range (<=42m): governed by Cqc and Reflex
                rangeAttrMod = (0.70f + attacker.Eff.Cqc * 0.026f) * (0.88f + attacker.Eff.Reflex * 0.011f);
            }
            else if (dist <= 78f)
            {
                // Mid range (42..78m): blended Cqc + LongRange + Reflex
                float t = (dist - 42f) / 36f;
                float blendedAttr = attacker.Eff.Cqc * (1f - t * 0.45f) + attacker.Eff.LongRange * (0.45f + t * 0.55f);
                rangeAttrMod = (0.72f + blendedAttr * 0.024f) * (0.90f + attacker.Eff.Reflex * 0.0085f);
            }
            else
            {
                // Long range (>78m): governed by LongRange + Cqc micro-tracking
                rangeAttrMod = (0.68f + attacker.Eff.LongRange * 0.027f) * (0.83f + attacker.Eff.Cqc * 0.015f);
            }

            // Pre-aim, strafe & angle selection governed by Awareness, Positioning, Movement, and Looting (attachments/readiness)
            float tacticalAimMod = (0.90f + attacker.Eff.Awareness * 0.0085f)
                                 * (0.89f + attacker.Eff.Positioning * 0.0095f)
                                 * (0.89f + attacker.Eff.Movement * 0.0095f)
                                 * (0.94f + attacker.Eff.Looting * 0.0050f);

            // CMB-08: RoleMod = 0.90 + 0.008 * RoleProficiency
            float roleMod = eCfg.RoleModBase + eCfg.RoleModScale * attacker.Eff.RoleProficiency;
            int weapTier = attacker.Eff.GetWeaponMasteryTier(weapon.Category);
            float weapAccMod = weapTier >= 1 ? 1.04f : 1.00f;
            float pHitBase = weaponAcc * aimMod * rangeAttrMod * tacticalAimMod * roleMod * weapAccMod;

            // CMB-04: Directional Target Cover, Flank Crossfire (>65 deg), and CQC Breach (<=12m)
            float rawCellCover = defCell.CoverNormalized;
            bool isFlankHit = false;
            bool isCqcBreach = false;

            if (defCell.CoverValue >= 35)
            {
                float incomingAngle = MathF.Atan2(attacker.Position.Y - defender.Position.Y, attacker.Position.X - defender.Position.X);
                float angleDiff = MathF.Abs(WrapAngle(incomingAngle - defender.CoverFacingRadians));
                if (angleDiff > 1.134f || (attacker.Role == Role.SecondRusher && dist <= 32f && defender.TargetUnitId != attacker.UnitId && defender.TargetUnitId >= 0))
                {
                    rawCellCover *= 0.50f;
                    isFlankHit = true;
                    defender.LastFlankedTick = _currentTick;
                }

                if (dist <= 12f)
                {
                    rawCellCover *= 0.35f;
                    isCqcBreach = true;
                }
            }

            float effectiveCover = Math.Clamp(rawCellCover * 0.55f + defender.Eff.Positioning * 0.020f + defender.Eff.Utility * 0.014f, 0f, 0.86f);
            float coverMitigation = 1f - effectiveCover * eCfg.CoverMitigationWeight;

            float evasionMod = Math.Clamp(
                (1f - (defender.Eff.Movement - eCfg.MovementNeutral) / 44f) * (1f - (defender.Eff.Reflex - 11.5f) * 0.010f),
                0.62f,
                1.28f);
            float heightMod = 1f + (attacker.HeightLevel - defender.HeightLevel) * eCfg.HeightAdvantagePerLevel;

            // Composure: affects Stage >= 3 and Clutch situations (<= 2 alive in squad)
            ref SquadRuntime attSquad = ref _squads[attacker.SquadId];
            ref SquadRuntime defSquad = ref _squads[defender.SquadId];
            bool isPressureSituation = _zone.CurrentStageNumber >= 3 || attSquad.AliveCount <= 2;
            float composureMod = isPressureSituation
                ? Math.Clamp(1f + (attacker.Eff.Composure - 11f) * 0.020f, 0.74f, 1.26f)
                : 1f + (attacker.Eff.Composure - 11f) * 0.005f;

            // Recoil: sustained fire stability & burst precision
            float effRecoil = attacker.Eff.Recoil * (weapTier >= 2 ? 1.06f : 1.00f);
            float recoilMod = Math.Clamp(
                (0.74f + effRecoil * 0.0225f) * (1f - Math.Min(6, attacker.ConsecutiveRoundsInEngagement) * Math.Max(0f, 20f - effRecoil) / 380f),
                0.55f,
                1.30f);

            // Stamina: physical steadiness in mid-to-late match (after tick 600)
            float staminaMod = _currentTick > 600
                ? Math.Clamp(1f + (attacker.Eff.Stamina - 11f) * 0.015f, 0.80f, 1.20f)
                : 1f;

            // IGL Decision, unit Decision & Squad Comms tactical coordination bonus
            float iglDecisionAtt = GetActiveIglDecision(ref attSquad);
            float avgCommsAtt = GetSquadAverageComms(ref attSquad);
            float decisionBlendAtt = iglDecisionAtt * 0.55f + attacker.Eff.Decision * 0.45f;
            float macroCoordinationMod = (0.81f + decisionBlendAtt * 0.0165f) * (0.80f + avgCommsAtt * 0.0175f);
            if (attSquad.HasActiveCommand && attSquad.ActiveCommandCompliant)
            {
                macroCoordinationMod *= 1.12f;
            }
            if (defSquad.HasActiveCommand && defSquad.ActiveCommandCompliant)
            {
                macroCoordinationMod *= 0.86f;
            }

            float pHit = Math.Clamp(
                pHitBase * coverMitigation * evasionMod * heightMod * composureMod * recoilMod * staminaMod * macroCoordinationMod,
                eCfg.MinHitChance,
                eCfg.MaxHitChance);

            int shots = isTradeShot ? Math.Max(1, weapon.ShotsPerRound - 1) : weapon.ShotsPerRound;
            if (bonusInitiativeShot && !isTradeShot) shots++;

            int hits = 0;
            for (int s = 0; s < shots; s++)
            {
                if (_rng.Chance(pHit))
                {
                    hits++;
                }
            }

            if (hits <= 0)
            {
                if (!isTradeShot && defCell.CoverValue >= 45 && !isFlankHit)
                {
                    _events.Record(new SimEvent(
                        _currentTick,
                        SimEventType.ShotFired,
                        actorUnitIdx: attacker.UnitId,
                        targetUnitIdx: defender.UnitId,
                        squadIdx: attacker.SquadId,
                        targetSquadIdx: defender.SquadId,
                        pos: attacker.Position,
                        pos2: defender.Position,
                        value: 0f,
                        extraCode: 32 | (isWindowPeek ? 256 : 0)));
                }
                return;
            }

            // CMB-05: Damage calculation with armor mitigation, Chrono Shield, Awareness ambush reduction, Determination resilience
            float armorMit = defender.ArmorTier * eCfg.ArmorMitigationPerTier
                           + defender.Eff.ArmorMitigationBonus
                           + (defender.Eff.Looting - 11.5f) * 0.003f;
            if (dist >= 45f && attacker.Eff.LongRangeArmorPenBonus > 0f)
            {
                armorMit -= attacker.Eff.LongRangeArmorPenBonus;
            }
            if (defender.ActiveSkillDurationTicks > 0 && defender.ActiveSkillCategory == SkillCategory.Shield)
            {
                armorMit += 0.40f; // Chrono Force Field mitigation
            }
            armorMit = Math.Clamp(armorMit, 0f, 0.75f);

            // High Awareness, Decision, Comms & CQC peek discipline reduce incoming burst damage
            float iglDecisionDef = GetActiveIglDecision(ref defSquad);
            float avgCommsDef = GetSquadAverageComms(ref defSquad);
            float decisionBlendDef = iglDecisionDef * 0.55f + defender.Eff.Decision * 0.45f;
            float awarenessProtection = Math.Clamp(
                (1f - (defender.Eff.Awareness - 11.5f) * 0.010f)
                * (1f - (decisionBlendDef - 11.5f) * 0.014f)
                * (1f - (avgCommsDef - 11.5f) * 0.015f)
                * (1f - (defender.Eff.Cqc - 11.5f) * 0.008f),
                0.65f,
                1.25f);

            // Attacker Cqc, LongRange, Aim, Recoil, Utility & Comms offensive lethality multipliers
            float offensiveLethality = (0.77f + attacker.Eff.Cqc * 0.020f)
                                     * (0.89f + attacker.Eff.LongRange * 0.0095f)
                                     * (0.88f + attacker.Eff.Aim * 0.0105f)
                                     * (0.88f + attacker.Eff.Recoil * 0.0105f)
                                     * (0.82f + attacker.Eff.Utility * 0.0155f)
                                     * (0.84f + avgCommsAtt * 0.0135f);
            if (weapTier >= 2) offensiveLethality *= 1.06f;
            if (weapTier >= 3) offensiveLethality *= 1.04f;
            if (dist <= 15f && attacker.ConsecutiveRoundsInEngagement <= 1 && attacker.Eff.CqcFirstShotBonus > 0f)
            {
                offensiveLethality *= 1f + attacker.Eff.CqcFirstShotBonus;
            }

            // Determination grants last-stand resilience when HP < 110 or squadmates are down
            float determinationMod = (defender.Hp < 110f || defSquad.AliveCount < 4)
                ? Math.Clamp(1f - (defender.Eff.Determination - 10f) * 0.015f, 0.78f, 1.15f)
                : 1f;

            float totalDamage = hits * weapon.Damage * offensiveLethality * (1f - armorMit) * awarenessProtection * determinationMod;

            // Free Fire Drag-Shot Headshot ("拉枪爆头") governed by Aim, Recoil, and mitigated by defender HelmetTier
            float dragHeadshotProb = Math.Clamp(
                0.08f + (attacker.Eff.Aim - 10f) * 0.015f + (attacker.Eff.Recoil - 10f) * 0.010f + attacker.Eff.HeadshotChanceBonus - defender.HelmetTier * 0.025f,
                0.04f,
                0.45f);
            bool isHeadshot = _rng.Chance(dragHeadshotProb);
            if (isHeadshot)
            {
                float headshotMult = Math.Clamp(1.42f - defender.HelmetTier * 0.07f, 1.18f, 1.42f);
                totalDamage *= headshotMult;
            }

            int coverBlockedBit = (defCell.CoverValue >= 50 && !isFlankHit && !isCqcBreach && hits <= 1 && shots >= 2) ? 32 : 0;
            int packedExtra = (hits & 15)
                            | (isHeadshot ? 16 : 0)
                            | coverBlockedBit
                            | (isFlankHit ? 64 : 0)
                            | (isCqcBreach ? 128 : 0)
                            | (isWindowPeek ? 256 : 0);

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.ShotFired,
                actorUnitIdx: attacker.UnitId,
                targetUnitIdx: defender.UnitId,
                squadIdx: attacker.SquadId,
                targetSquadIdx: defender.SquadId,
                pos: attacker.Position,
                pos2: defender.Position,
                value: totalDamage,
                extraCode: packedExtra));

            bool wasAlive = defender.State == UnitState.Alive;
            ApplyDamageToUnit(ref defender, attacker.UnitId, totalDamage, isZoneDamage: false);

            // Reactive Auto-Gloo Wall placement by defender if damaged and has Gloo (WP-08) — outdoor only
            if (defender.State == UnitState.Alive && !defCell.IsBuilding && defender.GlooCount > 0 && !HasNearbyFriendlyGloo(defender.Position, 3.8f))
            {
                float glooTriggerProb = Math.Clamp(0.25f + defender.Eff.Utility * 0.032f, 0.25f, 0.90f);
                if (_rng.Chance(glooTriggerProb))
                {
                    Vec2 dirToAttacker = (attacker.Position - defender.Position).Normalized();
                    PlaceGlooWall(ref defender, defender.Position + dirToAttacker * 2.0f);
                }
            }

            // CMB-06: Trade-Fire trigger when a teammate is knocked or heavily damaged within 20m
            if (!isTradeShot && (wasAlive && defender.State == UnitState.Knocked || totalDamage >= 35f))
            {
                TryTriggerSquadTradeFire(ref defender, ref attacker, eCfg);
            }
        }

        /// <summary>
        /// Formula CMB-06: Trade-fire probability governed by Teamwork, Squad Synergy, and SecondRusher proficiency.
        /// </summary>
        private void TryTriggerSquadTradeFire(ref UnitRuntime victim, ref UnitRuntime aggressor, EngagementBalanceConfig eCfg)
        {
            if (aggressor.State != UnitState.Alive) return;

            ref SquadRuntime victimSquad = ref _squads[victim.SquadId];
            int first = victimSquad.FirstUnitId;

            for (int m = 0; m < UnitsPerSquad; m++)
            {
                int allyId = first + m;
                if (allyId == victim.UnitId) continue;

                ref UnitRuntime ally = ref _units[allyId];
                if (ally.State != UnitState.Alive) continue;

                if (Vec2.DistanceSquared(ally.Position, victim.Position) > eCfg.TradeRadiusMeters * eCfg.TradeRadiusMeters)
                {
                    continue;
                }

                float distToAggressor = Vec2.Distance(ally.Position, aggressor.Position);
                WeaponDef allyWeapon = _cfg.Weapons.Weapons[ally.PrimaryWeaponIndex];
                if (distToAggressor > allyWeapon.RangeMeters) continue;

                if (!LineOfSight.HasLineOfSight(_map, ally.Position, aggressor.Position, _glooWalls, _glooWallCount))
                {
                    continue;
                }

                float secondRusherBonus = ally.Role == Role.SecondRusher ? ally.Eff.RoleProficiency / eCfg.TradeSecondRusherDivisor : 0f;
                float pTrade = Math.Clamp(
                    eCfg.TradeBaseChance
                    + ally.Eff.Teamwork / eCfg.TradeTeamworkDivisor
                    + victimSquad.Synergy / eCfg.TradeSynergyDivisor
                    + secondRusherBonus,
                    0.12f,
                    0.75f);

                if (_rng.Chance(pTrade))
                {
                    ally.TargetUnitId = aggressor.UnitId;
                    FireWeaponBurst(ref ally, ref aggressor, distToAggressor, eCfg, isTradeShot: true);
                    break; // Max 1 immediate trade burst per damage event
                }
            }
        }

        /// <summary>
        /// Formula CMB-09: Throwable Grenade trigger & blast damage governed by Utility and Grenadier role proficiency.
        /// Includes Free Fire Indoor Breach Grenade ("攻楼灌雷"): +30% trigger rate and +25% confined room blast damage.
        /// </summary>
        private bool TryThrowGrenade(ref UnitRuntime attacker, ref UnitRuntime defender, float dist)
        {
            if (attacker.GrenadeCount <= 0) return false;
            var tCfg = _cfg.Weapons.Throwable;
            if (dist < tCfg.MinRangeMeters || dist > tCfg.MaxRangeMeters) return false;

            bool targetIndoor = _map.GetCellAtWorld(defender.Position).IsBuilding;
            float grenadierBonus = attacker.Role == Role.Grenadier ? 0.18f : 0f;
            float pThrow = Math.Clamp(tCfg.BaseTriggerChance + attacker.Eff.Utility * 0.022f + grenadierBonus, 0.15f, 0.78f);
            if (targetIndoor)
            {
                pThrow = Math.Min(0.88f, pThrow * 1.30f);
            }
            if (!_rng.Chance(pThrow)) return false;

            attacker.GrenadeCount--;
            attacker.GrenadesThrown++;

            Vec2 blastCenter = defender.Position;
            float dmgMultiplier = (0.75f + attacker.Eff.Utility / 32f) * (attacker.Role == Role.Grenadier ? (0.95f + attacker.Eff.RoleProficiency * 0.015f) : 1.0f);
            if (targetIndoor)
            {
                dmgMultiplier *= 1.25f;
            }
            float baseBlastDmg = tCfg.BaseDamage * dmgMultiplier;
            float effBlastRadius = tCfg.BlastRadiusMeters * (1f + attacker.Eff.GrenadeRadiusBonus);

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.GrenadeThrown,
                actorUnitIdx: attacker.UnitId,
                targetUnitIdx: defender.UnitId,
                squadIdx: attacker.SquadId,
                targetSquadIdx: defender.SquadId,
                pos: attacker.Position,
                pos2: blastCenter,
                value: baseBlastDmg,
                extraCode: targetIndoor ? 1 : 0));

            // Damage Gloo Walls within blast radius (2.5x multiplier)
            for (int g = 0; g < _glooWallCount; g++)
            {
                if (_glooWalls[g].Hp > 0f && Vec2.Distance(blastCenter, _glooWalls[g].Position) <= effBlastRadius + _glooWalls[g].Radius)
                {
                    _glooWalls[g].Hp -= baseBlastDmg * tCfg.GlooDamageMultiplier;
                }
            }

            // Damage enemy units within blast radius
            Span<int> hitUnits = stackalloc int[48];
            int hitCount = _spatialGrid.QueryRadius(blastCenter, effBlastRadius, hitUnits);
            for (int i = 0; i < hitCount; i++)
            {
                int uId = hitUnits[i];
                if (_units[uId].SquadId == attacker.SquadId || _units[uId].State == UnitState.Eliminated) continue;
                float d = Vec2.Distance(blastCenter, _units[uId].Position);
                float falloff = Math.Clamp(1f - (d / (effBlastRadius + 1.5f)) * 0.65f, 0.25f, 1f);
                ApplyDamageToUnit(ref _units[uId], attacker.UnitId, baseBlastDmg * falloff, isZoneDamage: false);
            }

            return true;
        }

        /// <summary>
        /// Formula CMB-10: Active Skill trigger & cooldown reduction governed by Utility and SkillProficiency.
        /// </summary>
        private void TryTriggerActiveSkill(ref UnitRuntime unit, ref UnitRuntime target, float dist)
        {
            if (unit.ActiveSkillCooldownTicks > 0 || unit.ActiveSkillIndex < 0) return;

            SkillDef sk = _cfg.Skills.Skills[unit.ActiveSkillIndex];
            bool shouldTrigger = false;

            switch (unit.ActiveSkillCategory)
            {
                case SkillCategory.Shield:
                    shouldTrigger = unit.Hp < 165f || dist < 45f;
                    break;
                case SkillCategory.Dash:
                    shouldTrigger = dist < 38f || unit.Hp < 130f;
                    break;
                case SkillCategory.Heal:
                    shouldTrigger = unit.Hp < 170f;
                    break;
                case SkillCategory.Scan:
                    shouldTrigger = true;
                    break;
                case SkillCategory.Deploy:
                    // Skyler Riptide destroys enemy Gloo Walls or damages cover
                    shouldTrigger = _glooWallCount > 0 || dist < 50f;
                    break;
            }

            if (!shouldTrigger) return;

            // CMB-10: EffectiveCooldown = BaseCooldown * (1 - Utility/80) * (1 - SkillProf/100)
            float effCooldownSec = sk.CooldownSec
                                 * (1f - unit.Eff.Utility / 80f)
                                 * (1f - unit.Eff.ActiveSkillProficiency / 100f);
            unit.ActiveSkillCooldownTicks = Math.Max(80, (int)MathF.Round(effCooldownSec * 10f));
            unit.ActiveSkillDurationTicks = Math.Max(10, (int)MathF.Round(sk.DurationSec * 10f));
            unit.SkillsActivated++;

            // If Deploy (Skyler Riptide), destroy up to 2 Gloo Walls near target
            if (unit.ActiveSkillCategory == SkillCategory.Deploy)
            {
                int destroyed = 0;
                for (int g = 0; g < _glooWallCount && destroyed < 2; g++)
                {
                    if (_glooWalls[g].Hp > 0f && Vec2.Distance(target.Position, _glooWalls[g].Position) <= 18f)
                    {
                        _glooWalls[g].Hp = 0f;
                        destroyed++;
                    }
                }
            }

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.SkillTriggered,
                actorUnitIdx: unit.UnitId,
                squadIdx: unit.SquadId,
                pos: unit.Position,
                extraCode: unit.ActiveSkillIndex));
        }

        private void PlaceGlooWall(ref UnitRuntime unit, Vec2 pos)
        {
            if (unit.GlooCount <= 0 || _glooWallCount >= SnapshotBuffer.MaxGlooWalls) return;
            if (_map.GetCellAtWorld(unit.Position).IsBuilding || _map.GetCellAtWorld(pos).IsBuilding) return;

            unit.GlooCount--;
            unit.GlooWallsPlaced++;

            // Gloo Wall HP scales with Utility (220 + Utility * 10) * GlooHpMultiplier (tr_gloo_architect)
            float glooHp = (220f + unit.Eff.Utility * 10f) * Math.Max(1f, unit.Eff.GlooHpMultiplier);
            int id = _nextGlooId++;

            _glooWalls[_glooWallCount++] = new DynamicLosBlocker
            {
                Id = id,
                Position = _map.ClampToWalkableWorld(pos),
                Radius = 2.8f,
                Hp = glooHp,
                OwnerUnitId = unit.UnitId,
                SpawnTick = _currentTick,
                ExpireTick = _currentTick + 180 // 18s duration
            };

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.GlooPlaced,
                actorUnitIdx: unit.UnitId,
                squadIdx: unit.SquadId,
                pos: pos,
                value: glooHp,
                extraCode: id));
        }

        private void ApplyDamageToUnit(ref UnitRuntime victim, int attackerUnitId, float damage, bool isZoneDamage)
        {
            if (victim.State == UnitState.Eliminated || damage <= 0f) return;

            victim.DamageTaken += damage;
            victim.LastDamagedTick = _currentTick;
            if (attackerUnitId >= 0)
            {
                victim.LastAttackerUnitId = attackerUnitId;
                _units[attackerUnitId].DamageDealt += damage;
                _squads[_units[attackerUnitId].SquadId].TotalDamage += damage;
                _squads[victim.SquadId].UnderFireFromSquadId = _units[attackerUnitId].SquadId;
                _squads[victim.SquadId].UnderFireTick = _currentTick;
            }

            victim.Hp -= damage;
            if (victim.Hp > 0f) return;

            if (victim.State == UnitState.Alive)
            {
                // Dismount vehicle if knocked
                if (victim.VehicleId >= 0 && victim.VehicleId < _vehicleCount)
                {
                    _vehicles[victim.VehicleId].DriverUnitId = -1;
                    _vehicles[victim.VehicleId].SquadId = -1;
                    victim.VehicleId = -1;
                }

                // Check if any other squadmate is still Alive; if none, entire squad is wiped immediately!
                ref SquadRuntime sq = ref _squads[victim.SquadId];
                int otherAlive = 0;
                for (int m = 0; m < UnitsPerSquad; m++)
                {
                    int uId = sq.FirstUnitId + m;
                    if (uId != victim.UnitId && _units[uId].State == UnitState.Alive)
                    {
                        otherAlive++;
                    }
                }

                if (otherAlive > 0)
                {
                    victim.State = UnitState.Knocked;
                    victim.Hp = 100f;
                    victim.KnockedRemainTicks = (int)MathF.Round(_cfg.Engagement.BleedOutSec * 10f);
                    victim.ReviveProgressTicks = 0;
                    victim.ReviverUnitId = -1;

                    if (attackerUnitId >= 0)
                    {
                        _units[attackerUnitId].KnockdownsScored++;
                    }

                    _events.Record(new SimEvent(
                        _currentTick,
                        SimEventType.Knocked,
                        actorUnitIdx: attackerUnitId,
                        targetUnitIdx: victim.UnitId,
                        squadIdx: attackerUnitId >= 0 ? _units[attackerUnitId].SquadId : -1,
                        targetSquadIdx: victim.SquadId,
                        pos: victim.Position));
                }
                else
                {
                    // Wipe the victim and any already-knocked squadmates
                    EliminateUnit(ref victim, attackerUnitId);
                    for (int m = 0; m < UnitsPerSquad; m++)
                    {
                        int uId = sq.FirstUnitId + m;
                        if (_units[uId].State == UnitState.Knocked)
                        {
                            EliminateUnit(ref _units[uId], _units[uId].LastAttackerUnitId);
                        }
                    }
                }
            }
            else if (victim.State == UnitState.Knocked)
            {
                EliminateUnit(ref victim, attackerUnitId >= 0 ? attackerUnitId : victim.LastAttackerUnitId);
            }
        }

        private void EliminateUnit(ref UnitRuntime victim, int killerUnitId)
        {
            if (victim.State == UnitState.Eliminated) return;

            victim.State = UnitState.Eliminated;
            victim.Hp = 0f;
            _aliveUnitCount = Math.Max(0, _aliveUnitCount - 1);

            int killerSquadId = -1;
            if (killerUnitId >= 0 && killerUnitId < TotalUnits && _units[killerUnitId].SquadId != victim.SquadId)
            {
                _units[killerUnitId].Kills++;
                killerSquadId = _units[killerUnitId].SquadId;
                _squads[killerSquadId].TotalKills++;
            }

            _events.Record(new SimEvent(
                _currentTick,
                SimEventType.Eliminated,
                actorUnitIdx: killerUnitId,
                targetUnitIdx: victim.UnitId,
                squadIdx: killerSquadId,
                targetSquadIdx: victim.SquadId,
                pos: victim.Position));

            // Update squad alive count & check squad wipe
            ref SquadRuntime sq = ref _squads[victim.SquadId];
            int remainingInSquad = 0;
            for (int m = 0; m < UnitsPerSquad; m++)
            {
                if (_units[sq.FirstUnitId + m].State != UnitState.Eliminated)
                {
                    remainingInSquad++;
                }
            }
            sq.AliveCount = remainingInSquad;

            if (remainingInSquad == 0 && sq.Placement == 0)
            {
                sq.Placement = _nextPlacementToAssign--;
                sq.EliminatedTick = _currentTick;
                _aliveSquadCount = Math.Max(0, _aliveSquadCount - 1);

                _events.Record(new SimEvent(
                    _currentTick,
                    SimEventType.SquadWiped,
                    squadIdx: victim.SquadId,
                    targetSquadIdx: killerSquadId,
                    pos: victim.Position,
                    extraCode: sq.Placement));
            }
        }
    }
}
