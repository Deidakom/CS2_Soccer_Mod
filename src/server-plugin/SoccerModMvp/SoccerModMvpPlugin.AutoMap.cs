using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Cvars;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-26 owner (drop-in release): "copy the mod into the server and it's
// ready to use". A fresh server starts on whatever map its start line names
// (de_dust2 by default), so once per server process the plugin moves a
// non-soccer map to the SoccerMod stadium (Workshop 3361075564). Only once:
// an admin who later changes to another map on purpose is not overruled, and
// a failing Workshop download cannot loop. Admin: css_sm2_automap on|off.
//
// Workshop maps also refuse two of our cfg lines ("DISALLOWED WORKSHOP
// CONVAR"), so the plugin sets them itself on every map start.
public sealed partial class SoccerModMvpPlugin
{
    private bool _autoMapTried;

    // No timers here: an empty server hibernates and its timers stop, so on a
    // fresh server a delayed check only ran once the first player joined
    // (found by the drop-in fresh-server test, 2026-09-26). The command is
    // queued straight from map start; the console buffer still runs while
    // the server hibernates.
    private void AutoMapOnLoad()
    {
        AddCommand("css_sm2_automap", "Admin: move a fresh server to the SoccerMod stadium once (on|off).", OnAutoMapCommand);
        RegisterListener<Listeners.OnMapStart>(AutoMapCheck);
        Logger.LogInformation("[SM2DIAG] native_bridge installed={Installed} (ball spin {State})", NativeBridgeInstalled, NativeBridgeInstalled ? "on" : "off");
        // The plugin can also load after the first map has already started.
        if (!string.IsNullOrEmpty(Server.MapName)) AutoMapCheck(Server.MapName);
    }

    private static bool IsSoccerMap(string map) =>
        map.Contains("soccer", StringComparison.OrdinalIgnoreCase);

    private void AutoMapCheck(string map)
    {
        if (string.IsNullOrEmpty(map)) return;
        ApplyWorkshopBlockedCvars();
        if (_autoMapTried || !_menuParity.AutoMap) return;
        _autoMapTried = true;
        if (IsSoccerMap(map)) return;
        // A start line that names a Workshop map boots on +map first and loads
        // that map right after - the operator chose it, so stay out of the way
        // (our own servers, host panels). Found live 2026-09-26: the map-test
        // server would otherwise race between its test map and the stadium.
        if (ServerCommandLine().Contains("host_workshop_map", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation("[SM2DIAG] automap skipped: the start line loads a Workshop map");
            return;
        }
        Logger.LogInformation("[SM2DIAG] automap from={Map} to=workshop:{Id}", map, LegacyStadiumWorkshopId);
        Server.ExecuteCommand($"game_type 0; game_mode 0; host_workshop_map {LegacyStadiumWorkshopId}");
    }

    // The game's own start line: .NET runs hosted inside cs2, so its argument
    // list is not cs2's. Linux: /proc/self/cmdline; Windows: the process line.
    private static string ServerCommandLine()
    {
        try
        {
            return OperatingSystem.IsLinux()
                ? File.ReadAllText("/proc/self/cmdline").Replace('\0', ' ')
                : Environment.CommandLine;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    // Landing sound off (a soccer mod has constant jumps) and no radar rings
    // for sounds - the same values as soccermod_server.cfg.
    private void ApplyWorkshopBlockedCvars()
    {
        SetCvar("sv_min_jump_landing_sound", "100000");
        SetCvar("snd_disable_radar_visualize", "true");
    }

    private void SetCvar(string name, string value)
    {
        if (ConVar.Find(name) is not { } cvar) return;
        try
        {
            switch (cvar.Type)
            {
                case ConVarType.Bool: cvar.SetValue(value is "1" or "true"); break;
                case ConVarType.Int32: cvar.SetValue(int.Parse(value)); break;
                case ConVarType.Float32: cvar.SetValue(float.Parse(value, System.Globalization.CultureInfo.InvariantCulture)); break;
                default: Server.ExecuteCommand($"{name} {value}"); break;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning("[SM2DIAG] cvar_set_failed name={Name} type={Type} error={Error}", name, cvar.Type, ex.Message);
        }
    }

    private void OnAutoMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "";
        if (arg is "on" or "off")
        {
            _menuParity.AutoMap = arg == "on";
            SaveJsonAtomic(MenuParityFile, _menuParity);
        }
        command.ReplyToCommand($"[SM] Switch a fresh server to the stadium: {(_menuParity.AutoMap ? "on" : "off")} (usage: css_sm2_automap <on|off>)");
    }
}
