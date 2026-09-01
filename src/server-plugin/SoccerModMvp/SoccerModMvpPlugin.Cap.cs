using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using Timer = CounterStrikeSharp.API.Modules.Timers.Timer;

namespace SoccerModMvp;

// 2026-09-01 user request: the cap menu 1:1 as SoMoE-19 did it
// (soccer_mod/modules/cap.sp), no extra features. The earlier pool/draft
// port (!join/!draft/!capcancel) was dead code anyway - CapOnLoad was never
// wired into Load(), so the menu only ever showed the website hint.
//
// SoMoE flow: "Put all players to spectator" -> "Add random player" (one per
// side) -> "Start cap fight (<weapon>)": everyone alive is frozen, 3-2-1,
// FIGHT with the chosen weapon; a kill makes the killer the picker and the
// killer/victim the two captains; the round-end then opened the winner's
// pick menu, and the captains alternate picks from the spectators
// ("[Join Nr] Name [Positions]") until (matchMaxPlayers - 1) * 2 picks are
// used up. Engine adaptations, nothing more: mp_ignore_round_win_conditions
// is 1 here so the fight ends on the first kill that leaves a side with no
// alive fighters (there is no round end to wait for); godmode/health
// refill are bypassed only for the fight's participants
// (OnPlayerTakeDamagePre / Health.cs); respawn-on-death is suppressed for
// the fight so a death actually sticks (the same trick HandleWarmupGoal
// uses). Not ported because their backing systems do not exist here:
// password rotation (RandPass), the first12Set pick rule, ForfeitCapMode.
public sealed partial class SoccerModMvpPlugin
{
    // SoMoE globals.sp matchMaxPlayers default.
    private const int CapMatchMaxPlayers = 6;
    private const float CapFightCountdownSeconds = 3.0f;

    private sealed record CapWeaponOption(string Key, string Label, string Entity);

    // cap.sp OpenWeaponMenu, same order and labels; CS2 entity equivalents
    // for the weapons CS2 no longer ships under the CS:S name.
    private static readonly CapWeaponOption[] CapWeapons =
    {
        new("knife", "Knife", "weapon_knife"),
        new("glock", "Glock 18", "weapon_glock"),
        new("usp", "USP Tactical", "weapon_usp_silencer"),
        new("p228", "P228", "weapon_p250"),
        new("deagle", "Desert Eagle .50", "weapon_deagle"),
        new("57", "Five-seveN", "weapon_fiveseven"),
        new("dual", "Dual Elite Berettas", "weapon_elite"),
        new("mac10", "MAC10", "weapon_mac10"),
        new("tmp", "TMP", "weapon_mp9"),
        new("mp5", "MP5 Navy", "weapon_mp5sd"),
        new("ump", "UMP", "weapon_ump45"),
        new("p90", "P90", "weapon_p90"),
        new("m3", "M3 Super 90", "weapon_nova"),
        new("xm1014", "XM1014", "weapon_xm1014"),
        new("galil", "Galil", "weapon_galilar"),
        new("famas", "FAMAS", "weapon_famas"),
        new("ak47", "AK47", "weapon_ak47"),
        new("m4a1", "M4A1 Carbine", "weapon_m4a1"),
        new("sg552", "SG-552 Commando", "weapon_sg556"),
        new("aug", "AUG", "weapon_aug"),
        new("m249", "M249-SAW", "weapon_m249"),
        new("scout", "Scout", "weapon_ssg08"),
        new("g3sg1", "G3/SG-1", "weapon_g3sg1"),
        new("sg550", "SG-550 Commando", "weapon_scar20"),
        new("awp", "AWP", "weapon_awp"),
        new("he", "HE grenade", "weapon_hegrenade"),
        new("flash", "Flashbang", "weapon_flashbang"),
    };

    private bool _capFightPending;
    private bool _capFightStarted;
    private int _capPicker = -1;
    private int _capT = -1;
    private int _capCT = -1;
    private int _capPicksLeft;
    private string _capWeapon = "knife";
    private string? _capHostnameStatus;
    private readonly HashSet<int> _capFightSlots = new();
    private readonly HashSet<uint> _capFightPawnIndexes = new();
    private bool _capFightRespawnSuppressed;
    private Timer? _capGrenadeRefillTimer;
    private readonly List<Timer> _capCountdownTimers = new();

    private void CapOnLoad()
    {
        AddCommand("css_cap", "Opens the Soccer Mod cap menu.", OnCapCommand);
        AddCommand("css_pick", "Opens the cap pick menu (captain on turn).", OnCapPickCommand);
        RegisterListener<Listeners.OnClientDisconnect>(CapOnPlayerDisconnect);
    }

    private bool CapMatchRunning => _matchPhase is not (MatchPhase.Warmup or MatchPhase.Finished);

    private static void CapChat(CCSPlayerController player, string message) =>
        player.PrintToChat($" \x04[SM]\x01 {message}");

    private void CapAnnounce(string message) => AnnounceAll($" \x04[SM]\x01 {message}");

    private void OnCapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        if (IsWebsiteCapActive())
        {
            command.ReplyToCommand("[SM] a KICKOFF website cap is running - the in-game cap is unavailable until it ends");
            return;
        }

        OpenCapMenu(player);
    }

    private void OnCapPickCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        OpenCapPickMenu(player);
    }

    // cap.sp OpenCapMenu: "Soccer - Admin - Cap", four items, every one of
    // them refused while a match runs, menu reopens after each action.
    private void OpenCapMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer - Admin - Cap", OnBack = OpenMainMenu };
        menu.Add("Put all players to spectator", p => CapMenuAction(p, CapPutAllToSpec));
        menu.Add("Add random player", p => CapMenuAction(p, CapAddRandomPlayer));
        menu.Add($"Start cap fight ({_capWeapon})", p => CapMenuAction(p, CapStartFight));
        menu.Add("Weapon selection", p =>
        {
            if (CapMatchRunning)
            {
                CapChat(p, "You can not use this option during a match");
                OpenCapMenu(p);
                return;
            }

            OpenCapWeaponMenu(p);
        });
        OpenNumberMenu(player, menu);
    }

    private void CapMenuAction(CCSPlayerController player, Action<CCSPlayerController> action)
    {
        if (CapMatchRunning)
        {
            CapChat(player, "You can not use this option during a match");
        }
        else
        {
            action(player);
        }

        Server.NextFrame(() =>
        {
            if (player.IsValid)
            {
                OpenCapMenu(player);
            }
        });
    }

    private void OpenCapWeaponMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer - Cap - Weapons", OnBack = OpenCapMenu };
        foreach (var weapon in CapWeapons)
        {
            var key = weapon.Key;
            menu.Add(weapon.Label, p => CapSelectWeapon(p, key));
        }
        menu.Add("Random", p => CapSelectWeapon(p, "randwp"));
        OpenNumberMenu(player, menu);
    }

    private void CapSelectWeapon(CCSPlayerController player, string key)
    {
        if (CapMatchRunning)
        {
            CapChat(player, "You can not use this option during a match");
        }
        else
        {
            _capWeapon = key;
            Logger.LogInformation("[SM2DIAG] cap_weapon slot={Slot} weapon={Weapon}", player.Slot, key);
        }

        OpenCapMenu(player);
    }

    // cap.sp CapPutAllToSpec: every non-spectator to spectator, announced
    // per player, hostname status "Specced".
    private void CapPutAllToSpec(CCSPlayerController actor)
    {
        if (_capFightPending || _capFightStarted)
        {
            EndCapFight(null, "put_all_to_spec");
        }

        foreach (var target in Utilities.GetPlayers())
        {
            if (!target.IsValid || target.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
            {
                continue;
            }

            target.ChangeTeam(CsTeam.Spectator);
            CapAnnounce($"{actor.PlayerName} has put all players to spectator");
        }

        _capHostnameStatus = "Specced";
        UpdateHostname();
        Logger.LogInformation("[SM2DIAG] cap_spec_all by={By}", actor.PlayerName);
    }

    // cap.sp CapAddRandomPlayer: a random spectator joins whichever side
    // has fewer players.
    private void CapAddRandomPlayer(CCSPlayerController actor)
    {
        var spectators = Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsBot && p.Team is CsTeam.Spectator or CsTeam.None)
            .ToList();
        if (spectators.Count == 0)
        {
            CapChat(actor, "No players in spectator");
            return;
        }

        var target = spectators[Random.Shared.Next(spectators.Count)];
        var tCount = Utilities.GetPlayers().Count(p => p.IsValid && p.Team == CsTeam.Terrorist);
        var ctCount = Utilities.GetPlayers().Count(p => p.IsValid && p.Team == CsTeam.CounterTerrorist);
        var team = tCount <= ctCount ? CsTeam.Terrorist : CsTeam.CounterTerrorist;
        target.ChangeTeam(team);
        CapAnnounce($"{actor.PlayerName} has forced {target.PlayerName} as random player");
        Logger.LogInformation("[SM2DIAG] cap_random by={By} target={Target} team={Team}", actor.PlayerName, target.PlayerName, team);
    }

    // cap.sp CapStartFight.
    private void CapStartFight(CCSPlayerController actor)
    {
        if (_capFightPending || _capFightStarted)
        {
            CapChat(actor, "Cap fight already started");
            return;
        }

        var fighters = Utilities.GetPlayers()
            .Where(p => IsEligiblePlayer(p))
            .ToList();
        if (fighters.Count == 0)
        {
            CapChat(actor, "No players available for the cap fight");
            return;
        }

        AfkArmServerlock();
        if (_afkLockEnabled)
        {
            CapAnnounce("AFK Kick enabled.");
        }

        // tempSprint: no sprint during the duel.
        _sprintSuppressed = true;
        foreach (var fighter in fighters)
        {
            ResetSprint(fighter);
        }

        _capFightPending = true;
        _capFightSlots.Clear();
        foreach (var fighter in fighters)
        {
            _capFightSlots.Add(fighter.Slot);
        }
        FreezeAllPlayers(true);

        if (!HasAnyCapPosition(actor.AuthorizedSteamID?.SteamId64 ?? 0UL))
        {
            CapChat(actor, "Please set your position to help the caps with picking");
            OpenCapPositionMenu(actor);
        }

        CapAnnounce($"{actor.PlayerName} has started a cap fight");
        foreach (var fighter in fighters)
        {
            CapChat(fighter, $"You joined this cap on position number {CapJoinNumber(fighter.Slot)}.");
        }

        _capHostnameStatus = "Capfight";
        UpdateHostname();
        Logger.LogInformation("[SM2DIAG] cap_fight_start by={By} weapon={Weapon} fighters={Fighters}", actor.PlayerName, _capWeapon, fighters.Count);

        foreach (var timer in _capCountdownTimers)
        {
            timer.Kill();
        }
        _capCountdownTimers.Clear();
        CapCountdownText((int)CapFightCountdownSeconds);
        for (var i = 1; i < (int)CapFightCountdownSeconds; i++)
        {
            var secondsLeft = (int)CapFightCountdownSeconds - i;
            _capCountdownTimers.Add(AddTimer(i, () => CapCountdownText(secondsLeft), TimerFlags.STOP_ON_MAPCHANGE));
        }
        _capCountdownTimers.Add(AddTimer(CapFightCountdownSeconds, CapFightGo, TimerFlags.STOP_ON_MAPCHANGE));
    }

    private void CapCountdownText(int secondsLeft)
    {
        if (!_capFightPending)
        {
            return;
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid && !_openMenus.ContainsKey(player.Slot))
            {
                player.PrintToCenter($"Cap fight will start in {secondsLeft} seconds");
            }
        }
    }

    private CapWeaponOption ResolveCapWeapon()
    {
        if (_capWeapon == "randwp")
        {
            return CapWeapons[Random.Shared.Next(CapWeapons.Length)];
        }

        return CapWeapons.FirstOrDefault(w => w.Key == _capWeapon) ?? CapWeapons[0];
    }

    // cap.sp TimerCapFightCountDownEnd: "[prefix] FIGHT!", strip, arm, HP,
    // unfreeze.
    private void CapFightGo()
    {
        if (!_capFightPending)
        {
            return;
        }

        _capFightPending = false;
        _capFightStarted = true;
        _capCountdownTimers.Clear();
        _capFightPawnIndexes.Clear();
        SetRespawnOnDeathCvars(false);
        _capFightRespawnSuppressed = true;

        var weapon = ResolveCapWeapon();
        var isGrenade = weapon.Entity is "weapon_hegrenade" or "weapon_flashbang";
        foreach (var slot in _capFightSlots.ToArray())
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true } || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn))
            {
                _capFightSlots.Remove(slot);
                continue;
            }

            _capFightPawnIndexes.Add(pawn.Index);
            player.RemoveWeapons();
            if (weapon.Entity == "weapon_knife")
            {
                player.GiveNamedItem(player.Team == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife");
            }
            else
            {
                player.GiveNamedItem(player.Team == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife");
                player.GiveNamedItem(weapon.Entity);
            }

            // cap.sp:505-507 - a flashbang duel is decided by any hit, an HE
            // duel by one grenade, everything else needs real damage.
            var health = weapon.Entity switch
            {
                "weapon_flashbang" => 1,
                "weapon_hegrenade" => 98,
                _ => 101,
            };
            pawn.TakesDamage = true;
            pawn.Health = health;
            Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
            player.PrintToCenter("[SM] FIGHT!");
        }

        if (isGrenade)
        {
            _capGrenadeRefillTimer?.Kill();
            _capGrenadeRefillTimer = AddTimer(0.5f, () => CapRefillGrenades(weapon.Entity), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        }

        FreezeAllPlayers(false);
        Logger.LogInformation("[SM2DIAG] cap_fight_go weapon={Weapon} participants={Count}", weapon.Entity, _capFightSlots.Count);
    }

    // cap.sp GrenadeRefillTimer: a thrown grenade is handed straight back.
    private void CapRefillGrenades(string grenadeEntity)
    {
        if (!_capFightStarted)
        {
            _capGrenadeRefillTimer?.Kill();
            _capGrenadeRefillTimer = null;
            return;
        }

        var shortName = grenadeEntity.Replace("weapon_", string.Empty);
        foreach (var slot in _capFightSlots)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true } || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn))
            {
                continue;
            }

            var weapons = pawn.WeaponServices?.MyWeapons;
            var hasGrenade = weapons is not null && weapons.Any(handle =>
                handle.Value is { IsValid: true } w && w.DesignerName.Contains(shortName, StringComparison.OrdinalIgnoreCase));
            if (!hasGrenade)
            {
                player.GiveNamedItem(grenadeEntity);
            }
        }
    }

    // cap.sp CapEventPlayerDeath, plus the CS2 "no round end" adaptation.
    private void CapFightOnPlayerDeath(EventPlayerDeath @event)
    {
        if (!_capFightStarted)
        {
            return;
        }

        var victim = @event.Userid;
        if (victim is not { IsValid: true } || !_capFightSlots.Contains(victim.Slot))
        {
            return;
        }

        var attacker = @event.Attacker;
        if (attacker is not { IsValid: true } || attacker.Slot == victim.Slot || !_capFightSlots.Contains(attacker.Slot))
        {
            CapAnnounce("Cap fight invalid. Please restart the fight.");
            EndCapFight(null, "invalid_kill");
            return;
        }

        _capPicker = attacker.Slot;
        if (attacker.Team == CsTeam.Terrorist)
        {
            _capT = attacker.Slot;
            _capCT = victim.Slot;
        }
        else
        {
            _capCT = attacker.Slot;
            _capT = victim.Slot;
        }

        Logger.LogInformation(
            "[SM2DIAG] cap_fight_kill attacker={Attacker} victim={Victim} picker={Picker}",
            attacker.PlayerName,
            victim.PlayerName,
            attacker.PlayerName);

        // The victim's side is out once none of its fighters is alive; the
        // victim itself is already dead at this point.
        var victimTeam = victim.Team;
        var sideStillAlive = _capFightSlots.Any(slot =>
            slot != victim.Slot
            && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } other
            && other.Team == victimTeam
            && IsAlive(other.PlayerPawn.Value));
        if (!sideStillAlive)
        {
            EndCapFight(attacker.Team, "side_eliminated");
        }
    }

    // Winner side (null = aborted): weapons back to knives, HP back, sprint
    // back, respawn the fallen, then the winner's captain picks first
    // (cap.sp CapEventRoundEnd).
    private void EndCapFight(CsTeam? winner, string reason)
    {
        foreach (var timer in _capCountdownTimers)
        {
            timer.Kill();
        }
        _capCountdownTimers.Clear();
        _capGrenadeRefillTimer?.Kill();
        _capGrenadeRefillTimer = null;

        var wasActive = _capFightPending || _capFightStarted;
        _capFightPending = false;
        _capFightStarted = false;
        _sprintSuppressed = false;
        FreezeAllPlayers(false);

        foreach (var slot in _capFightSlots)
        {
            var player = Utilities.GetPlayerFromSlot(slot);
            if (player is not { IsValid: true })
            {
                continue;
            }

            var pawn = player.PlayerPawn.Value;
            if (pawn is { IsValid: true } && IsAlive(pawn))
            {
                player.RemoveWeapons();
                EnsurePlayerKnife(player, "capfight_end");
                pawn.Health = _healthGodmodeEnabled ? 100 : _healthAmount;
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_iHealth");
                pawn.VelocityModifier = 1.0f;
                Utilities.SetStateChanged(pawn, "CCSPlayerPawn", "m_flVelocityModifier");
            }
        }

        if (_capFightRespawnSuppressed)
        {
            _capFightRespawnSuppressed = false;
            SetRespawnOnDeathCvars(true);
            foreach (var slot in _capFightSlots)
            {
                var player = Utilities.GetPlayerFromSlot(slot);
                if (player is { IsValid: true }
                    && player.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist
                    && !IsAlive(player.PlayerPawn.Value))
                {
                    player.Respawn();
                }
            }
        }

        _capFightSlots.Clear();
        _capFightPawnIndexes.Clear();

        if (!wasActive)
        {
            return;
        }

        Logger.LogInformation("[SM2DIAG] cap_fight_end winner={Winner} reason={Reason}", winner?.ToString() ?? "none", reason);
        if (winner is not { } winningTeam)
        {
            _capHostnameStatus = null;
            UpdateHostname();
            return;
        }

        _capPicksLeft = (CapMatchMaxPlayers - 1) * 2;
        _capHostnameStatus = "Picking";
        UpdateHostname();
        var pickerSlot = winningTeam == CsTeam.Terrorist ? _capT : _capCT;
        _capPicker = pickerSlot;
        AddTimer(0.5f, () =>
        {
            if (Utilities.GetPlayerFromSlot(pickerSlot) is { IsValid: true } picker)
            {
                OpenCapPickMenu(picker);
            }
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void CapOnRoundStart()
    {
        if (_capFightPending || _capFightStarted)
        {
            EndCapFight(null, "round_start");
        }
    }

    private void CapOnPlayerDisconnect(int slot)
    {
        if (!_capFightSlots.Remove(slot))
        {
            return;
        }

        if (!_capFightStarted)
        {
            return;
        }

        var tAlive = _capFightSlots.Any(s => Utilities.GetPlayerFromSlot(s) is { IsValid: true, Team: CsTeam.Terrorist } p && IsAlive(p.PlayerPawn.Value));
        var ctAlive = _capFightSlots.Any(s => Utilities.GetPlayerFromSlot(s) is { IsValid: true, Team: CsTeam.CounterTerrorist } p && IsAlive(p.PlayerPawn.Value));
        if (tAlive && !ctAlive)
        {
            CapAnnounce("Cap fight invalid. Please restart the fight.");
            EndCapFight(null, "participant_disconnected");
        }
        else if (ctAlive && !tAlive)
        {
            CapAnnounce("Cap fight invalid. Please restart the fight.");
            EndCapFight(null, "participant_disconnected");
        }
    }

    private int CapJoinNumber(int slot)
    {
        var index = _connectOrder.IndexOf(slot);
        return index < 0 ? 0 : index + 1;
    }

    // cap.sp OpenCapPickMenu / CapCreatePickMenu: "[Join Nr] Name
    // [Positions]", one row per spectator/unassigned player.
    private void OpenCapPickMenu(CCSPlayerController player)
    {
        if (player.Slot != _capT && player.Slot != _capCT)
        {
            CapChat(player, "You are not a cap");
            return;
        }

        if (player.Slot != _capPicker)
        {
            CapChat(player, "It is not your turn to pick");
            return;
        }

        var candidates = Utilities.GetPlayers()
            .Where(p => p.IsValid && !p.IsBot && p.Team is CsTeam.Spectator or CsTeam.None)
            .OrderBy(p => CapJoinNumber(p.Slot) == 0 ? int.MaxValue : CapJoinNumber(p.Slot))
            .ToList();
        if (candidates.Count == 0)
        {
            CapChat(player, "No players available to pick");
            return;
        }

        var menu = new NumberMenu { Title = "[Join Nr] Name [Positions]" };
        foreach (var candidate in candidates)
        {
            var targetSlot = candidate.Slot;
            var positions = FormatCapPositions(candidate.AuthorizedSteamID?.SteamId64 ?? 0UL);
            var label = positions.Length > 0
                ? $"[{CapJoinNumber(targetSlot)}] {candidate.PlayerName} {positions}"
                : $"[{CapJoinNumber(targetSlot)}] {candidate.PlayerName}";
            menu.Add(label, p => CapPick(p, targetSlot));
        }
        OpenNumberMenu(player, menu);
    }

    // cap.sp CapPickMenuHandler.
    private void CapPick(CCSPlayerController picker, int targetSlot)
    {
        var target = Utilities.GetPlayerFromSlot(targetSlot);
        if (target is not { IsValid: true })
        {
            CapChat(picker, "Player is no longer on the server");
            OpenCapPickMenu(picker);
            return;
        }

        _capPicksLeft--;
        target.ChangeTeam(picker.Team);
        CloseMenu(target.Slot, "picked");
        CapAnnounce($"{picker.PlayerName} has picked {target.PlayerName}");
        Logger.LogInformation(
            "[SM2DIAG] cap_pick picker={Picker} target={Target} team={Team} picksLeft={PicksLeft}",
            picker.PlayerName,
            target.PlayerName,
            picker.Team,
            _capPicksLeft);

        _capPicker = picker.Slot == _capT ? _capCT : _capT;
        if (_capPicksLeft <= 0)
        {
            return;
        }

        if (Utilities.GetPlayerFromSlot(_capPicker) is { IsValid: true } next)
        {
            OpenCapPickMenu(next);
        }
    }
}
