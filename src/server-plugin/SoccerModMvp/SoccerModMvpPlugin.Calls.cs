using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: football calls on V ("Pass here!", "Cross!" ...).
// CS2's V opens the client-side radio wheel: only the picked entry reaches
// the server (measured with the key probe), and a server cannot change the
// wheel. So V opens this menu through a bind (bind v css_calls, part of the
// !binds line); !calls and the main menu work without it. A call goes to the
// caller's team chat and shows above the caller's head for teammates.
public sealed partial class SoccerModMvpPlugin
{
    // Owner's list and order (2026-09-25): at most 7 so they fit one page
    // (no page flipping mid-game), in English.
    internal static readonly string[] FootballCalls =
    {
        "Well done!", "Pass here!", "Cross!", "I'm free!", "Pass back!", "Nice pass!", "Sorry!",
    };

    // Owner's voice lines, one per call in the same order; sound events in
    // soundevents/soccermod_calls.vsndevts (Workshop item 3797479770). The
    // caller's team hears them like a radio command.
    internal const string CallSoundEventsFile = "soundevents/soccermod_calls.vsndevts";
    internal static readonly string[] CallSoundEvents =
    {
        "SoccerMod.Call.WellDone", "SoccerMod.Call.PassHere", "SoccerMod.Call.Cross", "SoccerMod.Call.ImFree",
        "SoccerMod.Call.PassBack", "SoccerMod.Call.NicePass", "SoccerMod.Call.Sorry",
    };

    private const double CallCooldownSeconds = 1.5;
    private const int CallBurstLimit = 3;
    private const double CallBurstWindowSeconds = 10.0;
    private readonly Dictionary<int, Queue<double>> _callTimes = new();
    private const double CallMarkerSeconds = 2.5;
    private const float CallMarkerHeight = 100.0f;

    private sealed class CallMarker
    {
        public required CPointWorldText Text;
        public required uint Pawn;
        public required CsTeam Team;
        public required double Until;
    }

    private readonly Dictionary<int, double> _lastCall = new();
    private readonly Dictionary<int, CallMarker> _callMarkers = new();

    private void CallsOnLoad()
    {
        AddCommand("css_calls", "Football calls for your team (bind v css_calls).", (player, _) =>
        {
            if (player is { IsValid: true, IsBot: false }) OpenCallsMenu(player);
        });
        // 2026-09-25 owner: no map pings on middle mouse. The client sends
        // player_ping (seen with the key probe); dropping it means no marker.
        AddCommandListener("player_ping", (_, _) => HookResult.Handled, HookMode.Pre);
        RegisterListener<Listeners.CheckTransmit>(CallsCheckTransmit);
        RegisterListener<Listeners.OnClientDisconnect>(slot => { RemoveCallMarker(slot); _lastCall.Remove(slot); _callTimes.Remove(slot); });
        RegisterListener<Listeners.OnMapEnd>(() => _callMarkers.Clear());
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest => manifest.AddResource(CallSoundEventsFile));
    }

    private void OpenCallsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Calls", Key = "calls" };
        foreach (var call in FootballCalls)
        {
            var text = call;
            menu.Add(text, p => SendFootballCall(p, text));
        }
        OpenNumberMenu(player, menu);
    }

    private void SendFootballCall(CCSPlayerController player, string call)
    {
        if (!player.IsValid || player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)) return;
        var now = (double)Server.TickedTime;
        if (_lastCall.TryGetValue(player.Slot, out var last) && now - last < CallCooldownSeconds) return;
        // Radio spam protection: at most CallBurstLimit calls per CallBurstWindowSeconds.
        if (!_callTimes.TryGetValue(player.Slot, out var recent)) _callTimes[player.Slot] = recent = new Queue<double>();
        while (recent.Count > 0 && now - recent.Peek() >= CallBurstWindowSeconds) recent.Dequeue();
        if (recent.Count >= CallBurstLimit)
        {
            var wait = Math.Ceiling(CallBurstWindowSeconds - (now - recent.Peek()));
            player.PrintToChat($" {ChatColors.Green}[Call]{ChatColors.Default} Calls on cooldown ({wait:0}s).");
            return;
        }
        recent.Enqueue(now);
        _lastCall[player.Slot] = now;

        var message = $" {ChatColors.Green}[Call]{ChatColors.Default} {player.PlayerName}: {ChatColors.Gold}{call}";
        foreach (var mate in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team == player.Team))
            mate.PrintToChat(message);
        var index = Array.IndexOf(FootballCalls, call);
        if (index >= 0 && player.PlayerPawn.Value is { IsValid: true } pawn)
            pawn.EmitSound(CallSoundEvents[index], SoundRecipients(SoccerSound.Radio, p => p.Team == player.Team));
        ShowCallMarker(player, call, now);
        Logger.LogInformation("[SM2DIAG] football_call slot={Slot} team={Team} call={Call}", player.Slot, player.Team, call);
    }

    private void ShowCallMarker(CCSPlayerController player, string call, double now)
    {
        if (player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn)) return;
        if (_callMarkers.TryGetValue(player.Slot, out var marker) && marker.Text.IsValid && marker.Pawn == pawn.EntityHandle.Raw)
        {
            marker.Text.MessageText = call;
            Utilities.SetStateChanged(marker.Text, "CPointWorldText", "m_messageText");
            marker.Until = now + CallMarkerSeconds;
            marker.Team = player.Team;
            return;
        }
        RemoveCallMarker(player.Slot);
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is null || !text.IsValid) return;
        text.Entity!.Name = $"sm2_call_{player.Slot}";
        text.MessageText = call;
        text.FontName = "Arial";
        text.FontSize = 40.0f;
        text.WorldUnitsPerPx = NameTagWorldUnitsPerPx;
        text.Fullbright = true;
        text.Enabled = true;
        text.DrawBackground = false;
        text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
        text.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
        text.Color = Color.FromArgb(255, 255, 215, 0);
        try
        {
            text.DispatchSpawn();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] call_marker_spawn_failed slot={Slot}", player.Slot);
            if (text.IsValid) text.Remove();
            return;
        }
        _callMarkers[player.Slot] = new CallMarker { Text = text, Pawn = pawn.EntityHandle.Raw, Team = player.Team, Until = now + CallMarkerSeconds };
        if (pawn.AbsOrigin is { } origin) text.Teleport(new Vector(origin.X, origin.Y, origin.Z + CallMarkerHeight), _nameTagAngles);
    }

    private void CallsOnTick()
    {
        if (_callMarkers.Count == 0) return;
        var now = (double)Server.TickedTime;
        foreach (var (slot, marker) in _callMarkers.ToList())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (now > marker.Until || !marker.Text.IsValid || player?.PlayerPawn.Value is not { IsValid: true } pawn
                || pawn.EntityHandle.Raw != marker.Pawn || !IsAlive(pawn) || pawn.AbsOrigin is not { } origin)
            {
                RemoveCallMarker(slot);
                continue;
            }
            marker.Text.Teleport(new Vector(origin.X, origin.Y, origin.Z + CallMarkerHeight), _nameTagAngles);
        }
    }

    private void RemoveCallMarker(int slot)
    {
        if (!_callMarkers.Remove(slot, out var marker)) return;
        try
        {
            if (marker.Text.IsValid) marker.Text.Remove();
        }
        catch
        {
            // Best effort during map end/unload.
        }
    }

    // Teammates (and spectators) see a call; the caller and the other team do
    // not. Only TransmitEntities.Remove is used (Add has crashed this server).
    private void CallsCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_callMarkers.Count == 0) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true }) continue;
            foreach (var (slot, marker) in _callMarkers)
            {
                if (!marker.Text.IsValid) continue;
                if (slot == receiver.Slot
                    || (receiver.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist && receiver.Team != marker.Team))
                    info.TransmitEntities.Remove(marker.Text);
            }
        }
    }
}
