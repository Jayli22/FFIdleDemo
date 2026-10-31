using System;
using FFM.Client.Camera;
using FFM.Client.Sim;
using FFM.Client.UI;
using FFM.Client.View;
using FFM.Meta;
using FFM.Meta.Models;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client
{
    /// <summary>
    /// Root Coordinator connecting the Esports Career Management Hub (<see cref="MetaScreens"/>)
    /// and the 3D Interactive Match Broadcast (<see cref="SimHost"/>, <see cref="MapBuilder"/>,
    /// <see cref="BattlefieldView"/>, <see cref="CameraRig"/>, <see cref="BroadcastDirector"/>,
    /// and <see cref="BroadcastHud"/>) (§3.2, §6, §7, WP-16, WP-24).
    /// </summary>
    public partial class AppRoot : Node
    {
        private ConfigBundle? _config;
        private MapData? _map;
        private CareerController? _career;

        // UI Layer: Career Hub vs Match Broadcast HUD
        private CanvasLayer? _uiLayer;
        private MetaScreens? _metaScreens;
        private BroadcastHud? _broadcastHud;

        // 3D Match Broadcast World
        private Node3D? _matchWorld;
        private MapBuilder? _mapBuilder;
        private BattlefieldView? _battlefieldView;
        private CameraRig? _cameraRig;
        private BroadcastDirector? _director;
        private SimHost? _simHost;

        private MatchConfig? _activeMatchConfig;
        private bool _inMatchMode;
        private bool _mapBuilt;

        public override void _Ready()
        {
            InitializeDataAndCareer();
            BuildSceneHierarchy();

            bool autoMatch = Array.Exists(OS.GetCmdlineArgs(), a => a == "--auto-match")
                || Array.Exists(OS.GetCmdlineUserArgs(), a => a == "--auto-match");
            if (autoMatch)
            {
                LaunchInteractiveMatchDay();
                _simHost?.SetSpeed(8f);
            }
            else
            {
                SwitchToCareerHub();
            }
        }

        private void InitializeDataAndCareer()
        {
            string configsPath = ProjectSettings.GlobalizePath("res://configs");
            string mapPath = ProjectSettings.GlobalizePath("res://configs/maps/bermuda_lite");

            _config = ConfigLoader.LoadFromDirectory(configsPath);
            _map = MapData.LoadFromDirectory(mapPath);
            _career = CareerController.CreateNewCareer(_config, _map);
        }

        private void BuildSceneHierarchy()
        {
            // 1. 3D Match World Root
            _matchWorld = new Node3D
            {
                Name = "MatchWorld3D",
                Visible = false
            };
            AddChild(_matchWorld);

            _mapBuilder = new MapBuilder { Name = "MapBuilder" };
            _matchWorld.AddChild(_mapBuilder);

            _battlefieldView = new BattlefieldView { Name = "BattlefieldView" };
            _matchWorld.AddChild(_battlefieldView);

            _cameraRig = new CameraRig { Name = "CameraRig" };
            _matchWorld.AddChild(_cameraRig);

            _director = new BroadcastDirector { Name = "BroadcastDirector" };
            _matchWorld.AddChild(_director);

            _simHost = new SimHost { Name = "SimHost" };
            _simHost.EventDispatched += OnSimEventDispatched;
            _simHost.MatchCompleted += OnInteractiveMatchCompleted;
            _matchWorld.AddChild(_simHost);

            // 2. 2D UI Canvas Layer
            _uiLayer = new CanvasLayer { Name = "UILayer" };
            AddChild(_uiLayer);

            _metaScreens = new MetaScreens { Name = "MetaScreens" };
            _metaScreens.PlayInteractiveMatchRequested += LaunchInteractiveMatchDay;
            _uiLayer.AddChild(_metaScreens);

            _broadcastHud = new BroadcastHud
            {
                Name = "BroadcastHud",
                Visible = false
            };
            _broadcastHud.ReturnToCareerRequested += SwitchToCareerHub;
            _uiLayer.AddChild(_broadcastHud);

            if (_career != null)
            {
                _metaScreens.BindCareer(_career);
            }
        }

        public void LaunchInteractiveMatchDay()
        {
            if (_career == null || _config == null || _map == null) return;
            if (_matchWorld == null || _mapBuilder == null || _battlefieldView == null ||
                _cameraRig == null || _director == null || _simHost == null || _broadcastHud == null || _metaScreens == null)
            {
                return;
            }

            // If in Offseason, complete offseason transition directly in Career Hub
            if (_career.CurrentStage == SeasonStage.Offseason)
            {
                _career.AdvanceWeekWithSimulation();
                _metaScreens.RefreshAllTabs();
                return;
            }

            _activeMatchConfig = _career.BuildMatchConfigForCurrentWeek(matchIndexOfDay: 1);

            int playerSquadIdx = 0;
            for (int i = 0; i < _activeMatchConfig.Squads.Count; i++)
            {
                if (_activeMatchConfig.Squads[i].SquadId == _career.State.PlayerTeamId)
                {
                    playerSquadIdx = i;
                    break;
                }
            }

            if (!_mapBuilt)
            {
                _mapBuilder.BuildMap(_map);
                _mapBuilt = true;
            }

            _battlefieldView.InitializeBattlefield(_map, _activeMatchConfig, _config, playerSquadIdx);
            _director.ResetDirector(playerSquadIdx);
            _cameraRig.SetMode(CameraViewMode.TopDownMacro);
            _simHost.StartMatch(new MatchRunner(_config, _map, _activeMatchConfig), playerSquadIdx);
            _broadcastHud.Bind(_simHost, _cameraRig, _director, _activeMatchConfig, _map, _config, _battlefieldView);

            _metaScreens.Visible = false;
            _matchWorld.Visible = true;
            _broadcastHud.Visible = true;
            _inMatchMode = true;
        }

        public void SwitchToCareerHub()
        {
            _inMatchMode = false;
            if (_matchWorld != null) _matchWorld.Visible = false;
            if (_broadcastHud != null) _broadcastHud.Visible = false;
            if (_metaScreens != null)
            {
                _metaScreens.Visible = true;
                _metaScreens.RefreshAllTabs();
            }
        }

        public override void _Process(double delta)
        {
            if (!_inMatchMode || _simHost == null || _cameraRig == null ||
                _director == null || _mapBuilder == null || _battlefieldView == null || _broadcastHud == null)
            {
                return;
            }

            float dt = (float)delta;

            InterpolatedFrameState frame = _simHost.CurrentFrame;
            _director.UpdateDirector(frame, _cameraRig, dt);
            _cameraRig.UpdateRig(frame, dt);

            _mapBuilder.UpdateZoneAndCamera(frame, _cameraRig.CurrentDistance);
            _battlefieldView.UpdateBattlefield(frame, _cameraRig.CurrentDistance, dt);
            _broadcastHud.UpdateHud(frame, dt);
        }

        public override void _UnhandledKeyInput(InputEvent @event)
        {
            if (!_inMatchMode || _simHost == null || _cameraRig == null) return;

            if (@event is InputEventKey key && key.Pressed && !key.Echo)
            {
                switch (key.Keycode)
                {
                    case Key.Space:
                        _simHost.TogglePause();
                        _broadcastHud?.ShowTacticalBanner(
                            _simHost.IsPaused
                                ? "⏸ MATCH PAUSED — TACTICAL PLANNING MODE"
                                : "▶ MATCH RESUMED (1x LIVE)",
                            EsportsUiTheme.AccentGold,
                            2.2f);
                        break;
                    case Key.Tab:
                        _cameraRig.CycleMode();
                        _broadcastHud?.ShowTacticalBanner(
                            $"🎥 CAMERA VIEW MODE: {_cameraRig.Mode.ToString().ToUpperInvariant()}",
                            EsportsUiTheme.AccentCyan,
                            1.8f);
                        break;
                    case Key.Backspace:
                        _simHost.Rewind10Seconds();
                        _broadcastHud?.ShowTacticalBanner(
                            "⏪ INSTANT REPLAY: REWINDING -10 SECONDS",
                            EsportsUiTheme.AccentCrimson,
                            2.5f);
                        break;
                }
            }
        }

        private void OnSimEventDispatched(SimEvent ev)
        {
            _battlefieldView?.OnSimEvent(ev);
            _director?.OnSimEvent(ev, _activeMatchConfig);
        }

        private void OnInteractiveMatchCompleted(MatchResult result)
        {
            if (_career == null || _broadcastHud == null) return;

            PostMatchReport? report = _career.AdvanceWeekWithSimulation(result);
            if (report != null)
            {
                _broadcastHud.ShowPostMatchReport(report);
            }
        }
    }
}
