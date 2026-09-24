using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
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
        if (_menuParity.ClickMenu) StartClickMenuBridge(hotReload);

        AddCommand("css_sm2menu_click", "Admin: clickable menu for everyone (on|off) or just you (me).", OnClickMenuCommand);
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
        RegisterListener<Listeners.OnClientDisconnect>(slot => _clickMenuViaBuyMenu.Remove(slot));
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
        else if (arg == "me" && player is { IsValid: true })
        {
            // Not a toggle: typed twice it used to switch itself back off
            // (2026-09-24 live). "me" = on for me, "me off" = off for me.
            var id = SteamIdOf(player);
            var off = command.ArgCount >= 3 && command.GetArg(2).Equals("off", StringComparison.OrdinalIgnoreCase);
            if (_openMenus.ContainsKey(player.Slot)) CloseMenu(player.Slot, "click_menu_toggle");
            if (off) _clickMenuTesters.Remove(id);
            else if (_clickMenuTesters.Add(id)) StartClickMenuBridge(true);
        }

        foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot)) UpdateClickMenuGrant(p);
        var mine = player is { IsValid: true } && _clickMenuTesters.Contains(SteamIdOf(player));
        command.ReplyToCommand($"[SM] Clickable menu: everyone={(_menuParity.ClickMenu ? "on" : "off")}, you={(mine || _menuParity.ClickMenu ? "on" : "off")}, testers={_clickMenuTesters.Count} (usage: css_sm2menu_click <on|off|me|me off>)");
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

        var rows = new (string Text, bool Enabled, bool Used)[9];
        foreach (var (key, text, enabled) in BuildMenuDisplayLines(page))
        {
            if (key is >= 1 and <= 9) rows[key - 1] = (text, enabled, true);
        }

        _clickMenuPanel.SetText(player, "sm_title", menu.Title);
        _clickMenuPanel.SetText(player, "sm_page", page.TotalPages > 1 ? $"{page.PageIndex + 1}/{page.TotalPages}" : string.Empty);
        for (var i = 0; i < rows.Length; i++)
        {
            _clickMenuPanel.SetText(player, $"sm_row_{i + 1}_text", rows[i].Text ?? string.Empty);
            _clickMenuPanel.SetClass(player, $"sm_row_{i + 1}", "empty", !rows[i].Used);
            _clickMenuPanel.SetClass(player, $"sm_row_{i + 1}", "info", rows[i].Used && !rows[i].Enabled);
        }

        // Opened with B: the buy menu holds the window and the cursor.
        if (!_clickMenuViaBuyMenu.Contains(player.Slot)) _clickMenuPanel.Show(player);
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
