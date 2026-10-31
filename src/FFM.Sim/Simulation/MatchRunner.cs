using System;
using System.Collections.Generic;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Entities;
using FFM.Sim.Map;

namespace FFM.Sim.Simulation
{
    /// <summary>
    /// Deterministic 10Hz Battle Royale match simulator for 12 squads (48 units).
    /// Split across partial files (MatchRunner, .SquadAI, .UnitAI, .Combat, .Commands) per §0.1.
    /// Zero heap allocations during Step().
    /// </summary>
    public sealed partial class MatchRunner : ISimulation
    {
        public const int SquadCount = 12;
        public const int UnitsPerSquad = 4;
        public const int TotalUnits = SquadCount * UnitsPerSquad; // 48
        public const int MaxWaypointsPerSquad = 32;
        public const int MaxTicksLimit = 3600; // 6 minutes max cap

        private readonly ConfigBundle _cfg;
        private readonly MatchConfig _matchCfg;
        private readonly MapData _map;

        private Rng _rng = new(1UL);
        private readonly ZoneController _zone = new();
        private readonly CoarseAStar _astar = new();
        private readonly SpatialGrid48 _spatialGrid = new();
        private readonly EventLog _events = new(16384);

        private readonly UnitRuntime[] _units = new UnitRuntime[TotalUnits];
        private readonly SquadRuntime[] _squads = new SquadRuntime[SquadCount];
        private readonly Vec2[,] _squadWaypoints = new Vec2[SquadCount, MaxWaypointsPerSquad];
        private readonly Vec2[] _pathScratch = new Vec2[MaxWaypointsPerSquad];

        private readonly DynamicLosBlocker[] _glooWalls = new DynamicLosBlocker[SnapshotBuffer.MaxGlooWalls];
        private int _glooWallCount;
        private int _nextGlooId = 1;

        private readonly VehicleRuntime[] _vehicles = new VehicleRuntime[SnapshotBuffer.MaxVehicles];
        private int _vehicleCount;

        private readonly bool[] _unitOffDay = new bool[TotalUnits];
        private readonly float[] _unitFatigueIn = new float[TotalUnits];
        private readonly int[] _unitSurvivedTicks = new int[TotalUnits];

        private readonly SnapshotBuffer _snapA = new();
        private readonly SnapshotBuffer _snapB = new();
        private SnapshotBuffer _prevSnap;
        private SnapshotBuffer _currSnap;

        private int _currentTick;
        private Phase _currentPhase;
        private int _aliveSquadCount;
        private int _aliveUnitCount;
        private int _nextPlacementToAssign;

        // Transport Aircraft Flight Path state (Phase.Drop)
        private Vec2 _flightStartPos;
        private Vec2 _flightEndPos;
        private Vec2 _flightDir;
        private float _flightLength;
        private float _flightFacingRadians;
        private bool _flightActive;
        private Vec2 _flightCurrentPos;
        private float _flightAltitudeMeters;

        public int CurrentTick => _currentTick;
        public Phase CurrentPhase => _currentPhase;
        public bool IsFinished => _currentPhase == Phase.Finished;
        public MapData Map => _map;
        public SnapshotBuffer PrevSnapshot => _prevSnap;
        public SnapshotBuffer CurrSnapshot => _currSnap;
        public EventLog Events => _events;

        public MatchRunner(ConfigBundle cfg, MapData map, MatchConfig matchCfg)
        {
            _cfg = cfg ?? throw new ArgumentNullException(nameof(cfg));
            _map = map ?? throw new ArgumentNullException(nameof(map));
            _matchCfg = matchCfg ?? throw new ArgumentNullException(nameof(matchCfg));

            if (matchCfg.Squads.Count != SquadCount)
            {
                throw new ArgumentException($"MatchRunner requires exactly {SquadCount} squads, got {matchCfg.Squads.Count}.");
            }

            _prevSnap = _snapA;
            _currSnap = _snapB;
            InitializeMatch();
        }

        private void InitializeMatch()
        {
            _rng = new Rng(_matchCfg.Seed == 0 ? 1UL : _matchCfg.Seed);
            _events.Reset();
            _zone.Initialize(_map, ref _rng);

            _currentTick = 0;
            _currentPhase = Phase.Drop;
            _aliveSquadCount = SquadCount;
            _aliveUnitCount = TotalUnits;
            _nextPlacementToAssign = SquadCount;
            _glooWallCount = 0;
            _nextGlooId = 1;

            // Initialize vehicles from map spawns
            _vehicleCount = Math.Min(_map.VehicleSpawns.Count, SnapshotBuffer.MaxVehicles);
            for (int v = 0; v < _vehicleCount; v++)
            {
                var vs = _map.VehicleSpawns[v];
                _vehicles[v] = new VehicleRuntime
                {
                    Id = vs.VehicleId,
                    Position = new Vec2(vs.X, vs.Y),
                    Hp = vs.MaxHp,
                    MaxHp = vs.MaxHp,
                    SpeedMultiplier = vs.SpeedMultiplier,
                    DriverUnitId = -1,
                    SquadId = -1
                };
            }

            // Initialize 12 squads & 48 units
            for (int s = 0; s < SquadCount; s++)
            {
                SquadDef sDef = _matchCfg.Squads[s];
                int firstUnitId = s * UnitsPerSquad;
                int iglUnitId = firstUnitId;

                ResolvePlaybookModifiers(sDef.Playbook, out float hotspotMod, out float earlyRotSec, out float aggroMod, out float hgBias, out float covBias, out bool glooFort);

                _squads[s] = new SquadRuntime
                {
                    SquadId = s,
                    FirstUnitId = firstUnitId,
                    Synergy = Math.Clamp(sDef.Synergy, 0f, 100f),
                    IglUnitId = firstUnitId,
                    HotspotWeightMod = hotspotMod,
                    EarlyRotateSecOffset = earlyRotSec,
                    AggressionMod = aggroMod,
                    HighGroundBias = hgBias,
                    CoverBias = covBias,
                    IsGlooFortEndgame = glooFort,
                    DropPoiId = 1,
                    MacroGoal = new Vec2(1000f, 1000f),
                    CurrentWaypointIndex = 0,
                    WaypointCount = 0,
                    NextAiEvalTick = s + 1, // Stagger AI eval across ticks
                    PendingRotateTargetReadyTick = -1,
                    FocusTargetUnitId = -1,
                    UnderFireFromSquadId = -1,
                    UnderFireTick = -100,
                    CommandsRemaining = _cfg.Commands.MaxCommandsPerMatch,
                    CommandCooldownTicks = 0,
                    HasPendingCommand = false,
                    HasActiveCommand = false,
                    AliveCount = UnitsPerSquad,
                    TotalKills = 0,
                    TotalDamage = 0f,
                    Placement = 0,
                    EliminatedTick = -1
                };

                // Check if any member in the squad has Falco pet / glide passive or Sky Eye IGL trait
                float squadDropSpeedMult = 1.0f;
                for (int mCheck = 0; mCheck < UnitsPerSquad; mCheck++)
                {
                    var checkSlot = sDef.Members[mCheck];
                    for (int pk = 0; pk < checkSlot.PassiveSkillIds.Count; pk++)
                    {
                        string pid = checkSlot.PassiveSkillIds[pk];
                        if (pid.Contains("falco", StringComparison.OrdinalIgnoreCase) || pid == "sk_ agility" || pid == "sk_dash_passive")
                        {
                            squadDropSpeedMult = 1.18f;
                        }
                    }
                    var progCheck = checkSlot.Progression ?? checkSlot.Player.GetProgression();
                    if (progCheck.SignatureTraitIds.Contains("igl_sky_eye"))
                    {
                        squadDropSpeedMult = Math.Max(squadDropSpeedMult, 1.18f);
                    }
                }

                for (int m = 0; m < UnitsPerSquad; m++)
                {
                    int uId = firstUnitId + m;
                    var slot = sDef.Members[m];
                    if (slot.IsIgl) iglUnitId = uId;

                    float fatIn = slot.Player.Condition.Fatigue;
                    if (_matchCfg.FatigueIn.TryGetValue(slot.Player.PlayerId, out float overrideFat))
                    {
                        fatIn = overrideFat;
                    }
                    _unitFatigueIn[uId] = fatIn;

                    EffectiveAttributes eff = ComputePreMatchEffectiveAttributes(slot, fatIn, _matchCfg.Context.IsFinals, out bool offDay);
                    _unitOffDay[uId] = offDay;
                    _unitSurvivedTicks[uId] = 0;

                    var prog = slot.Progression ?? slot.Player.GetProgression();
                    int bonusGloo = prog.SignatureTraitIds.Contains("gloo_architect") ? 1 : 0;
                    int bonusNade = prog.SignatureTraitIds.Contains("demolition_expert") ? 1 : 0;

                    int activeSkillIdx = FindSkillIndex(slot.ActiveSkillId, out SkillCategory skCat);
                    int startWeaponIdx = FindStarterWeaponIndex(slot.Role);
                    int secondaryWeaponIdx = FindSecondaryStarterWeaponIndex(slot.Role);

                    _units[uId] = new UnitRuntime
                    {
                        UnitId = uId,
                        SquadId = s,
                        SlotIndexInSquad = m,
                        Role = slot.Role,
                        IsIgl = slot.IsIgl,
                        Eff = eff,
                        Position = new Vec2(1000f, 1000f),
                        DesiredPosition = new Vec2(1000f, 1000f),
                        FacingRadians = 0f,
                        HeightLevel = 1,
                        State = UnitState.Alive,
                        DropState = UnitDropState.InPlane,
                        AltitudeMeters = 420f,
                        LandingTargetPos = new Vec2(1000f, 1000f),
                        DropSpeedMultiplier = squadDropSpeedMult,
                        Hp = 200f,
                        MaxHp = 200f,
                        ArmorTier = 1,
                        HelmetTier = 1,
                        ArmorDurability = 100f,
                        PrimaryWeaponIndex = startWeaponIdx,
                        SecondaryWeaponIndex = secondaryWeaponIdx,
                        GlooCount = 2 + bonusGloo,
                        GrenadeCount = (slot.Role == Role.Grenadier ? 2 : 1) + bonusNade,
                        MedkitCount = 2,
                        ActiveSkillIndex = activeSkillIdx,
                        ActiveSkillCategory = skCat,
                        ActiveSkillCooldownTicks = 0,
                        ActiveSkillDurationTicks = 0,
                        TargetUnitId = -1,
                        ConsecutiveRoundsInEngagement = 0,
                        LastAttackerUnitId = -1,
                        LastDamagedTick = -100,
                        SpottedEnemyMask = 0,
                        KnockedRemainTicks = 0,
                        ReviveProgressTicks = 0,
                        ReviverUnitId = -1,
                        HealProgressTicks = 0,
                        LootCooldownTicks = 10,
                        VehicleId = -1,
                        CoverFacingRadians = 0f,
                        LastFlankedTick = -100,
                        LastBreachTick = -100
                    };
                }

                _squads[s].IglUnitId = iglUnitId;
            }

            _events.Record(new SimEvent(0, SimEventType.MatchStarted));

            // Select drop POIs for all 12 squads and place units on the transport aircraft
            ExecuteDropPhaseSelection();

            WriteSnapshot(_currSnap);
            _currSnap.CopyTo(_prevSnap);
        }

        /// <summary>
        /// Formula CMB-11 & CMB-12: Pre-match Consistency daily roll, BigMatch Finals bonus, Form, Fatigue penalty,
        /// plus Player Level/Star progression, 5-Category Weapon Mastery tiers, and 8 Esports Signature Traits.
        /// </summary>
        private EffectiveAttributes ComputePreMatchEffectiveAttributes(
            SquadRuntimePlayerSlot slot,
            float fatigueIn,
            bool isFinals,
            out bool isOffDay)
        {
            PlayerDef p = slot.Player;
            PlayerProgressionData prog = slot.Progression ?? p.GetProgression();
            var fatCfg = _cfg.Fatigue;

            bool hasClutchGene = prog.SignatureTraitIds.Contains("clutch_gene");

            // CMB-11: P(normal) = 0.5 + Consistency / 25.0
            float pNormal = Math.Clamp(0.5f + p.Hidden.Consistency / fatCfg.ConsistencyNormalDivisor, 0.52f, 0.98f);
            isOffDay = !(isFinals && hasClutchGene) && !_rng.Chance(pNormal);
            float dailyDelta = isOffDay
                ? -(fatCfg.OffDayPenaltyMean + (_rng.NextFloat01() - 0.5f) * 2f * fatCfg.OffDayPenaltySpread)
                : (_rng.NextFloat01() - 0.35f) * 0.6f;

            // BigMatch bonus in Grand Finals
            float bigMatchDelta = isFinals ? (p.Hidden.BigMatch - 10f) * fatCfg.BigMatchFinalsScale : 0f;

            // CMB-12: Fatigue threshold = 25 + Stamina * 1.0; penalty = max(0, (Fatigue - Threshold) / 12.0)
            float fatThreshold = fatCfg.FatigueThresholdBase + p.Condition.Stamina * fatCfg.FatigueThresholdStaminaScale;
            float fatiguePenalty = Math.Max(0f, (fatigueIn - fatThreshold) / fatCfg.FatiguePenaltyDivisor);

            // Progression Level & Star bonus (+0.04 per level above 8, +0.18 per star above 1)
            float progressionBonus = Math.Max(0f, (prog.Level - 8) * 0.035f) + Math.Max(0, prog.StarTier - 1) * 0.15f;
            float globalMod = p.Condition.Form + dailyDelta + bigMatchDelta + progressionBonus - fatiguePenalty;

            // Apply passive skill attribute bonuses (+1.0 to bonusAttribute or +0.05 armor mitigation)
            float bonusAim = 0f, bonusMove = 0f, bonusUtility = 0f, bonusTeamwork = 0f, bonusLooting = 0f, bonusComposure = 0f;
            float armorBonus = 0f;
            for (int i = 0; i < slot.PassiveSkillIds.Count; i++)
            {
                var sk = _cfg.Skills.FindSkill(slot.PassiveSkillIds[i]);
                if (sk == null) continue;
                switch (sk.BonusAttribute)
                {
                    case "aim": bonusAim += sk.EffectValue; break;
                    case "movement": bonusMove += sk.EffectValue; break;
                    case "utility": bonusUtility += sk.EffectValue; break;
                    case "teamwork": bonusTeamwork += sk.EffectValue; break;
                    case "looting": bonusLooting += sk.EffectValue; break;
                    case "composure": armorBonus += sk.EffectValue; bonusComposure += 0.8f; break;
                }
            }

            if (hasClutchGene)
            {
                bonusComposure += 2.5f;
            }

            static float ClampAttr(float v) => Math.Clamp(v, 1f, 20f);

            float roleProf = 12f;
            if (p.RoleProficiency.TryGetValue(slot.Role.ToString(), out float rp)) roleProf = rp;

            float skillProf = 12f;
            if (p.SkillProficiency.TryGetValue(slot.ActiveSkillId, out float sp)) skillProf = sp;

            prog.WeaponMastery.TryGetValue("SG", out float sgM);
            prog.WeaponMastery.TryGetValue("SMG", out float smgM);
            prog.WeaponMastery.TryGetValue("AR", out float arM);
            prog.WeaponMastery.TryGetValue("DMR", out float dmrM);
            prog.WeaponMastery.TryGetValue("SR", out float srM);

            byte sgTier = (byte)PlayerProgressionData.GetWeaponMasteryTier(sgM);
            byte smgTier = (byte)PlayerProgressionData.GetWeaponMasteryTier(smgM);
            byte arTier = (byte)PlayerProgressionData.GetWeaponMasteryTier(arM);
            byte dmrTier = (byte)PlayerProgressionData.GetWeaponMasteryTier(dmrM);
            byte srTier = (byte)PlayerProgressionData.GetWeaponMasteryTier(srM);

            float cqcFirstShot = (sgTier >= 3 ? 0.08f : 0f) + (prog.SignatureTraitIds.Contains("rusher_entry_king") ? 0.12f : 0f);
            float longRangeArmorPen = (dmrTier >= 3 || srTier >= 3) ? 0.10f : 0f;
            float hsBonus = (arTier >= 3 ? 0.04f : 0f) + (prog.SignatureTraitIds.Contains("headshot_machine") ? 0.06f : 0f);
            float glooHpMult = prog.SignatureTraitIds.Contains("gloo_architect") ? 1.15f : 1.0f;
            float zoneDpsRed = prog.SignatureTraitIds.Contains("zone_survivor") ? 0.20f : 0f;
            float reviveSpdMult = prog.SignatureTraitIds.Contains("field_medic") ? 1.25f : 1.0f;
            float reviveHpBonus = prog.SignatureTraitIds.Contains("field_medic") ? 30f : 0f;
            float nadeRadBonus = prog.SignatureTraitIds.Contains("demolition_expert") ? 0.12f : 0f;

            return new EffectiveAttributes
            {
                Aim = ClampAttr(p.Mechanical.Aim + globalMod + bonusAim),
                Recoil = ClampAttr(p.Mechanical.Recoil + globalMod),
                Reflex = ClampAttr(p.Mechanical.Reflex + globalMod),
                Movement = ClampAttr(p.Mechanical.Movement + globalMod + bonusMove),
                Cqc = ClampAttr(p.Mechanical.Cqc + globalMod),
                LongRange = ClampAttr(p.Mechanical.LongRange + globalMod),

                Awareness = ClampAttr(p.Tactical.Awareness + globalMod + (prog.SignatureTraitIds.Contains("igl_sky_eye") ? 1.5f : 0f)),
                Positioning = ClampAttr(p.Tactical.Positioning + globalMod),
                Utility = ClampAttr(p.Tactical.Utility + globalMod + bonusUtility),
                Looting = ClampAttr(p.Tactical.Looting + globalMod + bonusLooting),
                Decision = ClampAttr(p.Tactical.Decision + globalMod + (prog.SignatureTraitIds.Contains("igl_sky_eye") ? 1.5f : 0f)),
                Comms = ClampAttr(p.Tactical.Comms + globalMod),

                Composure = ClampAttr(p.Mental.Composure + globalMod + bonusComposure),
                Teamwork = ClampAttr(p.Mental.Teamwork + globalMod + bonusTeamwork),
                Determination = ClampAttr(p.Mental.Determination + globalMod),
                Stamina = ClampAttr(p.Condition.Stamina),

                RoleProficiency = Math.Clamp(roleProf, 1f, 20f),
                ActiveSkillProficiency = Math.Clamp(skillProf, 1f, 20f),
                ArmorMitigationBonus = armorBonus,

                MasteryTierSg = sgTier,
                MasteryTierSmg = smgTier,
                MasteryTierAr = arTier,
                MasteryTierDmr = dmrTier,
                MasteryTierSr = srTier,
                CqcFirstShotBonus = cqcFirstShot,
                LongRangeArmorPenBonus = longRangeArmorPen,
                HeadshotChanceBonus = hsBonus,
                GlooHpMultiplier = glooHpMult,
                ZoneDpsReduction = zoneDpsRed,
                ReviveSpeedMultiplier = reviveSpdMult,
                ReviveBonusHp = reviveHpBonus,
                GrenadeRadiusBonus = nadeRadBonus
            };
        }

        private void ResolvePlaybookModifiers(
            PlaybookDef pb,
            out float hotspotMod,
            out float earlyRotSec,
            out float aggroMod,
            out float hgBias,
            out float covBias,
            out bool glooFort)
        {
            hotspotMod = 1f;
            earlyRotSec = 0f;
            aggroMod = 1f;
            hgBias = 1f;
            covBias = 1f;
            glooFort = string.Equals(pb.Endgame, "GlooFort", StringComparison.OrdinalIgnoreCase);

            ApplyModList(_cfg.Playbooks.DropOptions, pb.Drop, ref hotspotMod, ref earlyRotSec, ref aggroMod, ref hgBias, ref covBias);
            ApplyModList(_cfg.Playbooks.RotationOptions, pb.Rotation, ref hotspotMod, ref earlyRotSec, ref aggroMod, ref hgBias, ref covBias);
            ApplyModList(_cfg.Playbooks.EngageOptions, pb.Engage, ref hotspotMod, ref earlyRotSec, ref aggroMod, ref hgBias, ref covBias);
            ApplyModList(_cfg.Playbooks.EndgameOptions, pb.Endgame, ref hotspotMod, ref earlyRotSec, ref aggroMod, ref hgBias, ref covBias);
        }

        private static void ApplyModList(
            List<PlaybookOptionMod> list,
            string style,
            ref float hotspotMod,
            ref float earlyRotSec,
            ref float aggroMod,
            ref float hgBias,
            ref float covBias)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].Style, style, StringComparison.OrdinalIgnoreCase))
                {
                    hotspotMod *= list[i].HotspotWeightMod;
                    earlyRotSec += list[i].EarlyRotateSecOffset;
                    aggroMod *= list[i].AggressionMod;
                    hgBias *= list[i].HighGroundBias;
                    covBias *= list[i].CoverBias;
                    return;
                }
            }
        }

        private int FindSkillIndex(string skillId, out SkillCategory cat)
        {
            for (int i = 0; i < _cfg.Skills.Skills.Count; i++)
            {
                if (_cfg.Skills.Skills[i].SkillId == skillId)
                {
                    cat = _cfg.Skills.Skills[i].GetCategoryEnum();
                    return i;
                }
            }
            cat = SkillCategory.Dash;
            return 0;
        }

        private int FindStarterWeaponIndex(Role role)
        {
            RoleDef rDef = _cfg.Roles.GetRoleDef(role);
            for (int i = 0; i < _cfg.Weapons.Weapons.Count; i++)
            {
                var w = _cfg.Weapons.Weapons[i];
                if (w.Tier == 1 && string.Equals(w.Category, rDef.PreferredWeaponCategory, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }

        private int FindSecondaryStarterWeaponIndex(Role role)
        {
            string secondaryCat = role switch
            {
                Role.Rusher => "AR",
                Role.SecondRusher => "DMR",
                Role.Grenadier => "AR",
                Role.Sniper => "SMG",
                Role.Support => "SMG",
                _ => "AR"
            };

            for (int i = 0; i < _cfg.Weapons.Weapons.Count; i++)
            {
                var w = _cfg.Weapons.Weapons[i];
                if (w.Tier == 1 && string.Equals(w.Category, secondaryCat, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
            return 0;
        }

        public void Step()
        {
            if (_currentPhase == Phase.Finished)
            {
                return;
            }

            // Swap double-buffered snapshots so PrevSnapshot holds tick T-1 and CurrSnapshot will be written at end of tick T
            (_prevSnap, _currSnap) = (_currSnap, _prevSnap);

            _currentTick++;

            // 1. Step Transport Aircraft & Parachute Drop kinematics (Ticks 1..85)
            StepDropPhaseAndParachutes();

            // 2. Update Phase Transitions based on Tick, Airborne State & Zone Stage
            UpdateMatchPhase();

            // 3. Step Safe Zone & Apply Blue Zone DPS
            _zone.Step(_map, ref _rng, out bool zoneChanged);
            if (zoneChanged)
            {
                _events.Record(new SimEvent(
                    _currentTick,
                    _zone.IsShrinking ? SimEventType.ZoneShrinkStart : SimEventType.ZoneShrinkEnd,
                    pos: _zone.CurrentCenter,
                    value: _zone.CurrentRadius,
                    extraCode: _zone.CurrentStageNumber));
            }

            // 4. Process Coach Commands cooldowns & pending executions (WP-09)
            StepCoachCommands();

            // 5. Rebuild Spatial Grid for fast O(1) proximity queries
            _spatialGrid.Clear();
            for (int u = 0; u < TotalUnits; u++)
            {
                if (_units[u].State != UnitState.Eliminated)
                {
                    if (_units[u].DropState == UnitDropState.Grounded)
                    {
                        _spatialGrid.Insert(u, _units[u].Position);
                    }
                    _unitSurvivedTicks[u] = _currentTick;
                }
            }

            // 6. Step Gloo Wall expirations
            StepGlooWalls();

            // 7. Step Squad Macro AI (WP-05)
            StepSquadAi();

            // 8. Step Unit Micro AI: Movement, Looting, Healing, Reviving, Vehicles (WP-06)
            StepUnitAi();

            // 9. Step Combat & Zone Damage & Knockdown bleed-out (WP-07 & WP-08)
            StepCombatAndDamage();

            // 10. Check Match End condition
            CheckMatchCompletion();

            // 11. Write current state to CurrSnapshot
            WriteSnapshot(_currSnap);
        }

        private void UpdateMatchPhase()
        {
            bool anyAirborne = false;
            if (_currentTick <= 85)
            {
                for (int u = 0; u < TotalUnits; u++)
                {
                    if (_units[u].State != UnitState.Eliminated && _units[u].DropState != UnitDropState.Grounded)
                    {
                        anyAirborne = true;
                        break;
                    }
                }
            }

            if (_currentTick <= 35 || (_currentTick <= 85 && anyAirborne))
            {
                _currentPhase = Phase.Drop;
            }
            else if (_currentTick <= 260)
            {
                _currentPhase = Phase.Loot;
            }
            else if (_zone.CurrentStageNumber <= 2)
            {
                _currentPhase = Phase.Rotate;
            }
            else if (_zone.CurrentStageNumber <= 4)
            {
                _currentPhase = Phase.MidGame;
            }
            else
            {
                _currentPhase = Phase.EndGame;
            }
        }

        private void StepGlooWalls()
        {
            int writeIdx = 0;
            for (int i = 0; i < _glooWallCount; i++)
            {
                if (_glooWalls[i].Hp > 0f && _currentTick < _glooWalls[i].ExpireTick)
                {
                    _glooWalls[writeIdx++] = _glooWalls[i];
                }
                else
                {
                    _events.Record(new SimEvent(_currentTick, SimEventType.GlooDestroyed, pos: _glooWalls[i].Position, extraCode: _glooWalls[i].Id));
                }
            }
            _glooWallCount = writeIdx;
        }

        private void CheckMatchCompletion()
        {
            if (_aliveSquadCount <= 1 || _currentTick >= MaxTicksLimit)
            {
                // Assign placement 1 (and any remaining placements if time limit reached)
                for (int s = 0; s < SquadCount; s++)
                {
                    if (_squads[s].Placement == 0)
                    {
                        _squads[s].Placement = _aliveSquadCount <= 1 ? 1 : _nextPlacementToAssign--;
                        if (_squads[s].Placement == 1)
                        {
                            _events.Record(new SimEvent(_currentTick, SimEventType.Booyah, squadIdx: s));
                        }
                    }
                }
                _currentPhase = Phase.Finished;
                _events.Record(new SimEvent(_currentTick, SimEventType.MatchEnded, extraCode: _currentTick));
            }
        }

        private void WriteSnapshot(SnapshotBuffer snap)
        {
            snap.Tick = _currentTick;
            snap.Phase = _currentPhase;
            snap.AliveSquads = _aliveSquadCount;
            snap.AliveUnits = _aliveUnitCount;
            snap.FlightPath = new FlightPathSnapshot
            {
                IsActive = _flightActive,
                StartPos = _flightStartPos,
                EndPos = _flightEndPos,
                CurrentPos = _flightCurrentPos,
                FacingRadians = _flightFacingRadians,
                AltitudeMeters = _flightAltitudeMeters
            };
            snap.Zone = new ZoneSnapshot
            {
                Stage = _zone.CurrentStageNumber,
                IsShrinking = _zone.IsShrinking,
                CurrentCenter = _zone.CurrentCenter,
                CurrentRadius = _zone.CurrentRadius,
                NextCenter = _zone.NextCenter,
                NextRadius = _zone.NextRadius,
                SecondsUntilShrink = _zone.SecondsUntilShrink,
                CurrentDps = _zone.CurrentDps
            };

            for (int u = 0; u < TotalUnits; u++)
            {
                ref readonly UnitRuntime ur = ref _units[u];
                Behavior behavior = Behavior.Follow;
                if (ur.State == UnitState.Alive)
                {
                    if (ur.DropState != UnitDropState.Grounded) behavior = Behavior.Rotate;
                    else if (ur.ReviveProgressTicks > 0) behavior = Behavior.Revive;
                    else if (ur.HealProgressTicks > 0) behavior = Behavior.Heal;
                    else if (ur.TargetUnitId >= 0) behavior = Behavior.Fire;
                    else if (ur.VehicleId >= 0) behavior = Behavior.Mount;
                    else if (Vec2.DistanceSquared(ur.Position, ur.DesiredPosition) > 2.25f) behavior = Behavior.Rotate;
                    else if (_currentPhase == Phase.Loot) behavior = Behavior.Loot;
                    else behavior = Behavior.TakeCover;
                }

                MapData.WorldToFineCell(ur.Position, out int ucx, out int ucy);
                MapCell uCell = _map.GetCell(ucx, ucy);
                float covFacing = _map.GetCellCoverFacingRadians(ucx, ucy, ur.FacingRadians);

                snap.Units[u] = new UnitSnapshot
                {
                    UnitId = ur.UnitId,
                    SquadId = ur.SquadId,
                    Position = ur.Position,
                    FacingRadians = ur.FacingRadians,
                    HeightLevel = ur.HeightLevel,
                    State = ur.State,
                    Role = ur.Role,
                    Hp = ur.Hp,
                    MaxHp = ur.MaxHp,
                    ArmorTier = ur.ArmorTier,
                    HelmetTier = ur.HelmetTier,
                    GlooCount = ur.GlooCount,
                    GrenadeCount = ur.GrenadeCount,
                    MedkitCount = ur.MedkitCount,
                    PrimaryWeaponIndex = ur.PrimaryWeaponIndex,
                    ActiveSkillCooldownRemaining = ur.ActiveSkillCooldownTicks * 0.1f,
                    ActiveSkillDurationRemaining = ur.ActiveSkillDurationTicks * 0.1f,
                    TargetUnitId = ur.TargetUnitId,
                    VehicleId = ur.VehicleId,
                    Kills = ur.Kills,
                    DamageDealt = ur.DamageDealt,
                    IsIgl = ur.IsIgl,
                    CurrentBehavior = behavior,
                    ActiveSkillCategory = ur.ActiveSkillCategory,
                    CoverPercent = ur.DropState == UnitDropState.Grounded ? uCell.CoverValue : (byte)0,
                    TerrainFlags = ur.DropState == UnitDropState.Grounded ? (byte)uCell.Flags : (byte)0,
                    CoverFacingRadians = covFacing,
                    IsFlanked = ur.DropState == UnitDropState.Grounded && (_currentTick - ur.LastFlankedTick) <= 12,
                    IsBreaching = ur.DropState == UnitDropState.Grounded && (_currentTick - ur.LastBreachTick) <= 12,
                    DropState = ur.DropState,
                    AltitudeMeters = ur.AltitudeMeters
                };
            }

            for (int s = 0; s < SquadCount; s++)
            {
                ref readonly SquadRuntime sr = ref _squads[s];
                SquadMode mode = SquadMode.SecurePlacement;
                if (sr.HasActiveCommand && sr.ActiveCommandCompliant)
                {
                    mode = sr.ActiveCommand.Type switch
                    {
                        CommandType.ContestTarget => SquadMode.Contest,
                        CommandType.HoldPosition or CommandType.EndgameFormation => SquadMode.Hold,
                        CommandType.AvoidFight => SquadMode.Disengage,
                        _ => SquadMode.SecurePlacement
                    };
                }
                else if (sr.FocusTargetUnitId >= 0)
                {
                    mode = sr.AggressionMod >= 1.1f ? SquadMode.Push : SquadMode.Contest;
                }
                else if (_currentPhase == Phase.EndGame)
                {
                    mode = SquadMode.Hold;
                }

                snap.Squads[s] = new SquadSnapshot
                {
                    SquadId = sr.SquadId,
                    AliveCount = sr.AliveCount,
                    TotalKills = sr.TotalKills,
                    TotalDamage = sr.TotalDamage,
                    Placement = sr.Placement,
                    CenterPos = GetSquadCentroid(s),
                    MacroGoal = sr.MacroGoal,
                    CurrentMode = mode,
                    CommandsRemaining = sr.CommandsRemaining,
                    CommandCooldownTicks = sr.CommandCooldownTicks,
                    CommandCooldownSec = sr.CommandCooldownTicks * 0.1f,
                    ActiveCommand = sr.ActiveCommand.Type,
                    ActiveCommandTarget = sr.ActiveCommand.TargetPosition,
                    HasActiveCommand = sr.HasActiveCommand,
                    ActiveCommandCompliant = sr.ActiveCommandCompliant,
                    DropPoiId = sr.DropPoiId,
                    DropLaunchPos = sr.DropLaunchPos,
                    DropLaunchTick = sr.DropLaunchTick
                };
            }

            snap.ActiveGlooWallCount = _glooWallCount;
            for (int g = 0; g < _glooWallCount; g++)
            {
                ref readonly DynamicLosBlocker gw = ref _glooWalls[g];
                snap.GlooWalls[g] = new GlooWallSnapshot
                {
                    Id = gw.Id,
                    Position = gw.Position,
                    Radius = gw.Radius,
                    Hp = gw.Hp,
                    MaxHp = 350f,
                    OwnerSquadId = gw.OwnerUnitId >= 0 ? gw.OwnerUnitId / UnitsPerSquad : -1
                };
            }

            snap.ActiveVehicleCount = _vehicleCount;
            for (int v = 0; v < _vehicleCount; v++)
            {
                ref readonly VehicleRuntime vr = ref _vehicles[v];
                snap.Vehicles[v] = new VehicleSnapshot
                {
                    Id = vr.Id,
                    Position = vr.Position,
                    Hp = vr.Hp,
                    MaxHp = vr.MaxHp,
                    DriverUnitId = vr.DriverUnitId,
                    SquadId = vr.SquadId
                };
            }
        }

        public MatchResult GetMatchResult()
        {
            var result = new MatchResult
            {
                Seed = _matchCfg.Seed,
                DurationSec = _currentTick * 0.1f,
                EventHash = _events.DeterminismHash
            };

            // Build squad placements sorted by Placement ascending (1..12)
            for (int s = 0; s < SquadCount; s++)
            {
                ref readonly SquadRuntime sr = ref _squads[s];
                int placement = sr.Placement > 0 ? sr.Placement : s + 1;
                int placePts = _cfg.Scoring.GetPlacementPoints(placement);
                int killPts = sr.TotalKills * _cfg.Scoring.KillPoints;

                result.Placements.Add(new SquadPlacementResult
                {
                    SquadId = _matchCfg.Squads[s].SquadId,
                    Placement = placement,
                    Kills = sr.TotalKills,
                    PlacementPoints = placePts,
                    Points = placePts + killPts,
                    Booyah = placement == 1
                });
            }

            result.Placements.Sort((a, b) => a.Placement.CompareTo(b.Placement));

            // Build player stats & post-match fatigue (CMB-12)
            var fatCfg = _cfg.Fatigue;
            for (int u = 0; u < TotalUnits; u++)
            {
                ref readonly UnitRuntime ur = ref _units[u];
                SquadDef sqDef = _matchCfg.Squads[ur.SquadId];
                PlayerDef pDef = sqDef.Members[ur.SlotIndexInSquad].Player;

                float fatGain = Math.Max(2f, fatCfg.FatigueGainBasePerMatch - ur.Eff.Stamina * fatCfg.FatigueGainStaminaScale);
                float fatOut = Math.Clamp(_unitFatigueIn[u] + fatGain, 0f, 100f);

                result.PlayerStats[pDef.PlayerId] = new PlayerMatchStats
                {
                    Dmg = (int)MathF.Round(ur.DamageDealt),
                    Knocks = ur.KnockdownsScored,
                    Kills = ur.Kills,
                    Revives = ur.RevivesCompleted,
                    SurvivedSec = _unitSurvivedTicks[u] / 10,
                    Gloo = ur.GlooWallsPlaced,
                    Grenades = ur.GrenadesThrown,
                    SkillUses = ur.SkillsActivated,
                    OffDay = _unitOffDay[u],
                    FatigueOut = MathF.Round(fatOut, 2)
                };
            }

            // Evaluate Champion Rush eligibility & crowning if enabled
            if (_matchCfg.Context.ChampionRush.Enabled)
            {
                int threshold = _matchCfg.Context.ChampionRush.Threshold;
                var prevEligible = new HashSet<string>(_matchCfg.Context.ChampionRush.Eligible);

                foreach (var pl in result.Placements)
                {
                    if (pl.Booyah && prevEligible.Contains(pl.SquadId))
                    {
                        result.ChampionRush.Crowned = pl.SquadId;
                    }
                }

                foreach (var pl in result.Placements)
                {
                    int beforePts = _matchCfg.Context.Standings.TryGetValue(pl.SquadId, out int sp) ? sp : 0;
                    int afterPts = beforePts + pl.Points;
                    if (afterPts >= threshold && !prevEligible.Contains(pl.SquadId))
                    {
                        result.ChampionRush.NewlyEligible.Add(pl.SquadId);
                    }
                }
            }

            // Extract up to 6 key moments from EventLog (prioritizing PlayerSquadId, Booyah, SquadWiped, CommandExecuted)
            var allEvents = _events.PeekAllRetained();
            for (int i = 0; i < allEvents.Length; i++)
            {
                ref readonly SimEvent ev = ref allEvents[i];
                if (ev.Type == SimEventType.Booyah ||
                    ev.Type == SimEventType.SquadWiped ||
                    ev.Type == SimEventType.CommandExecuted ||
                    ev.Type == SimEventType.CommandIssued)
                {
                    int sqIdx = ev.SquadIdx >= 0 && ev.SquadIdx < SquadCount ? ev.SquadIdx : 0;
                    string sqId = _matchCfg.Squads[sqIdx].SquadId;
                    string sqName = _matchCfg.Squads[sqIdx].Name;
                    bool isPlayerSquad = sqId == _matchCfg.PlayerSquadId;

                    if (isPlayerSquad || ev.Type == SimEventType.Booyah || result.KeyMoments.Count < 6)
                    {
                        result.KeyMoments.Add(new KeyMomentDto
                        {
                            Tick = ev.Tick,
                            Type = ev.Type.ToString(),
                            SquadId = sqId,
                            Text = $"[{ev.Tick / 10}s] {sqName}: {ev.Type}"
                        });
                    }
                }
            }

            return result;
        }
    }
}
