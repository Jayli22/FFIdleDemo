using System;
using FFM.Client.Sim;
using FFM.Sim.Core;
using FFM.Sim.Data;
using FFM.Sim.Simulation;
using Godot;

namespace FFM.Client.UI
{
    /// <summary>
    /// Bottom-Right Coach Tactical Command Deck (§5.4, §6.5, WP-15).
    /// Allows the manager to issue live tactical overrides with budget, cooldown,
    /// CMD-01 transmission delay, CMD-02 IGL compliance feedback, 1..6 hotkeys,
    /// active order button highlighting, and explicit rejection feedback.
    /// </summary>
    public partial class CommandPanel : PanelContainer
    {
        private SimHost? _simHost;
        private Label? _budgetLabel;
        private Label? _radioStatusLabel;
        private ProgressBar? _cooldownBar;
        private StyleBoxFlat? _panelStyle;
        private readonly Button[] _cmdButtons = new Button[6];
        private readonly CommandType[] _buttonCommandTypes =
        {
            CommandType.RotateNow,
            CommandType.HoldPosition,
            CommandType.ContestTarget,
            CommandType.AvoidFight,
            CommandType.EndgameFormation,
            CommandType.PopSkill
        };
        private readonly string[] _buttonBaseLabels =
        {
            "1 · ROTATE",
            "2 · HOLD POS",
            "3 · PRESS FIGHT",
            "4 · DISENGAGE",
            "5 · GLOO FORT",
            "6 · POP SKILLS"
        };

        private float _statusHighlightSec;
        private float _borderFlashSec;
        private Color _borderFlashColor = EsportsUiTheme.AccentGold;
        private bool _hasPinnedTarget;
        private Vec2 _pinnedTargetWorld;
        private int _lastSelectedBtnIdx = -1;

        public event Action<CommandType, string>? CommandIssued;
        public event Action<string>? CommandRejected;

        public void SetPinnedTarget(Vec2 worldPos)
        {
            _hasPinnedTarget = true;
            _pinnedTargetWorld = worldPos;
            if (_radioStatusLabel != null)
            {
                _radioStatusLabel.Text = $"◎ PINNED ({worldPos.X:0},{worldPos.Y:0}) · PRESS [1 ROTATE] OR [2 HOLD POS]";
                _radioStatusLabel.Modulate = EsportsUiTheme.AccentGold;
                _statusHighlightSec = 4.5f;
            }
            TriggerBorderFlash(EsportsUiTheme.AccentCyan, 0.9f);
        }

        public void Bind(SimHost simHost)
        {
            if (_simHost != null)
            {
                _simHost.EventDispatched -= OnSimEvent;
            }
            _simHost = simHost;
            _simHost.EventDispatched += OnSimEvent;
            _hasPinnedTarget = false;
            _lastSelectedBtnIdx = -1;
        }

        public override void _ExitTree()
        {
            if (_simHost != null)
            {
                _simHost.EventDispatched -= OnSimEvent;
            }
        }

        public override void _Ready()
        {
            BuildUi();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!IsVisibleInTree() || _simHost == null) return;
            if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
            {
                switch (keyEvent.Keycode)
                {
                    case Key.Key1:
                        Issue(CommandType.RotateNow, btnIdx: 0);
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.Key2:
                        Issue(CommandType.HoldPosition, btnIdx: 1);
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.Key3:
                        Issue(CommandType.ContestTarget, btnIdx: 2);
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.Key4:
                        Issue(CommandType.AvoidFight, btnIdx: 3);
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.Key5:
                        Issue(CommandType.EndgameFormation, EndgameStyle.GlooFort, btnIdx: 4);
                        GetViewport().SetInputAsHandled();
                        break;
                    case Key.Key6:
                        Issue(CommandType.PopSkill, btnIdx: 5);
                        GetViewport().SetInputAsHandled();
                        break;
                }
            }
        }

        private void BuildUi()
        {
            _panelStyle = EsportsUiTheme.CreateBentoCardStyle(
                EsportsUiTheme.BgPanel,
                EsportsUiTheme.AccentGold,
                cornerRadius: 8,
                borderTop: 2,
                padding: 10);
            AddThemeStyleboxOverride("panel", _panelStyle);

            var rootVBox = new VBoxContainer();
            rootVBox.AddThemeConstantOverride("separation", 6);
            AddChild(rootVBox);

            // Row 1: Coach Radio Title + Call Budget Badge + Cooldown Bar
            var headerHBox = new HBoxContainer();
            headerHBox.AddThemeConstantOverride("separation", 10);
            rootVBox.AddChild(headerHBox);

            var titleLbl = EsportsUiTheme.CreateSectionHeader("COACH RADIO COMMS (KEYS 1-6 / R-CLICK TO PIN)", EsportsUiTheme.AccentGold, 11);
            titleLbl.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            headerHBox.AddChild(titleLbl);

            _budgetLabel = new Label
            {
                Text = "CALLS: 5/5",
                Modulate = EsportsUiTheme.TextPrimary
            };
            _budgetLabel.AddThemeFontSizeOverride("font_size", 11);
            headerHBox.AddChild(_budgetLabel);

            _cooldownBar = EsportsUiTheme.CreateStyledProgressBar(
                EsportsUiTheme.AccentCyan,
                minVal: 0f,
                maxVal: 60f,
                value: 0f,
                minSize: new Vector2(84f, 10f));
            _cooldownBar.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            headerHBox.AddChild(_cooldownBar);

            // Row 2: Live IGL Compliance / Radio Channel Status
            _radioStatusLabel = new Label
            {
                Text = "● CHANNEL READY · R-CLICK 3D GROUND/MINIMAP OR PRESS [1-6]",
                Modulate = EsportsUiTheme.AccentCyan,
                ClipText = true
            };
            _radioStatusLabel.AddThemeFontSizeOverride("font_size", 11);
            rootVBox.AddChild(_radioStatusLabel);

            // Row 3: 3x2 Tactical Command Button Grid
            var cmdGrid = new GridContainer
            {
                Columns = 3,
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            cmdGrid.AddThemeConstantOverride("h_separation", 6);
            cmdGrid.AddThemeConstantOverride("v_separation", 6);
            rootVBox.AddChild(cmdGrid);

            var specs = new (string label, string tooltip, bool isPrimary, Action onClick)[]
            {
                (_buttonBaseLabels[0], "Order immediate rotation toward Pinned Target (or Next Safe Zone center)", false, () => Issue(CommandType.RotateNow, btnIdx: 0)),
                (_buttonBaseLabels[1], "Anchor Pinned Compound (or current high ground) and play defensive angles", false, () => Issue(CommandType.HoldPosition, btnIdx: 1)),
                (_buttonBaseLabels[2], "Boost squad aggression (+45%) and hunt nearest enemy squad", true, () => Issue(CommandType.ContestTarget, btnIdx: 2)),
                (_buttonBaseLabels[3], "Drop aggression (-45%) and prioritize survival / placement", false, () => Issue(CommandType.AvoidFight, btnIdx: 3)),
                (_buttonBaseLabels[4], "Establish Gloo Wall perimeter for final circle defense", false, () => Issue(CommandType.EndgameFormation, EndgameStyle.GlooFort, btnIdx: 4)),
                (_buttonBaseLabels[5], "Synchronize all ready Active Character Skills immediately", true, () => Issue(CommandType.PopSkill, btnIdx: 5))
            };

            for (int i = 0; i < specs.Length; i++)
            {
                int idx = i;
                var btn = EsportsUiTheme.CreateActionButton(
                    specs[idx].label,
                    isPrimaryGold: specs[idx].isPrimary,
                    minSize: new Vector2(126f, 28f));
                btn.SizeFlagsHorizontal = SizeFlags.ExpandFill;
                btn.TooltipText = specs[idx].tooltip;
                btn.AddThemeFontSizeOverride("font_size", 11);
                btn.Pressed += specs[idx].onClick;
                cmdGrid.AddChild(btn);
                _cmdButtons[idx] = btn;
            }
        }

        private void Issue(CommandType type, EndgameStyle endgame = EndgameStyle.HighGround, int btnIdx = -1)
        {
            if (_simHost == null) return;

            int pIdx = _simHost.PlayerSquadIndex;
            SquadSnapshot sq = _simHost.CurrentFrame.Squads[pIdx];

            if (_simHost.IsFinished || _simHost.IsReplaying)
            {
                RejectWithFeedback("× CANNOT ISSUE COMMAND DURING REPLAY / POST-MATCH");
                return;
            }
            if (sq.AliveCount <= 0)
            {
                RejectWithFeedback("× PLAYER SQUAD ELIMINATED — RADIO OFFLINE");
                return;
            }
            if (sq.CommandsRemaining <= 0)
            {
                RejectWithFeedback("× NO COACH CALLS REMAINING (0/5 BUDGET EXHAUSTED)");
                return;
            }
            if (sq.CommandCooldownTicks > 0)
            {
                RejectWithFeedback($"⏳ RADIO CHANNEL ON COOLDOWN ({sq.CommandCooldownSec:0.0}s REMAINING)");
                return;
            }

            Vec2 targetPos;
            if (_hasPinnedTarget && (type == CommandType.RotateNow || type == CommandType.HoldPosition))
            {
                targetPos = _pinnedTargetWorld;
            }
            else if (type == CommandType.HoldPosition && FFM.Client.Camera.CameraRig.TryComputeSquadCentroid(_simHost.CurrentFrame, _simHost.PlayerSquadIndex, out Vector3 centroid))
            {
                targetPos = new Vec2(centroid.X, centroid.Z);
            }
            else
            {
                targetPos = _simHost.CurrentFrame.NextZoneCenter3D.X > 1f
                    ? new Vec2(_simHost.CurrentFrame.NextZoneCenter3D.X, _simHost.CurrentFrame.NextZoneCenter3D.Z)
                    : new Vec2(1000f, 1000f);
            }

            bool ok = _simHost.IssueCoachCommand(type, targetPos, EngageStyle.Balanced, endgame);
            if (ok)
            {
                _lastSelectedBtnIdx = btnIdx;
                string coordTag = _hasPinnedTarget && (type == CommandType.RotateNow || type == CommandType.HoldPosition)
                    ? $" @({targetPos.X:0},{targetPos.Y:0})"
                    : "";
                string msg = $"▶ TRANSMITTING [{type.ToString().ToUpperInvariant()}{coordTag}] TO IGL...";
                if (_radioStatusLabel != null)
                {
                    _radioStatusLabel.Text = msg;
                    _radioStatusLabel.Modulate = EsportsUiTheme.AccentGold;
                    _statusHighlightSec = 3.8f;
                }
                TriggerBorderFlash(EsportsUiTheme.AccentCyan, 1.1f);
                CommandIssued?.Invoke(type, msg);
            }
            else
            {
                RejectWithFeedback("⏳ RADIO CHANNEL BUSY — WAIT FOR COOLDOWN");
            }
        }

        private void RejectWithFeedback(string reason)
        {
            if (_radioStatusLabel != null)
            {
                _radioStatusLabel.Text = reason;
                _radioStatusLabel.Modulate = EsportsUiTheme.AccentCrimson;
                _statusHighlightSec = 2.6f;
            }
            TriggerBorderFlash(EsportsUiTheme.AccentCrimson, 0.65f);
            CommandRejected?.Invoke(reason);
        }

        private void TriggerBorderFlash(Color color, float durationSec)
        {
            _borderFlashColor = color;
            _borderFlashSec = durationSec;
            if (_panelStyle != null)
            {
                _panelStyle.BorderColor = color;
            }
        }

        private void OnSimEvent(SimEvent ev)
        {
            if (_simHost == null || ev.SquadIdx != _simHost.PlayerSquadIndex) return;

            if (ev.Type == SimEventType.CommandExecuted && _radioStatusLabel != null)
            {
                bool complied = ev.Value >= 0.5f;
                CommandType cType = (CommandType)ev.ExtraCode;
                if (complied)
                {
                    _radioStatusLabel.Text = $"✓ IGL COMPLIED: EXECUTING [{cType.ToString().ToUpperInvariant()}]";
                    _radioStatusLabel.Modulate = EsportsUiTheme.AccentGreen;
                    TriggerBorderFlash(EsportsUiTheme.AccentGreen, 1.4f);
                }
                else
                {
                    _radioStatusLabel.Text = $"! IGL OVERRODE [{cType.ToString().ToUpperInvariant()}] (LOCAL THREAT)";
                    _radioStatusLabel.Modulate = EsportsUiTheme.AccentCrimson;
                    TriggerBorderFlash(EsportsUiTheme.AccentCrimson, 1.4f);
                }
                _statusHighlightSec = 5.0f;
            }
        }

        public void UpdatePanel(InterpolatedFrameState frame, float dt)
        {
            if (_simHost == null) return;

            if (_borderFlashSec > 0f)
            {
                _borderFlashSec = Math.Max(0f, _borderFlashSec - dt);
                if (_borderFlashSec <= 0f && _panelStyle != null)
                {
                    _panelStyle.BorderColor = EsportsUiTheme.AccentGold;
                }
            }

            int pIdx = _simHost.PlayerSquadIndex;
            if ((uint)pIdx >= SnapshotBuffer.MaxSquads) return;

            SquadSnapshot sq = frame.Squads[pIdx];
            if (_budgetLabel != null)
            {
                _budgetLabel.Text = $"CALLS: {sq.CommandsRemaining}/5";
                _budgetLabel.Modulate = sq.CommandsRemaining > 0
                    ? EsportsUiTheme.AccentGold
                    : EsportsUiTheme.AccentCrimson;
            }

            if (_cooldownBar != null)
            {
                _cooldownBar.Value = sq.CommandCooldownSec;
            }

            if (_statusHighlightSec > 0f)
            {
                _statusHighlightSec -= dt;
            }
            else if (_radioStatusLabel != null)
            {
                if (sq.AliveCount <= 0)
                {
                    _radioStatusLabel.Text = "× SQUAD ELIMINATED · RADIO OFFLINE";
                    _radioStatusLabel.Modulate = EsportsUiTheme.AccentCrimson;
                }
                else if (sq.CommandCooldownSec > 0.1f)
                {
                    _radioStatusLabel.Text = $"⏳ RADIO COOLDOWN: {sq.CommandCooldownSec:0.0}s";
                    _radioStatusLabel.Modulate = EsportsUiTheme.TextSecondary;
                }
                else if (sq.HasActiveCommand)
                {
                    string compState = sq.ActiveCommandCompliant ? "COMPLIED" : "MODIFIED";
                    _radioStatusLabel.Text = $"★ ACTIVE ORDER: [{sq.ActiveCommand.ToString().ToUpperInvariant()}] ({compState})";
                    _radioStatusLabel.Modulate = EsportsUiTheme.AccentGreen;
                }
                else
                {
                    _radioStatusLabel.Text = "● CHANNEL READY · PRESS [1-6] OR R-CLICK 3D MAP";
                    _radioStatusLabel.Modulate = EsportsUiTheme.AccentCyan;
                }
            }

            bool readyToIssue = !_simHost.IsFinished &&
                                !_simHost.IsReplaying &&
                                sq.AliveCount > 0 &&
                                sq.CommandsRemaining > 0 &&
                                sq.CommandCooldownTicks <= 0;

            for (int i = 0; i < _cmdButtons.Length; i++)
            {
                Button? btn = _cmdButtons[i];
                if (btn == null) continue;

                bool isActiveOrder = sq.HasActiveCommand && sq.ActiveCommand == _buttonCommandTypes[i];
                btn.Text = isActiveOrder ? $"★ {_buttonBaseLabels[i]}" : _buttonBaseLabels[i];

                if (isActiveOrder)
                {
                    btn.Modulate = new Color(0.25f, 1.0f, 0.65f, 1.0f);
                }
                else if (readyToIssue)
                {
                    btn.Modulate = Colors.White;
                }
                else
                {
                    // Keep clickable so user gets explicit feedback toast if clicked during cooldown
                    btn.Modulate = new Color(0.58f, 0.62f, 0.70f, 0.78f);
                }
            }
        }
    }
}
