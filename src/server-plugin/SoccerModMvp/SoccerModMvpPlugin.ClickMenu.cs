using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using CS2UIKit;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-24 owner request: a menu that works without binds. The NumberMenu
// model stays exactly as it is; this is one more renderer for it, drawn in a
// Panorama layout (custom_hud_layout, CounterStrikeSharp 1.0.374+) that the
// players get from the Workshop addon. Rows are buttons: a click goes through
// OnMenuNumberKey like a number key, so paging, Back, info rows and page
// memory behave identically. Bound number keys keep working.
//
// Two ways in, both bind-free:
// - !menu (or anything that opens a menu) shows the panel and takes the
//   cursor;
// - the stock B key: while the stock buy menu is open the client itself shows
//   our window (BuyMenuBridge, vendored from cs2-ui-kit). Buying stays enabled
//   for that, and buy/autobuy/rebuy are blocked, so nothing can be bought.
//   Closing the buy menu closes our menu, and a menu that ends asks the client
//   to close the buy menu.
//
// Who gets it: everyone when css_sm2menu_click on (MenuParity.ClickMenu), or
// single testers (css_sm2menu_click me) while the layout is not yet in the
// Workshop addon. Everyone else keeps the plain/HTML menu.
public sealed partial class SoccerModMvpPlugin
{
    internal const string ClickMenuLayout = "panorama/layout/custom_game/soccermod_menu.xml";
    private const float ClickMenuRebuildDelaySeconds = 1.5f;
    // Same as MenuClassicPageCapacity: keys 8/9 are navigation.
    private const int ClickMenuOptionRows = 7;

    private Panel? _clickMenuPanel;
    private BuyMenuBridge? _clickMenuBridge;
    private bool _clickMenuBridgeStarted;
    private readonly HashSet<ulong> _clickMenuTesters = new();
    // Players whose current menu was opened with B (the buy menu holds it).
    private readonly HashSet<int> _clickMenuViaBuyMenu = new();

    private void ClickMenuOnLoad(bool hotReload)
    {
        UIKit.Init(this, hotReload, message => Logger.LogInformation("[SM2DIAG] {Message}", message));
        _clickMenuPanel = new Panel(ClickMenuLayout, new PanelOptions { Root = "sm_window", ShownClass = "shown" });
        _clickMenuPanel.Clicked += click => OnClickMenuClicked(click.Player, click.ButtonId);
        _clickMenuBridge = new BuyMenuBridge(_clickMenuPanel, "sm_window", "sm_dim",
            log: message => Logger.LogInformation("[SM2DIAG] {Message}", message));
        _clickMenuBridge.StockMenuToggled += OnClickMenuStockMenuToggled;
        // Testers survive reloads (2026-09-24: each hot deploy used to drop
        // the owner back to the old menu).
        foreach (var id in _menuParity.ClickMenuTesters) _clickMenuTesters.Add(id);
        if (_menuParity.ClickMenu || _clickMenuTesters.Count > 0) StartClickMenuBridge(hotReload);

        AddCommand("css_sm2menu_click", "Admin: clickable menu for everyone (on|off) or just you (me).", OnClickMenuCommand);
        AddCommand("css_menumouse", "Clickable menu with the mouse (on) or keys only (off).", OnMenuMouseCommand);
        RegisterEventHandler<EventPlayerConnectFull>((@event, _) =>
        {
            // CS2 1.41.8.x clears a slot's texts when a player takes it
            // (CounterStrikeSharp PR #1434): recreate the panel a moment later
            // and redraw whoever has a menu open. Remove once the fix ships.
            if (@event.Userid is { IsValid: true, IsBot: false } && (_menuParity.ClickMenu || _clickMenuTesters.Count > 0))
                AddTimer(ClickMenuRebuildDelaySeconds, RebuildClickMenu, TimerFlags.STOP_ON_MAPCHANGE);
            return HookResult.Continue;
        });
        RegisterEventHandler<EventPlayerSpawn>((@event, _) =>
        {
            if (@event.Userid is { IsValid: true, IsBot: false } player) Server.NextFrame(() => UpdateClickMenuGrant(player));
            return HookResult.Continue;
        });
        RegisterListener<Listeners.OnClientDisconnect>(slot => { _clickMenuViaBuyMenu.Remove(slot); _aimPicks.Remove(slot); });
    }

    private void ClickMenuOnUnload()
    {
        if (_clickMenuBridgeStarted) _clickMenuBridge?.Stop(this);
        _clickMenuBridgeStarted = false;
        UIKit.Shutdown();
    }

    private void StartClickMenuBridge(bool hotReload)
    {
        if (_clickMenuBridgeStarted || _clickMenuBridge is null) return;
        _clickMenuBridge.Start(this, hotReload);
        _clickMenuBridgeStarted = true;
    }

    private bool UsesClickMenu(CCSPlayerController player) =>
        _clickMenuPanel is not null && player.IsValid && !player.IsBot
        && (_menuParity.ClickMenu || _clickMenuTesters.Contains(SteamIdOf(player)));

    // B opens the menu for everyone who uses the clickable menu.
    private void UpdateClickMenuGrant(CCSPlayerController player)
    {
        if (!_clickMenuBridgeStarted || _clickMenuBridge is null || !player.IsValid) return;
        _clickMenuBridge.Allow(player, UsesClickMenu(player));
    }

    private void OnClickMenuCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : string.Empty;
        if (arg is "on" or "off")
        {
            var before = _menuParity.ClickMenu;
            _menuParity.ClickMenu = arg == "on";
            if (!SaveJsonAtomic(MenuParityFile, _menuParity)) _menuParity.ClickMenu = before;
            if (_menuParity.ClickMenu) StartClickMenuBridge(true);
            ResetOpenMenusForRendererChange();
        }
        else if (arg == "mouse" && command.ArgCount >= 3 && command.GetArg(2).ToLowerInvariant() is "on" or "off")
        {
            // Default for players who have not chosen themselves.
            _menuParity.ClickMenuMouseDefault = command.GetArg(2).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveJsonAtomic(MenuParityFile, _menuParity);
        }
        else if (arg == "me" && player is { IsValid: true })
        {
            // Not a toggle: typed twice it used to switch itself back off
            // (2026-09-24 live). "me" = on for me, "me off" = off for me.
            var id = SteamIdOf(player);
            var off = command.ArgCount >= 3 && command.GetArg(2).Equals("off", StringComparison.OrdinalIgnoreCase);
            if (_openMenus.ContainsKey(player.Slot)) CloseMenu(player.Slot, "click_menu_toggle");
            if (off) _clickMenuTesters.Remove(id);
            else if (_clickMenuTesters.Add(id)) StartClickMenuBridge(true);
            _menuParity.ClickMenuTesters = _clickMenuTesters.ToList();
            SaveJsonAtomic(MenuParityFile, _menuParity);
        }

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot)) UpdateClickMenuGrant(p);
        var mine = player is { IsValid: true } && _clickMenuTesters.Contains(SteamIdOf(player));
        command.ReplyToCommand($"[SM] Clickable menu: everyone={(_menuParity.ClickMenu ? "on" : "off")}, you={(mine || _menuParity.ClickMenu ? "on" : "off")}, testers={_clickMenuTesters.Count}, mouse default={(_menuParity.ClickMenuMouseDefault ? "on" : "off")} (usage: css_sm2menu_click <on|off|me|me off|mouse on|mouse off>)");
    }

    // --- Aim pick -----------------------------------------------------------
    // 2026-09-24 owner: menu items that use the crosshair (cannon position,
    // spawn at crosshair, props...) cannot be aimed while the panel holds the
    // cursor, and a HUD panel cannot pick a point in the 3D world. So such an
    // option (NumberMenu.AddAim) hides the menu and frees the view; the player
    // aims and left-clicks (or presses E) to run it at the crosshair, right-
    // click cancels. The menu comes back afterwards.
    private const double AimPickTimeoutSeconds = 20.0;
    private const double AimPickArmDelaySeconds = 0.25;

    private sealed class AimPickState
    {
        public required NumberMenuOption Option;
        public required NumberMenu ReturnMenu;
        public required double Started;
        public double NextHint;
        public PlayerButtons PreviousButtons;
    }

    private readonly Dictionary<int, AimPickState> _aimPicks = new();

    private void BeginAimPick(CCSPlayerController player, NumberMenuOption option, NumberMenu returnMenu)
    {
        _aimPicks[player.Slot] = new AimPickState
        {
            Option = option,
            ReturnMenu = returnMenu,
            Started = Server.TickedTime,
            PreviousButtons = player.Buttons,
        };
        player.PrintToChat($" \x04[SM]\x01 {option.Text}: aim at the spot and left-click (or press E). Right-click cancels.");
        Logger.LogInformation("[SM2DIAG] aim_pick_start slot={Slot} option={Option}", player.Slot, option.Text);
    }

    private void AimPickOnTick()
    {
        if (_aimPicks.Count == 0) return;
        var now = Server.TickedTime;
        foreach (var (slot, pick) in _aimPicks.ToArray())
        {
            if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player)
            {
                _aimPicks.Remove(slot);
                continue;
            }

            var buttons = player.Buttons;
            var pressed = buttons & ~pick.PreviousButtons;
            pick.PreviousButtons = buttons;
            if (now - pick.Started > AimPickTimeoutSeconds || (pressed & PlayerButtons.Attack2) != 0)
            {
                EndAimPick(player, pick, run: false);
                continue;
            }

            if (now - pick.Started >= AimPickArmDelaySeconds && (pressed & (PlayerButtons.Attack | PlayerButtons.Use)) != 0)
            {
                EndAimPick(player, pick, run: true);
                continue;
            }

            if (now >= pick.NextHint)
            {
                pick.NextHint = now + 1.0;
                player.PrintToCenter($"{pick.Option.Text}\nAim and left-click (or E) - right-click cancels");
            }
        }
    }

    private void EndAimPick(CCSPlayerController player, AimPickState pick, bool run)
    {
        var slot = player.Slot;
        _aimPicks.Remove(slot);
        player.PrintToCenter(" ");
        Logger.LogInformation("[SM2DIAG] aim_pick_end slot={Slot} option={Option} run={Run}", slot, pick.Option.Text, run);
        if (run) pick.Option.OnSelect(player);
        else player.PrintToChat(" \x04[SM]\x01 Cancelled.");
        // Most actions re-open their own menu; otherwise return to the one
        // the option came from.
        Server.NextFrame(() =>
        {
            if (player.IsValid && !_openMenus.ContainsKey(slot) && !_chatInputBySlot.ContainsKey(slot))
                OpenNumberMenu(player, pick.ReturnMenu);
        });
    }

    private void ResetOpenMenusForRendererChange()
    {
        foreach (var slot in _openMenus.Keys.ToArray()) CloseMenu(slot, "renderer_change");
    }

    // Called from DrawMenu for players on the clickable menu. Same pages as
    // the classic layout: 7 options, 8 = Back/Prev, 9 = Next.
    private void DrawClickMenu(CCSPlayerController player, NumberMenu menu)
    {
        if (_clickMenuPanel is null) return;
        var pages = BuildMenuPages(menu, classicLayout: true);
        var pageIndex = NormalizePageIndex(player.Slot, pages.Count);
        var page = pages[pageIndex];

        // Options are rows 1-7. Back/Previous (key 8) and Next (key 9) live in
        // the navigation bar under them, with the page count in between.
        _clickMenuPanel.SetText(player, "sm_title", menu.Title);
        for (var i = 0; i < ClickMenuOptionRows; i++)
        {
            var used = i < page.Items.Count;
            var enabled = used && page.Items[i].Enabled;
            _clickMenuPanel.SetText(player, $"sm_row_{i + 1}_text", used ? page.Items[i].Text : string.Empty);
            _clickMenuPanel.SetClass(player, $"sm_row_{i + 1}", "empty", !used);
            _clickMenuPanel.SetClass(player, $"sm_row_{i + 1}", "info", used && !enabled);
        }

        var multiPage = page.TotalPages > 1;
        _clickMenuPanel.SetText(player, "sm_back_text", page.BackGoesToParent ? "‹ Back" : "‹ Previous");
        _clickMenuPanel.SetClass(player, "sm_back", "hidden", !page.HasBack);
        _clickMenuPanel.SetClass(player, "sm_next", "hidden", !page.HasNext);
        _clickMenuPanel.SetText(player, "sm_page", multiPage ? $"Page {page.PageIndex + 1} / {page.TotalPages}" : string.Empty);
        _clickMenuPanel.SetClass(player, "sm_nav", "hidden", !page.HasBack && !page.HasNext && !multiPage);

        // Opened with B: the buy menu holds the window and the cursor.
        if (_clickMenuViaBuyMenu.Contains(player.Slot)) return;
        _clickMenuPanel.Show(player);
        // Keys-only players: the panel shows, the mouse keeps looking and
        // knifing; they navigate with their css_1..css_9 binds (or chat !1..!9).
        if (!ClickMenuMouse(player)) _clickMenuPanel.CaptureInput(player, false);
    }

    private bool ClickMenuMouse(CCSPlayerController player) =>
        _menuParity.ClickMenuMouse.TryGetValue(SteamIdOf(player), out var on) ? on : _menuParity.ClickMenuMouseDefault;

    // !menumouse [on|off]: the player's own choice, saved.
    private void OnMenuMouseCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true } || SteamIdOf(player) == 0)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : string.Empty;
        if (arg is "on" or "off") SetClickMenuMouse(player, arg == "on");
        player.PrintToChat($" \x04[SM]\x01 Menu mouse: {(ClickMenuMouse(player) ? "on - click the options" : "off - navigate with your number-key binds (!bind shows them)")}. Change with !menumouse on/off.");
    }

    private void SetClickMenuMouse(CCSPlayerController player, bool on)
    {
        _menuParity.ClickMenuMouse[SteamIdOf(player)] = on;
        SaveJsonAtomic(MenuParityFile, _menuParity);
        // Apply to a menu that is open right now.
        if (_openMenus.TryGetValue(player.Slot, out var menu)) DrawMenu(player, menu);
    }

    private void HideClickMenu(CCSPlayerController player)
    {
        _clickMenuPanel?.Hide(player);
        if (!_clickMenuViaBuyMenu.Contains(player.Slot)) return;
        // A menu that ends (not one replaced by another menu) closes the buy
        // menu too; the window would otherwise keep showing its last page.
        var slot = player.Slot;
        Server.NextFrame(() =>
        {
            if (_openMenus.ContainsKey(slot) || !_clickMenuViaBuyMenu.Remove(slot)) return;
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } p) _clickMenuBridge?.Close(p);
        });
    }

    private void OnClickMenuClicked(CCSPlayerController player, string buttonId)
    {
        if (!player.IsValid) return;
        Logger.LogInformation("[SM2DIAG] click_menu_click slot={Slot} button={Button}", player.Slot, buttonId);
        if (buttonId == "sm_close")
        {
            if (_openMenus.ContainsKey(player.Slot)) OnMenuCloseKey(player, "click");
            else HideClickMenu(player);
            return;
        }

        // The click layout always uses the classic keys: 8 = Back/Previous,
        // 9 = Next (MenuPage.BackKey / NextKey with UsesClassicKeys).
        if (buttonId == "sm_back")
        {
            OnMenuNumberKey(player, 8, "click");
            return;
        }

        if (buttonId == "sm_next")
        {
            OnMenuNumberKey(player, 9, "click");
            return;
        }

        // Rows 8/9 only exist in the first layout (Back/Next as rows); a client
        // that has not restarted since still sends them - treat them as the
        // navigation keys they were.
        if (buttonId.StartsWith("sm_row_", StringComparison.Ordinal)
            && int.TryParse(buttonId.AsSpan("sm_row_".Length), out var number) && number is >= 1 and <= 9)
        {
            OnMenuNumberKey(player, number, "click");
        }
    }

    private void OnClickMenuStockMenuToggled(CCSPlayerController player, bool open)
    {
        if (open)
        {
            _clickMenuViaBuyMenu.Add(player.Slot);
            if (_openMenus.TryGetValue(player.Slot, out var menu)) DrawMenu(player, menu);
            else OpenMainMenu(player);
            return;
        }

        if (!_clickMenuViaBuyMenu.Remove(player.Slot)) return;
        if (_openMenus.ContainsKey(player.Slot))
        {
            CloseMenu(player.Slot, "buy_menu_closed");
            ForgetMenuPages(player.Slot);
        }
    }

    private void RebuildClickMenu()
    {
        UIKit.Rebuild();
        foreach (var (slot, menu) in _openMenus.ToArray())
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player && UsesClickMenu(player)) DrawMenu(player, menu);
        }
        // The new entity has none of the old classes: take the B grant back
        // and give it again so the marker class is set on it.
        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot))
        {
            if (!_clickMenuBridgeStarted || _clickMenuBridge is null) break;
            _clickMenuBridge.Allow(p, false);
            UpdateClickMenuGrant(p);
        }
    }
}
