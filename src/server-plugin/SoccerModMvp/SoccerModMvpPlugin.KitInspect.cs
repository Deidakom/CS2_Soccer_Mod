using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Cvars;

namespace SoccerModMvp;

// 2026-09-27 owner: "a way to look at my jersey from third person, front and
// back". Test server only: enabled by the flag file soccermod_thirdperson.enabled
// in the plugin directory (present on 27018 only).
//
// It uses CS2's own third-person camera (thirdperson / cam_idealyaw 180 for the
// front), which needs sv_cheats 1 - kept on here while the flag exists. The
// client refuses those commands from the server ("missing required FCVAR
// flag"), so !tp and the menu entry print a one-time console bind line instead.
// A server-side camera entity (ViewEntity) was tried and dropped: CS2 turns the
// player to face where it looks (the front view showed the back), it locks the
// mouse, and the camera entity shows as an ERROR model to others (why the old
// !tp was removed, test/pitch-boundary-links.test.js).
public sealed partial class SoccerModMvpPlugin
{
    private const string ThirdPersonFlagFile = "soccermod_thirdperson.enabled";
    private const string ThirdPersonBindLine =
        "alias tp_back \"thirdperson; crosshair 0; cam_idealyaw 0; cam_idealdist 110; cam_idealpitch 5\"; "
        + "alias tp_front \"thirdperson; crosshair 0; c_maxyaw 180; c_minyaw -180; cam_idealyaw 180; cam_idealdist 110; cam_idealpitch 5\"; "
        + "bind F6 tp_back; bind F7 tp_front; bind F8 \"firstperson; crosshair 1\"";
    private int _tpCheatsTick;

    private bool ThirdPersonAllowed => File.Exists(ConfigPath(ThirdPersonFlagFile));

    private void KitInspectOnLoad()
    {
        AddCommand("css_tp", "Test server: how to view yourself in third person (front/back).", (player, command) =>
        {
            if (player is not { IsValid: true }) return;
            if (!ThirdPersonAllowed) { command.ReplyToCommand("[SM] Third-person view is not enabled on this server."); return; }
            PrintThirdPersonHelp(player);
        });
    }

    private void PrintThirdPersonHelp(CCSPlayerController player)
    {
        player.PrintToChat(" [SM] Third person: F6 behind, F7 front, F8 normal view. One-time setup - paste this in your console:");
        player.PrintToConsole(ThirdPersonBindLine);
        player.PrintToChat($" {ThirdPersonBindLine}");
    }

    // !tp needs sv_cheats 1; kept on only where the test-server flag exists.
    private void KitInspectOnTick()
    {
        if (++_tpCheatsTick < 128) return;
        _tpCheatsTick = 0;
        if (ThirdPersonAllowed && ConVar.Find("sv_cheats") is { } cheats && !cheats.GetPrimitiveValue<bool>())
            Server.ExecuteCommand("sv_cheats 1");
    }

    private void AddKitInspectEntries(NumberMenu menu, CCSPlayerController viewer, Action<CCSPlayerController> reopen)
    {
        if (!ThirdPersonAllowed) return;
        menu.Add("Third person (front / back view): how to", p => { PrintThirdPersonHelp(p); reopen(p); });
    }
}
