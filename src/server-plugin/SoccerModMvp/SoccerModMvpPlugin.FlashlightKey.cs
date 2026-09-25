using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: a personal setting "Flashlight on F". On = the inspect
// key (F, PlayerButtons.Inspect) toggles the flashlight instead of
// inspecting. The light itself is the installed Flashlight plugin
// (creazy.eth, command css_fl_toggle, no key of its own). The inspect
// animation is held back with the weapon services' "block inspect" flag
// while the setting is on. Per player, saved by SteamID (default off).
public sealed partial class SoccerModMvpPlugin
{
    private readonly HashSet<int> _inspectHeld = new();
    private readonly HashSet<int> _flashlightOn = new();

    private bool FlashlightOnInspect(CCSPlayerController player) =>
        _menuParity.FlashlightOnInspect.TryGetValue(SteamIdOf(player), out var on) && on;

    private void SetFlashlightOnInspect(CCSPlayerController player, bool on)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        _menuParity.FlashlightOnInspect[id] = on;
        SaveJsonAtomic(MenuParityFile, _menuParity);
        // Switching it off also puts out a light this setting turned on.
        if (!on && _flashlightOn.Remove(player.Slot)) player.ExecuteClientCommandFromServer("css_fl_toggle");
        player.PrintToChat(on
            ? " \x04[SM]\x01 Flashlight on F: \x04on\x01 - press F to switch the light on and off."
            : " \x04[SM]\x01 Flashlight on F: \x07off\x01 - F inspects again.");
    }

    private void FlashlightKeyOnTick()
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot || !FlashlightOnInspect(player)
                || player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn))
            {
                if (player.IsValid) _inspectHeld.Remove(player.Slot);
                continue;
            }

            if (pawn.WeaponServices is { } services)
                new CCSPlayer_WeaponServices(services.Handle).BlockInspectUntilNextGraphUpdate = true;

            var held = (player.Buttons & PlayerButtons.Inspect) != 0;
            if (held && _inspectHeld.Add(player.Slot))
            {
                player.ExecuteClientCommandFromServer("css_fl_toggle");
                if (!_flashlightOn.Add(player.Slot)) _flashlightOn.Remove(player.Slot);
                Logger.LogInformation("[SM2DIAG] flashlight_key slot={Slot} on={On}", player.Slot, _flashlightOn.Contains(player.Slot));
            }
            else if (!held)
            {
                _inspectHeld.Remove(player.Slot);
            }
        }
    }

    private void FlashlightKeyOnDisconnect(int slot)
    {
        _inspectHeld.Remove(slot);
        _flashlightOn.Remove(slot);
    }
}
