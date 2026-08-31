using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Own, lightweight admin module: no SourceMod on CS2, and no third-party
// CSSharp admin plugin (user's explicit call, 2026-08-29) - kick/ban/spec
// plus per-module permission flags (root/admin/ball/match/cap), mirroring
// the CS:S SoMoE-19 per-module admin grants. Two JSON files under
// ModuleDirectory (see SoccerModMvpPlugin.Config.cs): soccermod_admins.json
// and soccermod_bans.json. RCON/server console (player == null) always
// passes every gate, unchanged from the existing RequireServerConsole
// pattern used by the ball diagnostics commands.
public sealed partial class SoccerModMvpPlugin
{
    private const string AdminsFileName = "soccermod_admins.json";
    private const string BansFileName = "soccermod_bans.json";

    private AdminStore _adminStore = new();
    private BanStore _banStore = new();

    private sealed class AdminEntry
    {
        public ulong SteamId64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Flags { get; set; } = new();
    }

    private sealed class AdminStore
    {
        public int Version { get; set; } = 1;
        public List<AdminEntry> Admins { get; set; } = new();
    }

    private sealed class BanEntry
    {
        public ulong SteamId64 { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public ulong AddedBy { get; set; }
        public DateTime AddedAtUtc { get; set; }
        public DateTime? ExpiresAtUtc { get; set; }
    }

    private sealed class BanStore
    {
        public int Version { get; set; } = 1;
        public List<BanEntry> Bans { get; set; } = new();
    }

    private void AdminOnLoad()
    {
        _adminStore = LoadJsonOrNull<AdminStore>(AdminsFileName) ?? new AdminStore();
        if (!_adminStore.Admins.Any(a => a.Flags.Contains("root")))
        {
            Logger.LogWarning(
                "[SM2DIAG] no_root_admin_configured; use server console/RCON: css_admin_add <steamid64> root");
        }

        _banStore = LoadJsonOrNull<BanStore>(BansFileName) ?? new BanStore();

        RegisterListener<Listeners.OnClientAuthorized>(OnClientAuthorizedCheckBan);

        AddCommand("css_admin_add", "Root only: grant an admin flag to a player.", OnAdminAddCommand);
        AddCommand("css_admin_remove", "Root only: revoke a player's admin entry.", OnAdminRemoveCommand);
        AddCommand("css_admin_list", "List current admins.", OnAdminListCommand);
        AddCommand("css_kick", "Admin only: kick a player.", OnKickCommand);
        AddCommand("css_spec", "!spec me (anyone) or !spec all|<player> (admin).", OnSpecCommand);
        AddCommand("css_slay", "Admin only: slay a player.", OnSlayCommand);
        AddCommand("css_ban", "Admin only: ban a player (permanent unless minutes given).", OnBanCommand);
        AddCommand("css_unban", "Admin only: remove a ban by SteamID64.", OnUnbanCommand);
        AddCommand("css_banlist", "List current bans.", OnBanListCommand);

        Logger.LogInformation(
            "[SM2DIAG] admin_module_loaded admins={AdminCount} bans={BanCount}",
            _adminStore.Admins.Count,
            _banStore.Bans.Count);
    }

    private void SaveAdmins(string reason)
    {
        if (SaveJsonAtomic(AdminsFileName, _adminStore))
        {
            Logger.LogInformation("[SM2DIAG] admins_saved reason={Reason} count={Count}", reason, _adminStore.Admins.Count);
        }
    }

    private void SaveBans(string reason)
    {
        if (SaveJsonAtomic(BansFileName, _banStore))
        {
            Logger.LogInformation("[SM2DIAG] bans_saved reason={Reason} count={Count}", reason, _banStore.Bans.Count);
        }
    }

    private bool HasFlag(ulong steamId64, string flag)
    {
        if (steamId64 == 0)
        {
            return false;
        }

        foreach (var admin in _adminStore.Admins)
        {
            if (admin.SteamId64 != steamId64)
            {
                continue;
            }

            if (admin.Flags.Contains("root", StringComparer.OrdinalIgnoreCase)
                || admin.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    // Permission gate for the chat/console-invokable gameplay commands
    // (admin, ball panel, match, cap). RCON/server console always passes,
    // exactly like the existing RequireServerConsole used by the ball
    // diagnostics probes - those stay on RequireServerConsole unchanged.
    private bool RequirePermission(CCSPlayerController? player, CommandInfo command, string flag)
    {
        if (player is null)
        {
            return true;
        }

        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, flag))
        {
            return true;
        }

        command.ReplyToCommand("[SM] you do not have permission to use this command");
        return false;
    }

    private void OnClientAuthorizedCheckBan(int playerSlot, CounterStrikeSharp.API.Modules.Entities.SteamID steamId)
    {
        var ban = FindActiveBan(steamId.SteamId64);
        if (ban is null)
        {
            return;
        }

        var player = Utilities.GetPlayerFromSlot(playerSlot);
        Server.NextFrame(() =>
        {
            if (player is null || !player.IsValid || player.UserId is not { } userId)
            {
                return;
            }

            Server.ExecuteCommand($"kickid {userId} \"Banned: {ban.Reason}\"");
            Logger.LogInformation(
                "[SM2DIAG] ban_enforced steamid={SteamId} name={Name} reason={Reason}",
                ban.SteamId64,
                player.PlayerName,
                ban.Reason);
        });
    }

    private BanEntry? FindActiveBan(ulong steamId64)
    {
        var now = DateTime.UtcNow;
        // Lazily prune expired bans whenever we look one up - no timer needed.
        var expired = _banStore.Bans.Where(b => b.ExpiresAtUtc is { } exp && exp <= now).ToList();
        if (expired.Count > 0)
        {
            foreach (var entry in expired)
            {
                _banStore.Bans.Remove(entry);
            }
            SaveBans("expired_prune");
        }

        return _banStore.Bans.FirstOrDefault(b => b.SteamId64 == steamId64);
    }

    // Resolves a command target argument as #<userid>, an exact SteamID64,
    // or a unique case-insensitive name substring. Ambiguous/empty matches
    // are refused rather than guessed.
    private CCSPlayerController? ResolveTarget(CommandInfo command, int argIndex, out string error)
    {
        error = string.Empty;
        if (command.ArgCount <= argIndex)
        {
            error = "missing target argument";
            return null;
        }

        var arg = command.GetArg(argIndex);
        if (arg.StartsWith('#') && int.TryParse(arg.AsSpan(1), out var userId))
        {
            var byUserId = Utilities.GetPlayerFromUserid(userId);
            if (byUserId is { IsValid: true })
            {
                return byUserId;
            }

            error = $"no player with userid {userId}";
            return null;
        }

        if (ulong.TryParse(arg, out var steamId64) && steamId64 > 76561197960265728UL)
        {
            var bySteamId = Utilities.GetPlayerFromSteamId64(steamId64);
            if (bySteamId is { IsValid: true })
            {
                return bySteamId;
            }
            // Falls through: a SteamID64 that isn't currently connected is a
            // valid target for css_ban (offline ban), just not for kick/spec/slay.
            error = $"no connected player with SteamID64 {steamId64}";
            return null;
        }

        var matches = Utilities.GetPlayers()
            .Where(p => p.IsValid && p.PlayerName.Contains(arg, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 1)
        {
            return matches[0];
        }

        error = matches.Count == 0
            ? $"no connected player matches '{arg}'"
            : $"'{arg}' matches {matches.Count} players; be more specific";
        return null;
    }

    private void OnAdminAddCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "root"))
        {
            return;
        }

        if (command.ArgCount < 3 || !ulong.TryParse(command.GetArg(1), out var steamId64))
        {
            command.ReplyToCommand("[SM] usage: css_admin_add <steamid64> <flag> [name]");
            return;
        }

        var flag = command.GetArg(2).ToLowerInvariant();
        var name = command.ArgCount >= 4
            ? string.Join(' ', Enumerable.Range(3, command.ArgCount - 3).Select(command.GetArg))
            : $"steamid:{steamId64}";

        var entry = _adminStore.Admins.FirstOrDefault(a => a.SteamId64 == steamId64);
        if (entry is null)
        {
            entry = new AdminEntry { SteamId64 = steamId64, Name = name };
            _adminStore.Admins.Add(entry);
        }

        if (!entry.Flags.Contains(flag, StringComparer.OrdinalIgnoreCase))
        {
            entry.Flags.Add(flag);
        }

        SaveAdmins("admin_add");
        command.ReplyToCommand($"[SM] granted '{flag}' to {steamId64} ({entry.Name})");
    }

    private void OnAdminRemoveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "root"))
        {
            return;
        }

        if (command.ArgCount < 2 || !ulong.TryParse(command.GetArg(1), out var steamId64))
        {
            command.ReplyToCommand("[SM] usage: css_admin_remove <steamid64>");
            return;
        }

        var removed = _adminStore.Admins.RemoveAll(a => a.SteamId64 == steamId64);
        if (removed > 0)
        {
            SaveAdmins("admin_remove");
        }

        command.ReplyToCommand(removed > 0
            ? $"[SM] removed admin entry for {steamId64}"
            : $"[SM] no admin entry for {steamId64}");
    }

    private void OnAdminListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (_adminStore.Admins.Count == 0)
        {
            ReplyAdminList(player, command, "[SM] no admins configured");
            return;
        }

        foreach (var admin in _adminStore.Admins)
        {
            ReplyAdminList(player, command, $"[SM] {admin.SteamId64} ({admin.Name}): {string.Join(",", admin.Flags)}");
        }
    }

    private static void ReplyAdminList(CCSPlayerController? player, CommandInfo command, string text)
    {
        if (player is { IsValid: true })
        {
            var body = text.StartsWith("[SM] ", StringComparison.Ordinal) ? text[5..] : text;
            player.PrintToChat($" \x04[SM]\x01 {body}");
        }
        else
        {
            command.ReplyToCommand(text);
        }
    }

    private void OnKickCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        var target = ResolveTarget(command, 1, out var error);
        if (target is null || target.UserId is not { } userId)
        {
            command.ReplyToCommand($"[SM] {error}");
            return;
        }

        Server.ExecuteCommand($"kickid {userId}");
        Logger.LogInformation("[SM2DIAG] admin_kick target={Name} by={By}", target.PlayerName, player?.PlayerName ?? "RCON");
        command.ReplyToCommand($"[SM] kicked {target.PlayerName}");
    }

    // SoMoE parity: "!spec me" is public/self-service (any player can bench
    // themselves any time), "!spec all"/"!spec <name>" move someone else and
    // need admin.
    private void OnSpecCommand(CCSPlayerController? player, CommandInfo command)
    {
        var arg = command.ArgCount >= 2 ? command.GetArg(1) : "me";

        if (arg.Equals("me", StringComparison.OrdinalIgnoreCase))
        {
            if (player is null)
            {
                command.ReplyToCommand("[SM] this command is for in-game players");
                return;
            }

            player.ChangeTeam(CsTeam.Spectator);
            command.ReplyToCommand("[SM] moved you to spectator");
            return;
        }

        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (arg.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            var moved = 0;
            foreach (var p in Utilities.GetPlayers())
            {
                if (p.IsValid && p.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist)
                {
                    p.ChangeTeam(CsTeam.Spectator);
                    moved++;
                }
            }

            command.ReplyToCommand($"[SM] moved {moved} player(s) to spectator");
            return;
        }

        var target = ResolveTarget(command, 1, out var error);
        if (target is null)
        {
            command.ReplyToCommand($"[SM] {error}");
            return;
        }

        target.ChangeTeam(CsTeam.Spectator);
        command.ReplyToCommand($"[SM] moved {target.PlayerName} to spectator");
    }

    private void OnSlayCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        var target = ResolveTarget(command, 1, out var error);
        if (target is null)
        {
            command.ReplyToCommand($"[SM] {error}");
            return;
        }

        target.PlayerPawn.Value?.CommitSuicide(false, true);
        command.ReplyToCommand($"[SM] slayed {target.PlayerName}");
    }

    private void OnBanCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount < 2)
        {
            command.ReplyToCommand("[SM] usage: css_ban <target|steamid64> [minutes] [reason...]");
            return;
        }

        ulong steamId64;
        string name;
        var target = ResolveTarget(command, 1, out var resolveError);
        if (target is not null)
        {
            steamId64 = target.AuthorizedSteamID?.SteamId64 ?? 0UL;
            name = target.PlayerName;
            if (steamId64 == 0UL)
            {
                command.ReplyToCommand("[SM] target is not yet Steam-authorized, try again in a moment");
                return;
            }
        }
        else if (ulong.TryParse(command.GetArg(1), out steamId64))
        {
            name = $"steamid:{steamId64}";
        }
        else
        {
            command.ReplyToCommand($"[SM] {resolveError}");
            return;
        }

        var minutes = 0.0;
        var reasonStartArg = 2;
        if (command.ArgCount >= 3 && double.TryParse(command.GetArg(2), out minutes))
        {
            reasonStartArg = 3;
        }

        var reason = command.ArgCount > reasonStartArg
            ? string.Join(' ', Enumerable.Range(reasonStartArg, command.ArgCount - reasonStartArg).Select(command.GetArg))
            : "no reason given";

        _banStore.Bans.RemoveAll(b => b.SteamId64 == steamId64);
        _banStore.Bans.Add(new BanEntry
        {
            SteamId64 = steamId64,
            Name = name,
            Reason = reason,
            AddedBy = player?.AuthorizedSteamID?.SteamId64 ?? 0UL,
            AddedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = minutes > 0 ? DateTime.UtcNow.AddMinutes(minutes) : null,
        });
        SaveBans("ban_added");

        if (target is { UserId: { } userId })
        {
            Server.ExecuteCommand($"kickid {userId} \"Banned: {reason}\"");
        }

        command.ReplyToCommand(minutes > 0
            ? $"[SM] banned {name} ({steamId64}) for {minutes} minutes: {reason}"
            : $"[SM] banned {name} ({steamId64}) permanently: {reason}");
    }

    private void OnUnbanCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount < 2 || !ulong.TryParse(command.GetArg(1), out var steamId64))
        {
            command.ReplyToCommand("[SM] usage: css_unban <steamid64>");
            return;
        }

        var removed = _banStore.Bans.RemoveAll(b => b.SteamId64 == steamId64);
        if (removed > 0)
        {
            SaveBans("unban");
        }

        command.ReplyToCommand(removed > 0
            ? $"[SM] unbanned {steamId64}"
            : $"[SM] no ban found for {steamId64}");
    }

    private void OnBanListCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (_banStore.Bans.Count == 0)
        {
            ReplyAdminList(player, command, "[SM] no active bans");
            return;
        }

        foreach (var ban in _banStore.Bans)
        {
            var expiry = ban.ExpiresAtUtc is { } exp ? exp.ToString("u") : "never";
            ReplyAdminList(player, command, $"[SM] {ban.SteamId64} ({ban.Name}) reason={ban.Reason} expires={expiry}");
        }
    }
}
