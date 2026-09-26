using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CS2UIKit;

namespace SoccerModMvp;

// 2026-09-25 owner: a proper sprint bar instead of the text line in the
// centre hint box. A custom_hud_layout (soccermod_sprint.xml, Workshop item
// 3797479770) at the bottom centre: blue and draining while sprinting, red
// with the seconds left while cooling down, green READY, then it fades.
// SprintBarOnTick decides visibility exactly as before (preference, menu
// open, keeper sprint); this only draws, and only sends what changed. The old
// text bar stays available: css_sm2sprint_hud text.
public sealed partial class SoccerModMvpPlugin
{
    internal const string SprintHudLayout = "panorama/layout/custom_game/soccermod_sprint.xml";
    private const double SprintHudReadyHoldSeconds = 1.2;
    private Panel? _sprintHudPanel;

    private sealed class SprintHudState
    {
        public bool? Faded;
        public string? Label;
        public double ReadySince = double.NaN;
        public bool WasVisible;
    }

    private readonly Dictionary<int, SprintHudState> _sprintHud = new();

    private bool SprintHudPanorama => _menuParity.SprintHudPanorama && _sprintHudPanel is not null;

    // After ClickMenuOnLoad: the UI kit must be initialised first.
    private void SprintHudOnLoad()
    {
        _sprintHudPanel = new Panel(SprintHudLayout, new PanelOptions { Root = "sm_sprint", ShownClass = "shown", CaptureInput = false });
        AddCommand("css_sm2sprint_hud", "Admin: sprint bar style (panorama|text).", OnSprintHudCommand);
        // A round restart or a panel rebuild gives the entity none of our classes.
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(ResetSprintHud);
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot => _sprintHud.Remove(slot));
    }

    private void OnSprintHudCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "";
        if (arg is "panorama" or "text")
        {
            _menuParity.SprintHudPanorama = arg == "panorama";
            SaveJsonAtomic(MenuParityFile, _menuParity);
            if (_menuParity.SprintHudPanorama) ClearSprintBars();
            else
            {
                _sprintHudPanel?.HideAll();
                ResetSprintHud();
            }
        }
        command.ReplyToCommand($"[SM] Sprint bar: {(_menuParity.SprintHudPanorama ? "panorama" : "text")} (usage: css_sm2sprint_hud <panorama|text>)");
    }

    private void ResetSprintHud()
    {
        _sprintHud.Clear();
        if (_sprintHudPanel is null) return;
        foreach (var player in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
        {
            _sprintHudPanel.SetVariant(player, "sm_sprint", "", null);
            _sprintHudPanel.SetVariant(player, "sm_sprint_fill", "fill-", null);
        }
    }

    // amount 0-100, label "SPRINT" / "4.1 s" / "READY" (or a percentage in
    // stamina mode); visible = what the text bar would have shown.
    private void DrawSprintHud(CCSPlayerController player, float amount, bool active, bool visible, string cooldownLabel, bool keeper = false, bool libero = false)
    {
        var panel = _sprintHudPanel!;
        var now = (double)Server.TickedTime;
        if (!_sprintHud.TryGetValue(player.Slot, out var state)) _sprintHud[player.Slot] = state = new SprintHudState();
        var full = !active && (!float.IsFinite(amount) || amount >= 99.95f);
        if (!full) state.ReadySince = double.NaN;
        else if (double.IsNaN(state.ReadySince)) state.ReadySince = now;
        // "Fades out a moment after ready": keep a just-refilled bar up briefly.
        var show = visible || (full && state.WasVisible && now - state.ReadySince < SprintHudReadyHoldSeconds);
        if (visible) state.WasVisible = true;
        else if (!show) state.WasVisible = false;

        if (!panel.IsOpen(player))
        {
            panel.Show(player);
            if (!panel.IsOpen(player)) return; // layout entity not there yet
            state.Faded = null;
            state.Label = null;
        }
        if (state.Faded != !show)
        {
            panel.SetClass(player, "sm_sprint", "faded", !show);
            state.Faded = !show;
        }
        if (!show) return;

        panel.SetVariant(player, "sm_sprint", "", libero ? "libero" : keeper ? "keeper" : active ? "sprinting" : full ? "ready" : "cooldown");
        panel.SetVariant(player, "sm_sprint_fill", "fill-", keeper ? "100" : SprintBarView.FillStep(amount).ToString());
        var label = libero ? "LIBERO \u00b7 UNLIMITED" : keeper ? "KEEPER \u00b7 UNLIMITED" : SprintBarView.HudLabel(amount, active, full, cooldownLabel);
        if (state.Label != label)
        {
            panel.SetText(player, "sm_sprint_label", label);
            state.Label = label;
        }
    }
}
