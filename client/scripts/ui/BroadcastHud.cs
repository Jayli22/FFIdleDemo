using System;
using System.Collections.Generic;
using FFM.Client.Art;
using FFM.Client.Camera;
using FFM.Client.Sim;
using FFM.Client.View;
using FFM.Meta.Models;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Map;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.UI
{
    /// <summary>
    /// Esports Broadcast HUD Overlay (§6.5, §6.7, WP-15).
    /// Includes Top Broadcast Header Bar, Left Graphical 12-Squad Live Leaderboard,
    /// Top-Right Interactive 2D Bermuda Radar Minimap, Live Kill/Tactical Feed,
    /// Bottom Featured Squad 4-Player Live Telemetry Bar, Bottom-Right Coach Command Deck,
    /// and Post-Match Causal Attribution &amp; 15s Highlight Replay Modal.
    /// </summary>
    public partial class BroadcastHud : Control
    {
        private sealed class SquadLeaderboardRowUi
        {
            public PanelContainer Card = null!;
            public StyleBoxFlat CardStyle = null!;
            public ColorRect TeamStrip = null!;
            public TextureRect LogoRect = null!;
            public Label TagLabel = null!;
            public readonly ColorRect[]HpBarBgs = new ColorRect[4];
            public readonly ColorRect[] HpBarFills = new ColorRect[4];
            public Label KillsLabel = null!;
            public Label ModeLabel = null!;
        }

        private sealed class FeaturedPlayerCardUi
        {
            public PanelContainer Card = null!;
            public StyleBoxFlat CardStyle = null!;
            public TextureRect PortraitRect = null!;
            public TextureRect RoleIconRect = null!;
            public Label NameLabel = null!;
            public Label BehaviorLabel = null!;
            public ProgressBar HpBar = null!;
            public StyleBoxFlat HpFillStyle = null!;
            public Label HpValueLabel = null!;
            public TextureRect WeaponIconRect = null!;
            public Label WeaponNameLabel = null!;
            public TextureRect SkillIconRect = null!;
            public Label SkillStatusLabel = null!;
            public Label UtilityAndStatsLabel = null!;
        }

        private SimHost? _simHost;
        private CameraRig? _cameraRig;
        private BroadcastDirector? _director;
        private BattlefieldView? _battlefieldView;
        private MatchConfig? _matchCfg;
        private MapData? _map;
        private ConfigBundle? _config;
        private int _playerSquadIdx;
        private int _lastFeaturedSquadIdx = -1;

        // Top Bar widgets
        private Label? _phaseAndZoneLabel;
        private Label? _aliveCounterLabel;
        private Label? _clockLabel;
        private Label? _directorHeadlineLabel;
        private Label? _replayBannerLabel;
        private Button? _pauseBtn;
        private Button? _speed1xBtn;
        private Button? _speed2xBtn;
        private Button? _speed4xBtn;
        private Button? _autoObBtn;
        private Button? _camModeBtn;
        private Button? _toggleReportBtn;

        // Center-Top Tactical Action & Radio Feedback Banner
        private PanelContainer? _tacticalBannerPanel;
        private StyleBoxFlat? _tacticalBannerStyle;
        private Label? _tacticalBannerLabel;
        private float _tacticalBannerTimer;

        // Center-Top Drop Phase Tactical Control & Clash Alert Bar
        private PanelContainer? _dropPhaseBarPanel;
        private Label? _dropStatusLabel;
        private Label? _dropClashLabel;
        private OptionButton? _dropPoiSelector;
        private bool _suppressPoiSelectorSignal;
        private int _lastSyncedDropPoiId = -1;

        // Left 12-Squad Graphical Leaderboard rows
        private readonly SquadLeaderboardRowUi[] _squadRows = new SquadLeaderboardRowUi[SnapshotBuffer.MaxSquads];

        // Top-Right Interactive Radar Minimap & Kill Feed
        private MinimapWidget? _minimapWidget;
        private VBoxContainer? _eventFeedBox;
        private readonly Queue<Control> _eventFeedItems = new(8);

        // Bottom Center-Left: Featured Squad 4-Player Live Telemetry Bar
        private TextureRect? _featuredSquadLogo;
        private Label? _featuredSquadHeaderLabel;
        private Label? _featuredSquadMetaLabel;
        private Button? _focusMySquadBtn;
        private readonly FeaturedPlayerCardUi[] _featuredPlayerCards = new FeaturedPlayerCardUi[4];

        // Bottom-Right Coach Command Panel
        private CommandPanel? _commandPanel;

        // Post-Match Attribution & Highlight Replay Overlay
        private PanelContainer? _postMatchModal;
        private Label? _postMatchSummaryLabel;
        private VBoxContainer? _keyMomentsBox;
        private VBoxContainer? _insightsBox;

        public event Action? ReturnToCareerRequested;

        public override void _Ready()
        {
            EsportsUiTheme.SetFullRect(this, 1920f, 1080f);
            SyncViewportRect();
            MouseFilter = MouseFilterEnum.Pass;
            BuildBroadcastHud();
        }

        private void SyncViewportRect()
        {
            Vector2 vpSize = GetViewportRect().Size;
            if (vpSize.X < 640f || vpSize.Y < 360f)
            {
                vpSize = new Vector2(1920f, 1080f);
            }
            if (Size != vpSize)
            {
                Position = Vector2.Zero;
                Size = vpSize;
            }
        }

        public void Bind(
            SimHost simHost,
            CameraRig cameraRig,
            BroadcastDirector director,
            MatchConfig matchCfg,
            MapData? map = null,
            ConfigBundle? config = null,
            BattlefieldView? battlefieldView = null)
        {
            if (_simHost != null)
            {
                _simHost.EventDispatched -= OnSimEvent;
            }
            if (_cameraRig != null)
            {
                _cameraRig.GroundLeftClicked -= OnGroundLeftClicked;
                _cameraRig.GroundRightClicked -= OnGroundRightClicked;
            }

            _simHost = simHost;
            _cameraRig = cameraRig;
            _director = director;
            _battlefieldView = battlefieldView;
            _matchCfg = matchCfg;
            _map = map;
            _config = config;
            _playerSquadIdx = simHost.PlayerSquadIndex;
            _lastFeaturedSquadIdx = -1;

            _simHost.EventDispatched += OnSimEvent;
            _cameraRig.GroundLeftClicked += OnGroundLeftClicked;
            _cameraRig.GroundRightClicked += OnGroundRightClicked;

            _commandPanel?.Bind(simHost);
            _minimapWidget?.Bind(_map, _matchCfg, _playerSquadIdx);

            PopulateDropPoiSelector();

            if (_postMatchModal != null)
            {
                _postMatchModal.Visible = false;
            }
            if (_toggleReportBtn != null)
            {
                _toggleReportBtn.Visible = false;
            }

            ClearEventFeed();
            RefreshSquadRowsInitial();
            RefreshFeaturedSquadStaticInfo(_playerSquadIdx);
            ShowTacticalBanner("✈️ C-130 DROP PHASE · SELECT DROP POI OR R-CLICK RADAR BEFORE LAUNCH!", EsportsUiTheme.AccentCyan, 4.0f);
        }

        private void PopulateDropPoiSelector()
        {
            if (_dropPoiSelector == null) return;
            _suppressPoiSelectorSignal = true;
            _dropPoiSelector.Clear();
            if (_map != null && _map.Pois != null)
            {
                for (int i = 0; i < _map.Pois.Count; i++)
                {
                    var p = _map.Pois[i];
                    string tag = p.IsHotspot ? $"★ {p.Name} (HOT)" : p.Name;
                    _dropPoiSelector.AddItem(tag, p.PoiId);
                }
            }
            _suppressPoiSelectorSignal = false;
        }

        private void TryOverridePlayerDropPoi(int poiId)
        {
            if (_simHost == null || _map == null) return;
            bool ok = _simHost.OverridePlayerDropPoi(poiId);
            string poiName = $"POI #{poiId}";
            for (int i = 0; i < _map.Pois.Count; i++)
            {
                if (_map.Pois[i].PoiId == poiId)
                {
                    poiName = _map.Pois[i].Name;
                    break;
                }
            }

            if (ok)
            {
                ShowTacticalBanner($"🪂 DROP TARGET OVERRIDDEN → {poiName.ToUpperInvariant()}!", EsportsUiTheme.AccentGold, 2.8f);
            }
            else
            {
                ShowTacticalBanner("⚠️ SQUAD ALREADY LAUNCHED — CANNOT CHANGE DROP POI AFTER JUMP!", EsportsUiTheme.AccentAmber, 2.4f);
            }
        }

        public override void _ExitTree()
        {
            if (_simHost != null)
            {
                _simHost.EventDispatched -= OnSimEvent;
            }
            if (_cameraRig != null)
            {
                _cameraRig.GroundLeftClicked -= OnGroundLeftClicked;
                _cameraRig.GroundRightClicked -= OnGroundRightClicked;
            }
        }

        private void BuildBroadcastHud()
        {
            // 1. TOP BROADCAST BAR
            var topBar = new PanelContainer
            {
                Name = "TopBroadcastBar"
            };
            EsportsUiTheme.SetTopWide(topBar, 46f);
            topBar.AddThemeStyleboxOverride("panel", EsportsUiTheme.CreateHeaderBarStyle());
            AddChild(topBar);

            var topHBox = new HBoxContainer();
            topHBox.AddThemeConstantOverride("separation", 10);
            topBar.AddChild(topHBox);

            _phaseAndZoneLabel = new Label
            {
                Text = "STAGE 0 · DROP PHASE",
                Modulate = EsportsUiTheme.AccentCyan
            };
            _phaseAndZoneLabel.AddThemeFontSizeOverride("font_size", 12);
            topHBox.AddChild(_phaseAndZoneLabel);

            _aliveCounterLabel = new Label
            {
                Text = "SQUADS: 12/12  |  ALIVE: 48/48",
                Modulate = EsportsUiTheme.TextPrimary
            };
            _aliveCounterLabel.AddThemeFontSizeOverride("font_size", 12);
            topHBox.AddChild(_aliveCounterLabel);

            _clockLabel = new Label
            {
                Text = "00:00",
                Modulate = EsportsUiTheme.AccentGold
            };
            _clockLabel.AddThemeFontSizeOverride("font_size", 14);
            topHBox.AddChild(_clockLabel);

            _directorHeadlineLabel = new Label
            {
                Text = "BROADCAST OB · LIVE",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = EsportsUiTheme.TextPrimary,
                ClipText = true
            };
            _directorHeadlineLabel.AddThemeFontSizeOverride("font_size", 12);
            topHBox.AddChild(_directorHeadlineLabel);

            _replayBannerLabel = new Label
            {
                Text = "",
                Modulate = EsportsUiTheme.AccentCrimson,
                Visible = false
            };
            _replayBannerLabel.AddThemeFontSizeOverride("font_size", 12);
            topHBox.AddChild(_replayBannerLabel);

            // Playback controls: Pause, 1x, 2x, 4x, Rewind -10s, Skip, OB Auto, Cam Mode, Report, Hub
            _pauseBtn = AddTopButton(topHBox, "||", 36f, () =>
            {
                if (_simHost == null) return;
                _simHost.TogglePause();
                ShowTacticalBanner(
                    _simHost.IsPaused
                        ? "⏸ MATCH PAUSED — TACTICAL PLANNING MODE"
                        : "▶ MATCH RESUMED (1x LIVE)",
                    EsportsUiTheme.AccentGold,
                    2.2f);
            });
            _speed1xBtn = AddTopButton(topHBox, "1x", 36f, () =>
            {
                _simHost?.SetSpeed(1f);
                ShowTacticalBanner("⏩ PLAYBACK SPEED: 1x LIVE", EsportsUiTheme.AccentCyan, 1.6f);
            });
            _speed2xBtn = AddTopButton(topHBox, "2x", 36f, () =>
            {
                _simHost?.SetSpeed(2f);
                ShowTacticalBanner("⏩ PLAYBACK SPEED: 2x FAST", EsportsUiTheme.AccentCyan, 1.6f);
            });
            _speed4xBtn = AddTopButton(topHBox, "4x", 36f, () =>
            {
                _simHost?.SetSpeed(4f);
                ShowTacticalBanner("⏩ PLAYBACK SPEED: 4x TURBO", EsportsUiTheme.AccentGold, 1.6f);
            });
            AddTopButton(topHBox, "-10s", 46f, () =>
            {
                _simHost?.Rewind10Seconds();
                ShowTacticalBanner("⏪ INSTANT REPLAY: REWINDING -10 SECONDS", EsportsUiTheme.AccentCrimson, 2.5f);
            });
            AddTopButton(topHBox, "SKIP", 48f, () =>
            {
                ShowTacticalBanner("⏭ FAST-FORWARDING TO MATCH RESULT...", EsportsUiTheme.AccentGold, 2.0f);
                _simHost?.SkipToEnd();
            });

            _autoObBtn = AddTopButton(topHBox, "OB: AUTO", 76f, () =>
            {
                if (_director == null) return;
                _director.AutoDirectorEnabled = !_director.AutoDirectorEnabled;
                if (_autoObBtn != null)
                {
                    _autoObBtn.Text = _director.AutoDirectorEnabled ? "OB: AUTO" : "OB: MANUAL";
                }
                ShowTacticalBanner(
                    _director.AutoDirectorEnabled
                        ? "🎥 BROADCAST OB: AUTO AI DIRECTOR ENABLED"
                        : "🎥 BROADCAST OB: MANUAL CAMERA CONTROL",
                    EsportsUiTheme.AccentCyan,
                    2.0f);
            });

            _camModeBtn = AddTopButton(topHBox, "CAM: FOLLOW", 92f, () =>
            {
                _cameraRig?.CycleMode();
                if (_cameraRig != null)
                {
                    ShowTacticalBanner(
                        $"🎥 CAMERA VIEW MODE: {_cameraRig.Mode.ToString().ToUpperInvariant()}",
                        EsportsUiTheme.AccentCyan,
                        1.8f);
                }
            });

            _toggleReportBtn = AddTopButton(topHBox, "REPORT", 68f, () =>
            {
                if (_postMatchModal != null)
                {
                    _postMatchModal.Visible = !_postMatchModal.Visible;
                }
            });
            _toggleReportBtn.Visible = false;

            var hubBtn = EsportsUiTheme.CreateActionButton("<< HUB", isPrimaryGold: false, minSize: new Vector2(64f, 28f));
            hubBtn.AddThemeFontSizeOverride("font_size", 11);
            hubBtn.Pressed += () => ReturnToCareerRequested?.Invoke();
            topHBox.AddChild(hubBtn);

            // 1B. CENTER-TOP TACTICAL ACTION & RADIO FEEDBACK BANNER
            BuildCenterTacticalBanner();

            // 2. LEFT GRAPHICAL 12-SQUAD LIVE LEADERBOARD
            BuildLeftSquadLeaderboard();

            // 3. TOP-RIGHT INTERACTIVE 2D RADAR MINIMAP & KILL FEED
            BuildRightMinimapAndKillFeed();

            // 4. BOTTOM CENTER-LEFT: FEATURED SQUAD 4-PLAYER LIVE TELEMETRY BAR
            BuildBottomFeaturedSquadTelemetryBar();

            // 5. BOTTOM-RIGHT: COACH COMMAND DECK
            _commandPanel = new CommandPanel
            {
                Name = "BottomCommandPanel"
            };
            _commandPanel.AnchorLeft = 1f;
            _commandPanel.AnchorRight = 1f;
            _commandPanel.AnchorTop = 1f;
            _commandPanel.AnchorBottom = 1f;
            _commandPanel.OffsetLeft = -428f;
            _commandPanel.OffsetRight = -10f;
            _commandPanel.OffsetTop = -138f;
            _commandPanel.OffsetBottom = -10f;
            _commandPanel.CommandIssued += (cmdType, desc) =>
            {
                _battlefieldView?.TriggerCommandFeedback3D(cmdType);
                ShowTacticalBanner($"📡 {desc}", EsportsUiTheme.AccentGold, 2.8f);
            };
            _commandPanel.CommandRejected += (reason) =>
            {
                ShowTacticalBanner($"⚠️ {reason}", EsportsUiTheme.AccentCrimson, 2.4f);
            };
            AddChild(_commandPanel);

            // 6. POST-MATCH ATTRIBUTION & 15s HIGHLIGHT REPLAY MODAL
            BuildPostMatchModal();
        }

        private void BuildCenterTacticalBanner()
        {
            _tacticalBannerStyle = EsportsUiTheme.CreateBentoCardStyle(
                new Color(0.05f, 0.08f, 0.14f, 0.94f),
                EsportsUiTheme.AccentGold,
                cornerRadius: 6,
                borderTop: 2,
                padding: 6);

            _tacticalBannerPanel = new PanelContainer
            {
                Name = "CenterTacticalBanner",
                CustomMinimumSize = new Vector2(560f, 34f),
                Visible = false,
                MouseFilter = MouseFilterEnum.Ignore
            };
            _tacticalBannerPanel.AnchorLeft = 0.5f;
            _tacticalBannerPanel.AnchorRight = 0.5f;
            _tacticalBannerPanel.AnchorTop = 0f;
            _tacticalBannerPanel.AnchorBottom = 0f;
            _tacticalBannerPanel.OffsetLeft = -280f;
            _tacticalBannerPanel.OffsetRight = 280f;
            _tacticalBannerPanel.OffsetTop = 54f;
            _tacticalBannerPanel.OffsetBottom = 88f;
            _tacticalBannerPanel.PivotOffset = new Vector2(280f, 17f);
            _tacticalBannerPanel.AddThemeStyleboxOverride("panel", _tacticalBannerStyle);
            AddChild(_tacticalBannerPanel);

            _tacticalBannerLabel = new Label
            {
                Text = "",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = EsportsUiTheme.AccentGold,
                MouseFilter = MouseFilterEnum.Ignore
            };
            _tacticalBannerLabel.AddThemeFontSizeOverride("font_size", 12);
            _tacticalBannerPanel.AddChild(_tacticalBannerLabel);

            // Drop Phase Tactical Bar (visible while player squad is aboard C-130 or skydiving/gliding)
            _dropPhaseBarPanel = new PanelContainer
            {
                Name = "DropPhaseTacticalBar",
                CustomMinimumSize = new Vector2(680f, 38f),
                Visible = false
            };
            _dropPhaseBarPanel.AnchorLeft = 0.5f;
            _dropPhaseBarPanel.AnchorRight = 0.5f;
            _dropPhaseBarPanel.AnchorTop = 0f;
            _dropPhaseBarPanel.AnchorBottom = 0f;
            _dropPhaseBarPanel.OffsetLeft = -340f;
            _dropPhaseBarPanel.OffsetRight = 340f;
            _dropPhaseBarPanel.OffsetTop = 92f;
            _dropPhaseBarPanel.OffsetBottom = 130f;
            _dropPhaseBarPanel.AddThemeStyleboxOverride(
                "panel",
                EsportsUiTheme.CreateBentoCardStyle(
                    new Color(0.045f, 0.075f, 0.135f, 0.94f),
                    EsportsUiTheme.AccentCyan,
                    cornerRadius: 6,
                    borderTop: 2,
                    padding: 6));
            AddChild(_dropPhaseBarPanel);

            var dropHBox = new HBoxContainer();
            dropHBox.AddThemeConstantOverride("separation", 10);
            _dropPhaseBarPanel.AddChild(dropHBox);

            _dropStatusLabel = new Label
            {
                Text = "✈️ C-130 AIRDROP · 350m",
                Modulate = EsportsUiTheme.AccentCyan,
                VerticalAlignment = VerticalAlignment.Center
            };
            _dropStatusLabel.AddThemeFontSizeOverride("font_size", 11);
            dropHBox.AddChild(_dropStatusLabel);

            _dropClashLabel = new Label
            {
                Text = "✅ UNCONTESTED DROP",
                Modulate = EsportsUiTheme.AccentGreen,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _dropClashLabel.AddThemeFontSizeOverride("font_size", 11);
            dropHBox.AddChild(_dropClashLabel);

            var poiHint = new Label
            {
                Text = "DROP TARGET:",
                Modulate = EsportsUiTheme.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            poiHint.AddThemeFontSizeOverride("font_size", 10);
            dropHBox.AddChild(poiHint);

            _dropPoiSelector = new OptionButton
            {
                CustomMinimumSize = new Vector2(168f, 24f)
            };
            _dropPoiSelector.AddThemeFontSizeOverride("font_size", 11);
            _dropPoiSelector.ItemSelected += (idx) =>
            {
                if (_suppressPoiSelectorSignal || _dropPoiSelector == null) return;
                int poiId = _dropPoiSelector.GetItemId((int)idx);
                TryOverridePlayerDropPoi(poiId);
            };
            dropHBox.AddChild(_dropPoiSelector);
        }

        public void ShowTacticalBanner(string message, Color accentColor, float durationSec = 2.5f)
        {
            if (_tacticalBannerPanel == null || _tacticalBannerLabel == null || _tacticalBannerStyle == null) return;

            _tacticalBannerLabel.Text = message;
            _tacticalBannerLabel.Modulate = accentColor;
            _tacticalBannerStyle.BorderColor = accentColor;
            _tacticalBannerPanel.Visible = true;
            _tacticalBannerTimer = durationSec;

            _tacticalBannerPanel.Scale = new Vector2(1.04f, 1.04f);
            Tween tw = CreateTween();
            tw.TweenProperty(_tacticalBannerPanel, "scale", Vector2.One, 0.16f)
              .SetTrans(Tween.TransitionType.Back)
              .SetEase(Tween.EaseType.Out);
        }

        private void BuildLeftSquadLeaderboard()
        {
            var leftPanel = new PanelContainer
            {
                Name = "LeftSquadLeaderboard",
                Position = new Vector2(10f, 54f),
                CustomMinimumSize = new Vector2(246f, 452f)
            };
            leftPanel.AddThemeStyleboxOverride(
                "panel",
                EsportsUiTheme.CreateBentoCardStyle(
                    new Color(0.055f, 0.075f, 0.12f, 0.88f),
                    EsportsUiTheme.BorderSubtle,
                    cornerRadius: 6,
                    borderTop: 2,
                    padding: 8));
            AddChild(leftPanel);

            var leftVBox = new VBoxContainer();
            leftVBox.AddThemeConstantOverride("separation", 3);
            leftPanel.AddChild(leftVBox);

            var lbHeader = EsportsUiTheme.CreateSectionHeader("12-SQUAD LIVE STANDINGS", EsportsUiTheme.AccentGold, 11);
            leftVBox.AddChild(lbHeader);

            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                int sqIdx = s;
                var rowStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0.09f, 0.12f, 0.19f, 0.90f),
                    BorderColor = new Color(0.20f, 0.26f, 0.36f, 0.65f),
                    BorderWidthLeft = 1,
                    BorderWidthRight = 1,
                    BorderWidthTop = 1,
                    BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 4,
                    CornerRadiusTopRight = 4,
                    CornerRadiusBottomLeft = 4,
                    CornerRadiusBottomRight = 4,
                    ContentMarginLeft = 4,
                    ContentMarginRight = 6,
                    ContentMarginTop = 2,
                    ContentMarginBottom = 2
                };

                var rowCard = new PanelContainer
                {
                    CustomMinimumSize = new Vector2(228f, 31f),
                    MouseFilter = MouseFilterEnum.Stop,
                    MouseDefaultCursorShape = CursorShape.PointingHand
                };
                rowCard.AddThemeStyleboxOverride("panel", rowStyle);
                rowCard.GuiInput += (@event) =>
                {
                    if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        FocusOnSquad(sqIdx);
                    }
                };

                var rowHBox = new HBoxContainer();
                rowHBox.AddThemeConstantOverride("separation", 5);
                rowHBox.MouseFilter = MouseFilterEnum.Ignore;
                rowCard.AddChild(rowHBox);

                var strip = new ColorRect
                {
                    CustomMinimumSize = new Vector2(4f, 22f),
                    Color = EsportsUiTheme.AccentGold,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                rowHBox.AddChild(strip);

                var logo = new TextureRect
                {
                    CustomMinimumSize = new Vector2(18f, 18f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                rowHBox.AddChild(logo);

                var tagLbl = new Label
                {
                    Text = $"#{sqIdx + 1:00} SQ{sqIdx + 1:00}",
                    CustomMinimumSize = new Vector2(68f, 0f),
                    ClipText = true,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                tagLbl.AddThemeFontSizeOverride("font_size", 11);
                rowHBox.AddChild(tagLbl);

                // 4 Graphical Mini Player HP/Status Bars
                var barsHBox = new HBoxContainer
                {
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                barsHBox.AddThemeConstantOverride("separation", 2);
                rowHBox.AddChild(barsHBox);

                var rowUi = new SquadLeaderboardRowUi
                {
                    Card = rowCard,
                    CardStyle = rowStyle,
                    TeamStrip = strip,
                    LogoRect = logo,
                    TagLabel = tagLbl
                };

                for (int m = 0; m < 4; m++)
                {
                    var barBg = new ColorRect
                    {
                        CustomMinimumSize = new Vector2(6f, 16f),
                        Color = new Color(0.04f, 0.06f, 0.10f, 0.95f),
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    var barFill = new ColorRect
                    {
                        Position = new Vector2(0f, 0f),
                        Size = new Vector2(6f, 16f),
                        Color = EsportsUiTheme.AccentGreen,
                        MouseFilter = MouseFilterEnum.Ignore
                    };
                    barBg.AddChild(barFill);
                    barsHBox.AddChild(barBg);
                    rowUi.HpBarBgs[m] = barBg;
                    rowUi.HpBarFills[m] = barFill;
                }

                var killsLbl = new Label
                {
                    Text = "0K",
                    CustomMinimumSize = new Vector2(28f, 0f),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Modulate = EsportsUiTheme.TextPrimary,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                killsLbl.AddThemeFontSizeOverride("font_size", 11);
                rowHBox.AddChild(killsLbl);
                rowUi.KillsLabel = killsLbl;

                var modeLbl = new Label
                {
                    Text = "DROP",
                    CustomMinimumSize = new Vector2(36f, 0f),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Modulate = EsportsUiTheme.TextSecondary,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                modeLbl.AddThemeFontSizeOverride("font_size", 10);
                rowHBox.AddChild(modeLbl);
                rowUi.ModeLabel = modeLbl;

                leftVBox.AddChild(rowCard);
                _squadRows[sqIdx] = rowUi;
            }
        }

        private void BuildRightMinimapAndKillFeed()
        {
            // 3A. Top-Right Interactive Radar Minimap Card
            var minimapCard = new PanelContainer
            {
                Name = "TopRightMinimapCard",
                CustomMinimumSize = new Vector2(244f, 260f)
            };
            minimapCard.AnchorLeft = 1f;
            minimapCard.AnchorRight = 1f;
            minimapCard.AnchorTop = 0f;
            minimapCard.AnchorBottom = 0f;
            minimapCard.OffsetLeft = -254f;
            minimapCard.OffsetRight = -10f;
            minimapCard.OffsetTop = 54f;
            minimapCard.OffsetBottom = 314f;
            minimapCard.AddThemeStyleboxOverride(
                "panel",
                EsportsUiTheme.CreateBentoCardStyle(
                    new Color(0.055f, 0.075f, 0.12f, 0.90f),
                    EsportsUiTheme.AccentCyan,
                    cornerRadius: 6,
                    borderTop: 2,
                    padding: 8));
            AddChild(minimapCard);

            var mapVBox = new VBoxContainer();
            mapVBox.AddThemeConstantOverride("separation", 4);
            minimapCard.AddChild(mapVBox);

            var mapHeader = EsportsUiTheme.CreateSectionHeader("BERMUDA RADAR (L:PAN · R:PIN)", EsportsUiTheme.AccentCyan, 11);
            mapVBox.AddChild(mapHeader);

            _minimapWidget = new MinimapWidget
            {
                Name = "MinimapWidget",
                CustomMinimumSize = new Vector2(228f, 228f)
            };
            _minimapWidget.SquadMarkerClicked += FocusOnSquad;
            _minimapWidget.DropPoiClicked += TryOverridePlayerDropPoi;
            _minimapWidget.TacticalTargetPinned += (worldPos) =>
            {
                _commandPanel?.SetPinnedTarget(worldPos);
                _battlefieldView?.TriggerGroundPin3D(new Vector3(worldPos.X, 0f, worldPos.Y));
                ShowTacticalBanner(
                    $"📍 RADAR TARGET PINNED AT ({worldPos.X:0}, {worldPos.Y:0})m",
                    EsportsUiTheme.AccentGold,
                    2.4f);
            };
            _minimapWidget.WorldPointClicked += (worldPos) =>
            {
                if (_director != null)
                {
                    _director.AutoDirectorEnabled = false;
                    if (_autoObBtn != null) _autoObBtn.Text = "OB: MANUAL";
                }
                _cameraRig?.SetMode(CameraViewMode.FreeControl);
                _cameraRig?.FocusWorldPoint(worldPos, 235f);
                ShowTacticalBanner(
                    $"🎥 CAMERA PAN TO ({worldPos.X:0}, {worldPos.Z:0})m",
                    EsportsUiTheme.AccentCyan,
                    1.6f);
            };
            mapVBox.AddChild(_minimapWidget);

            // 3B. Live Kill & Tactical Event Feed below Minimap
            var rightFeedPanel = new PanelContainer
            {
                Name = "RightKillFeedPanel",
                CustomMinimumSize = new Vector2(300f, 186f)
            };
            rightFeedPanel.AnchorLeft = 1f;
            rightFeedPanel.AnchorRight = 1f;
            rightFeedPanel.AnchorTop = 0f;
            rightFeedPanel.AnchorBottom = 0f;
            rightFeedPanel.OffsetLeft = -310f;
            rightFeedPanel.OffsetRight = -10f;
            rightFeedPanel.OffsetTop = 320f;
            rightFeedPanel.OffsetBottom = 506f;
            rightFeedPanel.AddThemeStyleboxOverride(
                "panel",
                EsportsUiTheme.CreateBentoCardStyle(
                    new Color(0.055f, 0.075f, 0.12f, 0.84f),
                    EsportsUiTheme.BorderSubtle,
                    cornerRadius: 6,
                    borderTop: 2,
                    padding: 8));
            AddChild(rightFeedPanel);

            var rightVBox = new VBoxContainer();
            rightVBox.AddThemeConstantOverride("separation", 4);
            rightFeedPanel.AddChild(rightVBox);

            var feedTitle = EsportsUiTheme.CreateSectionHeader("LIVE KILL & TACTICAL FEED", EsportsUiTheme.AccentGold, 11);
            rightVBox.AddChild(feedTitle);

            _eventFeedBox = new VBoxContainer();
            _eventFeedBox.AddThemeConstantOverride("separation", 3);
            rightVBox.AddChild(_eventFeedBox);
        }

        private void BuildBottomFeaturedSquadTelemetryBar()
        {
            var bottomBar = new PanelContainer
            {
                Name = "FeaturedSquadTelemetryBar"
            };
            bottomBar.AnchorLeft = 0f;
            bottomBar.AnchorRight = 1f;
            bottomBar.AnchorTop = 1f;
            bottomBar.AnchorBottom = 1f;
            bottomBar.OffsetLeft = 10f;
            bottomBar.OffsetRight = -436f;
            bottomBar.OffsetTop = -138f;
            bottomBar.OffsetBottom = -10f;
            bottomBar.AddThemeStyleboxOverride(
                "panel",
                EsportsUiTheme.CreateBentoCardStyle(
                    new Color(0.055f, 0.075f, 0.12f, 0.92f),
                    EsportsUiTheme.AccentCyan,
                    cornerRadius: 8,
                    borderTop: 2,
                    padding: 8));
            AddChild(bottomBar);

            var rootVBox = new VBoxContainer();
            rootVBox.AddThemeConstantOverride("separation", 6);
            bottomBar.AddChild(rootVBox);

            // Header strip: Featured Squad Logo + Name + Mode + Synergy + Focus My Squad Button
            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 8);
            rootVBox.AddChild(headerHBox);

            _featuredSquadLogo = new TextureRect
            {
                CustomMinimumSize = new Vector2(20f, 20f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            headerHBox.AddChild(_featuredSquadLogo);

            _featuredSquadHeaderLabel = EsportsUiTheme.CreateSectionHeader(
                "OBSERVING SQUAD TELEMETRY",
                EsportsUiTheme.AccentGold,
                12);
            headerHBox.AddChild(_featuredSquadHeaderLabel);

            _featuredSquadMetaLabel = new Label
            {
                Text = "MODE: DROP · KILLS: 0",
                Modulate = EsportsUiTheme.TextSecondary,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true
            };
            _featuredSquadMetaLabel.AddThemeFontSizeOverride("font_size", 11);
            headerHBox.AddChild(_featuredSquadMetaLabel);

            _focusMySquadBtn = EsportsUiTheme.CreateActionButton(
                "★ FOCUS MY SQUAD",
                isPrimaryGold: true,
                minSize: new Vector2(132f, 22f));
            _focusMySquadBtn.AddThemeFontSizeOverride("font_size", 10);
            _focusMySquadBtn.Pressed += () => FocusOnSquad(_playerSquadIdx);
            headerHBox.AddChild(_focusMySquadBtn);

            // 4 Side-by-Side Player Live Telemetry Cards
            var cardsHBox = new HBoxContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            cardsHBox.AddThemeConstantOverride("separation", 6);
            rootVBox.AddChild(cardsHBox);

            for (int m = 0; m < 4; m++)
            {
                var pCardStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0.09f, 0.13f, 0.20f, 0.94f),
                    BorderColor = EsportsUiTheme.BorderSubtle,
                    BorderWidthLeft = 2,
                    BorderWidthRight = 1,
                    BorderWidthTop = 1,
                    BorderWidthBottom = 1,
                    CornerRadiusTopLeft = 6,
                    CornerRadiusTopRight = 6,
                    CornerRadiusBottomLeft = 6,
                    CornerRadiusBottomRight = 6,
                    ContentMarginLeft = 6,
                    ContentMarginRight = 6,
                    ContentMarginTop = 4,
                    ContentMarginBottom = 4
                };

                var pCard = new PanelContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ExpandFill,
                    MouseFilter = MouseFilterEnum.Stop,
                    MouseDefaultCursorShape = CursorShape.PointingHand
                };
                pCard.AddThemeStyleboxOverride("panel", pCardStyle);
                pCard.GuiInput += (@event) =>
                {
                    if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
                    {
                        int targetSq = _lastFeaturedSquadIdx >= 0 ? _lastFeaturedSquadIdx : _playerSquadIdx;
                        FocusOnSquad(targetSq);
                    }
                };
                cardsHBox.AddChild(pCard);

                var cardHBox = new HBoxContainer
                {
                    MouseFilter = MouseFilterEnum.Ignore
                };
                cardHBox.AddThemeConstantOverride("separation", 6);
                pCard.AddChild(cardHBox);

                // Left column of player card: Portrait + Role Icon
                var avatarCol = new VBoxContainer
                {
                    Alignment = BoxContainer.AlignmentMode.Center,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                avatarCol.AddThemeConstantOverride("separation", 2);
                cardHBox.AddChild(avatarCol);

                var portrait = new TextureRect
                {
                    CustomMinimumSize = new Vector2(40f, 40f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                avatarCol.AddChild(portrait);

                var roleIcon = new TextureRect
                {
                    CustomMinimumSize = new Vector2(16f, 16f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                avatarCol.AddChild(roleIcon);

                // Right column of player card: Name & Behavior, HP Bar, Weapon & Skill, Utility & Kills
                var infoCol = new VBoxContainer
                {
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    Alignment = BoxContainer.AlignmentMode.Center,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                infoCol.AddThemeConstantOverride("separation", 2);
                cardHBox.AddChild(infoCol);

                // Row A: Player Name + Behavior Tag
                var nameRow = new HBoxContainer
                {
                    MouseFilter = MouseFilterEnum.Ignore
                };
                nameRow.AddThemeConstantOverride("separation", 4);
                infoCol.AddChild(nameRow);

                var nameLbl = new Label
                {
                    Text = $"PLAYER {m + 1}",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    ClipText = true,
                    Modulate = EsportsUiTheme.TextPrimary,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                nameLbl.AddThemeFontSizeOverride("font_size", 11);
                nameRow.AddChild(nameLbl);

                var behLbl = new Label
                {
                    Text = "IDLE",
                    Modulate = EsportsUiTheme.AccentCyan,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                behLbl.AddThemeFontSizeOverride("font_size", 10);
                nameRow.AddChild(behLbl);

                // Row B: HP Bar + Numerical HP
                var hpRow = new HBoxContainer
                {
                    MouseFilter = MouseFilterEnum.Ignore
                };
                hpRow.AddThemeConstantOverride("separation", 4);
                infoCol.AddChild(hpRow);

                var hpFillStyle = new StyleBoxFlat
                {
                    BgColor = EsportsUiTheme.AccentGreen,
                    CornerRadiusTopLeft = 3,
                    CornerRadiusTopRight = 3,
                    CornerRadiusBottomLeft = 3,
                    CornerRadiusBottomRight = 3
                };
                var hpBgStyle = new StyleBoxFlat
                {
                    BgColor = new Color(0.04f, 0.06f, 0.10f, 0.95f),
                    CornerRadiusTopLeft = 3,
                    CornerRadiusTopRight = 3,
                    CornerRadiusBottomLeft = 3,
                    CornerRadiusBottomRight = 3
                };

                var hpBar = new ProgressBar
                {
                    MinValue = 0,
                    MaxValue = 200,
                    Value = 200,
                    ShowPercentage = false,
                    CustomMinimumSize = new Vector2(64f, 9f),
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                hpBar.AddThemeStyleboxOverride("background", hpBgStyle);
                hpBar.AddThemeStyleboxOverride("fill", hpFillStyle);
                hpRow.AddChild(hpBar);

                var hpValLbl = new Label
                {
                    Text = "200",
                    CustomMinimumSize = new Vector2(26f, 0f),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Modulate = EsportsUiTheme.TextPrimary,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                hpValLbl.AddThemeFontSizeOverride("font_size", 10);
                hpRow.AddChild(hpValLbl);

                // Row C: Weapon Icon + Weapon Name + Skill Icon + Skill Status
                var gearRow = new HBoxContainer
                {
                    MouseFilter = MouseFilterEnum.Ignore
                };
                gearRow.AddThemeConstantOverride("separation", 4);
                infoCol.AddChild(gearRow);

                var wpnIcon = new TextureRect
                {
                    CustomMinimumSize = new Vector2(24f, 13f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                gearRow.AddChild(wpnIcon);

                var wpnLbl = new Label
                {
                    Text = "AK47",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    ClipText = true,
                    Modulate = EsportsUiTheme.TextSecondary,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                wpnLbl.AddThemeFontSizeOverride("font_size", 10);
                gearRow.AddChild(wpnLbl);

                var skIcon = new TextureRect
                {
                    CustomMinimumSize = new Vector2(14f, 14f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    SizeFlagsVertical = SizeFlags.ShrinkCenter,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                gearRow.AddChild(skIcon);

                var skStatusLbl = new Label
                {
                    Text = "RDY",
                    Modulate = EsportsUiTheme.AccentGreen,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                skStatusLbl.AddThemeFontSizeOverride("font_size", 10);
                gearRow.AddChild(skStatusLbl);

                // Row D: Armor/Helmet + Gloo + Grenades + Kills/Damage
                var utilLbl = new Label
                {
                    Text = "A1·H1  GLOO:2  GRN:1  0K",
                    ClipText = true,
                    Modulate = EsportsUiTheme.TextSecondary,
                    MouseFilter = MouseFilterEnum.Ignore
                };
                utilLbl.AddThemeFontSizeOverride("font_size", 10);
                infoCol.AddChild(utilLbl);

                _featuredPlayerCards[m] = new FeaturedPlayerCardUi
                {
                    Card = pCard,
                    CardStyle = pCardStyle,
                    PortraitRect = portrait,
                    RoleIconRect = roleIcon,
                    NameLabel = nameLbl,
                    BehaviorLabel = behLbl,
                    HpBar = hpBar,
                    HpFillStyle = hpFillStyle,
                    HpValueLabel = hpValLbl,
                    WeaponIconRect = wpnIcon,
                    WeaponNameLabel = wpnLbl,
                    SkillIconRect = skIcon,
                    SkillStatusLabel = skStatusLbl,
                    UtilityAndStatsLabel = utilLbl
                };
            }
        }

        private void BuildPostMatchModal()
        {
            _postMatchModal = new PanelContainer
            {
                Name = "PostMatchAttributionModal",
                Visible = false,
                CustomMinimumSize = new Vector2(840f, 530f)
            };
            _postMatchModal.AnchorLeft = 0.5f;
            _postMatchModal.AnchorRight = 0.5f;
            _postMatchModal.AnchorTop = 0.5f;
            _postMatchModal.AnchorBottom = 0.5f;
            _postMatchModal.OffsetLeft = -420f;
            _postMatchModal.OffsetRight = 420f;
            _postMatchModal.OffsetTop = -265f;
            _postMatchModal.OffsetBottom = 265f;

            _postMatchModal.AddThemeStyleboxOverride(
                "panel",
                EsportsUiTheme.CreateBentoCardStyle(
                    new Color(0.055f, 0.075f, 0.12f, 0.97f),
                    EsportsUiTheme.AccentGold,
                    cornerRadius: 10,
                    borderTop: 3,
                    padding: 20));
            AddChild(_postMatchModal);

            var rootVBox = new VBoxContainer();
            rootVBox.AddThemeConstantOverride("separation", 12);
            _postMatchModal.AddChild(rootVBox);

            _postMatchSummaryLabel = EsportsUiTheme.CreateSectionHeader(
                "POST-MATCH ANALYTICS & CAUSAL ATTRIBUTION",
                EsportsUiTheme.AccentGold,
                16);
            rootVBox.AddChild(_postMatchSummaryLabel);

            var columnsHBox = new HBoxContainer
            {
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            columnsHBox.AddThemeConstantOverride("separation", 18);
            rootVBox.AddChild(columnsHBox);

            // Left Column: 3 Key Moments + 15s Highlight Replay Buttons
            var momentsCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            momentsCol.AddThemeConstantOverride("separation", 8);
            columnsHBox.AddChild(momentsCol);

            momentsCol.AddChild(EsportsUiTheme.CreateSectionHeader(
                "★ 3 KEY MATCH TURNING POINTS (15s REPLAY)",
                EsportsUiTheme.AccentCyan,
                13));

            _keyMomentsBox = new VBoxContainer();
            _keyMomentsBox.AddThemeConstantOverride("separation", 8);
            momentsCol.AddChild(_keyMomentsBox);

            // Right Column: 3 Causal Attribution Insights
            var insightsCol = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            insightsCol.AddThemeConstantOverride("separation", 8);
            columnsHBox.AddChild(insightsCol);

            insightsCol.AddChild(EsportsUiTheme.CreateSectionHeader(
                "◆ 3 CAUSAL ATTRIBUTION INSIGHTS (WHY WE WON/LOST)",
                EsportsUiTheme.AccentGreen,
                13));

            _insightsBox = new VBoxContainer();
            _insightsBox.AddThemeConstantOverride("separation", 8);
            insightsCol.AddChild(_insightsBox);

            // Footer buttons
            var footerHBox = new HBoxContainer
            {
                Alignment = BoxContainer.AlignmentMode.End
            };
            footerHBox.AddThemeConstantOverride("separation", 12);
            rootVBox.AddChild(footerHBox);

            var hideModalBtn = EsportsUiTheme.CreateActionButton(
                "INSPECT 3D BATTLEFIELD",
                isPrimaryGold: false,
                minSize: new Vector2(195f, 36f));
            hideModalBtn.Pressed += () =>
            {
                if (_postMatchModal != null) _postMatchModal.Visible = false;
            };
            footerHBox.AddChild(hideModalBtn);

            var returnBtn = EsportsUiTheme.CreateActionButton(
                "CONTINUE TO CAREER HUB >>",
                isPrimaryGold: true,
                minSize: new Vector2(235f, 36f));
            returnBtn.Pressed += () => ReturnToCareerRequested?.Invoke();
            footerHBox.AddChild(returnBtn);
        }

        private void FocusOnSquad(int squadIdx)
        {
            if (_director != null)
            {
                _director.AutoDirectorEnabled = false;
                if (_autoObBtn != null) _autoObBtn.Text = "OB: MANUAL";
            }
            _cameraRig?.FollowSquad(squadIdx, 215f);
            _battlefieldView?.PulseSquadSelection(squadIdx);

            string sqTag = GetSquadShortTag(squadIdx);
            bool isPlayer = (squadIdx == _playerSquadIdx);
            ShowTacticalBanner(
                isPlayer
                    ? $"★ CAMERA LOCKED ON YOUR SQUAD ({sqTag})"
                    : $"🎯 OBSERVING SQUAD: {sqTag} (#{squadIdx + 1:00})",
                isPlayer ? EsportsUiTheme.AccentGold : EsportsUiTheme.AccentCyan,
                2.0f);
        }

        private void OnGroundLeftClicked(Vector3 groundPos)
        {
            if (_battlefieldView == null) return;
            int pickedSquadIdx = _battlefieldView.TryPickSquadAtWorldPos(groundPos, 48f);
            if (pickedSquadIdx >= 0)
            {
                FocusOnSquad(pickedSquadIdx);
            }
        }

        private void OnGroundRightClicked(Vector3 groundPos)
        {
            Vec2 pinnedSimPos = _battlefieldView != null
                ? _battlefieldView.TriggerGroundPin3D(groundPos)
                : new Vec2(groundPos.X, groundPos.Z);
            _commandPanel?.SetPinnedTarget(pinnedSimPos);
            _minimapWidget?.SetPinnedTarget(pinnedSimPos);
            ShowTacticalBanner(
                $"📍 3D TACTICAL TARGET PINNED AT ({pinnedSimPos.X:0}, {pinnedSimPos.Y:0})m",
                EsportsUiTheme.AccentGold,
                2.5f);
        }

        public void ShowPostMatchReport(PostMatchReport report)
        {
            if (_postMatchModal == null || _keyMomentsBox == null || _insightsBox == null) return;

            _postMatchModal.Visible = true;
            if (_toggleReportBtn != null)
            {
                _toggleReportBtn.Visible = true;
            }

            if (_postMatchSummaryLabel != null)
            {
                string booyahTag = report.Booyah ? "★ BOOYAH! ★ " : "";
                _postMatchSummaryLabel.Text =
                    $"{booyahTag}MATCH COMPLETE · PLACEMENT #{report.Placement} · KILLS: {report.Kills} · TOTAL POINTS: {report.TotalPoints}";
            }

            foreach (Node c in _keyMomentsBox.GetChildren()) c.QueueFree();
            foreach (var km in report.KeyMoments)
            {
                KeyMomentItem item = km;
                var cardPanel = new PanelContainer();
                cardPanel.AddThemeStyleboxOverride(
                    "panel",
                    EsportsUiTheme.CreateBentoCardStyle(
                        EsportsUiTheme.BgCard,
                        EsportsUiTheme.BorderSubtle,
                        cornerRadius: 6,
                        borderTop: 2,
                        padding: 10));

                var card = new VBoxContainer();
                card.AddThemeConstantOverride("separation", 4);
                cardPanel.AddChild(card);

                card.AddChild(new Label
                {
                    Text = $"[{FormatTime(item.TimeSec)}] {item.Title}",
                    Modulate = EsportsUiTheme.AccentGold,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                });
                card.AddChild(new Label
                {
                    Text = item.Description,
                    Modulate = EsportsUiTheme.TextPrimary,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                });

                var replayBtn = EsportsUiTheme.CreateActionButton(
                    $"▶ WATCH 15s HIGHLIGHT ({FormatTime(item.TimeSec)})",
                    isPrimaryGold: false,
                    minSize: new Vector2(240f, 28f));
                replayBtn.Pressed += () =>
                {
                    _postMatchModal.Visible = false;
                    _simHost?.PlayHighlightClip(item.Tick, $"HIGHLIGHT: {item.Title}", 15f);
                    if (item.WorldX > 1f || item.WorldY > 1f)
                    {
                        _cameraRig?.FocusWorldPoint(new Vector3(item.WorldX, 0f, item.WorldY), 205f);
                    }
                    ShowTacticalBanner($"🎬 PLAYING 15s HIGHLIGHT REPLAY: {item.Title}", EsportsUiTheme.AccentCrimson, 3.0f);
                };
                card.AddChild(replayBtn);
                _keyMomentsBox.AddChild(cardPanel);
            }

            foreach (Node c in _insightsBox.GetChildren()) c.QueueFree();
            foreach (var ins in report.AttributionInsights)
            {
                Color accent = ins.IsPositive ? EsportsUiTheme.AccentGreen : EsportsUiTheme.AccentCrimson;
                var cardPanel = new PanelContainer();
                cardPanel.AddThemeStyleboxOverride(
                    "panel",
                    EsportsUiTheme.CreateBentoCardStyle(
                        EsportsUiTheme.BgCard,
                        accent,
                        cornerRadius: 6,
                        borderTop: 2,
                        padding: 10));

                var card = new VBoxContainer();
                card.AddThemeConstantOverride("separation", 4);
                cardPanel.AddChild(card);

                string prefix = ins.IsPositive ? "[STRENGTH]" : "[BOTTLENECK]";
                card.AddChild(new Label
                {
                    Text = $"{prefix} {ins.Dimension}: {ins.Headline}",
                    Modulate = accent,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                });
                card.AddChild(new Label
                {
                    Text = $"Cause: {ins.CausalDetail}\nCoach Advice: {ins.RecommendedAction}",
                    Modulate = EsportsUiTheme.TextPrimary,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                });
                _insightsBox.AddChild(cardPanel);
            }
        }

        public void UpdateHud(InterpolatedFrameState frame, float dt)
        {
            SyncViewportRect();

            if (_phaseAndZoneLabel != null)
            {
                string shrinkText = frame.ZoneIsShrinking
                    ? "SHRINKING NOW!"
                    : $"SHRINK IN {MathF.Ceiling(frame.SecondsUntilShrink):0}s";
                _phaseAndZoneLabel.Text = $"STAGE {frame.ZoneStage} ({frame.Phase.ToString().ToUpperInvariant()}) · {shrinkText}";
            }

            if (_aliveCounterLabel != null)
            {
                _aliveCounterLabel.Text = $"SQUADS: {frame.AliveSquads}/12  |  PLAYERS: {frame.AliveUnits}/48";
            }

            if (_clockLabel != null)
            {
                _clockLabel.Text = FormatTime(frame.TimeSec);
            }

            if (_directorHeadlineLabel != null && _director != null)
            {
                _directorHeadlineLabel.Text = _director.CurrentHeadline;
            }

            if (_replayBannerLabel != null && _simHost != null)
            {
                _replayBannerLabel.Visible = _simHost.IsReplaying;
                if (_simHost.IsReplaying)
                {
                    _replayBannerLabel.Text = $"● {_simHost.ReplayLabel}";
                }
            }

            if (_camModeBtn != null && _cameraRig != null)
            {
                _camModeBtn.Text = $"CAM: {_cameraRig.Mode.ToString().ToUpperInvariant()}";
            }

            UpdateSpeedButtonHighlights();
            UpdateTacticalBannerState(dt);
            UpdateDropPhaseBar(frame);

            int focusedSquadIdx = _cameraRig != null ? _cameraRig.FollowedSquadIndex : _playerSquadIdx;
            if ((uint)focusedSquadIdx >= SnapshotBuffer.MaxSquads)
            {
                focusedSquadIdx = _playerSquadIdx;
            }

            // 1. Update Left 12-Squad Graphical Leaderboard
            UpdateLeftSquadLeaderboard(frame, focusedSquadIdx);

            // 2. Update Top-Right Interactive Radar Minimap
            Vector3 camFocus = _cameraRig != null ? _cameraRig.CurrentFocus : new Vector3(1000f, 0f, 1000f);
            _minimapWidget?.UpdateMinimap(frame, focusedSquadIdx, camFocus, dt);

            // 3. Update Bottom Featured Squad 4-Player Live Telemetry Bar
            UpdateFeaturedSquadTelemetry(frame, focusedSquadIdx);

            // 4. Update Bottom-Right Coach Command Panel
            _commandPanel?.UpdatePanel(frame, dt);
        }

        private void UpdateDropPhaseBar(InterpolatedFrameState frame)
        {
            if (_dropPhaseBarPanel == null || _dropStatusLabel == null || _dropClashLabel == null) return;

            bool isDropPhase = frame.Phase == Phase.Drop || frame.FlightPath.IsActive;
            if (!isDropPhase || (uint)_playerSquadIdx >= SnapshotBuffer.MaxSquads)
            {
                _dropPhaseBarPanel.Visible = false;
                return;
            }

            int baseUnit = _playerSquadIdx * 4;
            bool anyInPlane = false;
            bool anyAirborne = false;
            float maxAlt = 0f;
            UnitDropState domState = UnitDropState.Grounded;
            for (int m = 0; m < 4; m++)
            {
                ref readonly InterpolatedUnitState u = ref frame.Units[baseUnit + m];
                if (u.DropState != UnitDropState.Grounded)
                {
                    anyAirborne = true;
                    if (u.DropState == UnitDropState.InPlane) anyInPlane = true;
                    if (u.AltitudeMeters > maxAlt)
                    {
                        maxAlt = u.AltitudeMeters;
                        domState = u.DropState;
                    }
                }
            }

            if (!anyAirborne && frame.TimeSec > 28f)
            {
                _dropPhaseBarPanel.Visible = false;
                return;
            }

            _dropPhaseBarPanel.Visible = true;
            ref readonly SquadSnapshot pSq = ref frame.Squads[_playerSquadIdx];
            string poiName = (pSq.DropPoiId >= 0 && _map?.Pois != null && pSq.DropPoiId < _map.Pois.Count)
                ? _map.Pois[pSq.DropPoiId].Name
                : $"POI #{pSq.DropPoiId}";

            if (anyInPlane)
            {
                float eta = Math.Max(0f, pSq.DropLaunchTick * 0.1f - frame.TimeSec);
                _dropStatusLabel.Text = $"✈️ C-130 INBOUND → {poiName.ToUpperInvariant()} (LAUNCH IN {eta:0.0}s)";
                _dropStatusLabel.Modulate = EsportsUiTheme.AccentCyan;
            }
            else if (domState == UnitDropState.Freefall)
            {
                _dropStatusLabel.Text = $"🏄 SURFBOARD DIVE → {poiName.ToUpperInvariant()} · ALT {maxAlt:0}m";
                _dropStatusLabel.Modulate = EsportsUiTheme.AccentGold;
            }
            else if (domState == UnitDropState.Gliding)
            {
                _dropStatusLabel.Text = $"🪂 CANOPY GLIDE → {poiName.ToUpperInvariant()} · ALT {maxAlt:0}m";
                _dropStatusLabel.Modulate = EsportsUiTheme.AccentGreen;
            }
            else
            {
                _dropStatusLabel.Text = $"✅ TOUCHDOWN AT {poiName.ToUpperInvariant()} — LOOTING";
                _dropStatusLabel.Modulate = EsportsUiTheme.AccentGreen;
            }

            // Count rival squads targeting the same POI
            int rivalCount = 0;
            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                if (s == _playerSquadIdx || frame.Squads[s].AliveCount <= 0) continue;
                if (frame.Squads[s].DropPoiId == pSq.DropPoiId)
                {
                    rivalCount++;
                }
            }

            if (rivalCount == 0)
            {
                _dropClashLabel.Text = "🟢 UNCONTESTED (0 RIVALS)";
                _dropClashLabel.Modulate = EsportsUiTheme.AccentGreen;
            }
            else if (rivalCount == 1)
            {
                _dropClashLabel.Text = "🟡 CONTESTED (1 RIVAL SQUAD)";
                _dropClashLabel.Modulate = EsportsUiTheme.AccentGold;
            }
            else
            {
                _dropClashLabel.Text = $"🔴 HOT DROP ({rivalCount} RIVAL SQUADS!)";
                _dropClashLabel.Modulate = EsportsUiTheme.AccentCrimson;
            }

            if (_dropPoiSelector != null)
            {
                _dropPoiSelector.Disabled = !anyInPlane;
                if (pSq.DropPoiId != _lastSyncedDropPoiId && _map?.Pois != null)
                {
                    _lastSyncedDropPoiId = pSq.DropPoiId;
                    for (int i = 0; i < _dropPoiSelector.ItemCount; i++)
                    {
                        if (_dropPoiSelector.GetItemId(i) == pSq.DropPoiId)
                        {
                            _dropPoiSelector.Select(i);
                            break;
                        }
                    }
                }
            }
        }

        private void UpdateSpeedButtonHighlights()
        {
            if (_simHost == null) return;
            bool paused = _simHost.IsPaused;
            float spd = _simHost.SpeedMultiplier;

            if (_pauseBtn != null)
            {
                _pauseBtn.Modulate = paused ? EsportsUiTheme.AccentGold : Colors.White;
            }
            if (_speed1xBtn != null)
            {
                _speed1xBtn.Modulate = (!paused && MathF.Abs(spd - 1f) < 0.2f) ? EsportsUiTheme.AccentCyan : Colors.White;
            }
            if (_speed2xBtn != null)
            {
                _speed2xBtn.Modulate = (!paused && MathF.Abs(spd - 2f) < 0.2f) ? EsportsUiTheme.AccentCyan : Colors.White;
            }
            if (_speed4xBtn != null)
            {
                _speed4xBtn.Modulate = (!paused && spd >= 3.5f) ? EsportsUiTheme.AccentGold : Colors.White;
            }
        }

        private void UpdateTacticalBannerState(float dt)
        {
            if (_tacticalBannerPanel == null || _tacticalBannerLabel == null || _tacticalBannerStyle == null) return;

            if (_tacticalBannerTimer > 0f)
            {
                _tacticalBannerTimer = Math.Max(0f, _tacticalBannerTimer - dt);
            }

            if (_tacticalBannerTimer <= 0f)
            {
                if (_simHost != null && _simHost.IsPaused && !_simHost.IsFinished && !_simHost.IsReplaying)
                {
                    _tacticalBannerLabel.Text = "⏸ MATCH PAUSED — TACTICAL PLANNING MODE (PRESS SPACE OR 1x..4x TO RESUME)";
                    _tacticalBannerLabel.Modulate = EsportsUiTheme.AccentGold;
                    _tacticalBannerStyle.BorderColor = EsportsUiTheme.AccentGold;
                    _tacticalBannerPanel.Visible = true;
                }
                else
                {
                    _tacticalBannerPanel.Visible = false;
                }
            }
        }

        private void UpdateLeftSquadLeaderboard(InterpolatedFrameState frame, int focusedSquadIdx)
        {
            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                SquadLeaderboardRowUi row = _squadRows[s];
                if (row == null) continue;

                SquadSnapshot sq = frame.Squads[s];
                bool isAlive = sq.AliveCount > 0;
                bool isFocused = (s == focusedSquadIdx);
                bool isPlayer = (s == _playerSquadIdx);

                Color teamCol = isPlayer
                    ? EsportsUiTheme.AccentGold
                    : BattlefieldView.GetSquadColor(s, GetSquadColorHex(s));

                row.TeamStrip.Color = isAlive ? teamCol : EsportsUiTheme.TextMuted;

                if (isFocused)
                {
                    row.CardStyle.BgColor = new Color(0.14f, 0.20f, 0.30f, 0.96f);
                    row.CardStyle.BorderColor = isPlayer ? EsportsUiTheme.AccentGold : EsportsUiTheme.AccentCyan;
                }
                else
                {
                    row.CardStyle.BgColor = isAlive
                        ? new Color(0.08f, 0.11f, 0.18f, 0.88f)
                        : new Color(0.05f, 0.07f, 0.11f, 0.60f);
                    row.CardStyle.BorderColor = isPlayer
                        ? new Color(1.0f, 0.72f, 0.0f, 0.55f)
                        : new Color(0.20f, 0.26f, 0.36f, 0.55f);
                }

                string tag = GetSquadShortTag(s);
                string prefix = isPlayer ? "★" : $"{s + 1:00}";
                row.TagLabel.Text = $"{prefix} {tag}";
                row.TagLabel.Modulate = isAlive
                    ? (isPlayer ? EsportsUiTheme.AccentGold : EsportsUiTheme.TextPrimary)
                    : EsportsUiTheme.TextMuted;

                // Update 4 mini vertical HP bars
                int baseUnit = s * 4;
                for (int m = 0; m < 4; m++)
                {
                    ref readonly InterpolatedUnitState u = ref frame.Units[baseUnit + m];
                    ColorRect fill = row.HpBarFills[m];
                    if (u.State == UnitState.Eliminated)
                    {
                        fill.Size = new Vector2(6f, 2f);
                        fill.Position = new Vector2(0f, 14f);
                        fill.Color = new Color(0.25f, 0.28f, 0.34f, 0.5f);
                    }
                    else if (u.State == UnitState.Knocked)
                    {
                        fill.Size = new Vector2(6f, 8f);
                        fill.Position = new Vector2(0f, 8f);
                        fill.Color = EsportsUiTheme.AccentAmber;
                    }
                    else
                    {
                        float ratio = Math.Clamp(u.Hp / 200f, 0.12f, 1f);
                        float h = MathF.Round(16f * ratio);
                        fill.Size = new Vector2(6f, h);
                        fill.Position = new Vector2(0f, 16f - h);
                        fill.Color = ratio > 0.5f ? EsportsUiTheme.AccentGreen : EsportsUiTheme.AccentGold;
                    }
                }

                row.KillsLabel.Text = $"{sq.TotalKills}K";
                row.KillsLabel.Modulate = sq.TotalKills > 0 ? EsportsUiTheme.AccentGold : EsportsUiTheme.TextSecondary;

                string modeStr = isAlive
                    ? sq.CurrentMode.ToString().ToUpperInvariant()
                    : (sq.Placement > 0 ? $"#{sq.Placement}" : "OUT");
                if (modeStr.Length > 4) modeStr = modeStr[..4];
                row.ModeLabel.Text = modeStr;
                row.ModeLabel.Modulate = isAlive ? EsportsUiTheme.AccentCyan : EsportsUiTheme.TextMuted;
            }
        }

        private void RefreshFeaturedSquadStaticInfo(int squadIdx)
        {
            if (_matchCfg == null || (uint)squadIdx >= _matchCfg.Squads.Count) return;
            _lastFeaturedSquadIdx = squadIdx;

            SquadDef sqDef = _matchCfg.Squads[squadIdx];
            bool isPlayerSquad = (squadIdx == _playerSquadIdx);
            Color teamCol = isPlayerSquad
                ? EsportsUiTheme.AccentGold
                : BattlefieldView.GetSquadColor(squadIdx, sqDef.ColorHex);

            if (_featuredSquadLogo != null)
            {
                _featuredSquadLogo.Texture = VisualAssetRegistry.GetTeamLogo(sqDef.SquadId, sqDef.Name, teamCol);
            }

            if (_featuredSquadHeaderLabel != null)
            {
                string badge = isPlayerSquad ? " [YOUR SQUAD]" : "";
                _featuredSquadHeaderLabel.Text = $"OBSERVING: {sqDef.Name.ToUpperInvariant()}{badge}";
                _featuredSquadHeaderLabel.Modulate = isPlayerSquad ? EsportsUiTheme.AccentGold : EsportsUiTheme.AccentCyan;
            }

            if (_focusMySquadBtn != null)
            {
                _focusMySquadBtn.Visible = (squadIdx != _playerSquadIdx);
            }

            for (int m = 0; m < 4; m++)
            {
                FeaturedPlayerCardUi card = _featuredPlayerCards[m];
                if (card == null) continue;

                if (m < sqDef.Members.Count)
                {
                    var slot = sqDef.Members[m];
                    card.PortraitRect.Texture = VisualAssetRegistry.GetPlayerPortrait(slot.Player.PlayerId, slot.Role, teamCol);
                    card.RoleIconRect.Texture = VisualAssetRegistry.GetRoleIcon(slot.Role);

                    string iglBadge = slot.IsIgl ? "👑 " : "";
                    card.NameLabel.Text = $"{iglBadge}{slot.Player.Name}";

                    SkillCategory skCat = SkillCategory.Dash;
                    if (_config?.Skills != null)
                    {
                        var sDef = _config.Skills.FindSkill(slot.ActiveSkillId);
                        if (sDef != null) skCat = sDef.GetCategoryEnum();
                    }
                    card.SkillIconRect.Texture = VisualAssetRegistry.GetSkillIcon(slot.ActiveSkillId, isActive: true, skCat);
                }
            }
        }

        private void UpdateFeaturedSquadTelemetry(InterpolatedFrameState frame, int focusedSquadIdx)
        {
            if (focusedSquadIdx != _lastFeaturedSquadIdx)
            {
                RefreshFeaturedSquadStaticInfo(focusedSquadIdx);
            }

            if ((uint)focusedSquadIdx >= SnapshotBuffer.MaxSquads) return;
            SquadSnapshot sqSnap = frame.Squads[focusedSquadIdx];

            if (_featuredSquadMetaLabel != null)
            {
                string status = sqSnap.AliveCount > 0
                    ? $"MODE: {sqSnap.CurrentMode.ToString().ToUpperInvariant()}  ·  ALIVE: {sqSnap.AliveCount}/4  ·  SQUAD KILLS: {sqSnap.TotalKills}"
                    : $"ELIMINATED (PLACEMENT #{sqSnap.Placement})  ·  SQUAD KILLS: {sqSnap.TotalKills}";
                _featuredSquadMetaLabel.Text = status;
            }

            int baseUnit = focusedSquadIdx * 4;
            for (int m = 0; m < 4; m++)
            {
                FeaturedPlayerCardUi card = _featuredPlayerCards[m];
                if (card == null) continue;

                ref readonly InterpolatedUnitState u = ref frame.Units[baseUnit + m];

                // State & Behavior styling
                if (u.State == UnitState.Eliminated)
                {
                    card.CardStyle.BorderColor = new Color(0.25f, 0.28f, 0.35f, 0.5f);
                    card.PortraitRect.Modulate = new Color(0.4f, 0.42f, 0.48f, 0.75f);
                    card.BehaviorLabel.Text = "OUT";
                    card.BehaviorLabel.Modulate = EsportsUiTheme.AccentCrimson;
                    card.HpBar.Value = 0;
                    card.HpValueLabel.Text = "0";
                }
                else if (u.State == UnitState.Knocked)
                {
                    card.CardStyle.BorderColor = EsportsUiTheme.AccentAmber;
                    card.PortraitRect.Modulate = new Color(1.0f, 0.78f, 0.45f, 0.95f);
                    card.BehaviorLabel.Text = "KNOCKED!";
                    card.BehaviorLabel.Modulate = EsportsUiTheme.AccentAmber;
                    card.HpBar.Value = u.Hp;
                    card.HpFillStyle.BgColor = EsportsUiTheme.AccentAmber;
                    card.HpValueLabel.Text = $"{MathF.Ceiling(u.Hp):0}";
                }
                else
                {
                    card.PortraitRect.Modulate = Colors.White;
                    bool isFighting = u.CurrentBehavior == Behavior.Fire || u.CurrentBehavior == Behavior.Throw || u.CurrentBehavior == Behavior.PlaceGloo;
                    card.CardStyle.BorderColor = isFighting ? EsportsUiTheme.AccentCrimson : EsportsUiTheme.BorderSubtle;

                    string behText;
                    if (u.DropState == UnitDropState.InPlane)
                    {
                        behText = "✈️C-130 ABOARD";
                    }
                    else if (u.DropState == UnitDropState.Freefall)
                    {
                        behText = $"🏄SURF {u.AltitudeMeters:0}m";
                    }
                    else if (u.DropState == UnitDropState.Gliding)
                    {
                        behText = $"🪂GLIDE {u.AltitudeMeters:0}m";
                    }
                    else
                    {
                        behText = FormatShortBehavior(u.CurrentBehavior, u.Role, sqSnap.CurrentMode);
                        if (u.IsFlanked)
                            behText = $"⚠️FLANK {behText}";
                        else if (u.AtWindow)
                            behText = $"🪟PEEK {behText}";
                        else if (u.AtDoorway && isFighting)
                            behText = $"🚪BREACH {behText}";
                        else if (u.IsIndoors)
                            behText = $"🏠INDOOR {behText}";
                        else if (u.EffectiveCoverPct >= 0.08f)
                            behText = $"🛡{(int)MathF.Round(u.EffectiveCoverPct * 100f)}% {behText}";
                    }

                    card.BehaviorLabel.Text = behText;
                    card.BehaviorLabel.Modulate = u.IsAirborne
                        ? EsportsUiTheme.AccentCyan
                        : (u.IsFlanked
                            ? EsportsUiTheme.AccentCrimson
                            : (isFighting ? EsportsUiTheme.AccentCrimson : (u.IsIndoors ? EsportsUiTheme.AccentGold : EsportsUiTheme.AccentCyan)));

                    float hp = Math.Clamp(u.Hp, 0f, 200f);
                    card.HpBar.Value = hp;
                    card.HpFillStyle.BgColor = hp > 110f
                        ? EsportsUiTheme.AccentGreen
                        : (hp > 55f ? EsportsUiTheme.AccentGold : EsportsUiTheme.AccentCrimson);
                    card.HpValueLabel.Text = $"{MathF.Ceiling(hp):0}";
                }

                // Weapon Icon & Name (reflects live dual-weapon switching between AR/DMR/Sniper & CQC SMG/Shotgun)
                WeaponCategory wCat = WeaponCategory.AR;
                int wTier = 1;
                string wName = "AR T1";
                if (_config?.Weapons?.Weapons != null &&
                    (uint)u.PrimaryWeaponIndex < (uint)_config.Weapons.Weapons.Count)
                {
                    WeaponDef wDef = _config.Weapons.Weapons[u.PrimaryWeaponIndex];
                    wCat = wDef.GetCategoryEnum();
                    wTier = wDef.Tier;
                    wName = $"{wDef.WeaponId.ToUpperInvariant()} T{wDef.Tier}";
                }
                card.WeaponIconRect.Texture = VisualAssetRegistry.GetWeaponIcon(wCat, wTier);
                card.WeaponNameLabel.Text = wName;

                // Active Skill Status
                if (u.ActiveSkillDurationRemaining > 0.05f)
                {
                    card.SkillStatusLabel.Text = "ACTIVE!";
                    card.SkillStatusLabel.Modulate = EsportsUiTheme.AccentCyan;
                }
                else if (u.ActiveSkillCooldownRemaining <= 0.1f)
                {
                    card.SkillStatusLabel.Text = "READY";
                    card.SkillStatusLabel.Modulate = EsportsUiTheme.AccentGreen;
                }
                else
                {
                    card.SkillStatusLabel.Text = $"{MathF.Ceiling(u.ActiveSkillCooldownRemaining):0}s";
                    card.SkillStatusLabel.Modulate = EsportsUiTheme.TextMuted;
                }

                // Utility & Stats (with Indoor / Cover / Flank / Airborne indicator)
                int dmgInt = (int)MathF.Round(u.DamageDealt);
                string posBadge = u.IsAirborne
                    ? $"🪂{u.AltitudeMeters:0}m"
                    : (u.IsFlanked
                        ? "⚠️FLK"
                        : (u.AtWindow ? "🪟WIN" : (u.IsIndoors ? "🏠IND" : (u.EffectiveCoverPct >= 0.08f ? $"🛡{(int)MathF.Round(u.EffectiveCoverPct * 100f)}%" : "OPEN"))));
                card.UtilityAndStatsLabel.Text =
                    $"{posBadge}  A{u.ArmorTier}·H{u.HelmetTier}  🧊{u.GlooCount}·💣{u.GrenadeCount}  {u.Kills}K({dmgInt})";
            }
        }

        private static string FormatShortBehavior(Behavior beh, Role role, SquadMode mode)
        {
            return beh switch
            {
                Behavior.Fire => role == Role.Rusher ? "CQC FIRE" : (role == Role.Sniper ? "SNIPE" : "FIRE"),
                Behavior.Reload => "RELOAD",
                Behavior.TakeCover => "COVER",
                Behavior.Heal => "HEAL",
                Behavior.Revive => "REVIVE",
                Behavior.PlaceGloo => "GLOO",
                Behavior.Throw => "NADE",
                Behavior.Mount => "DRIVE",
                Behavior.Loot => "LOOT",
                Behavior.Rotate => "ROTATE",
                _ => (mode == SquadMode.Push || mode == SquadMode.Contest)
                    ? (role == Role.Rusher ? "RUSH CQC" : (role == Role.SecondRusher ? "FLANK" : "ENGAGE"))
                    : "FOLLOW"
            };
        }

        private void OnSimEvent(SimEvent ev)
        {
            if (ev.Type == SimEventType.CommandExecuted && ev.SquadIdx == _playerSquadIdx)
            {
                bool complied = ev.Value >= 0.5f;
                string cmdTag = ((CommandType)ev.ExtraCode) switch
                {
                    CommandType.RotateNow => "ROTATE NOW",
                    CommandType.HoldPosition => "HOLD & FORTIFY",
                    CommandType.ContestTarget => "PUSH / CONTEST",
                    CommandType.AvoidFight => "DISENGAGE",
                    CommandType.PopSkill => "POP ACTIVE SKILLS",
                    CommandType.EndgameFormation => "ENDGAME FORMATION",
                    _ => "COACH ORDER"
                };
                ShowTacticalBanner(
                    complied
                        ? $"📻 IGL RADIO: COMPLIED WITH [{cmdTag}]!"
                        : $"⚠️ IGL RADIO: OVERRODE [{cmdTag}] (UNDER HEAVY COMBAT PRESSURE)!",
                    complied ? EsportsUiTheme.AccentGreen : EsportsUiTheme.AccentAmber,
                    2.8f);
            }
            else if (ev.Type == SimEventType.DropVeerOff && ev.SquadIdx == _playerSquadIdx)
            {
                string veerPoi = (_map?.Pois != null && ev.ExtraCode >= 0 && ev.ExtraCode < _map.Pois.Count)
                    ? _map.Pois[ev.ExtraCode].Name
                    : $"POI #{ev.ExtraCode}";
                ShowTacticalBanner(
                    $"🪂 IGL MID-AIR VEER-OFF → DIVERTING TO {veerPoi.ToUpperInvariant()}!",
                    EsportsUiTheme.AccentGold,
                    3.0f);
            }
            else if (ev.Type == SimEventType.Booyah)
            {
                ShowTacticalBanner(
                    $"🏆 ★ BOOYAH! {GetSquadShortTag(ev.SquadIdx)} WINS THE MATCH! ★",
                    EsportsUiTheme.AccentGold,
                    4.0f);
            }

            if (_eventFeedBox == null) return;

            string? text = FormatEventFeedLine(ev);
            if (string.IsNullOrEmpty(text)) return;

            Color col = ev.Type switch
            {
                SimEventType.SquadWiped => EsportsUiTheme.AccentCrimson,
                SimEventType.Eliminated => new Color(1.0f, 0.48f, 0.38f),
                SimEventType.Knocked => EsportsUiTheme.AccentGold,
                SimEventType.ShotFired => EsportsUiTheme.AccentGold,
                SimEventType.GrenadeThrown => new Color(1.0f, 0.58f, 0.22f),
                SimEventType.SkillTriggered => EsportsUiTheme.AccentCyan,
                SimEventType.DropLaunched => EsportsUiTheme.AccentCyan,
                SimEventType.ParachuteOpened => EsportsUiTheme.AccentGreen,
                SimEventType.DropVeerOff => EsportsUiTheme.AccentAmber,
                SimEventType.CommandExecuted => EsportsUiTheme.AccentGreen,
                SimEventType.Booyah => EsportsUiTheme.AccentGold,
                _ => EsportsUiTheme.TextPrimary
            };

            var lbl = new Label
            {
                Text = $"[{FormatTime(ev.Tick * 0.1f)}] {text}",
                Modulate = col,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            lbl.AddThemeFontSizeOverride("font_size", 11);
            _eventFeedBox.AddChild(lbl);
            _eventFeedItems.Enqueue(lbl);

            while (_eventFeedItems.Count > 6)
            {
                Control oldest = _eventFeedItems.Dequeue();
                oldest.QueueFree();
            }
        }

        private string? FormatEventFeedLine(SimEvent ev)
        {
            string sqA = GetSquadShortTag(ev.SquadIdx);
            string sqB = GetSquadShortTag(ev.TargetSquadIdx);

            return ev.Type switch
            {
                SimEventType.DropLaunched =>
                    $"🏄 {sqA} launched surfboard dive → {((_map?.Pois != null && ev.ExtraCode >= 0 && ev.ExtraCode < _map.Pois.Count) ? _map.Pois[ev.ExtraCode].Name : "POI")}",
                SimEventType.ParachuteOpened when ev.SquadIdx == _playerSquadIdx =>
                    $"🪂 {sqA} deployed parachute canopy ({ev.Value:0}m)",
                SimEventType.DropVeerOff =>
                    $"🪂⚠️ {sqA} veered off mid-air → {((_map?.Pois != null && ev.ExtraCode >= 0 && ev.ExtraCode < _map.Pois.Count) ? _map.Pois[ev.ExtraCode].Name : "Alternate POI")}",
                SimEventType.Knocked => $"{sqA} knocked down {sqB}",
                SimEventType.Eliminated => $"{sqA} eliminated {sqB} player",
                SimEventType.SquadWiped => $"★ {sqB} SQUAD WIPED by {sqA}!",
                SimEventType.Revived => $"{sqA} revived knocked teammate",
                SimEventType.ShotFired when (ev.ExtraCode & 512) != 0 && ev.Value >= 28f =>
                    $"🚪💥 {sqA} CQC BREACH hit {sqB} (-{ev.Value:0} HP)!",
                SimEventType.ShotFired when (ev.ExtraCode & 64) != 0 && ev.Value >= 28f =>
                    $"⚠️ {sqA} FLANKED {sqB} cover (-{ev.Value:0} HP)!",
                SimEventType.ShotFired when (ev.ExtraCode & 16) != 0 && ev.Value >= 42f =>
                    $"🎯 {sqA} DRAG-SHOT HEADSHOT on {sqB} (-{ev.Value:0} HP)!",
                SimEventType.GrenadeThrown when (ev.ExtraCode & 1) != 0 && ev.Value >= 20f =>
                    $"🪟💣 {sqA} INDOOR ROOM BLAST on {sqB} (-{ev.Value:0} HP)!",
                SimEventType.GrenadeThrown when ev.Value >= 35f =>
                    $"💣 {sqA} Frag Grenade blasted {sqB} (-{ev.Value:0} HP)!",
                SimEventType.SkillTriggered => $"{sqA} activated Tactical Skill",
                SimEventType.GlooPlaced => $"{sqA} deployed Gloo Wall cover",
                SimEventType.ZoneShrinkStart => $"Safe Zone Stage {ev.ExtraCode} shrinking!",
                SimEventType.CommandExecuted => ev.Value >= 0.5f
                    ? $"{sqA} IGL complied with Coach Order"
                    : $"{sqA} IGL overrode Coach Order",
                SimEventType.Booyah => $"★ BOOYAH! {sqA} wins the match! ★",
                _ => null
            };
        }

        private void RefreshSquadRowsInitial()
        {
            for (int s = 0; s < SnapshotBuffer.MaxSquads; s++)
            {
                SquadLeaderboardRowUi row = _squadRows[s];
                if (row == null) continue;

                Color teamCol = (s == _playerSquadIdx)
                    ? EsportsUiTheme.AccentGold
                    : BattlefieldView.GetSquadColor(s, GetSquadColorHex(s));

                string sqId = (_matchCfg != null && s < _matchCfg.Squads.Count)
                    ? _matchCfg.Squads[s].SquadId
                    : $"sq_{s}";
                string sqName = (_matchCfg != null && s < _matchCfg.Squads.Count)
                    ? _matchCfg.Squads[s].Name
                    : $"Squad {s + 1}";

                row.LogoRect.Texture = VisualAssetRegistry.GetTeamLogo(sqId, sqName, teamCol);
                row.TeamStrip.Color = teamCol;
                row.TagLabel.Text = $"{(s == _playerSquadIdx ? "★" : $"{s + 1:00}")} {GetSquadShortTag(s)}";
                row.KillsLabel.Text = "0K";
                row.ModeLabel.Text = "DROP";
            }
        }

        private void ClearEventFeed()
        {
            _eventFeedItems.Clear();
            if (_eventFeedBox == null) return;
            foreach (Node child in _eventFeedBox.GetChildren())
            {
                child.QueueFree();
            }
        }

        private string GetSquadShortTag(int squadIdx)
        {
            if (_matchCfg != null && (uint)squadIdx < _matchCfg.Squads.Count)
            {
                return BattlefieldView.GetShortTag(_matchCfg.Squads[squadIdx].Name, squadIdx);
            }
            return squadIdx >= 0 ? $"T{squadIdx + 1:00}" : "MAP";
        }

        private string? GetSquadColorHex(int squadIdx)
        {
            if (_matchCfg != null && (uint)squadIdx < _matchCfg.Squads.Count)
            {
                return _matchCfg.Squads[squadIdx].ColorHex;
            }
            return null;
        }

        private static Button AddTopButton(HBoxContainer parent, string text, float minWidth, Action onPressed)
        {
            var btn = EsportsUiTheme.CreateActionButton(text, isPrimaryGold: false, minSize: new Vector2(minWidth, 28f));
            btn.AddThemeFontSizeOverride("font_size", 11);
            btn.Pressed += onPressed;
            parent.AddChild(btn);
            return btn;
        }

        private static string FormatTime(float sec)
        {
            int total = Math.Max(0, (int)MathF.Floor(sec));
            int mins = total / 60;
            int rem = total % 60;
            return $"{mins:00}:{rem:00}";
        }
    }
}
