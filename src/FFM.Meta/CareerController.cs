using System;
using System.Collections.Generic;
using FFM.Meta.Models;
using FFM.Meta.Save;
using FFM.Meta.Services;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;

namespace FFM.Meta
{
    public sealed class CareerController
    {
        public ConfigBundle Config { get; }
        public MapData Map { get; }
        public CareerSaveData State { get; private set; }

        public TrainingService Training { get; } = new();
        public SeasonCalendarService Calendar { get; } = new();
        public LineupValidator LineupValidator { get; } = new();
        public PlaybookService Playbook { get; } = new();
        public PostMatchAnalyzer Analyzer { get; } = new();
        public ScoutingService Scouting { get; } = new();
        public TransferService Transfers { get; } = new();
        public EconomyService Economy { get; } = new();
        public PatchNotesService PatchNotes { get; } = new();
        public SaveService SaveService { get; } = new();

        public CareerController(ConfigBundle config, MapData map, CareerSaveData state)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        public static CareerController CreateNewCareer(
            ConfigBundle config,
            MapData map,
            string? playerTeamId = null,
            ulong seed = 20260416UL)
        {
            var teams = new List<TeamDef>(config.SampleTeams.Count);
            for (int i = 0; i < config.SampleTeams.Count; i++)
            {
                teams.Add(config.SampleTeams[i].Clone());
            }

            string chosenTeamId = playerTeamId ?? (teams.Count > 0 ? teams[0].TeamId : "team_01");
            var calendar = new SeasonCalendarService();
            var transfers = new TransferService();
            var rng = new Rng(seed);

            var save = new CareerSaveData
            {
                SchemaVersion = SaveService.CurrentSchemaVersion,
                CareerSeed = seed,
                Season = 1,
                Week = 1,
                PatchVersion = "v1.0",
                PlayerTeamId = chosenTeamId,
                CashBalance = 180000,
                ScoutingPointsAvailable = 120,
                TeamTrainingFocus = TeamTrainingFocus.Shooting,
                Teams = teams,
                Standings = calendar.InitializeRegularSeasonStandings(teams),
                TransferMarket = transfers.GenerateMarketListings(1, ref rng, 10)
            };

            // Player's own roster starts with Exact scouting knowledge
            var controller = new CareerController(config, map, save);
            var playerTeam = controller.GetPlayerTeam();
            for (int i = 0; i < playerTeam.Players.Count; i++)
            {
                string pid = playerTeam.Players[i].PlayerId;
                save.ScoutingBook[pid] = new ScoutingEntry
                {
                    PlayerId = pid,
                    Stage = ScoutingStage.Exact,
                    ProgressPoints = 100
                };
            }

            return controller;
        }

        public void LoadState(CareerSaveData loaded)
        {
            State = loaded ?? throw new ArgumentNullException(nameof(loaded));
        }

        public TeamDef GetPlayerTeam()
        {
            for (int i = 0; i < State.Teams.Count; i++)
            {
                if (State.Teams[i].TeamId == State.PlayerTeamId)
                {
                    return State.Teams[i];
                }
            }
            return State.Teams[0];
        }

        public TeamDef? FindTeam(string teamId)
        {
            for (int i = 0; i < State.Teams.Count; i++)
            {
                if (State.Teams[i].TeamId == teamId)
                {
                    return State.Teams[i];
                }
            }
            return null;
        }

        public SeasonStage CurrentStage => Calendar.GetStageForWeek(State.Week);

        public MatchConfig BuildMatchConfigForCurrentWeek(int matchIndexOfDay = 1)
        {
            var participatingIds = Calendar.GetMatchDayTeamIds(State.Week, State.Standings);
            var squads = new List<SquadDef>(12);

            for (int i = 0; i < participatingIds.Count && squads.Count < 12; i++)
            {
                var team = FindTeam(participatingIds[i]);
                if (team != null)
                {
                    squads.Add(SquadDef.FromTeamDef(team));
                }
            }

            // Fill up to 12 if needed
            for (int i = 0; i < State.Teams.Count && squads.Count < 12; i++)
            {
                bool exists = false;
                for (int j = 0; j < squads.Count; j++)
                {
                    if (squads[j].SquadId == State.Teams[i].TeamId) { exists = true; break; }
                }
                if (!exists)
                {
                    squads.Add(SquadDef.FromTeamDef(State.Teams[i]));
                }
            }

            bool isFinals = CurrentStage == SeasonStage.GrandFinals;
            var standingsMap = new Dictionary<string, int>(StringComparer.Ordinal);
            var eligibleList = new List<string>();

            for (int i = 0; i < State.Standings.Count; i++)
            {
                var s = State.Standings[i];
                standingsMap[s.TeamId] = s.TotalPoints;
                if (s.MatchPointEligible)
                {
                    eligibleList.Add(s.TeamId);
                }
            }

            ulong matchSeed = State.CareerSeed
                              + (ulong)(State.Season * 10000)
                              + (ulong)(State.Week * 100)
                              + (ulong)matchIndexOfDay;

            return new MatchConfig
            {
                Seed = matchSeed,
                MapId = "bermuda_lite",
                TickHz = 10,
                Squads = squads,
                PlayerSquadId = State.PlayerTeamId,
                Context = new MatchContextDef
                {
                    MatchIndexOfDay = matchIndexOfDay,
                    IsFinals = isFinals,
                    Standings = standingsMap,
                    ChampionRush = new ChampionRushContextDef
                    {
                        Enabled = isFinals,
                        Threshold = Config.Scoring.ChampionRushThreshold,
                        Eligible = eligibleList
                    }
                }
            };
        }

        private MatchResult RunSimulatedMatch(int matchIndexOfDay)
        {
            var mCfg = BuildMatchConfigForCurrentWeek(matchIndexOfDay);
            var runner = new MatchRunner(Config, Map, mCfg);
            while (!runner.IsFinished)
            {
                runner.Step();
            }
            return runner.GetMatchResult();
        }

        public WeeklyTrainingSummary ExecuteWeeklyTrainingStep()
        {
            ulong trainSeed = State.CareerSeed + (ulong)(State.Season * 5000 + State.Week * 37);
            var rng = new Rng(trainSeed);

            var playerTeam = GetPlayerTeam();
            var playerSummary = Training.ExecuteWeeklyTraining(
                playerTeam,
                State.Week,
                State.TeamTrainingFocus,
                State.IndividualFocuses,
                ref rng);

            // Train AI teams deterministically
            for (int i = 0; i < State.Teams.Count; i++)
            {
                var aiTeam = State.Teams[i];
                if (aiTeam.TeamId == State.PlayerTeamId) continue;

                TeamTrainingFocus aiFocus = (i + State.Week) % 4 == 0
                    ? TeamTrainingFocus.Rest
                    : (TeamTrainingFocus)((i + State.Week) % 3);

                Training.ExecuteWeeklyTraining(aiTeam, State.Week, aiFocus, null, ref rng);
            }

            return playerSummary;
        }

        /// <summary>
        /// Simulates the current week's matches (or applies an already-played interactive match + remaining matches of the day)
        /// and advances the calendar by 1 week.
        /// </summary>
        public PostMatchReport? AdvanceWeekWithSimulation(MatchResult? interactiveMatchResult = null)
        {
            SeasonStage stage = CurrentStage;

            if (stage == SeasonStage.Offseason)
            {
                CompleteOffseasonAndStartNextSeason();
                return State.LastMatchReport;
            }

            // 1. Weekly Training
            ExecuteWeeklyTrainingStep();

            int playerPointsThisWeek = 0;
            MatchResult? featuredMatch = interactiveMatchResult;

            if (stage == SeasonStage.GrandFinals)
            {
                int maxMatches = Config.Scoring.ChampionRushMaxMatches;
                string? crowned = null;

                for (int m = 1; m <= maxMatches; m++)
                {
                    MatchResult res = (m == 1 && interactiveMatchResult != null)
                        ? interactiveMatchResult
                        : RunSimulatedMatch(m);

                    featuredMatch = res;
                    playerPointsThisWeek += GetTeamPointsFromMatch(res, State.PlayerTeamId);
                    crowned = Calendar.ApplyMatchResult(
                        State.Standings,
                        res,
                        isGrandFinals: true,
                        matchNumberInFinals: m,
                        championRushThreshold: Config.Scoring.ChampionRushThreshold,
                        maxFinalsMatches: maxMatches);

                    if (crowned != null)
                    {
                        State.ReigningChampionTeamId = crowned;
                        break;
                    }
                }

                // Award Grand Finals prize money to player team
                int playerRank = GetTeamRankInStandings(State.PlayerTeamId);
                int cash = State.CashBalance;
                Economy.AwardFinalsPrize(State.Ledger, ref cash, State.Season, State.Week, playerRank);
                State.CashBalance = cash;
            }
            else
            {
                // Regular Season (Weeks 1..8) or Point Rush (Weeks 9..10): 3 matches per week for snappy pacing
                const int matchesPerWeek = 3;
                for (int m = 1; m <= matchesPerWeek; m++)
                {
                    MatchResult res = (m == 1 && interactiveMatchResult != null)
                        ? interactiveMatchResult
                        : RunSimulatedMatch(m);

                    if (m == 1) featuredMatch = res;
                    playerPointsThisWeek += GetTeamPointsFromMatch(res, State.PlayerTeamId);
                    Calendar.ApplyMatchResult(State.Standings, res, isGrandFinals: false);
                }
            }

            if (featuredMatch != null)
            {
                State.LastMatchReport = Analyzer.AnalyzeMatch(featuredMatch, GetPlayerTeam());
                Training.AwardPostMatchProgression(GetPlayerTeam(), featuredMatch, State.IdleClock);
            }

            // 2. Weekly Economy & Scouting allowance
            int balance = State.CashBalance;
            Economy.ProcessWeeklyFinances(
                GetPlayerTeam(),
                State.Ledger,
                ref balance,
                State.Season,
                State.Week,
                playerPointsThisWeek);
            State.CashBalance = balance;
            State.ScoutingPointsAvailable += 40;

            // 3. Stage transitions at end of Week 8 and Week 10
            if (State.Week == 8)
            {
                State.Standings = Calendar.TransitionToPointRush(State.Standings);
            }
            else if (State.Week == 10)
            {
                State.Standings = Calendar.TransitionToGrandFinals(State.Standings);
            }

            State.Week += 1;
            State.IdleClock.CurrentDayOfWeek = 1;
            State.IdleClock.HourProgress = 8.0f;
            State.IdleClock.PushLog($"=== Entered Season {State.Season} Week {State.Week} ({CurrentStage}) ===");
            return State.LastMatchReport;
        }

        /// <summary>
        /// Real-time Out-of-Match Idle Progression Clock (Mon -> Sun).
        /// At 1x speed, 1 game day = 8 real seconds (3 game hours per real second).
        /// Automatically trains players, recovers fatigue, accumulates sponsor cash/XP, and handles Sunday Match Day policy.
        /// Manual advance buttons remain fully available at any time.
        /// </summary>
        public IdleTickResult TickIdleProgression(float deltaSeconds)
        {
            var result = new IdleTickResult();
            var idle = State.IdleClock;
            if (idle.SpeedMode == IdleSpeedMode.Paused || deltaSeconds <= 0f)
            {
                return result;
            }

            float speedMult = (int)idle.SpeedMode;
            float hoursToAdd = deltaSeconds * 3.0f * speedMult; // 3 hours/sec at 1x => 8s per day
            int prevWholeHour = (int)MathF.Floor(idle.HourProgress);
            idle.HourProgress += hoursToAdd;
            int newWholeHour = (int)MathF.Floor(idle.HourProgress);

            int hoursCrossed = newWholeHour - prevWholeHour;
            if (hoursCrossed > 0)
            {
                result.AdvancedHour = true;
                var playerTeam = GetPlayerTeam();
                ulong rngSeed = State.CareerSeed + (ulong)(State.Season * 100000 + State.Week * 1000 + idle.CurrentDayOfWeek * 31 + newWholeHour);
                var rng = new Rng(rngSeed);

                int steps = Math.Min(hoursCrossed, 12);
                for (int h = 0; h < steps; h++)
                {
                    Training.StepPlayerHourlyIdleTraining(
                        playerTeam,
                        State.Facilities,
                        State.TeamTrainingFocus,
                        State.IndividualFocuses,
                        idle,
                        ref rng);
                }
            }

            if (idle.HourProgress >= 24f)
            {
                idle.HourProgress -= 24f;
                idle.CurrentDayOfWeek++;
                result.AdvancedDay = true;

                if (idle.CurrentDayOfWeek >= 7)
                {
                    idle.CurrentDayOfWeek = 7;
                    // Reached Sunday Match Day! Execute configured MatchDayAutoPolicy
                    switch (idle.MatchDayPolicy)
                    {
                        case MatchDayAutoPolicy.AutoSimIdle:
                            idle.PushLog($"⚡ [SUN] Auto-Simulating Week {State.Week} Match Day...");
                            result.AutoQuickSimReport = AdvanceWeekWithSimulation(null);
                            result.TriggeredAutoSimMatchWeek = true;
                            result.NewLogEntries.Add($"Auto-simulated Week {State.Week - 1} Match Day.");
                            break;

                        case MatchDayAutoPolicy.AutoWatch3D:
                            idle.HourProgress = 18.0f;
                            idle.SpeedMode = IdleSpeedMode.Paused;
                            idle.PushLog($"🎬 [SUN] Match Day reached — Auto-launching 3D Tactical Broadcast!");
                            result.TriggeredAutoLaunch3DMatch = true;
                            result.NewLogEntries.Add("Match Day reached — launching 3D broadcast.");
                            break;

                        case MatchDayAutoPolicy.PauseBeforeMatch:
                            idle.HourProgress = 18.0f;
                            idle.SpeedMode = IdleSpeedMode.Paused;
                            idle.PushLog($"⏸ [SUN] Match Day Eve — Clock paused for pre-match tactical setup.");
                            result.PausedAtMatchDay = true;
                            result.NewLogEntries.Add("Match Day Eve — clock paused.");
                            break;
                    }
                }
            }

            return result;
        }

        public (int cash, int xp, int tp) ClaimIdleVaultRewards()
        {
            var idle = State.IdleClock;
            int cash = idle.UnclaimedIdleCash;
            int xp = idle.UnclaimedClubXp;
            int tp = idle.UnclaimedTacticalPoints;

            if (cash <= 0 && xp <= 0 && tp <= 0)
            {
                return (0, 0, 0);
            }

            State.CashBalance += cash;
            idle.ClubXpPool += xp;
            idle.TacticalPoints += tp;

            idle.UnclaimedIdleCash = 0;
            idle.UnclaimedClubXp = 0;
            idle.UnclaimedTacticalPoints = 0;

            if (cash > 0)
            {
                State.Ledger.Add(new FinanceLedgerEntry
                {
                    Season = State.Season,
                    Week = State.Week,
                    Category = "Idle Sponsor & Stream Vault",
                    Amount = cash,
                    BalanceAfter = State.CashBalance,
                    Description = $"Claimed idle sponsor stream & club base revenue (+${cash:N0})"
                });
            }

            idle.PushLog($"💰 Claimed Idle Vault: +${cash:N0} Cash, +{xp} Club XP, +{tp} Tactical Pts!");
            return (cash, xp, tp);
        }

        public PlayerDef? FindPlayerOnPlayerTeam(string playerId)
        {
            var team = GetPlayerTeam();
            for (int i = 0; i < team.Players.Count; i++)
            {
                if (team.Players[i].PlayerId == playerId)
                {
                    return team.Players[i];
                }
            }
            return null;
        }

        public bool LevelUpPlayer(string playerId)
        {
            var p = FindPlayerOnPlayerTeam(playerId);
            if (p == null) return false;
            return Training.LevelUpPlayerWithClubXp(p, State.IdleClock);
        }

        public bool StarBreakPlayer(string playerId)
        {
            var p = FindPlayerOnPlayerTeam(playerId);
            if (p == null) return false;
            int cash = State.CashBalance;
            bool ok = Training.StarLimitBreakPlayer(p, ref cash, State.IdleClock);
            State.CashBalance = cash;
            return ok;
        }

        public bool RunBootcampDrill(string playerId, BootcampDrillType drill)
        {
            var p = FindPlayerOnPlayerTeam(playerId);
            if (p == null) return false;
            int cash = State.CashBalance;
            bool ok = Training.ExecuteBootcampDrill(p, drill, ref cash, State.IdleClock);
            State.CashBalance = cash;
            return ok;
        }

        public bool UpgradeFacility(string facilityKey)
        {
            int cash = State.CashBalance;
            bool ok = Training.UpgradeClubFacility(State.Facilities, facilityKey, ref cash, State.IdleClock);
            State.CashBalance = cash;
            return ok;
        }

        public bool TogglePlayerAutoRest(string playerId)
        {
            var p = FindPlayerOnPlayerTeam(playerId);
            if (p == null) return false;
            var prog = p.GetProgression();
            prog.AutoRestEnabled = !prog.AutoRestEnabled;
            p.Condition.AutoRestThreshold = prog.AutoRestEnabled;
            if (!prog.AutoRestEnabled) prog.IsCurrentlyResting = false;
            return prog.AutoRestEnabled;
        }

        public bool CyclePlayerSignatureTrait(string playerId, int slotIndex, int tpCost = 40)
        {
            var p = FindPlayerOnPlayerTeam(playerId);
            if (p == null) return false;
            return Training.CycleSignatureTrait(p, slotIndex, State.IdleClock, tpCost);
        }

        public void CompleteOffseasonAndStartNextSeason()
        {
            Training.AdvanceOffseasonAges(State.Teams);
            State.Season += 1;
            State.Week = 1;

            var patch = PatchNotes.GetPatchForSeason(State.Season);
            State.PatchVersion = patch.PatchVersion;
            PatchNotes.ApplyPatchToWeaponsAndSkills(Config.Weapons, Config.Skills, patch);

            State.Standings = Calendar.InitializeRegularSeasonStandings(State.Teams);

            var rng = new Rng(State.CareerSeed + (ulong)(State.Season * 99991));
            State.TransferMarket = Transfers.GenerateMarketListings(State.Season, ref rng, 10);
        }

        public bool InvestScoutingOnPlayer(string playerId, int points = 50)
        {
            if (State.ScoutingPointsAvailable < points) return false;

            if (!State.ScoutingBook.TryGetValue(playerId, out var entry))
            {
                entry = new ScoutingEntry { PlayerId = playerId, Stage = ScoutingStage.Unknown, ProgressPoints = 0 };
                State.ScoutingBook[playerId] = entry;
            }

            if (entry.Stage == ScoutingStage.Exact) return false;

            State.ScoutingPointsAvailable -= points;
            Scouting.InvestScoutingPoints(entry, points);
            return true;
        }

        private static int GetTeamPointsFromMatch(MatchResult res, string teamId)
        {
            for (int i = 0; i < res.Placements.Count; i++)
            {
                if (res.Placements[i].SquadId == teamId)
                {
                    return res.Placements[i].Points;
                }
            }
            return 0;
        }

        private int GetTeamRankInStandings(string teamId)
        {
            for (int i = 0; i < State.Standings.Count; i++)
            {
                if (State.Standings[i].TeamId == teamId)
                {
                    return i + 1;
                }
            }
            return State.Standings.Count;
        }
    }
}
