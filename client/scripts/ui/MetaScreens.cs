using System;
using System.Collections.Generic;
using System.IO;
using FFM.Client.Art;
using FFM.Meta;
using FFM.Meta.Models;
using FFM.Sim.Data;
using FFM.Sim.Map;
using Godot;

namespace FFM.Client.UI
{
    /// <summary>
    /// Full Esports Franchise Career Hub UI (§7, §8, WP-16, WP-24).
    /// Overhauled with EsportsUiTheme Bento cards, VisualAssetRegistry portraits/logos/skill icons,
    /// interactive 1-Active + 3-Passive skill selectors, 15-attribute player inspector,
    /// side-by-side 2D Bermuda TacticalMapPreview, visual training bars, and aligned FFWS standings table.
    /// </summary>
    public partial class MetaScreens : Control
    {
        private CareerController? _career;

        // Top Header widgets
        private TextureRect? _clubLogoRect;
        private Label? _clubTitleLabel;
        private Label? _calendarLabel;
        private Label? _economyLabel;
        private PanelContainer? _toastCard;
        private StyleBoxFlat? _toastStyle;
        private Label? _toastLabel;
        private float _toastPulseTimer;
        private Color _toastAccentColor = EsportsUiTheme.CyanElectric;
        private int _actionCounter;

        // Idle Clock & Vault Header Bar widgets
        private Label? _idleDayLabel;
        private ProgressBar? _idleDayProgress;
        private Button? _idlePauseBtn;
        private Button? _idle1xBtn;
        private Button? _idle2xBtn;
        private Button? _idle4xBtn;
        private Button? _idlePolicyBtn;
        private Label? _idleResourcesLabel;
        private Button? _claimVaultBtn;

        // Tab content containers
        private PanelContainer? _bgPanel;
        private TabContainer? _tabs;
        private VBoxContainer? _overviewTab;
        private VBoxContainer? _lineupTab;
        private VBoxContainer? _playbookTab;
        private VBoxContainer? _trainingTab;
        private VBoxContainer? _standingsTab;
        private VBoxContainer? _scoutingTab;

        // Selection states for rich inspection
        private string? _inspectedPlayerId;
        private PoiDef? _inspectedPoi;

        public event Action? PlayInteractiveMatchRequested;

        public override void _Ready()
        {
            Theme = EsportsUiTheme.GetOrCreateTheme();
            EsportsUiTheme.SetFullRect(this, 1920f, 1080f);
            SyncViewportRect();
            BuildMetaHubUi();
            RefreshAllTabs();
        }

        public override void _Process(double delta)
        {
            if (Visible)
            {
                SyncViewportRect();

                if (_career != null)
                {
                    var tickRes = _career.TickIdleProgression((float)delta);
                    UpdateIdleClockWidgets();

                    if (tickRes.AutoQuickSimReport != null)
                    {
                        var rep = tickRes.AutoQuickSimReport;
                        ShowToast($"[挂机自动完赛] 排名 #{rep.Placement} · 淘汰 {rep.Kills} · 积分 +{rep.TotalPoints} PTS");
                        RefreshAllTabs();
                    }
                    else if (tickRes.ShouldAutoLaunch3DMatch)
                    {
                        ShowToast("[挂机赛程] 已抵达 Day 7 比赛日，自动拉起 3D 赛场！");
                        RefreshAllTabs();
                        PlayInteractiveMatchRequested?.Invoke();
                    }
                    else if (tickRes.AdvancedDay)
                    {
                        if (tickRes.NewLogEntries.Count > 0)
                        {
                            ShowToast(tickRes.NewLogEntries[^1]);
                        }
                        RefreshAllTabs();
                    }
                }

                if (_toastPulseTimer > 0f)
                {
                    _toastPulseTimer = MathF.Max(0f, _toastPulseTimer - (float)delta);
                    float t = _toastPulseTimer / 1.6f;
                    if (_toastStyle != null)
                    {
                        _toastStyle.BorderColor = EsportsUiTheme.NavyBorder.Lerp(_toastAccentColor, t);
                        _toastStyle.BgColor = new Color(0.06f, 0.09f, 0.15f, 0.94f).Lerp(
                            new Color(_toastAccentColor.R * 0.22f, _toastAccentColor.G * 0.22f, _toastAccentColor.B * 0.22f, 0.98f),
                            t * 0.85f);
                    }
                    if (_toastLabel != null)
                    {
                        _toastLabel.Modulate = EsportsUiTheme.TextPrimary.Lerp(_toastAccentColor.Lightened(0.25f), t);
                    }
                }
            }
        }

        private void SyncViewportRect()
        {
            Vector2 vpSize = GetViewportRect().Size;
            if (vpSize.X < 100f || vpSize.Y < 100f)
            {
                vpSize = new Vector2(1920f, 1080f);
            }
            if (Size != vpSize)
            {
                Size = vpSize;
            }
            if (_bgPanel != null && _bgPanel.CustomMinimumSize != vpSize)
            {
                _bgPanel.CustomMinimumSize = vpSize;
                _bgPanel.Size = vpSize;
            }
        }

        public void BindCareer(CareerController career)
        {
            _career = career;
            RefreshAllTabs();
        }

        private void BuildMetaHubUi()
        {
            _bgPanel = new PanelContainer();
            EsportsUiTheme.SetFullRect(_bgPanel, 1920f, 1080f);
            _bgPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = EsportsUiTheme.NavyBgDark,
                ContentMarginLeft = 18,
                ContentMarginRight = 18,
                ContentMarginTop = 12,
                ContentMarginBottom = 12
            });
            AddChild(_bgPanel);

            var mainVBox = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            mainVBox.AddThemeConstantOverride("separation", 8);
            _bgPanel.AddChild(mainVBox);

            // 1. Top Franchise Broadcast Header Bar
            var headerPanel = new PanelContainer();
            headerPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = EsportsUiTheme.NavySurfaceElevated,
                BorderColor = EsportsUiTheme.GoldPrimary,
                BorderWidthBottom = 2,
                BorderWidthLeft = 4,
                CornerRadiusTopLeft = 6,
                CornerRadiusTopRight = 6,
                CornerRadiusBottomLeft = 6,
                CornerRadiusBottomRight = 6,
                ContentMarginLeft = 16,
                ContentMarginRight = 16,
                ContentMarginTop = 8,
                ContentMarginBottom = 8
            });
            mainVBox.AddChild(headerPanel);

            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 14);
            headerPanel.AddChild(headerHBox);

            _clubLogoRect = new TextureRect
            {
                CustomMinimumSize = new Vector2(42f, 42f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            headerHBox.AddChild(_clubLogoRect);

            var titleVBox = new VBoxContainer();
            titleVBox.AddThemeConstantOverride("separation", 2);
            headerHBox.AddChild(titleVBox);

            _clubTitleLabel = new Label
            {
                Text = "FREE FIRE ESPORTS MANAGER",
                Modulate = EsportsUiTheme.GoldPrimary
            };
            _clubTitleLabel.AddThemeFontSizeOverride("font_size", 18);
            titleVBox.AddChild(_clubTitleLabel);

            _calendarLabel = new Label
            {
                Text = "SEASON 1 · WEEK 1",
                Modulate = EsportsUiTheme.CyanElectric
            };
            _calendarLabel.AddThemeFontSizeOverride("font_size", 13);
            titleVBox.AddChild(_calendarLabel);

            headerHBox.AddChild(new VSeparator());

            _economyLabel = new Label
            {
                Text = "BUDGET: $180,000 | SCOUT PTS: 120",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = EsportsUiTheme.EmeraldSuccess
            };
            _economyLabel.AddThemeFontSizeOverride("font_size", 15);
            headerHBox.AddChild(_economyLabel);

            var saveBtn = EsportsUiTheme.CreateStyledButton("SAVE", isPrimaryGold: false, minWidth: 84f, minHeight: 34f);
            saveBtn.Pressed += OnSaveCareerPressed;
            headerHBox.AddChild(saveBtn);

            var loadBtn = EsportsUiTheme.CreateStyledButton("LOAD", isPrimaryGold: false, minWidth: 84f, minHeight: 34f);
            loadBtn.Pressed += OnLoadCareerPressed;
            headerHBox.AddChild(loadBtn);

            var quickSimBtn = EsportsUiTheme.CreateStyledButton("⚡ QUICK SIM WEEK", isPrimaryGold: false, minWidth: 150f, minHeight: 34f);
            quickSimBtn.Pressed += OnQuickSimWeekPressed;
            headerHBox.AddChild(quickSimBtn);

            var launchMatchBtn = EsportsUiTheme.CreateStyledButton("▶ LAUNCH 3D MATCH DAY", isPrimaryGold: true, minWidth: 215f, minHeight: 36f);
            launchMatchBtn.Pressed += () => PlayInteractiveMatchRequested?.Invoke();
            headerHBox.AddChild(launchMatchBtn);

            // 1B. Continuous Idle Progression Clock & Idle Vault Header Bar
            var idleBarPanel = new PanelContainer();
            idleBarPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.055f, 0.085f, 0.145f, 0.96f),
                BorderColor = EsportsUiTheme.CyanElectric,
                BorderWidthLeft = 4,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 5,
                CornerRadiusTopRight = 5,
                CornerRadiusBottomLeft = 5,
                CornerRadiusBottomRight = 5,
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 6,
                ContentMarginBottom = 6
            });
            mainVBox.AddChild(idleBarPanel);

            var idleHBox = new HBoxContainer();
            idleHBox.AddThemeConstantOverride("separation", 10);
            idleBarPanel.AddChild(idleHBox);

            _idleDayLabel = new Label
            {
                Text = "⏱ 挂机日程: DAY 1/7 (基地日常特训)",
                CustomMinimumSize = new Vector2(235f, 0f),
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = EsportsUiTheme.CyanElectric
            };
            _idleDayLabel.AddThemeFontSizeOverride("font_size", 13);
            idleHBox.AddChild(_idleDayLabel);

            _idleDayProgress = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 0,
                ShowPercentage = true,
                CustomMinimumSize = new Vector2(150f, 22f),
                SizeFlagsVertical = SizeFlags.ShrinkCenter
            };
            idleHBox.AddChild(_idleDayProgress);

            _idlePauseBtn = EsportsUiTheme.CreateStyledButton("⏸", isPrimaryGold: false, minWidth: 40f, minHeight: 28f);
            _idlePauseBtn.Pressed += () =>
            {
                if (_career == null) return;
                _career.State.IdleClock.IsPaused = !_career.State.IdleClock.IsPaused;
                ShowToast(_career.State.IdleClock.IsPaused ? "挂机时钟已暂停 (可随时手动推进或恢复挂机)。" : "挂机时钟已恢复自动推进。");
                UpdateIdleClockWidgets();
            };
            idleHBox.AddChild(_idlePauseBtn);

            _idle1xBtn = EsportsUiTheme.CreateStyledButton("▶1x", isPrimaryGold: false, minWidth: 48f, minHeight: 28f);
            _idle1xBtn.Pressed += () => SetIdleSpeed(1f);
            idleHBox.AddChild(_idle1xBtn);

            _idle2xBtn = EsportsUiTheme.CreateStyledButton("⏩2x", isPrimaryGold: false, minWidth: 52f, minHeight: 28f);
            _idle2xBtn.Pressed += () => SetIdleSpeed(2f);
            idleHBox.AddChild(_idle2xBtn);

            _idle4xBtn = EsportsUiTheme.CreateStyledButton("⏩4x", isPrimaryGold: false, minWidth: 52f, minHeight: 28f);
            _idle4xBtn.Pressed += () => SetIdleSpeed(4f);
            idleHBox.AddChild(_idle4xBtn);

            _idlePolicyBtn = EsportsUiTheme.CreateStyledButton("比赛日策略: 自动快进完赛", isPrimaryGold: false, minWidth: 195f, minHeight: 28f);
            _idlePolicyBtn.Pressed += () =>
            {
                if (_career == null) return;
                var values = Enum.GetValues<MatchDayAutoPolicy>();
                int cur = Array.IndexOf(values, _career.State.IdleClock.MatchDayPolicy);
                _career.State.IdleClock.MatchDayPolicy = values[(cur + 1) % values.Length];
                ShowToast($"比赛日自动策略已切换为: {FormatMatchDayPolicy(_career.State.IdleClock.MatchDayPolicy)}");
                UpdateIdleClockWidgets();
            };
            idleHBox.AddChild(_idlePolicyBtn);

            _idleResourcesLabel = new Label
            {
                Text = "基地XP池: 350 XP | 战术点: 45 TP",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Modulate = EsportsUiTheme.GoldPrimary
            };
            _idleResourcesLabel.AddThemeFontSizeOverride("font_size", 13);
            idleHBox.AddChild(_idleResourcesLabel);

            _claimVaultBtn = EsportsUiTheme.CreateStyledButton("💰 领取挂机金库", isPrimaryGold: true, minWidth: 230f, minHeight: 30f);
            _claimVaultBtn.Pressed += () =>
            {
                if (_career == null) return;
                var (cash, xp, tp) = _career.ClaimIdleVaultRewards();
                if (cash == 0 && xp == 0 && tp == 0)
                {
                    ShowToast("当前挂机金库暂无待领取奖励，随日程推进自动产出。");
                }
                else
                {
                    ShowToast($"已领取基地挂机金库: +${cash:N0} 资金, +{xp} 基地XP, +{tp} 战术点！");
                    RefreshAllTabs();
                }
            };
            idleHBox.AddChild(_claimVaultBtn);

            // Toast notification banner
            _toastCard = new PanelContainer();
            _toastStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.06f, 0.09f, 0.15f, 0.94f),
                BorderColor = EsportsUiTheme.CyanElectric,
                BorderWidthLeft = 4,
                BorderWidthBottom = 1,
                CornerRadiusTopLeft = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 14,
                ContentMarginRight = 14,
                ContentMarginTop = 5,
                ContentMarginBottom = 5
            };
            _toastCard.AddThemeStyleboxOverride("panel", _toastStyle);
            _toastLabel = new Label
            {
                Text = "✓ [MANAGER LOG] 俱乐部基地挂机系统运行中：Day 1~6 自动进行日常特训与金库累积，也可随时手动推进。",
                Modulate = EsportsUiTheme.TextPrimary
            };
            _toastLabel.AddThemeFontSizeOverride("font_size", 13);
            _toastCard.AddChild(_toastLabel);
            mainVBox.AddChild(_toastCard);

            // 2. 6-Tab Management Container
            _tabs = new TabContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(1860f, 840f)
            };
            _tabs.TabChanged += tabIdx =>
            {
                if (_tabs != null && tabIdx >= 0 && tabIdx < _tabs.GetTabCount())
                {
                    ShowToast($"Switched view -> {_tabs.GetTabTitle((int)tabIdx)}");
                }
            };
            mainVBox.AddChild(_tabs);

            _overviewTab = CreateScrollTab(_tabs, "1 · 俱乐部总览 & 版本");
            _lineupTab = CreateScrollTab(_tabs, "2 · 首发阵容 & 1主3被技能");
            _playbookTab = CreateScrollTab(_tabs, "3 · 战术板 & 跳点地图");
            _trainingTab = CreateScrollTab(_tabs, "4 · 选手养成 & 基地挂机");
            _standingsTab = CreateScrollTab(_tabs, "5 · FFWS 积分榜");
            _scoutingTab = CreateScrollTab(_tabs, "6 · 球探转会 & 财政");
        }

        private void SetIdleSpeed(float speed)
        {
            if (_career == null) return;
            _career.State.IdleClock.IsPaused = false;
            _career.State.IdleClock.SpeedMultiplier = speed;
            ShowToast($"挂机时钟倍速已设为 {speed:0}x。");
            UpdateIdleClockWidgets();
        }

        private static string FormatMatchDayPolicy(MatchDayAutoPolicy policy)
        {
            return policy switch
            {
                MatchDayAutoPolicy.AutoQuickSim => "自动快进完赛",
                MatchDayAutoPolicy.AutoLaunch3D => "自动进入3D赛场",
                MatchDayAutoPolicy.PauseOnMatchDay => "比赛日暂停等待",
                _ => policy.ToString()
            };
        }

        private void UpdateIdleClockWidgets()
        {
            if (_career == null) return;
            var clock = _career.State.IdleClock;

            if (_idleDayLabel != null)
            {
                string dayRole = clock.CurrentDayOfWeek < 7 ? "基地日常特训" : "FFWS 比赛日就绪";
                string pauseTag = clock.IsPaused ? " [已暂停]" : $" [{clock.SpeedMultiplier:0}x]";
                _idleDayLabel.Text = $"⏱ DAY {clock.CurrentDayOfWeek}/7 ({dayRole}){pauseTag}";
                _idleDayLabel.Modulate = clock.CurrentDayOfWeek == 7
                    ? EsportsUiTheme.GoldPrimary
                    : (clock.IsPaused ? EsportsUiTheme.AmberWarning : EsportsUiTheme.CyanElectric);
            }

            if (_idleDayProgress != null)
            {
                _idleDayProgress.Value = Math.Clamp(clock.DayProgress01 * 100f, 0f, 100f);
            }

            if (_idlePauseBtn != null)
            {
                _idlePauseBtn.Text = clock.IsPaused ? "▶恢复" : "⏸暂停";
            }

            if (_idlePolicyBtn != null)
            {
                _idlePolicyBtn.Text = $"周日策略: {FormatMatchDayPolicy(clock.MatchDayPolicy)}";
            }

            if (_idleResourcesLabel != null)
            {
                _idleResourcesLabel.Text = $"基地经验池: {clock.ClubXpPool:N0} XP   |   战术点: {clock.TacticalPoints} TP";
            }

            if (_claimVaultBtn != null)
            {
                bool hasVault = clock.UnclaimedVaultCash > 0 || clock.UnclaimedVaultXp > 0 || clock.UnclaimedTacticalPoints > 0;
                _claimVaultBtn.Text = hasVault
                    ? $"💰 领取金库 (+${clock.UnclaimedVaultCash:N0} / +{clock.UnclaimedVaultXp}XP / +{clock.UnclaimedTacticalPoints}TP)"
                    : "💰 挂机金库累积中 (0)";
            }
        }

        private static VBoxContainer CreateScrollTab(TabContainer parent, string tabTitle)
        {
            var scroll = new ScrollContainer
            {
                Name = tabTitle,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(1820f, 790f)
            };
            parent.AddChild(scroll);

            var vbox = new VBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            vbox.AddThemeConstantOverride("separation", 12);
            scroll.AddChild(vbox);
            return vbox;
        }

        public void RefreshAllTabs()
        {
            if (_career == null || !IsInsideTree()) return;

            UpdateIdleClockWidgets();

            TeamDef team = _career.GetPlayerTeam();
            if (_clubLogoRect != null)
            {
                _clubLogoRect.Texture = VisualAssetRegistry.GetTeamLogo(team.TeamId, team.Name, team.ColorHex);
            }

            if (_clubTitleLabel != null)
            {
                string shortTag = FFM.Client.View.BattlefieldView.GetShortTag(team.Name, 0);
                _clubTitleLabel.Text = $"★ {team.Name.ToUpperInvariant()} [{shortTag}] · 地区: {team.Region} · 团队默契: {team.Synergy:0.0}%";
            }

            if (_calendarLabel != null)
            {
                _calendarLabel.Text = $"SEASON {_career.State.Season} · WEEK {_career.State.Week} · DAY {_career.State.IdleClock.CurrentDayOfWeek}/7 ({_career.CurrentStage}) · PATCH {_career.State.PatchVersion}";
            }

            if (_economyLabel != null)
            {
                _economyLabel.Text = $"俱乐部资金: ${_career.State.CashBalance:N0}   |   球探点数: {_career.State.ScoutingPointsAvailable} PTS";
            }

            RebuildOverviewTab(team);
            RebuildLineupTab(team);
            RebuildPlaybookTab(team);
            RebuildTrainingTab(team);
            RebuildStandingsTab();
            RebuildScoutingAndTransferTab(team);
        }

        private void RebuildOverviewTab(TeamDef team)
        {
            if (_overviewTab == null || _career == null) return;
            ClearChildren(_overviewTab);

            // Top Row: 3 Bento Cards (Club Snapshot, Active Meta Patch, Tactical Readiness)
            var topGrid = new HBoxContainer();
            topGrid.AddThemeConstantOverride("separation", 14);
            _overviewTab.AddChild(topGrid);

            // Card 1: Club Snapshot & Starting 4 Preview
            var clubCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
            clubCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            topGrid.AddChild(clubCard);

            var clubVBox = new VBoxContainer();
            clubVBox.AddThemeConstantOverride("separation", 8);
            clubCard.AddChild(clubVBox);

            clubVBox.AddChild(EsportsUiTheme.CreateSectionTitle("CLUB SNAPSHOT · 战队概况", EsportsUiTheme.GoldPrimary));
            clubVBox.AddChild(EsportsUiTheme.CreateStatBar("Team Synergy (团队默契)", team.Synergy, 100f, EsportsUiTheme.GoldPrimary));

            int currentRank = 1;
            int currentPts = 0;
            for (int i = 0; i < _career.State.Standings.Count; i++)
            {
                if (_career.State.Standings[i].TeamId == team.TeamId)
                {
                    currentRank = i + 1;
                    currentPts = _career.State.Standings[i].TotalPoints;
                    break;
                }
            }

            var rankRow = new HBoxContainer();
            rankRow.AddThemeConstantOverride("separation", 10);
            rankRow.AddChild(EsportsUiTheme.CreatePillBadge($"FFWS 排名 #{currentRank:00}", EsportsUiTheme.CyanElectric));
            rankRow.AddChild(EsportsUiTheme.CreatePillBadge($"联赛积分 {currentPts} PTS", EsportsUiTheme.GoldPrimary));
            rankRow.AddChild(EsportsUiTheme.CreatePillBadge($"赛段 {_career.CurrentStage}", EsportsUiTheme.EmeraldSuccess));
            clubVBox.AddChild(rankRow);

            var starterStrip = new HBoxContainer();
            starterStrip.AddThemeConstantOverride("separation", 10);
            clubVBox.AddChild(starterStrip);

            foreach (var slot in team.Lineup.Starters)
            {
                PlayerDef? p = team.FindPlayer(slot.PlayerId);
                if (p == null) continue;
                Enum.TryParse<Role>(slot.Role, true, out Role r);

                var miniBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                miniBox.AddThemeConstantOverride("separation", 3);

                var portrait = new TextureRect
                {
                    Texture = VisualAssetRegistry.GetPlayerPortrait(p.PlayerId, r),
                    CustomMinimumSize = new Vector2(56f, 56f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                miniBox.AddChild(portrait);

                var nameLbl = new Label
                {
                    Text = $"{p.Name} {(team.Lineup.Igl == p.PlayerId ? "★" : "")}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Modulate = EsportsUiTheme.TextPrimary
                };
                nameLbl.AddThemeFontSizeOverride("font_size", 12);
                miniBox.AddChild(nameLbl);

                var ovrLbl = new Label
                {
                    Text = $"{slot.Role} · {p.CalculateTrueRating():0.0}",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Modulate = EsportsUiTheme.GoldPrimary
                };
                ovrLbl.AddThemeFontSizeOverride("font_size", 11);
                miniBox.AddChild(ovrLbl);

                starterStrip.AddChild(miniBox);
            }

            // Card 2: Active Meta Patch
            var patch = _career.PatchNotes.GetPatchForSeason(_career.State.Season);
            var patchCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.CyanElectric);
            patchCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            topGrid.AddChild(patchCard);

            var patchVBox = new VBoxContainer();
            patchVBox.AddThemeConstantOverride("separation", 8);
            patchCard.AddChild(patchVBox);

            patchVBox.AddChild(EsportsUiTheme.CreateSectionTitle($"META PATCH {patch.PatchVersion} · {patch.Title}", EsportsUiTheme.CyanElectric));
            patchVBox.AddChild(new Label
            {
                Text = patch.Summary,
                Modulate = EsportsUiTheme.TextPrimary,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            });

            foreach (string hl in patch.TacticalHighlights)
            {
                var hlLbl = new Label
                {
                    Text = $"  ◆ {hl}",
                    Modulate = EsportsUiTheme.CyanElectric,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                hlLbl.AddThemeFontSizeOverride("font_size", 13);
                patchVBox.AddChild(hlLbl);
            }

            // Card 3: Tactical Readiness & Playbook Fit
            var fitCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.EmeraldSuccess);
            fitCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            topGrid.AddChild(fitCard);

            var fitVBox = new VBoxContainer();
            fitVBox.AddThemeConstantOverride("separation", 8);
            fitCard.AddChild(fitVBox);

            fitVBox.AddChild(EsportsUiTheme.CreateSectionTitle("TACTICAL FIT · 战术契合度评估", EsportsUiTheme.EmeraldSuccess));

            var pbBadges = new HBoxContainer();
            pbBadges.AddThemeConstantOverride("separation", 6);
            pbBadges.AddChild(EsportsUiTheme.CreatePillBadge($"跳点:{team.Playbook.Drop}", EsportsUiTheme.GoldPrimary));
            pbBadges.AddChild(EsportsUiTheme.CreatePillBadge($"进圈:{team.Playbook.Rotation}", EsportsUiTheme.CyanElectric));
            pbBadges.AddChild(EsportsUiTheme.CreatePillBadge($"交战:{team.Playbook.Engage}", EsportsUiTheme.AmberWarning));
            pbBadges.AddChild(EsportsUiTheme.CreatePillBadge($"决赛:{team.Playbook.Endgame}", EsportsUiTheme.EmeraldSuccess));
            fitVBox.AddChild(pbBadges);

            List<string> fitNotes = _career.Playbook.EvaluateTacticalFit(team);
            foreach (string note in fitNotes)
            {
                bool isWarn = note.Contains("风险") || note.Contains("提醒");
                var noteLbl = new Label
                {
                    Text = $"▸ {note}",
                    Modulate = isWarn ? EsportsUiTheme.AmberWarning : EsportsUiTheme.EmeraldSuccess,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                noteLbl.AddThemeFontSizeOverride("font_size", 13);
                fitVBox.AddChild(noteLbl);
            }

            // Bottom Row: Last Match Attribution Report
            if (_career.State.LastMatchReport != null)
            {
                PostMatchReport rep = _career.State.LastMatchReport;
                var repCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
                _overviewTab.AddChild(repCard);

                var repVBox = new VBoxContainer();
                repVBox.AddThemeConstantOverride("separation", 10);
                repCard.AddChild(repVBox);

                var repHeader = new HBoxContainer();
                repHeader.AddThemeConstantOverride("separation", 12);
                repHeader.AddChild(EsportsUiTheme.CreateSectionTitle("LAST MATCH ATTRIBUTION REPORT · 上场赛后归因报告", EsportsUiTheme.GoldPrimary));
                repHeader.AddChild(EsportsUiTheme.CreatePillBadge($"排名 #{rep.Placement}", rep.Placement == 1 ? EsportsUiTheme.GoldPrimary : EsportsUiTheme.CyanElectric));
                repHeader.AddChild(EsportsUiTheme.CreatePillBadge($"淘汰 {rep.Kills}", EsportsUiTheme.CrimsonDanger));
                repHeader.AddChild(EsportsUiTheme.CreatePillBadge($"总分 +{rep.TotalPoints} PTS", EsportsUiTheme.EmeraldSuccess));
                repVBox.AddChild(repHeader);

                foreach (var ins in rep.AttributionInsights)
                {
                    Color insCol = ins.IsPositive ? EsportsUiTheme.EmeraldSuccess : EsportsUiTheme.AmberWarning;
                    var insPanel = EsportsUiTheme.CreateBentoCard(accentBorder: insCol, bgOverride: new Color(0.06f, 0.09f, 0.15f, 0.9f));
                    var insHBox = new HBoxContainer();
                    insHBox.AddThemeConstantOverride("separation", 10);
                    insPanel.AddChild(insHBox);

                    insHBox.AddChild(EsportsUiTheme.CreatePillBadge(ins.IsPositive ? "★ 优势归因" : "! 改进建议", insCol));
                    insHBox.AddChild(EsportsUiTheme.CreatePillBadge(ins.Dimension, EsportsUiTheme.CyanElectric));

                    var detailLbl = new Label
                    {
                        Text = $"{ins.Headline} — {ins.CausalDetail}  (教练建议: {ins.RecommendedAction})",
                        SizeFlagsHorizontal = SizeFlags.ExpandFill,
                        Modulate = EsportsUiTheme.TextPrimary,
                        AutowrapMode = TextServer.AutowrapMode.WordSmart
                    };
                    insHBox.AddChild(detailLbl);
                    repVBox.AddChild(insPanel);
                }
            }
        }

        private void RebuildLineupTab(TeamDef team)
        {
            if (_lineupTab == null || _career == null) return;
            ClearChildren(_lineupTab);

            // Ensure default inspected player is valid
            if (string.IsNullOrEmpty(_inspectedPlayerId) || team.FindPlayer(_inspectedPlayerId) == null)
            {
                _inspectedPlayerId = team.Lineup.Starters.Count > 0 ? team.Lineup.Starters[0].PlayerId : (team.Players.Count > 0 ? team.Players[0].PlayerId : null);
            }

            // Header & Validation Banner
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 12);
            headerRow.AddChild(EsportsUiTheme.CreateSectionTitle("STARTING 4 LINEUP & 1-ACTIVE + 3-PASSIVE SKILL LOADOUT"));

            List<string> errors = _career.LineupValidator.ValidateLineup(team, _career.Config);
            if (errors.Count == 0)
            {
                headerRow.AddChild(EsportsUiTheme.CreatePillBadge("✓ 阵容合规：4名首发 · 唯一IGL · 主动技能全队不重复 · 每人3个不重复被动", EsportsUiTheme.EmeraldSuccess));
            }
            else
            {
                foreach (string err in errors)
                {
                    headerRow.AddChild(EsportsUiTheme.CreatePillBadge($"! {err}", EsportsUiTheme.CrimsonDanger));
                }
            }
            _lineupTab.AddChild(headerRow);

            string[] roleOptions = { "Rusher", "SecondRusher", "Sniper", "Support", "Grenadier" };
            List<SkillDef> activeSkills = new();
            List<SkillDef> passiveSkills = new();
            foreach (var sk in _career.Config.Skills.Skills)
            {
                if (sk.IsActive) activeSkills.Add(sk);
                else passiveSkills.Add(sk);
            }

            // 4 Vertical Pro Player Cards
            var cardsRow = new HBoxContainer();
            cardsRow.AddThemeConstantOverride("separation", 12);
            _lineupTab.AddChild(cardsRow);

            for (int i = 0; i < team.Lineup.Starters.Count; i++)
            {
                int slotIdx = i;
                LineupStarterSlot slot = team.Lineup.Starters[slotIdx];
                PlayerDef? p = team.FindPlayer(slot.PlayerId);
                if (p == null) continue;

                Enum.TryParse<Role>(slot.Role, true, out Role parsedRole);
                bool isIgl = team.Lineup.Igl == p.PlayerId;
                bool isInspected = p.PlayerId == _inspectedPlayerId;

                var card = EsportsUiTheme.CreateBentoCard(
                    accentBorder: isIgl ? EsportsUiTheme.GoldPrimary : (isInspected ? EsportsUiTheme.CyanElectric : null));
                card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                cardsRow.AddChild(card);

                var cardVBox = new VBoxContainer();
                cardVBox.AddThemeConstantOverride("separation", 8);
                card.AddChild(cardVBox);

                // Top Row: Portrait + Name/Age/OVR + IGL Crown
                var topHBox = new HBoxContainer();
                topHBox.AddThemeConstantOverride("separation", 10);
                cardVBox.AddChild(topHBox);

                var portrait = new TextureRect
                {
                    Texture = VisualAssetRegistry.GetPlayerPortrait(p.PlayerId, parsedRole),
                    CustomMinimumSize = new Vector2(68f, 68f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                topHBox.AddChild(portrait);

                var idVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                idVBox.AddThemeConstantOverride("separation", 3);
                topHBox.AddChild(idVBox);

                var nameRow = new HBoxContainer();
                nameRow.AddThemeConstantOverride("separation", 6);
                var nameLbl = new Label
                {
                    Text = $"#{slotIdx + 1} {p.Name}",
                    Modulate = EsportsUiTheme.TextPrimary
                };
                nameLbl.AddThemeFontSizeOverride("font_size", 16);
                nameRow.AddChild(nameLbl);
                if (isIgl)
                {
                    nameRow.AddChild(EsportsUiTheme.CreatePillBadge("★ IGL指挥", EsportsUiTheme.GoldPrimary));
                }
                idVBox.AddChild(nameRow);

                var metaLbl = new Label
                {
                    Text = $"Age {p.Age} · Lv.{p.Level}/{p.GetMaxLevelForStar()} · {new string('★', Math.Clamp(p.StarRating, 1, 5))} · 疲劳 {p.Condition.Fatigue:0}%",
                    Modulate = p.Condition.Fatigue >= 65f ? EsportsUiTheme.CrimsonDanger : EsportsUiTheme.TextSecondary
                };
                metaLbl.AddThemeFontSizeOverride("font_size", 12);
                idVBox.AddChild(metaLbl);

                var badgeRow = new HBoxContainer();
                badgeRow.AddThemeConstantOverride("separation", 6);
                badgeRow.AddChild(EsportsUiTheme.CreatePillBadge($"OVR {p.CalculateTrueRating():0.0}", EsportsUiTheme.CyanElectric));
                badgeRow.AddChild(EsportsUiTheme.CreatePillBadge($"POT {p.Potential:0}", EsportsUiTheme.GoldPrimary));
                idVBox.AddChild(badgeRow);

                // Role Selector + IGL Appointment Row
                var roleIglRow = new HBoxContainer();
                roleIglRow.AddThemeConstantOverride("separation", 6);
                cardVBox.AddChild(roleIglRow);

                var roleIcon = new TextureRect
                {
                    Texture = VisualAssetRegistry.GetRoleIcon(parsedRole),
                    CustomMinimumSize = new Vector2(26f, 26f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                roleIglRow.AddChild(roleIcon);

                var roleBtn = EsportsUiTheme.CreateStyledButton($"位置: {slot.Role}", isPrimaryGold: false, minWidth: 120f, minHeight: 30f);
                roleBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                roleBtn.Pressed += () =>
                {
                    int cur = Array.IndexOf(roleOptions, slot.Role);
                    slot.Role = roleOptions[(cur + 1) % roleOptions.Length];
                    ShowToast($"Updated {p.Name} role to {slot.Role}.");
                    RefreshAllTabs();
                };
                roleIglRow.AddChild(roleBtn);

                var iglBtn = EsportsUiTheme.CreateStyledButton(isIgl ? "★ 队长" : "设为IGL", isPrimaryGold: isIgl, minWidth: 74f, minHeight: 30f);
                iglBtn.Disabled = isIgl;
                iglBtn.Pressed += () =>
                {
                    team.Lineup.Igl = p.PlayerId;
                    ShowToast($"{p.Name} appointed as Squad IGL.");
                    RefreshAllTabs();
                };
                roleIglRow.AddChild(iglBtn);

                // Core 4 Attribute Mini Bars
                float attrCap = p.GetEffectiveAttributeCap();
                cardVBox.AddChild(EsportsUiTheme.CreateStatBar($"Aim (上限 {attrCap:0})", p.Mechanical.Aim, 20f));
                cardVBox.AddChild(EsportsUiTheme.CreateStatBar($"CQC (上限 {attrCap:0})", p.Mechanical.Cqc, 20f));
                cardVBox.AddChild(EsportsUiTheme.CreateStatBar($"Decision (上限 {attrCap:0})", p.Tactical.Decision, 20f));
                cardVBox.AddChild(EsportsUiTheme.CreateStatBar($"Utility (上限 {attrCap:0})", p.Tactical.Utility, 20f));

                // 1 Active Skill Slot
                SkillDef? curActive = _career.Config.Skills.FindSkill(slot.Active);
                var activeRow = new HBoxContainer();
                activeRow.AddThemeConstantOverride("separation", 8);
                cardVBox.AddChild(activeRow);

                var activeIcon = new TextureRect
                {
                    Texture = VisualAssetRegistry.GetSkillIcon(slot.Active, curActive?.GetCategoryEnum() ?? SkillCategory.Dash, isActive: true),
                    CustomMinimumSize = new Vector2(32f, 32f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                activeRow.AddChild(activeIcon);

                var activeBtn = EsportsUiTheme.CreateStyledButton(
                    $"主动: {curActive?.Name ?? slot.Active} [{curActive?.Category}]",
                    isPrimaryGold: true,
                    minWidth: 160f,
                    minHeight: 32f);
                activeBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                activeBtn.Pressed += () =>
                {
                    CycleUniqueActiveSkill(team, slot, activeSkills);
                    RefreshAllTabs();
                };
                activeRow.AddChild(activeBtn);

                // 3 Passive Skill Slots (Interactive!)
                while (slot.Passives.Count < 3 && passiveSkills.Count >= 3)
                {
                    slot.Passives.Add(passiveSkills[slot.Passives.Count].SkillId);
                }

                for (int pIdx = 0; pIdx < 3 && pIdx < slot.Passives.Count; pIdx++)
                {
                    int capturedPIdx = pIdx;
                    string pid = slot.Passives[capturedPIdx];
                    SkillDef? pSkill = _career.Config.Skills.FindSkill(pid);

                    var pRow = new HBoxContainer();
                    pRow.AddThemeConstantOverride("separation", 6);
                    cardVBox.AddChild(pRow);

                    var pIcon = new TextureRect
                    {
                        Texture = VisualAssetRegistry.GetSkillIcon(pid, SkillCategory.Passive, isActive: false),
                        CustomMinimumSize = new Vector2(24f, 24f),
                        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                    };
                    pRow.AddChild(pIcon);

                    var pBtn = EsportsUiTheme.CreateStyledButton(
                        $"被动#{capturedPIdx + 1}: {pSkill?.Name ?? pid}",
                        isPrimaryGold: false,
                        minWidth: 150f,
                        minHeight: 28f);
                    pBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                    pBtn.Pressed += () =>
                    {
                        CycleUniquePassiveSkill(slot, capturedPIdx, passiveSkills);
                        RefreshAllTabs();
                    };
                    pRow.AddChild(pBtn);
                }

                // Bottom Action Row: Inspect 15 Stats & Swap w/ Sub
                var actionRow = new HBoxContainer();
                actionRow.AddThemeConstantOverride("separation", 6);
                cardVBox.AddChild(actionRow);

                var inspectBtn = EsportsUiTheme.CreateStyledButton(
                    isInspected ? "● 正在查看15维属性" : "查看15维属性",
                    isPrimaryGold: isInspected,
                    minWidth: 110f,
                    minHeight: 30f);
                inspectBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                inspectBtn.Pressed += () =>
                {
                    _inspectedPlayerId = p.PlayerId;
                    ShowToast($"已展开选手 {p.Name} 的15维详细属性面板 (OVR {p.CalculateTrueRating():0.0})。");
                    RefreshAllTabs();
                };
                actionRow.AddChild(inspectBtn);

                if (!string.IsNullOrEmpty(team.Lineup.Sub))
                {
                    string subId = team.Lineup.Sub!;
                    PlayerDef? subP = team.FindPlayer(subId);
                    var subSwapBtn = EsportsUiTheme.CreateStyledButton(
                        $"轮换 {subP?.Name ?? "替补"}",
                        isPrimaryGold: false,
                        minWidth: 95f,
                        minHeight: 30f);
                    subSwapBtn.Pressed += () =>
                    {
                        if (_career.LineupValidator.SubstituteStarter(team, slot.PlayerId, subId))
                        {
                            _inspectedPlayerId = subId;
                            ShowToast($"Rotated {subP?.Name} into Starting Lineup.");
                            RefreshAllTabs();
                        }
                    };
                    actionRow.AddChild(subSwapBtn);
                }
            }

            // Bottom Split: 5th-Man Substitute Card + Selected Player 15-Attribute Inspector
            var bottomRow = new HBoxContainer();
            bottomRow.AddThemeConstantOverride("separation", 14);
            _lineupTab.AddChild(bottomRow);

            BuildSubstituteCard(bottomRow, team);
            BuildPlayerAttributeInspector(bottomRow, team);
        }

        private void BuildSubstituteCard(HBoxContainer parent, TeamDef team)
        {
            var subCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.NavyBorder);
            subCard.CustomMinimumSize = new Vector2(340f, 0f);
            parent.AddChild(subCard);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            subCard.AddChild(vbox);

            vbox.AddChild(EsportsUiTheme.CreateSectionTitle("5TH MAN · 替补席轮换", EsportsUiTheme.CyanElectric));

            PlayerDef? subP = !string.IsNullOrEmpty(team.Lineup.Sub) ? team.FindPlayer(team.Lineup.Sub!) : null;
            if (subP == null)
            {
                vbox.AddChild(new Label { Text = "当前无替补选手", Modulate = EsportsUiTheme.TextSecondary });
                return;
            }

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            vbox.AddChild(row);

            var portrait = new TextureRect
            {
                Texture = VisualAssetRegistry.GetPlayerPortrait(subP.PlayerId, Role.Grenadier),
                CustomMinimumSize = new Vector2(64f, 64f),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
            };
            row.AddChild(portrait);

            var infoVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            infoVBox.AddChild(new Label
            {
                Text = $"{subP.Name} (Age {subP.Age} · Lv.{subP.Level} · {new string('★', Math.Clamp(subP.StarRating, 1, 5))})",
                Modulate = EsportsUiTheme.TextPrimary
            });
            infoVBox.AddChild(EsportsUiTheme.CreatePillBadge($"OVR {subP.CalculateTrueRating():0.0} · POT {subP.Potential:0} · 疲劳 {subP.Condition.Fatigue:0}%", EsportsUiTheme.EmeraldSuccess));
            row.AddChild(infoVBox);

            vbox.AddChild(EsportsUiTheme.CreateStatBar("Aim (精准)", subP.Mechanical.Aim, 20f));
            vbox.AddChild(EsportsUiTheme.CreateStatBar("CQC (近战)", subP.Mechanical.Cqc, 20f));
            vbox.AddChild(EsportsUiTheme.CreateStatBar("Decision (决策)", subP.Tactical.Decision, 20f));

            bool isSubInspected = _inspectedPlayerId == subP.PlayerId;
            var inspectSubBtn = EsportsUiTheme.CreateStyledButton(
                isSubInspected ? "● 正在查看替补15维详情" : "查看替补15维详情",
                isPrimaryGold: isSubInspected,
                minWidth: 160f,
                minHeight: 30f);
            inspectSubBtn.Pressed += () =>
            {
                _inspectedPlayerId = subP.PlayerId;
                ShowToast($"已展开替补选手 {subP.Name} 的15维详细属性面板 (OVR {subP.CalculateTrueRating():0.0})。");
                RefreshAllTabs();
            };
            vbox.AddChild(inspectSubBtn);
        }

        private void BuildPlayerAttributeInspector(HBoxContainer parent, TeamDef team)
        {
            var inspectorCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.CyanElectric);
            inspectorCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            parent.AddChild(inspectorCard);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 8);
            inspectorCard.AddChild(vbox);

            PlayerDef? p = !string.IsNullOrEmpty(_inspectedPlayerId) ? team.FindPlayer(_inspectedPlayerId!) : null;
            if (p == null && team.Players.Count > 0) p = team.Players[0];
            if (p == null) return;

            float cap = p.GetEffectiveAttributeCap();
            var titleRow = new HBoxContainer();
            titleRow.AddThemeConstantOverride("separation", 10);
            titleRow.AddChild(EsportsUiTheme.CreateSectionTitle($"15-ATTRIBUTE PROFILER · {p.Name.ToUpperInvariant()} (Age {p.Age})", EsportsUiTheme.CyanElectric));
            titleRow.AddChild(EsportsUiTheme.CreatePillBadge($"Lv.{p.Level}/{p.GetMaxLevelForStar()} · {new string('★', Math.Clamp(p.StarRating, 1, 5))}", EsportsUiTheme.GoldPrimary));
            titleRow.AddChild(EsportsUiTheme.CreatePillBadge($"OVR {p.CalculateTrueRating():0.0} | 潜力 POT {p.Potential:0} (当前属性上限 {cap:0.0})", EsportsUiTheme.CyanElectric));
            titleRow.AddChild(EsportsUiTheme.CreatePillBadge($"体能 {p.Condition.Stamina:0.0} · 疲劳 {p.Condition.Fatigue:0}%", EsportsUiTheme.EmeraldSuccess));
            vbox.AddChild(titleRow);

            var cols = new HBoxContainer();
            cols.AddThemeConstantOverride("separation", 18);
            vbox.AddChild(cols);

            // Column 1: 6 Mechanical Attributes
            var mechCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            mechCol.AddThemeConstantOverride("separation", 5);
            mechCol.AddChild(EsportsUiTheme.CreateSectionTitle("MECHANICAL · 枪法与身法 (6)", EsportsUiTheme.GoldPrimary));
            mechCol.AddChild(EsportsUiTheme.CreateStatBar($"Aim 瞄准 ({p.Mechanical.Aim:0.0}/{cap:0})", p.Mechanical.Aim, 20f));
            mechCol.AddChild(EsportsUiTheme.CreateStatBar($"Recoil 压枪 ({p.Mechanical.Recoil:0.0}/{cap:0})", p.Mechanical.Recoil, 20f));
            mechCol.AddChild(EsportsUiTheme.CreateStatBar($"Reflex 反应 ({p.Mechanical.Reflex:0.0}/{cap:0})", p.Mechanical.Reflex, 20f));
            mechCol.AddChild(EsportsUiTheme.CreateStatBar($"Movement 身法 ({p.Mechanical.Movement:0.0}/{cap:0})", p.Mechanical.Movement, 20f));
            mechCol.AddChild(EsportsUiTheme.CreateStatBar($"CQC 近战 ({p.Mechanical.Cqc:0.0}/{cap:0})", p.Mechanical.Cqc, 20f));
            mechCol.AddChild(EsportsUiTheme.CreateStatBar($"LongRange 远射 ({p.Mechanical.LongRange:0.0}/{cap:0})", p.Mechanical.LongRange, 20f));
            cols.AddChild(mechCol);

            // Column 2: 6 Tactical Attributes
            var tacCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            tacCol.AddThemeConstantOverride("separation", 5);
            tacCol.AddChild(EsportsUiTheme.CreateSectionTitle("TACTICAL · 意识与运营 (6)", EsportsUiTheme.CyanElectric));
            tacCol.AddChild(EsportsUiTheme.CreateStatBar($"Awareness 感知 ({p.Tactical.Awareness:0.0}/{cap:0})", p.Tactical.Awareness, 20f));
            tacCol.AddChild(EsportsUiTheme.CreateStatBar($"Positioning 选位 ({p.Tactical.Positioning:0.0}/{cap:0})", p.Tactical.Positioning, 20f));
            tacCol.AddChild(EsportsUiTheme.CreateStatBar($"Utility 道具 ({p.Tactical.Utility:0.0}/{cap:0})", p.Tactical.Utility, 20f));
            tacCol.AddChild(EsportsUiTheme.CreateStatBar($"Looting 搜刮 ({p.Tactical.Looting:0.0}/{cap:0})", p.Tactical.Looting, 20f));
            tacCol.AddChild(EsportsUiTheme.CreateStatBar($"Decision 决策 ({p.Tactical.Decision:0.0}/{cap:0})", p.Tactical.Decision, 20f));
            tacCol.AddChild(EsportsUiTheme.CreateStatBar($"Comms 指挥 ({p.Tactical.Comms:0.0}/{cap:0})", p.Tactical.Comms, 20f));
            cols.AddChild(tacCol);

            // Column 3: 3 Mental Attributes + Weapon Mastery & Traits preview
            var mentalCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            mentalCol.AddThemeConstantOverride("separation", 5);
            mentalCol.AddChild(EsportsUiTheme.CreateSectionTitle("MENTAL & TRAITS · 心态与专精特质", EsportsUiTheme.EmeraldSuccess));
            mentalCol.AddChild(EsportsUiTheme.CreateStatBar($"Composure 抗压 ({p.Mental.Composure:0.0}/{cap:0})", p.Mental.Composure, 20f));
            mentalCol.AddChild(EsportsUiTheme.CreateStatBar($"Teamwork 协同 ({p.Mental.Teamwork:0.0}/{cap:0})", p.Mental.Teamwork, 20f));
            mentalCol.AddChild(EsportsUiTheme.CreateStatBar($"Determination 韧性 ({p.Mental.Determination:0.0}/{cap:0})", p.Mental.Determination, 20f));
            mentalCol.AddChild(EsportsUiTheme.CreateStatBar("Stamina 体能储备", p.Condition.Stamina, 20f));

            var wmRow = new HBoxContainer();
            wmRow.AddThemeConstantOverride("separation", 4);
            wmRow.AddChild(EsportsUiTheme.CreatePillBadge($"SG T{WeaponMasteryBlock.GetMasteryTier(p.WeaponMastery.Shotgun)}", EsportsUiTheme.GoldPrimary));
            wmRow.AddChild(EsportsUiTheme.CreatePillBadge($"SMG T{WeaponMasteryBlock.GetMasteryTier(p.WeaponMastery.Smg)}", EsportsUiTheme.CyanElectric));
            wmRow.AddChild(EsportsUiTheme.CreatePillBadge($"AR T{WeaponMasteryBlock.GetMasteryTier(p.WeaponMastery.Ar)}", EsportsUiTheme.EmeraldSuccess));
            wmRow.AddChild(EsportsUiTheme.CreatePillBadge($"DMR T{WeaponMasteryBlock.GetMasteryTier(p.WeaponMastery.Dmr)}", EsportsUiTheme.CyanElectric));
            wmRow.AddChild(EsportsUiTheme.CreatePillBadge($"SR T{WeaponMasteryBlock.GetMasteryTier(p.WeaponMastery.Sr)}", EsportsUiTheme.GoldPrimary));
            mentalCol.AddChild(wmRow);

            foreach (string traitId in p.SignatureTraits)
            {
                var (tTitle, _) = FFM.Meta.Services.SignatureTraitCatalog.GetTraitDisplay(traitId);
                mentalCol.AddChild(EsportsUiTheme.CreatePillBadge($"★ 招牌特质: {tTitle}", EsportsUiTheme.GoldPrimary));
            }
            cols.AddChild(mentalCol);
        }

        private void CycleUniqueActiveSkill(TeamDef team, LineupStarterSlot targetSlot, List<SkillDef> activePool)
        {
            if (activePool.Count == 0) return;

            var usedByOthers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in team.Lineup.Starters)
            {
                if (s != targetSlot && !string.IsNullOrEmpty(s.Active))
                {
                    usedByOthers.Add(s.Active);
                }
            }

            int startIdx = activePool.FindIndex(sk => sk.SkillId == targetSlot.Active);
            for (int step = 1; step <= activePool.Count; step++)
            {
                SkillDef candidate = activePool[(startIdx + step + activePool.Count) % activePool.Count];
                if (!usedByOthers.Contains(candidate.SkillId))
                {
                    targetSlot.Active = candidate.SkillId;
                    ShowToast($"Equipped Active Skill: {candidate.Name} ({candidate.Category}).");
                    return;
                }
            }
        }

        private void CycleUniquePassiveSkill(LineupStarterSlot targetSlot, int passiveSlotIdx, List<SkillDef> passivePool)
        {
            if (passivePool.Count == 0 || passiveSlotIdx < 0 || passiveSlotIdx >= targetSlot.Passives.Count) return;

            var usedInOtherSlots = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < targetSlot.Passives.Count; i++)
            {
                if (i != passiveSlotIdx && !string.IsNullOrEmpty(targetSlot.Passives[i]))
                {
                    usedInOtherSlots.Add(targetSlot.Passives[i]);
                }
            }

            string currentId = targetSlot.Passives[passiveSlotIdx];
            int startIdx = passivePool.FindIndex(sk => sk.SkillId == currentId);
            for (int step = 1; step <= passivePool.Count; step++)
            {
                SkillDef candidate = passivePool[(startIdx + step + passivePool.Count) % passivePool.Count];
                if (!usedInOtherSlots.Contains(candidate.SkillId))
                {
                    targetSlot.Passives[passiveSlotIdx] = candidate.SkillId;
                    ShowToast($"Equipped Passive #{passiveSlotIdx + 1}: {candidate.Name}.");
                    return;
                }
            }
        }

        private void RebuildPlaybookTab(TeamDef team)
        {
            if (_playbookTab == null || _career == null) return;
            ClearChildren(_playbookTab);

            var mainSplit = new HBoxContainer();
            mainSplit.AddThemeConstantOverride("separation", 16);
            _playbookTab.AddChild(mainSplit);

            // Left Column: Interactive 2D Bermuda Tactical Map Preview
            var mapCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.CyanElectric);
            mainSplit.AddChild(mapCard);

            var mapVBox = new VBoxContainer();
            mapVBox.AddThemeConstantOverride("separation", 8);
            mapCard.AddChild(mapVBox);

            mapVBox.AddChild(EsportsUiTheme.CreateSectionTitle("BERMUDA-LITE 2D TACTICAL RADAR · 跳点与进圈航线推演", EsportsUiTheme.CyanElectric));

            var mapPreview = new TacticalMapPreview();
            mapPreview.SetData(_career.Map, team.Playbook);
            mapPreview.PoiSelected += poi =>
            {
                _inspectedPoi = poi;
                ShowToast($"已聚焦战术点位: {poi.Name} ({(poi.IsHotspot ? "★高级热区" : "标准资源点")})");
            };
            mapVBox.AddChild(mapPreview);

            if (_inspectedPoi != null)
            {
                mapVBox.AddChild(EsportsUiTheme.CreatePillBadge(
                    $"当前选中 POI: {_inspectedPoi.Name} · 半径 {_inspectedPoi.RadiusMeters:0}m · 海拔 L{_inspectedPoi.HeightLevel}",
                    EsportsUiTheme.GoldPrimary));
            }

            // Right Column: 4-Dimension Playbook Selectors + Live Analyst Feedback
            var rightVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            rightVBox.AddThemeConstantOverride("separation", 12);
            mainSplit.AddChild(rightVBox);

            var configCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
            rightVBox.AddChild(configCard);

            var configVBox = new VBoxContainer();
            configVBox.AddThemeConstantOverride("separation", 12);
            configCard.AddChild(configVBox);

            configVBox.AddChild(EsportsUiTheme.CreateSectionTitle("4-DIMENSION TACTICAL PLAYBOOK · 四维战术纲领配置", EsportsUiTheme.GoldPrimary));

            DropStyle curDrop = team.Playbook.GetDropStyle();
            RotationStyle curRot = team.Playbook.GetRotationStyle();
            EngageStyle curEng = team.Playbook.GetEngageStyle();
            EndgameStyle curEnd = team.Playbook.GetEndgameStyle();

            AddPlaybookSelectorRow(
                configVBox,
                "1. 跳点策略 (Drop Style — 决定落地资源等级与前期Roll点风险):",
                Enum.GetValues<DropStyle>(),
                curDrop,
                next => _career.Playbook.UpdatePlaybook(team, next, curRot, curEng, curEnd));

            AddPlaybookSelectorRow(
                configVBox,
                "2. 进圈节奏 (Rotation Timing — 提前抢中心房区 vs 卡圈边清尾):",
                Enum.GetValues<RotationStyle>(),
                curRot,
                next => _career.Playbook.UpdatePlaybook(team, curDrop, next, curEng, curEnd));

            AddPlaybookSelectorRow(
                configVBox,
                "3. 中期交战原则 (Mid-Game Engagement — 主动找架 vs 运营避战):",
                Enum.GetValues<EngageStyle>(),
                curEng,
                next => _career.Playbook.UpdatePlaybook(team, curDrop, curRot, next, curEnd));

            AddPlaybookSelectorRow(
                configVBox,
                "4. 决赛圈阵型 (Stage 4~5 Endgame — 抢高坡 vs 守房区 vs 冰墙推进):",
                Enum.GetValues<EndgameStyle>(),
                curEnd,
                next => _career.Playbook.UpdatePlaybook(team, curDrop, curRot, curEng, next));

            // Analyst Fit Card
            var analystCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.EmeraldSuccess);
            rightVBox.AddChild(analystCard);

            var analystVBox = new VBoxContainer();
            analystVBox.AddThemeConstantOverride("separation", 8);
            analystCard.AddChild(analystVBox);

            analystVBox.AddChild(EsportsUiTheme.CreateSectionTitle("LIVE ANALYST TACTICAL FIT · 教练组阵容契合度诊断", EsportsUiTheme.EmeraldSuccess));
            foreach (string note in _career.Playbook.EvaluateTacticalFit(team))
            {
                bool isWarn = note.Contains("风险") || note.Contains("提醒");
                var lbl = new Label
                {
                    Text = $"  • {note}",
                    Modulate = isWarn ? EsportsUiTheme.AmberWarning : EsportsUiTheme.EmeraldSuccess,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                lbl.AddThemeFontSizeOverride("font_size", 14);
                analystVBox.AddChild(lbl);
            }
        }

        private void AddPlaybookSelectorRow<TEnum>(
            VBoxContainer parent,
            string title,
            TEnum[] values,
            TEnum current,
            Action<TEnum> onSelect) where TEnum : struct, Enum
        {
            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", 6);
            box.AddChild(new Label
            {
                Text = title,
                Modulate = EsportsUiTheme.TextSecondary
            });

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 10);
            box.AddChild(row);

            foreach (TEnum val in values)
            {
                TEnum captured = val;
                bool active = EqualityComparer<TEnum>.Default.Equals(captured, current);
                var btn = EsportsUiTheme.CreateStyledButton(
                    active ? $"★ {captured}" : captured.ToString(),
                    isPrimaryGold: active,
                    minWidth: 145f,
                    minHeight: 34f);
                btn.Pressed += () =>
                {
                    onSelect(captured);
                    ShowToast($"Playbook updated: {captured}");
                    RefreshAllTabs();
                };
                row.AddChild(btn);
            }
            parent.AddChild(box);
        }

        private void RebuildTrainingTab(TeamDef team)
        {
            if (_trainingTab == null || _career == null) return;
            ClearChildren(_trainingTab);

            if (string.IsNullOrEmpty(_inspectedPlayerId) || team.FindPlayer(_inspectedPlayerId) == null)
            {
                _inspectedPlayerId = team.Players.Count > 0 ? team.Players[0].PlayerId : null;
            }
            PlayerDef? selectedPlayer = !string.IsNullOrEmpty(_inspectedPlayerId) ? team.FindPlayer(_inspectedPlayerId!) : null;
            if (selectedPlayer != null)
            {
                FFM.Meta.Services.SignatureTraitCatalog.EnsureStarterTraits(selectedPlayer);
            }

            // 1. Top Bar: Weekly Team Training Focus + Idle Bootcamp Economy Summary
            var topCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
            _trainingTab.AddChild(topCard);

            var topHBox = new HBoxContainer();
            topHBox.AddThemeConstantOverride("separation", 12);
            topCard.AddChild(topHBox);

            topHBox.AddChild(EsportsUiTheme.CreateSectionTitle("TEAM IDLE FOCUS · 全队每日挂机合练重心:", EsportsUiTheme.GoldPrimary));

            foreach (TeamTrainingFocus tf in Enum.GetValues<TeamTrainingFocus>())
            {
                TeamTrainingFocus captured = tf;
                bool active = _career.State.TeamTrainingFocus == captured;
                var btn = EsportsUiTheme.CreateStyledButton(
                    active ? $"★ {captured}" : captured.ToString(),
                    isPrimaryGold: active,
                    minWidth: 132f,
                    minHeight: 32f);
                btn.Pressed += () =>
                {
                    _career.State.TeamTrainingFocus = captured;
                    ShowToast($"全队每日挂机合练重心已切换为: {captured}");
                    RefreshAllTabs();
                };
                topHBox.AddChild(btn);
            }

            var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            topHBox.AddChild(spacer);

            topHBox.AddChild(EsportsUiTheme.CreatePillBadge($"基地XP池: {_career.State.IdleClock.ClubXpPool:N0} XP", EsportsUiTheme.CyanElectric));
            topHBox.AddChild(EsportsUiTheme.CreatePillBadge($"战术点: {_career.State.IdleClock.TacticalPoints} TP", EsportsUiTheme.GoldPrimary));
            topHBox.AddChild(EsportsUiTheme.CreatePillBadge($"可用资金: ${_career.State.CashBalance:N0}", EsportsUiTheme.EmeraldSuccess));

            // 2. Main 3-Column Player Development & Idle Bootcamp Hub
            var mainSplit = new HBoxContainer();
            mainSplit.AddThemeConstantOverride("separation", 12);
            _trainingTab.AddChild(mainSplit);

            // =========================================================================
            // LEFT COLUMN: Roster Progression List (Lv / Star / XP / Fatigue / Auto-Rest / Individual Drill)
            // =========================================================================
            var leftCol = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(435f, 0f)
            };
            leftCol.AddThemeConstantOverride("separation", 8);
            mainSplit.AddChild(leftCol);

            leftCol.AddChild(EsportsUiTheme.CreateSectionTitle("ROSTER PROGRESSION · 选手养成队列", EsportsUiTheme.CyanElectric));

            var indivValues = Enum.GetValues<IndividualTrainingFocus>();
            foreach (var p in team.Players)
            {
                PlayerDef player = p;
                FFM.Meta.Services.SignatureTraitCatalog.EnsureStarterTraits(player);
                if (!_career.State.IndividualFocuses.TryGetValue(player.PlayerId, out IndividualTrainingFocus curFocus))
                {
                    curFocus = IndividualTrainingFocus.AimAndRecoil;
                    _career.State.IndividualFocuses[player.PlayerId] = curFocus;
                }

                bool isSel = player.PlayerId == _inspectedPlayerId;
                bool highFatigue = player.Condition.Fatigue >= 65f;
                var pCard = EsportsUiTheme.CreateBentoCard(
                    accentBorder: isSel ? EsportsUiTheme.GoldPrimary : (highFatigue ? EsportsUiTheme.CrimsonDanger : EsportsUiTheme.NavyBorder));
                leftCol.AddChild(pCard);

                var pVBox = new VBoxContainer();
                pVBox.AddThemeConstantOverride("separation", 5);
                pCard.AddChild(pVBox);

                var pHeader = new HBoxContainer();
                pHeader.AddThemeConstantOverride("separation", 8);
                pVBox.AddChild(pHeader);

                var portrait = new TextureRect
                {
                    Texture = VisualAssetRegistry.GetPlayerPortrait(player.PlayerId, Role.Rusher),
                    CustomMinimumSize = new Vector2(46f, 46f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                pHeader.AddChild(portrait);

                var titleVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                titleVBox.AddThemeConstantOverride("separation", 2);
                string stars = new string('★', Math.Clamp(player.StarRating, 1, 5)) + new string('☆', Math.Clamp(5 - player.StarRating, 0, 5));
                var nLbl = new Label
                {
                    Text = $"{player.Name}  [{stars}]  Lv.{player.Level}/{player.GetMaxLevelForStar()}",
                    Modulate = isSel ? EsportsUiTheme.GoldPrimary : EsportsUiTheme.TextPrimary
                };
                nLbl.AddThemeFontSizeOverride("font_size", 14);
                titleVBox.AddChild(nLbl);

                var subLbl = new Label
                {
                    Text = $"OVR {player.CalculateTrueRating():0.0}  |  潜力 POT {player.Potential:0}  |  EXP {player.CurrentXp}/{player.GetXpRequiredForNextLevel()}",
                    Modulate = EsportsUiTheme.CyanElectric
                };
                subLbl.AddThemeFontSizeOverride("font_size", 11);
                titleVBox.AddChild(subLbl);
                pHeader.AddChild(titleVBox);

                var selBtn = EsportsUiTheme.CreateStyledButton(
                    isSel ? "● 养成中" : "查看养成",
                    isPrimaryGold: isSel,
                    minWidth: 82f,
                    minHeight: 28f);
                selBtn.Pressed += () =>
                {
                    _inspectedPlayerId = player.PlayerId;
                    RefreshAllTabs();
                };
                pHeader.AddChild(selBtn);

                Color fatColor = player.Condition.Fatigue >= 65f
                    ? EsportsUiTheme.CrimsonDanger
                    : (player.Condition.Fatigue >= 40f ? EsportsUiTheme.AmberWarning : EsportsUiTheme.EmeraldSuccess);
                pVBox.AddChild(EsportsUiTheme.CreateStatBar($"疲劳度 {player.Condition.Fatigue:0}%", player.Condition.Fatigue, 100f, fatColor));

                var ctrlRow = new HBoxContainer();
                ctrlRow.AddThemeConstantOverride("separation", 6);
                pVBox.AddChild(ctrlRow);

                var focusCycleBtn = EsportsUiTheme.CreateStyledButton(
                    $"挂机科目: {curFocus}",
                    isPrimaryGold: false,
                    minWidth: 195f,
                    minHeight: 26f);
                focusCycleBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                focusCycleBtn.Pressed += () =>
                {
                    int idx = Array.IndexOf(indivValues, curFocus);
                    var nextFocus = indivValues[(idx + 1) % indivValues.Length];
                    _career.State.IndividualFocuses[player.PlayerId] = nextFocus;
                    ShowToast($"{player.Name} 挂机专精科目已切换为: {nextFocus}");
                    RefreshAllTabs();
                };
                ctrlRow.AddChild(focusCycleBtn);

                var autoRestBtn = EsportsUiTheme.CreateStyledButton(
                    player.Condition.AutoRestThreshold ? "自动轮休>70%:开" : "自动轮休>70%:关",
                    isPrimaryGold: player.Condition.AutoRestThreshold,
                    minWidth: 120f,
                    minHeight: 26f);
                autoRestBtn.Pressed += () =>
                {
                    _career.TogglePlayerAutoRest(player.PlayerId);
                    ShowToast($"{player.Name} 疲劳>70%自动轮休已{(player.Condition.AutoRestThreshold ? "开启" : "关闭")}。");
                    RefreshAllTabs();
                };
                ctrlRow.AddChild(autoRestBtn);
            }

            // =========================================================================
            // CENTER COLUMN: Selected Player 5-Dimension Progression Inspector
            // =========================================================================
            var centerCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.CyanElectric);
            centerCard.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            mainSplit.AddChild(centerCard);

            var centerVBox = new VBoxContainer();
            centerVBox.AddThemeConstantOverride("separation", 8);
            centerCard.AddChild(centerVBox);

            if (selectedPlayer != null)
            {
                PlayerDef sp = selectedPlayer;
                float cap = sp.GetEffectiveAttributeCap();
                int maxLv = sp.GetMaxLevelForStar();
                int xpNeeded = Math.Max(0, sp.GetXpRequiredForNextLevel() - sp.CurrentXp);

                var spHeader = new HBoxContainer();
                spHeader.AddThemeConstantOverride("separation", 10);
                spHeader.AddChild(EsportsUiTheme.CreateSectionTitle(
                    $"5-DIMENSION PROGRESSION · {sp.Name.ToUpperInvariant()} (Age {sp.Age})",
                    EsportsUiTheme.GoldPrimary));
                spHeader.AddChild(EsportsUiTheme.CreatePillBadge(
                    $"Lv.{sp.Level}/{maxLv} · {new string('★', Math.Clamp(sp.StarRating, 1, 5))}",
                    EsportsUiTheme.GoldPrimary));
                spHeader.AddChild(EsportsUiTheme.CreatePillBadge(
                    $"OVR {sp.CalculateTrueRating():0.0} | 潜力 POT {sp.Potential:0} (当前星级上限 {cap:0.0})",
                    EsportsUiTheme.CyanElectric));
                centerVBox.AddChild(spHeader);

                // Level Up & Star Limit Break Action Row
                var upgRow = new HBoxContainer();
                upgRow.AddThemeConstantOverride("separation", 10);
                centerVBox.AddChild(upgRow);

                bool canLvUp = sp.Level < maxLv && _career.State.IdleClock.ClubXpPool >= xpNeeded;
                string lvBtnText = sp.Level >= maxLv
                    ? $"已达当前星级上限 Lv.{maxLv} (需升星突破)"
                    : $"⬆ 注入基地XP升至 Lv.{sp.Level + 1} (消耗 {xpNeeded} XP)";
                var lvUpBtn = EsportsUiTheme.CreateStyledButton(lvBtnText, isPrimaryGold: canLvUp, minWidth: 240f, minHeight: 34f);
                lvUpBtn.Disabled = !canLvUp;
                lvUpBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                lvUpBtn.Pressed += () =>
                {
                    if (_career.LevelUpPlayer(sp.PlayerId))
                    {
                        ShowToast($"★ {sp.Name} 成功升至 Lv.{sp.Level}！全属性获得成长！");
                        RefreshAllTabs();
                    }
                };
                upgRow.AddChild(lvUpBtn);

                int nextStar = sp.StarRating + 1;
                int reqLvForStar = sp.StarRating * 6;
                int starCashCost = sp.StarRating * 15000;
                int starTpCost = sp.StarRating * 30;
                bool canStarBreak = sp.StarRating < 5
                    && sp.Level >= reqLvForStar
                    && _career.State.CashBalance >= starCashCost
                    && _career.State.IdleClock.TacticalPoints >= starTpCost;
                string starBtnText = sp.StarRating >= 5
                    ? "★★★★★ 已达最高传奇五星"
                    : $"★ 升星突破 {sp.StarRating}★ -> {nextStar}★ (需Lv.{reqLvForStar} · ${starCashCost:N0} + {starTpCost}TP)";
                var starBreakBtn = EsportsUiTheme.CreateStyledButton(starBtnText, isPrimaryGold: canStarBreak, minWidth: 270f, minHeight: 34f);
                starBreakBtn.Disabled = !canStarBreak;
                starBreakBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                starBreakBtn.Pressed += () =>
                {
                    if (_career.StarBreakPlayer(sp.PlayerId))
                    {
                        ShowToast($"★★ {sp.Name} 完成 {sp.StarRating}★ 升星突破！属性上限解放并解锁新阶特质！");
                        RefreshAllTabs();
                    }
                };
                upgRow.AddChild(starBreakBtn);

                // Dimension 2: 15 Attributes (3 Sub-Columns)
                var attrCols = new HBoxContainer();
                attrCols.AddThemeConstantOverride("separation", 12);
                centerVBox.AddChild(attrCols);

                var mCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                mCol.AddThemeConstantOverride("separation", 3);
                mCol.AddChild(EsportsUiTheme.CreateSectionTitle("枪法身法 (Mechanical)", EsportsUiTheme.GoldPrimary));
                mCol.AddChild(EsportsUiTheme.CreateStatBar($"Aim ({sp.Mechanical.Aim:0.0}/{cap:0})", sp.Mechanical.Aim, 20f));
                mCol.AddChild(EsportsUiTheme.CreateStatBar($"Recoil ({sp.Mechanical.Recoil:0.0}/{cap:0})", sp.Mechanical.Recoil, 20f));
                mCol.AddChild(EsportsUiTheme.CreateStatBar($"Reflex ({sp.Mechanical.Reflex:0.0}/{cap:0})", sp.Mechanical.Reflex, 20f));
                mCol.AddChild(EsportsUiTheme.CreateStatBar($"Movement ({sp.Mechanical.Movement:0.0}/{cap:0})", sp.Mechanical.Movement, 20f));
                mCol.AddChild(EsportsUiTheme.CreateStatBar($"CQC ({sp.Mechanical.Cqc:0.0}/{cap:0})", sp.Mechanical.Cqc, 20f));
                mCol.AddChild(EsportsUiTheme.CreateStatBar($"LongRange ({sp.Mechanical.LongRange:0.0}/{cap:0})", sp.Mechanical.LongRange, 20f));
                attrCols.AddChild(mCol);

                var tCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                tCol.AddThemeConstantOverride("separation", 3);
                tCol.AddChild(EsportsUiTheme.CreateSectionTitle("战术意识 (Tactical)", EsportsUiTheme.CyanElectric));
                tCol.AddChild(EsportsUiTheme.CreateStatBar($"Awareness ({sp.Tactical.Awareness:0.0}/{cap:0})", sp.Tactical.Awareness, 20f));
                tCol.AddChild(EsportsUiTheme.CreateStatBar($"Positioning ({sp.Tactical.Positioning:0.0}/{cap:0})", sp.Tactical.Positioning, 20f));
                tCol.AddChild(EsportsUiTheme.CreateStatBar($"Utility ({sp.Tactical.Utility:0.0}/{cap:0})", sp.Tactical.Utility, 20f));
                tCol.AddChild(EsportsUiTheme.CreateStatBar($"Looting ({sp.Tactical.Looting:0.0}/{cap:0})", sp.Tactical.Looting, 20f));
                tCol.AddChild(EsportsUiTheme.CreateStatBar($"Decision ({sp.Tactical.Decision:0.0}/{cap:0})", sp.Tactical.Decision, 20f));
                tCol.AddChild(EsportsUiTheme.CreateStatBar($"Comms ({sp.Tactical.Comms:0.0}/{cap:0})", sp.Tactical.Comms, 20f));
                attrCols.AddChild(tCol);

                var menCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                menCol.AddThemeConstantOverride("separation", 3);
                menCol.AddChild(EsportsUiTheme.CreateSectionTitle("心态与位置熟练度 (Mental & Roles)", EsportsUiTheme.EmeraldSuccess));
                menCol.AddChild(EsportsUiTheme.CreateStatBar($"Composure ({sp.Mental.Composure:0.0}/{cap:0})", sp.Mental.Composure, 20f));
                menCol.AddChild(EsportsUiTheme.CreateStatBar($"Teamwork ({sp.Mental.Teamwork:0.0}/{cap:0})", sp.Mental.Teamwork, 20f));
                menCol.AddChild(EsportsUiTheme.CreateStatBar($"Determination ({sp.Mental.Determination:0.0}/{cap:0})", sp.Mental.Determination, 20f));
                menCol.AddChild(EsportsUiTheme.CreateStatBar("Rusher 突击熟练", sp.RoleProficiency.GetProficiency(Role.Rusher), 100f, EsportsUiTheme.GoldPrimary));
                menCol.AddChild(EsportsUiTheme.CreateStatBar("Sniper 狙击熟练", sp.RoleProficiency.GetProficiency(Role.Sniper), 100f, EsportsUiTheme.CyanElectric));
                menCol.AddChild(EsportsUiTheme.CreateStatBar("Support 指挥支援", sp.RoleProficiency.GetProficiency(Role.Support), 100f, EsportsUiTheme.EmeraldSuccess));
                attrCols.AddChild(menCol);

                // Dimension 3: 5-Weapon Class Mastery (Tier I / II / III)
                centerVBox.AddChild(EsportsUiTheme.CreateSectionTitle(
                    "WEAPON CLASS MASTERY · 五大枪械流派专精 (Tier I ≥30 · Tier II ≥65 · Tier III ≥90 解锁局内弹道与爆头加成)",
                    EsportsUiTheme.CyanElectric));

                var wmGrid = new HBoxContainer();
                wmGrid.AddThemeConstantOverride("separation", 8);
                centerVBox.AddChild(wmGrid);

                AddWeaponMasteryMiniBox(wmGrid, "SG 霰弹 (M1887)", sp.WeaponMastery.Shotgun);
                AddWeaponMasteryMiniBox(wmGrid, "SMG 冲锋 (MP40)", sp.WeaponMastery.Smg);
                AddWeaponMasteryMiniBox(wmGrid, "AR 步枪 (AK/SCAR)", sp.WeaponMastery.Ar);
                AddWeaponMasteryMiniBox(wmGrid, "DMR 连狙 (AC80)", sp.WeaponMastery.Dmr);
                AddWeaponMasteryMiniBox(wmGrid, "SR 栓狙 (AWM)", sp.WeaponMastery.Sr);

                // Dimension 4: Signature Esports Traits ([1★], [3★], [5★])
                centerVBox.AddChild(EsportsUiTheme.CreateSectionTitle(
                    "SIGNATURE ESPORTS TRAITS · 选手招牌词条 (1★初始 · 3★Lv15进阶 · 5★Lv30觉醒 — 直接驱动局内AI战术行为)",
                    EsportsUiTheme.GoldPrimary));

                var traitsBox = new VBoxContainer();
                traitsBox.AddThemeConstantOverride("separation", 4);
                centerVBox.AddChild(traitsBox);

                for (int slotIdx = 0; slotIdx < 3; slotIdx++)
                {
                    int capturedSlot = slotIdx;
                    string slotTierTag = slotIdx == 0 ? "[1★ 初始特质]" : (slotIdx == 1 ? "[3★ 进阶特质]" : "[5★ 传奇觉醒]");
                    if (slotIdx < sp.SignatureTraits.Count)
                    {
                        var trRow = new HBoxContainer();
                        trRow.AddThemeConstantOverride("separation", 8);
                        traitsBox.AddChild(trRow);

                        var (title, desc) = FFM.Meta.Services.SignatureTraitCatalog.GetTraitDisplay(sp.SignatureTraits[slotIdx]);
                        var trLbl = new Label
                        {
                            Text = $"  ★ {slotTierTag} {title} — {desc}",
                            Modulate = EsportsUiTheme.GoldPrimary,
                            SizeFlagsHorizontal = SizeFlags.ExpandFill,
                            VerticalAlignment = VerticalAlignment.Center,
                            AutowrapMode = TextServer.AutowrapMode.WordSmart
                        };
                        trLbl.AddThemeFontSizeOverride("font_size", 12);
                        trRow.AddChild(trLbl);

                        bool canReforge = _career.State.IdleClock.TacticalPoints >= 40;
                        var reforgeBtn = EsportsUiTheme.CreateStyledButton(
                            "🔄 洗练切换 (40 TP)",
                            isPrimaryGold: false,
                            minWidth: 132f,
                            minHeight: 26f);
                        reforgeBtn.Disabled = !canReforge;
                        reforgeBtn.Pressed += () =>
                        {
                            if (_career.CyclePlayerSignatureTrait(sp.PlayerId, capturedSlot, 40))
                            {
                                var (newTitle, _) = FFM.Meta.Services.SignatureTraitCatalog.GetTraitDisplay(sp.SignatureTraits[capturedSlot]);
                                ShowToast($"🦅 {sp.Name} 槽位#{capturedSlot + 1} 特质已切换为: {newTitle}！");
                                RefreshAllTabs();
                            }
                        };
                        trRow.AddChild(reforgeBtn);
                    }
                    else
                    {
                        string lockHint = slotIdx == 1 ? "达成 3★ 突破 (或升至 Lv.15) 解锁第二招牌特质槽" : "达成 5★ 突破 (或升至 Lv.30) 解锁终极传奇觉醒词条";
                        var lockLbl = new Label
                        {
                            Text = $"  🔒 {slotTierTag} 未解锁 — {lockHint}",
                            Modulate = EsportsUiTheme.TextSecondary
                        };
                        lockLbl.AddThemeFontSizeOverride("font_size", 12);
                        traitsBox.AddChild(lockLbl);
                    }
                }
            }

            // =========================================================================
            // RIGHT COLUMN: Instant Bootcamp Drills, Club Idle Facilities & Live Log
            // =========================================================================
            var rightCol = new VBoxContainer
            {
                CustomMinimumSize = new Vector2(465f, 0f)
            };
            rightCol.AddThemeConstantOverride("separation", 8);
            mainSplit.AddChild(rightCol);

            // Card A: Instant Bootcamp Drills
            var drillCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
            rightCol.AddChild(drillCard);

            var drillVBox = new VBoxContainer();
            drillVBox.AddThemeConstantOverride("separation", 6);
            drillCard.AddChild(drillVBox);

            drillVBox.AddChild(EsportsUiTheme.CreateSectionTitle(
                $"BOOTCAMP SPECIAL DRILLS · 专项特训课 ({selectedPlayer?.Name ?? "选手"} · 每次 $8,000 + 15 TP)",
                EsportsUiTheme.GoldPrimary));

            var drillGrid = new GridContainer { Columns = 2 };
            drillGrid.AddThemeConstantOverride("h_separation", 8);
            drillGrid.AddThemeConstantOverride("v_separation", 6);
            drillVBox.AddChild(drillGrid);

            AddBootcampDrillButton(drillGrid, selectedPlayer, BootcampDrillType.LiveFireReflex, "🎯 靶场极限拉枪 (+枪法/武器专精)");
            AddBootcampDrillButton(drillGrid, selectedPlayer, BootcampDrillType.TacticalSandbox, "🧠 沙盘进圈复盘 (+意识/指挥XP)");
            AddBootcampDrillButton(drillGrid, selectedPlayer, BootcampDrillType.GlooGymnastics, "🧱 冰墙身法体操 (+身法/道具熟练)");
            AddBootcampDrillButton(drillGrid, selectedPlayer, BootcampDrillType.SpaRecovery, "💆 理疗冥想恢复 (-38%疲劳/+抗压)");

            // Card B: 4 Club Idle Facilities (Lv.1~5)
            var facCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.EmeraldSuccess);
            rightCol.AddChild(facCard);

            var facVBox = new VBoxContainer();
            facVBox.AddThemeConstantOverride("separation", 5);
            facCard.AddChild(facVBox);

            facVBox.AddChild(EsportsUiTheme.CreateSectionTitle("CLUB IDLE FACILITIES · 俱乐部基地挂机设施 (Lv.1~5)", EsportsUiTheme.EmeraldSuccess));

            var fac = _career.State.Facilities;
            AddFacilityRow(facVBox, "shooting", "射击训练舱 (Shooting Lab)", fac.ShootingLabLevel, $"每日挂机XP产出 +{fac.ShootingLabLevel * 15}%");
            AddFacilityRow(facVBox, "tactical", "战术复盘室 (Tactical Lab)", fac.TacticalLabLevel, $"每日战术点与专精 +{fac.TacticalLabLevel * 20}%");
            AddFacilityRow(facVBox, "recovery", "体能康复中心 (Recovery Center)", fac.RecoveryCenterLevel, $"挂机疲劳恢复速率 +{fac.RecoveryCenterLevel * 18}%");
            AddFacilityRow(facVBox, "media", "直播商务中心 (Media Studio)", fac.MediaStudioLevel, $"每日金库赞助 +${fac.MediaStudioLevel * 2200:N0}/天");

            // Card C: Live Idle Progression Log Feed
            var logCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.CyanElectric);
            rightCol.AddChild(logCard);

            var logVBox = new VBoxContainer();
            logVBox.AddThemeConstantOverride("separation", 4);
            logCard.AddChild(logVBox);

            logVBox.AddChild(EsportsUiTheme.CreateSectionTitle("IDLE PROGRESSION LOG · 基地挂机实时动态", EsportsUiTheme.CyanElectric));

            var logs = _career.State.IdleClock.RecentLogs;
            if (logs.Count == 0)
            {
                logVBox.AddChild(new Label
                {
                    Text = "  • 基地日程挂机运行中，每完成1天自动结算特训成长与金库收益...",
                    Modulate = EsportsUiTheme.TextSecondary
                });
            }
            else
            {
                int startIdx = Math.Max(0, logs.Count - 6);
                for (int i = logs.Count - 1; i >= startIdx; i--)
                {
                    var lLbl = new Label
                    {
                        Text = $"▸ {logs[i]}",
                        Modulate = i == logs.Count - 1 ? EsportsUiTheme.GoldPrimary : EsportsUiTheme.TextSecondary,
                        AutowrapMode = TextServer.AutowrapMode.WordSmart
                    };
                    lLbl.AddThemeFontSizeOverride("font_size", 11);
                    logVBox.AddChild(lLbl);
                }
            }
        }

        private static void AddWeaponMasteryMiniBox(HBoxContainer parent, string label, float val)
        {
            int tier = WeaponMasteryBlock.GetMasteryTier(val);
            Color tierCol = tier switch
            {
                3 => EsportsUiTheme.GoldPrimary,
                2 => EsportsUiTheme.CyanElectric,
                1 => EsportsUiTheme.EmeraldSuccess,
                _ => EsportsUiTheme.TextSecondary
            };

            var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            box.AddThemeConstantOverride("separation", 2);
            box.AddChild(EsportsUiTheme.CreatePillBadge($"{label} [T{tier}]", tierCol));
            box.AddChild(EsportsUiTheme.CreateStatBar($"{val:0}/100", val, 100f, tierCol));
            parent.AddChild(box);
        }

        private void AddBootcampDrillButton(GridContainer grid, PlayerDef? player, BootcampDrillType drill, string title)
        {
            bool canAfford = _career != null
                && player != null
                && _career.State.CashBalance >= 8000
                && _career.State.IdleClock.TacticalPoints >= 15;
            var btn = EsportsUiTheme.CreateStyledButton(title, isPrimaryGold: false, minWidth: 210f, minHeight: 32f);
            btn.Disabled = !canAfford;
            btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            btn.Pressed += () =>
            {
                if (_career == null || player == null) return;
                if (_career.RunBootcampDrill(player.PlayerId, drill))
                {
                    ShowToast($"已对 {player.Name} 执行专项特训 [{title}]！");
                    RefreshAllTabs();
                }
            };
            grid.AddChild(btn);
        }

        private void AddFacilityRow(VBoxContainer parent, string key, string name, int level, string bonusDesc)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var lbl = new Label
            {
                Text = $"{name} [Lv.{level}/5] · {bonusDesc}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = EsportsUiTheme.TextPrimary
            };
            lbl.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(lbl);

            int cost = ClubFacilitiesState.GetUpgradeCost(level);
            bool canUpgrade = _career != null && level < 5 && _career.State.CashBalance >= cost;
            var btn = EsportsUiTheme.CreateStyledButton(
                level >= 5 ? "MAX Lv.5" : $"升级 (${cost:N0})",
                isPrimaryGold: canUpgrade,
                minWidth: 115f,
                minHeight: 26f);
            btn.Disabled = !canUpgrade;
            btn.Pressed += () =>
            {
                if (_career != null && _career.UpgradeFacility(key))
                {
                    ShowToast($"基地设施 [{name}] 已升级！");
                    RefreshAllTabs();
                }
            };
            row.AddChild(btn);
            parent.AddChild(row);
        }

        private void RebuildStandingsTab()
        {
            if (_standingsTab == null || _career == null) return;
            ClearChildren(_standingsTab);

            var bannerCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
            _standingsTab.AddChild(bannerCard);

            var bannerHBox = new HBoxContainer();
            bannerHBox.AddThemeConstantOverride("separation", 14);
            bannerCard.AddChild(bannerHBox);

            bannerHBox.AddChild(EsportsUiTheme.CreateSectionTitle(
                $"FFWS OFFICIAL STANDINGS · 当前赛段: {_career.CurrentStage}", EsportsUiTheme.GoldPrimary));
            bannerHBox.AddChild(EsportsUiTheme.CreatePillBadge(
                $"CHAMPION RUSH 赛点线: {_career.Config.Scoring.ChampionRushThreshold} PTS (达标后下一局吃鸡直接夺冠)",
                EsportsUiTheme.CyanElectric));

            // Table Header Row
            var headerPanel = new PanelContainer();
            headerPanel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = EsportsUiTheme.NavySurfaceElevated,
                BorderColor = EsportsUiTheme.GoldPrimary,
                BorderWidthBottom = 2,
                ContentMarginLeft = 12,
                ContentMarginRight = 12,
                ContentMarginTop = 6,
                ContentMarginBottom = 6
            });
            headerPanel.AddChild(BuildStandingsRowHBox(
                "排名", "分组", "战队名称", "场次", "排名分", "淘汰分", "起跑分", "吃鸡数", "总积分", "状态 / 晋级区",
                EsportsUiTheme.GoldPrimary, null, isHeader: true));
            _standingsTab.AddChild(headerPanel);

            for (int i = 0; i < _career.State.Standings.Count; i++)
            {
                LeagueStandingEntry s = _career.State.Standings[i];
                bool isPlayer = s.TeamId == _career.State.PlayerTeamId;
                char grp = (char)('A' + s.GroupIndex);

                TeamDef? teamDef = null;
                foreach (var t in _career.State.Teams)
                {
                    if (t.TeamId == s.TeamId)
                    {
                        teamDef = t;
                        break;
                    }
                }

                Texture2D logo = VisualAssetRegistry.GetTeamLogo(s.TeamId, s.TeamName, teamDef?.ColorHex);
                string statusText = s.WonChampionRush
                    ? "★ FFWS 总冠军 ★"
                    : (s.MatchPointEligible ? "⚡ 赛点听牌 (MATCH POINT)" : (i < 12 ? "晋级区 (TOP 12)" : "淘汰风险区"));

                Color rowAccent = isPlayer
                    ? EsportsUiTheme.GoldPrimary
                    : (s.MatchPointEligible ? EsportsUiTheme.CyanElectric : (i < 12 ? EsportsUiTheme.EmeraldSuccess : EsportsUiTheme.TextSecondary));

                var rowCard = new PanelContainer();
                rowCard.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = isPlayer
                        ? new Color(0.16f, 0.14f, 0.06f, 0.96f)
                        : ((i & 1) == 0 ? EsportsUiTheme.NavySurface : new Color(0.065f, 0.095f, 0.16f, 0.96f)),
                    BorderColor = isPlayer ? EsportsUiTheme.GoldPrimary : Color.FromHtml(teamDef?.ColorHex ?? "#2A3656"),
                    BorderWidthLeft = isPlayer ? 5 : 3,
                    CornerRadiusTopRight = 4,
                    CornerRadiusBottomRight = 4,
                    ContentMarginLeft = 12,
                    ContentMarginRight = 12,
                    ContentMarginTop = 5,
                    ContentMarginBottom = 5
                });

                rowCard.AddChild(BuildStandingsRowHBox(
                    $"#{i + 1:00}",
                    $"Grp {grp}",
                    s.TeamName,
                    s.MatchesPlayed.ToString(),
                    s.PlacementPoints.ToString(),
                    s.KillPoints.ToString(),
                    $"+{s.HeadStartPoints}",
                    s.Booyahs.ToString(),
                    $"{s.TotalPoints} PTS",
                    statusText,
                    rowAccent,
                    logo,
                    isHeader: false));

                _standingsTab.AddChild(rowCard);
            }
        }

        private static HBoxContainer BuildStandingsRowHBox(
            string rank,
            string group,
            string teamName,
            string mp,
            string placePts,
            string killPts,
            string bonusPts,
            string booyahs,
            string totalPts,
            string status,
            Color textColor,
            Texture2D? logo,
            bool isHeader)
        {
            var hbox = new HBoxContainer();
            hbox.AddThemeConstantOverride("separation", 10);

            AddCellLabel(hbox, rank, 52f, isHeader ? EsportsUiTheme.GoldPrimary : textColor);
            AddCellLabel(hbox, group, 60f, EsportsUiTheme.TextSecondary);

            var teamBox = new HBoxContainer { CustomMinimumSize = new Vector2(230f, 0f) };
            teamBox.AddThemeConstantOverride("separation", 8);
            if (logo != null)
            {
                teamBox.AddChild(new TextureRect
                {
                    Texture = logo,
                    CustomMinimumSize = new Vector2(24f, 24f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                });
            }
            var tLbl = new Label
            {
                Text = teamName,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = isHeader ? EsportsUiTheme.GoldPrimary : EsportsUiTheme.TextPrimary
            };
            teamBox.AddChild(tLbl);
            hbox.AddChild(teamBox);

            AddCellLabel(hbox, mp, 55f, EsportsUiTheme.TextSecondary, HorizontalAlignment.Center);
            AddCellLabel(hbox, placePts, 70f, EsportsUiTheme.TextPrimary, HorizontalAlignment.Center);
            AddCellLabel(hbox, killPts, 70f, EsportsUiTheme.CrimsonDanger.Lightened(0.25f), HorizontalAlignment.Center);
            AddCellLabel(hbox, bonusPts, 65f, EsportsUiTheme.CyanElectric, HorizontalAlignment.Center);
            AddCellLabel(hbox, booyahs, 65f, EsportsUiTheme.GoldPrimary, HorizontalAlignment.Center);
            AddCellLabel(hbox, totalPts, 95f, EsportsUiTheme.GoldBright, HorizontalAlignment.Right);

            var statusLbl = new Label
            {
                Text = status,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = textColor
            };
            hbox.AddChild(statusLbl);

            return hbox;
        }

        private static void AddCellLabel(
            HBoxContainer row,
            string text,
            float width,
            Color color,
            HorizontalAlignment align = HorizontalAlignment.Left)
        {
            row.AddChild(new Label
            {
                Text = text,
                CustomMinimumSize = new Vector2(width, 0f),
                HorizontalAlignment = align,
                VerticalAlignment = VerticalAlignment.Center,
                Modulate = color
            });
        }

        private void RebuildScoutingAndTransferTab(TeamDef team)
        {
            if (_scoutingTab == null || _career == null) return;
            ClearChildren(_scoutingTab);

            var topSplit = new HBoxContainer();
            topSplit.AddThemeConstantOverride("separation", 14);
            _scoutingTab.AddChild(topSplit);

            // Left Column: 2-Column Grid of Transfer Market Cards
            var marketVBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            marketVBox.AddThemeConstantOverride("separation", 10);
            topSplit.AddChild(marketVBox);

            var marketHeader = new HBoxContainer();
            marketHeader.AddThemeConstantOverride("separation", 12);
            marketHeader.AddChild(EsportsUiTheme.CreateSectionTitle("TRANSFER MARKET & 3-STAGE SCOUTING · 自由市场与渐进式球探"));
            marketHeader.AddChild(EsportsUiTheme.CreatePillBadge($"可用球探点: {_career.State.ScoutingPointsAvailable} PTS", EsportsUiTheme.CyanElectric));
            marketHeader.AddChild(EsportsUiTheme.CreatePillBadge($"转会预算: ${_career.State.CashBalance:N0}", EsportsUiTheme.EmeraldSuccess));
            marketVBox.AddChild(marketHeader);

            var grid = new GridContainer
            {
                Columns = 2,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            grid.AddThemeConstantOverride("h_separation", 12);
            grid.AddThemeConstantOverride("v_separation", 12);
            marketVBox.AddChild(grid);

            for (int i = 0; i < _career.State.TransferMarket.Count; i++)
            {
                TransferListing listing = _career.State.TransferMarket[i];
                PlayerDef candidate = listing.Player;

                ScoutingStage stage = ScoutingStage.Unknown;
                if (_career.State.ScoutingBook.TryGetValue(candidate.PlayerId, out ScoutingEntry? entry))
                {
                    stage = entry.Stage;
                }

                ScoutedPlayerView view = _career.Scouting.BuildPlayerView(candidate, stage);
                Color stageCol = stage switch
                {
                    ScoutingStage.Exact => EsportsUiTheme.EmeraldSuccess,
                    ScoutingStage.Range => EsportsUiTheme.CyanElectric,
                    _ => EsportsUiTheme.TextSecondary
                };

                var card = EsportsUiTheme.CreateBentoCard(accentBorder: stageCol);
                card.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                grid.AddChild(card);

                var cVBox = new VBoxContainer();
                cVBox.AddThemeConstantOverride("separation", 6);
                card.AddChild(cVBox);

                var topRow = new HBoxContainer();
                topRow.AddThemeConstantOverride("separation", 10);
                cVBox.AddChild(topRow);

                var portrait = new TextureRect
                {
                    Texture = VisualAssetRegistry.GetPlayerPortrait(candidate.PlayerId, Role.Rusher),
                    CustomMinimumSize = new Vector2(56f, 56f),
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
                };
                topRow.AddChild(portrait);

                var titleBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
                titleBox.AddChild(new Label
                {
                    Text = $"[{view.OverallTierTag}] {view.Name} (Age {view.Age})",
                    Modulate = EsportsUiTheme.TextPrimary
                });
                titleBox.AddChild(new Label
                {
                    Text = $"买断费: ${listing.BuyoutFee:N0} · 周薪: ${listing.AskedWeeklySalary:N0}/w",
                    Modulate = EsportsUiTheme.GoldPrimary
                });
                topRow.AddChild(titleBox);

                topRow.AddChild(EsportsUiTheme.CreatePillBadge($"情报: {stage}", stageCol));

                var statsLbl = new Label
                {
                    Text = $"Aim: {view.Attributes["aim"].DisplayText}  |  CQC: {view.Attributes["cqc"].DisplayText}  |  Long: {view.Attributes["longRange"].DisplayText}  |  Dec: {view.Attributes["decision"].DisplayText}",
                    Modulate = EsportsUiTheme.CyanElectric
                };
                statsLbl.AddThemeFontSizeOverride("font_size", 12);
                cVBox.AddChild(statsLbl);

                string traits = view.HiddenTraitHints.Count > 0
                    ? string.Join(" · ", view.HiddenTraitHints)
                    : "隐藏特质未知 (投入球探点解锁精确数值与大赛特质)";
                var traitLbl = new Label
                {
                    Text = $"球探报告: {traits}",
                    Modulate = EsportsUiTheme.TextSecondary,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                traitLbl.AddThemeFontSizeOverride("font_size", 12);
                cVBox.AddChild(traitLbl);

                var btnRow = new HBoxContainer();
                btnRow.AddThemeConstantOverride("separation", 8);
                cVBox.AddChild(btnRow);

                var scoutBtn = EsportsUiTheme.CreateStyledButton(
                    stage == ScoutingStage.Exact ? "✓ 已完全探明" : "🔍 球探调查 (50 PTS)",
                    isPrimaryGold: false,
                    minWidth: 140f,
                    minHeight: 30f);
                scoutBtn.Disabled = stage == ScoutingStage.Exact || _career.State.ScoutingPointsAvailable < 50;
                scoutBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                scoutBtn.Pressed += () =>
                {
                    if (_career.InvestScoutingOnPlayer(candidate.PlayerId, 50))
                    {
                        ShowToast($"Scouted {candidate.Name} -> {_career.State.ScoutingBook[candidate.PlayerId].Stage}.");
                        RefreshAllTabs();
                    }
                };
                btnRow.AddChild(scoutBtn);

                var signBtn = EsportsUiTheme.CreateStyledButton(
                    $"✍ 签约引进 (${listing.BuyoutFee:N0})",
                    isPrimaryGold: true,
                    minWidth: 145f,
                    minHeight: 30f);
                signBtn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                signBtn.Pressed += () =>
                {
                    int cash = _career.State.CashBalance;
                    if (_career.Transfers.TrySignPlayer(
                        team,
                        _career.State.TransferMarket,
                        candidate.PlayerId,
                        _career.Economy,
                        _career.State.Ledger,
                        ref cash,
                        _career.State.Season,
                        _career.State.Week,
                        out string msg))
                    {
                        _career.State.CashBalance = cash;
                        _career.State.ScoutingBook[candidate.PlayerId] = new ScoutingEntry
                        {
                            PlayerId = candidate.PlayerId,
                            Stage = ScoutingStage.Exact,
                            ProgressPoints = 100
                        };
                    }
                    ShowToast(msg);
                    RefreshAllTabs();
                };
                btnRow.AddChild(signBtn);
            }

            // Right Column: Club Financial Ledger Card
            var ledgerCard = EsportsUiTheme.CreateBentoCard(accentBorder: EsportsUiTheme.GoldPrimary);
            ledgerCard.CustomMinimumSize = new Vector2(390f, 0f);
            topSplit.AddChild(ledgerCard);

            var ledgerVBox = new VBoxContainer();
            ledgerVBox.AddThemeConstantOverride("separation", 8);
            ledgerCard.AddChild(ledgerVBox);

            ledgerVBox.AddChild(EsportsUiTheme.CreateSectionTitle("CLUB FINANCIAL LEDGER · 财政收支流水", EsportsUiTheme.GoldPrimary));

            int startLedger = Math.Max(0, _career.State.Ledger.Count - 10);
            for (int i = _career.State.Ledger.Count - 1; i >= startLedger; i--)
            {
                FinanceLedgerEntry le = _career.State.Ledger[i];
                bool positive = le.Amount >= 0;
                var entryPanel = EsportsUiTheme.CreateBentoCard(
                    accentBorder: positive ? EsportsUiTheme.EmeraldSuccess : EsportsUiTheme.CrimsonDanger,
                    bgOverride: new Color(0.06f, 0.09f, 0.15f, 0.9f));

                var entryVBox = new VBoxContainer();
                entryVBox.AddThemeConstantOverride("separation", 2);
                entryPanel.AddChild(entryVBox);

                var topLine = new HBoxContainer();
                topLine.AddChild(new Label
                {
                    Text = $"[S{le.Season} W{le.Week}] {le.Category}",
                    SizeFlagsHorizontal = SizeFlags.ExpandFill,
                    Modulate = EsportsUiTheme.TextPrimary
                });
                topLine.AddChild(new Label
                {
                    Text = $"{(positive ? "+" : "")}${le.Amount:N0}",
                    Modulate = positive ? EsportsUiTheme.EmeraldSuccess : EsportsUiTheme.CrimsonDanger
                });
                entryVBox.AddChild(topLine);

                var descLine = new Label
                {
                    Text = $"{le.Description} (结余: ${le.BalanceAfter:N0})",
                    Modulate = EsportsUiTheme.TextSecondary,
                    AutowrapMode = TextServer.AutowrapMode.WordSmart
                };
                descLine.AddThemeFontSizeOverride("font_size", 11);
                entryVBox.AddChild(descLine);

                ledgerVBox.AddChild(entryPanel);
            }
        }

        private void OnSaveCareerPressed()
        {
            if (_career == null) return;
            string savePath = ProjectSettings.GlobalizePath("user://career_save.json");
            _career.SaveService.SaveToFile(savePath, _career.State);
            ShowToast($"Career saved to {savePath}");
        }

        private void OnLoadCareerPressed()
        {
            if (_career == null) return;
            string savePath = ProjectSettings.GlobalizePath("user://career_save.json");
            if (!File.Exists(savePath))
            {
                ShowToast("No saved career file found at user://career_save.json.");
                return;
            }
            var loaded = _career.SaveService.LoadFromFile(savePath);
            _career.LoadState(loaded);
            ShowToast($"Loaded Career: Season {loaded.Season}, Week {loaded.Week}.");
            RefreshAllTabs();
        }

        private void OnQuickSimWeekPressed()
        {
            if (_career == null) return;
            var rep = _career.AdvanceWeekWithSimulation();
            if (rep != null)
            {
                ShowToast($"Week simulated! Placement #{rep.Placement}, Kills: {rep.Kills}, Points: {rep.TotalPoints}.");
            }
            else
            {
                ShowToast("Offseason completed! New Season & Meta Patch applied.");
            }
            RefreshAllTabs();
        }

        private void ShowToast(string message)
        {
            _actionCounter++;
            bool isWarn = message.Contains("No ", StringComparison.OrdinalIgnoreCase)
                || message.Contains("Insufficient", StringComparison.OrdinalIgnoreCase)
                || message.Contains("不足", StringComparison.OrdinalIgnoreCase)
                || message.Contains("failed", StringComparison.OrdinalIgnoreCase);
            bool isInfo = message.Contains("Switched", StringComparison.OrdinalIgnoreCase)
                || message.Contains("已展开", StringComparison.OrdinalIgnoreCase)
                || message.Contains("已聚焦", StringComparison.OrdinalIgnoreCase);

            _toastAccentColor = isWarn
                ? EsportsUiTheme.AmberWarning
                : (isInfo ? EsportsUiTheme.CyanElectric : EsportsUiTheme.GoldPrimary);
            _toastPulseTimer = 1.6f;

            string prefix = isWarn ? "⚠️" : (isInfo ? "◈" : "✓");
            if (_toastLabel != null)
            {
                _toastLabel.Text = $"{prefix} [ACTION #{_actionCounter:00}] {message}";
                _toastLabel.Modulate = _toastAccentColor.Lightened(0.25f);
            }
            if (_toastStyle != null)
            {
                _toastStyle.BorderColor = _toastAccentColor;
                _toastStyle.BgColor = new Color(
                    _toastAccentColor.R * 0.22f,
                    _toastAccentColor.G * 0.22f,
                    _toastAccentColor.B * 0.22f,
                    0.98f);
            }
        }

        private static void ClearChildren(Node container)
        {
            foreach (Node child in container.GetChildren())
            {
                child.QueueFree();
            }
        }
    }
}
