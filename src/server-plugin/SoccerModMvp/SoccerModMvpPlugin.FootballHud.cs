using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CS2UIKit;

namespace SoccerModMvp;

// Football mode HUD (owner 2026-09-26): with empty hands CS2 draws no
// crosshair, so this layout (soccermod_football.xml, shipped for the test
// server in Workshop item 3807839299) draws one: a dot and a ring, green
// while the ball is kickable, red while overhitting, gold while the keeper
// holds the ball. Under it the charge bar, the kick's name and curl arrows.
// Without the layout mounted, the charge shows in the centre text box.
// Per player: Settings - Football HUD (default on).
public sealed partial class SoccerModMvpPlugin
{
    internal const string FootballHudLayout = "panorama/layout/custom_game/soccermod_football.xml";
    private const string FootballHudCompiled = "panorama/layout/custom_game/soccermod_football.vxml_c";
    private Panel? _footballHudPanel;
    private bool? _footballHudMounted;
    private double _footballHudMountedCheckedAt = -1000;

    private sealed class FootballHudState
    {
        public string? Mode;
        public bool? Reach;
        public string? Curl;
        public int Fill = -1;
        public string? Label;
    }

    private readonly Dictionary<int, FootballHudState> _footballHud = new();

    // Called next to SprintHudOnLoad: the UI kit must be initialised first.
    private void FootballHudOnLoad()
    {
        _footballHudPanel = new Panel(FootballHudLayout, new PanelOptions { Root = "sm_fb", ShownClass = "shown", CaptureInput = false });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => _footballHud.Clear());
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot => _footballHud.Remove(slot));
        RegisterListener<Listeners.OnMapStart>(_ => _footballHudMounted = null);
    }

    private bool FootballHudMounted
    {
        get
        {
            var now = Server.TickedTime;
            if (_footballHudMounted is null || now - _footballHudMountedCheckedAt > 30)
            {
                _footballHudMountedCheckedAt = now;
                try { _footballHudMounted = MountedAddonFiles().Contains(FootballHudCompiled); }
                catch { _footballHudMounted = false; }
            }
            return _footballHudMounted == true && _footballHudPanel is not null;
        }
    }

    private bool FootballHudWanted(CCSPlayerController player) =>
        !_menuParity.FootballHud.TryGetValue(player.AuthorizedSteamID?.SteamId64 ?? 0, out var on) || on;

    private void SetFootballHud(CCSPlayerController player, bool on)
    {
        if (player.AuthorizedSteamID?.SteamId64 is not { } id) return;
        _menuParity.FootballHud[id] = on;
        SaveJsonAtomic(MenuParityFile, _menuParity);
    }

    // !menu - Help - Football controls.
    private static void PrintFootballControls(CCSPlayerController player)
    {
        foreach (var line in new[]
        {
            "Hold a mouse button to charge, release to kick. The bar under the crosshair shows the power.",
            "Left = shot, Ctrl + left = chip. Right = ground pass, Ctrl + right = lofted pass.",
            "Hold A or D while releasing to curl the ball left or right. Shift + left = finesse (softer, more curl).",
            "Holding past full power overhits: the ball rises and sprays. A ball above your chest is a header.",
            "R = first touch: the next contact stops the ball at your feet. Green crosshair = the ball is kickable.",
            "Keeper (!gk, in your box): right click catches (fast balls are parried), Space + A/D dives.",
            "Ball in hands (6 s max): right click throws, left click punts (both charged). Leaving the box drops it.",
        })
            player.PrintToChat($" \x04[Football]\x01 {line}");
    }

    private void FootballHudHideAll()
    {
        _footballHudPanel?.HideAll();
        _footballHud.Clear();
    }

    private void FootballHudOnTick()
    {
        if (Server.TickCount % 2 != 0) return;
        var panorama = FootballHudMounted;
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot) continue;
            var pawn = player.PlayerPawn.Value;
            var alive = IsEligiblePlayer(player) && pawn is { IsValid: true };
            var wanted = alive && FootballHudWanted(player);
            var view = alive ? FootballHudView(player, pawn!) : default;
            if (panorama) DrawFootballHud(player, wanted, view);
            else if (wanted && view.Charging && Server.TickCount % 4 == 0)
                player.PrintToCenterHtml(FootballKickRules.BarHtml(view.Label, view.Charge, view.Overhit, view.CurlSide), 1);
        }
    }

    private readonly record struct FootballView(bool Reach, bool Charging, bool Holding, string Label, float Charge, float Overhit, int CurlSide);

    private FootballView FootballHudView(CCSPlayerController player, CCSPlayerPawn pawn)
    {
        var reach = FootballBallInReach(pawn);
        var now = Server.TickedTime;
        if (_keeperHold is { } hold && hold.Slot == player.Slot)
        {
            if (hold.Charge is { } kc)
                return new(reach, true, true, kc.Secondary ? "THROW" : "PUNT", FootballKickRules.ChargeFraction(now - kc.Started), 0, 0);
            var left = Math.Max(0, FootballKeeperRules.HoldSeconds - (now - hold.Since));
            return new(reach, false, true, $"IN HANDS  {left:0.0}s", (float)(left / FootballKeeperRules.HoldSeconds), 0, 0);
        }
        if (_footballCharges.TryGetValue(player.Slot, out var charge))
        {
            var kick = ReadFootballKick(player, pawn, charge);
            var header = BallAboveChest(pawn);
            return new(reach, true, false, FootballKickRules.Label(kick.Style, header), kick.Charge, kick.Overhit, Math.Sign(kick.Curl));
        }
        return new(reach, false, false, "", 0, 0, 0);
    }

    private void DrawFootballHud(CCSPlayerController player, bool wanted, FootballView view)
    {
        var panel = _footballHudPanel!;
        if (!wanted)
        {
            if (panel.IsOpen(player)) panel.Hide(player);
            _footballHud.Remove(player.Slot);
            return;
        }
        if (!_footballHud.TryGetValue(player.Slot, out var state)) _footballHud[player.Slot] = state = new FootballHudState();
        if (!panel.IsOpen(player))
        {
            panel.Show(player);
            if (!panel.IsOpen(player)) return; // layout entity not there yet
            _footballHud[player.Slot] = state = new FootballHudState();
        }
        var mode = view.Holding && !view.Charging ? "hold" : view.Overhit > 0 ? "overhit" : view.Charging ? "charging" : "idle";
        if (state.Mode != mode) { panel.SetVariant(player, "sm_fb", "m-", mode); state.Mode = mode; }
        if (state.Reach != view.Reach) { panel.SetClass(player, "sm_fb", "reach", view.Reach); state.Reach = view.Reach; }
        var curl = view.CurlSide > 0 ? "left" : view.CurlSide < 0 ? "right" : "none";
        if (state.Curl != curl) { panel.SetVariant(player, "sm_fb", "curl-", curl); state.Curl = curl; }
        var fill = FootballKickRules.FillStep(view.Charge);
        if (state.Fill != fill) { panel.SetVariant(player, "sm_fb_fill", "fill-", fill.ToString()); state.Fill = fill; }
        if (state.Label != view.Label) { panel.SetText(player, "sm_fb_label", view.Label); state.Label = view.Label; }
    }

    // The kick's own reach and cone test (TryApplyPrimaryKnifeKick), for the crosshair colour.
    private bool FootballBallInReach(CCSPlayerPawn pawn)
    {
        if (pawn.AbsOrigin is not { } feet || KeeperHoldsBall) return false;
        var eye = new Vector(feet.X, feet.Y, feet.Z + pawn.ViewOffset.Z);
        var yaw = pawn.EyeAngles.Y * MathF.PI / 180.0f;
        var forward = KeeperForward(pawn.EyeAngles, horizontalOnly: false);
        foreach (var target in PlayableBalls())
        {
            var toBall = new Vector(target.Origin.X - eye.X, target.Origin.Y - eye.Y, target.Origin.Z - eye.Z);
            var distance = VectorSpeed(toBall);
            if (!float.IsFinite(distance) || distance <= 0.0001f || distance > _kickSurfaceReach + BallCollisionRadius) continue;
            if (BallContactMath.KickSphereInCone(Dot(forward, toBall) / distance, distance, BallCollisionRadius, _kickAimConeDegrees)
                && BallContactMath.HorizontalKickAim(N(toBall), yaw, BallCollisionRadius, _kickAimConeDegrees)) return true;
        }
        return false;
    }

    // Airborne ball at chest height or above within reach: the kick is a header.
    private bool BallAboveChest(CCSPlayerPawn pawn)
    {
        if (pawn.AbsOrigin is not { } feet || _ball is not { IsValid: true } || _ball.AbsOrigin is not { } ball) return false;
        var dx = ball.X - feet.X;
        var dy = ball.Y - feet.Y;
        return dx * dx + dy * dy < 140 * 140 && ball.Z > feet.Z + pawn.ViewOffset.Z - 24.0f;
    }
}
