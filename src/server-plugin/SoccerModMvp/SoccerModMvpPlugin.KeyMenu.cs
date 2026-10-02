using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using CS2UIKit;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-10-01 owner: the SoccerMod menu on the number keys exactly as in CS:S - no binds, no mouse.
//
// How a stock CS2 client can send a number key at all (measured on the owner's client, see
// docs/number-key-menu.md): the client's radio panel takes the RAW number keys while it is open,
// whatever the keys are bound to, and its entries come from resource/ui/radiopanel.txt, which an
// add-on can replace (the file is read again on every map load). An entry runs console commands,
// so it sends its number to the server and opens the panel again.
//
//   Z (stock radio key)  -> group "common": page one of the menu, drawn by the client itself;
//                           entry N runs "css_menu r N; radio1".
//   radio1               -> group "standard": empty entries, only there to catch the keys while
//                           the server draws the page; entry N runs "css_N r; radio1".
//
// Both are the old commands with one more word ("r" = from the radio panel). The Workshop item
// is mounted on every SoccerMod server, also on one whose plugin does not know this file: there
// css_menu still opens the menu and css_N still presses the key, the extra words are ignored.
//
// The NumberMenu model (Menu.cs) stays as it is: this is one more renderer and one more key
// source for it. The page is drawn where the radio panel is and with its measures
// (soccermod_keymenu.xml), so both look like one menu.
//
// What the client does not tell the server:
//   - key 0 is the game's own Exit of the radio panel and sends nothing;
//   - the panel closes by itself after its timeout; a server cannot open or close it.
// So a page closes KeyMenuTimeoutSeconds after the last key (the panel's own timeout).
//
// Spectators and dead players have no radio panel: they keep the other renderers.
public sealed partial class SoccerModMvpPlugin
{
    internal const string KeyMenuLayout = "panorama/layout/custom_game/soccermod_keymenu.xml";
    // The "timeout" of both groups in radiopanel.txt.
    private const double KeyMenuTimeoutSeconds = 12.0;
    private const int KeyMenuRows = 9;
    // A raw key that is also bound to css_N would arrive twice.
    private const double KeyMenuRepeatSeconds = 0.2;

    private Panel? _keyMenuPanel;
    // Players who have sent a key through the radio panel since they connected.
    private readonly HashSet<int> _keyMenuRadioUsers = new();
    private readonly Dictionary<int, double> _keyMenuTouched = new();
    private readonly Dictionary<int, (int Number, string Source, double Time)> _keyMenuLastKey = new();
    private readonly HashSet<int> _keyMenuHintShown = new();

    private bool KeyMenuOn => _menuParity.KeyMenu && _keyMenuPanel is not null;

    private bool UsesKeyMenu(CCSPlayerController player) =>
        KeyMenuOn && player.IsValid && !player.IsBot
        && (player.Team == CsTeam.Terrorist || player.Team == CsTeam.CounterTerrorist)
        && IsAlive(player.PlayerPawn.Value);

    // radiopanel.txt, group "group": how long the key catcher stays after a call.
    private const double KeyMenuCallSeconds = 3.0;

    private void KeyMenuOnLoad()
    {
        // 2026-10-02 owner: the radio key is for the football calls (was: V with a bind), the
        // SoccerMod menu opens with the menu command. "css_call r N" is key N on the calls page of
        // the radio panel.
        AddCommand("css_call", "Radio panel: football call N (css_call r N).", (player, command) =>
        {
            if (player is not { IsValid: true, IsBot: false } || command.ArgCount < 3 || command.GetArg(1) != "r"
                || !int.TryParse(command.GetArg(2), out var number) || number is < 1 or > KeyMenuRows) return;
            OnKeyMenuCallKey(player, number);
        });
        // UIKit.Init ran in ClickMenuOnLoad; the panel's entity is made and removed by the kit.
        _keyMenuPanel = new Panel(KeyMenuLayout, new PanelOptions { Root = "km_window", ShownClass = "shown", CaptureInput = false });

        AddCommand("css_sm2menu_keys", "Admin: number-key menu through the radio panel (on|off).", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : string.Empty;
            if (arg is "on" or "off")
            {
                var before = _menuParity.KeyMenu;
                // Open menus were drawn by the renderer of the old setting: close them with it.
                ResetOpenMenusForRendererChange();
                _menuParity.KeyMenu = arg == "on";
                if (!SaveJsonAtomic(MenuParityFile, _menuParity)) _menuParity.KeyMenu = before;
            }
            command.ReplyToCommand($"[SM] Number-key menu: {(_menuParity.KeyMenu ? "on" : "off")}, {_keyMenuRadioUsers.Count} player(s) used the radio keys, page closes {KeyMenuTimeoutSeconds:0} s after the last key (usage: css_sm2menu_keys <on|off>)");
        });
    }

    private void KeyMenuOnPlayerDisconnect(int slot)
    {
        _keyMenuRadioUsers.Remove(slot);
        _keyMenuTouched.Remove(slot);
        _keyMenuLastKey.Remove(slot);
        _keyMenuHintShown.Remove(slot);
    }

    // ---- keys -----------------------------------------------------------------------------

    // css_menu: "css_menu r N" is key N on page one of the radio panel. True: handled here.
    private bool KeyMenuRadioMenuCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true, IsBot: false } || command.ArgCount < 3 || command.GetArg(1) != "r"
            || !int.TryParse(command.GetArg(2), out var number) || number is < 1 or > KeyMenuRows) return false;
        OnKeyMenuKey(player, number, firstPage: true);
        return true;
    }

    // css_N: "css_N r" is key N in the key-catching radio group. True: handled here.
    private bool KeyMenuRadioKeyCommand(CCSPlayerController? player, CommandInfo command, int number)
    {
        if (player is not { IsValid: true, IsBot: false } || command.ArgCount < 2 || command.GetArg(1) != "r") return false;
        OnKeyMenuKey(player, number, firstPage: false);
        return true;
    }

    private void OnKeyMenuKey(CCSPlayerController player, int number, bool firstPage)
    {
        var slot = player.Slot;
        // Proof that the player has the Workshop files: only our radiopanel.txt sends these.
        MarkWorkshopVerified(player);

        // A page the server has open is what the player sees (it covers the radio panel): the
        // key is for that page, whichever radio group sent it.
        if (_openMenus.ContainsKey(slot))
        {
            if (KeyMenuOn) _keyMenuRadioUsers.Add(slot);
            OnMenuNumberKey(player, number, "radio");
            return;
        }

        if (!KeyMenuOn)
        {
            // Switch off: what a plugin without this file does - the first key opens the menu.
            if (!firstPage) return;
            ForgetMenuPages(slot);
            CancelPendingChatInput(player);
            OpenMainMenu(player);
            return;
        }

        _keyMenuRadioUsers.Add(slot);
        if (KeyMenuRepeated(slot, number, "radio")) return;
        KeyMenuTouch(slot);
        Logger.LogInformation("[SM2DIAG] menu_key source={Source} number={Number} slot={Slot} team={Team} hasOpenMenu=False",
            firstPage ? "radio_main" : "radio_stray", number, slot, player.Team);
        ForgetMenuPages(slot);
        CancelPendingChatInput(player);
        if (!firstPage)
        {
            // The menu ended but the key catcher is still open (it only closes by its timeout or
            // key 0) and shows numbers without texts: bring the main page back.
            OpenMainMenu(player);
            return;
        }

        // Page one is drawn by the client from radiopanel.txt: the same entries, in the same
        // order, as BuildKeyMainMenu.

        var menu = BuildKeyMainMenu(player);
        if (number <= menu.Options.Count && menu.Options[number - 1].Enabled)
        {
            MenuPages(slot).Leave(menu.MemoryKey, 0);
            menu.Options[number - 1].OnSelect(player);
            return;
        }
        // Not available to this player: show him the page with what is.
        player.PrintToChat(" \x04[SM]\x01 That entry is not available to you.");
        OpenNumberMenu(player, menu);
    }

    // True: the same number just came in over the other path (radio panel and a css_N bind on
    // the same key). The first one counts.
    private bool KeyMenuRepeated(int slot, int number, string source)
    {
        var now = (double)Server.TickedTime;
        if (_keyMenuLastKey.TryGetValue(slot, out var last) && last.Number == number && last.Source != source
            && now - last.Time < KeyMenuRepeatSeconds)
        {
            return true;
        }
        _keyMenuLastKey[slot] = (number, source, now);
        return false;
    }

    // A key press or the menu command: the page stays for KeyMenuTimeoutSeconds from now.
    private void KeyMenuTouch(int slot)
    {
        _keyMenuTouched[slot] = Server.TickedTime;
        if (_openMenus.ContainsKey(slot) && KeyMenuExpiry(slot) is { } expiry) _menuExpiryBySlot[slot] = expiry;
    }

    // When the page of a radio-key player closes by itself; null: the normal menu rule applies.
    // That is the case for players who use css_N binds or the chat numbers, while the switch is
    // off, and for a menu the SERVER put in front of the player (a captain's pick list, a
    // question): it has to stay until he answers. Only a menu he reached with a key runs out.
    private double? KeyMenuExpiry(int slot)
    {
        if (!KeyMenuOn || !_keyMenuRadioUsers.Contains(slot) || !_keyMenuTouched.TryGetValue(slot, out var touched)) return null;
        return touched + KeyMenuTimeoutSeconds;
    }

    // OpenNumberMenu: the expiry of a menu that opens now.
    private double? KeyMenuExpiryOnOpen(int slot)
    {
        if (KeyMenuExpiry(slot) is not { } expiry) return null;
        // Opened by his own key or command of this moment (a reopen runs a frame or two later).
        return Server.TickedTime - _keyMenuTouched[slot] <= 0.5 ? expiry : null;
    }

    // ---- pages ----------------------------------------------------------------------------

    // The fixed main menu: what page one of radiopanel.txt lists, entry for entry. An entry the
    // player may not use keeps its number and is shown as an information row.
    private NumberMenu BuildKeyMainMenu(CCSPlayerController player)
    {
        var id = player.AuthorizedSteamID?.SteamId64 ?? 0UL;
        var admin = HasFlag(id, "admin");
        var control = HasPublicControl(player);
        var menu = new NumberMenu { Title = "Soccer Mod" };
        // 2026-10-02 owner: "why do I have Match, Cap etc. on the first page and the same ones under
        // Admin?" - they are here only now, for admins too (OpenAdminMenu leaves them out while the key
        // menu is on), and Back from them comes back here. An entry a player may not use is not shown
        // (the first page of the radio panel is the calls now, nothing has to line up with it).
        void Entry(string text, bool allowed, Action<CCSPlayerController> open)
        {
            if (allowed) menu.Add(text, open);
        }
        Entry("Admin", admin, OpenAdminMenu);
        Entry("Match", control, OpenMatchMenu);
        Entry("Cap", control && _menuParity.IngameCap && !IsWebsiteCapActive(), OpenCapMenu);
        Entry("Training", admin || control, OpenTrainingMenu);
        Entry("Referee", admin ? HasFlag(id, "match") : control, OpenRefereeMenu);
        Entry("Reload Map", admin || control, OpenMapSelectMenu);
        Entry("Settings", true, OpenClientSettingsMenu);
        Entry("Stats & Ranking", admin || !_menuParity.PublicServer, OpenStatsRankingMenu);
        Entry("Help", true, OpenHelpMenu);
        return menu;
    }

    // SourceMod paging: 7 options, 8 Back / Previous, 9 Next. A menu without Back that fits the
    // nine rows is one page and uses 8 and 9 as options (the main menu).
    private List<MenuPage> BuildKeyMenuPages(NumberMenu menu)
    {
        if (menu.OnBack is null && menu.Options.Count is > MenuClassicPageCapacity and <= KeyMenuRows)
        {
            return new List<MenuPage>
            {
                new() { ShowTitle = true, Items = menu.Options, UsesClassicKeys = true, PageIndex = 0, TotalPages = 1 },
            };
        }
        return BuildMenuPages(menu, classicLayout: true);
    }

    private void DrawKeyMenu(CCSPlayerController player, NumberMenu menu)
    {
        if (_keyMenuPanel is null) return;
        var pages = BuildKeyMenuPages(menu);
        var pageIndex = NormalizePageIndex(player.Slot, pages.Count);
        var page = pages[pageIndex];

        var title = page.TotalPages > 1 ? $"{menu.Title} ({page.PageIndex + 1}/{page.TotalPages})" : menu.Title;
        _keyMenuPanel.SetText(player, "km_title", title);
        for (var i = 0; i < KeyMenuRows; i++)
        {
            var text = " ";
            var info = false;
            var empty = false;
            if (i < page.Items.Count)
            {
                var option = page.Items[i];
                info = !option.Enabled;
                text = info ? option.Text : $"{i + 1}. {option.Text}";
            }
            else if (i + 1 == page.BackKey && page.HasBack) text = page.BackGoesToParent ? "8. Back" : "8. Previous";
            else if (i + 1 == page.NextKey && page.HasNext) text = "9. Next";
            else empty = true;

            var row = $"km_row_{i + 1}";
            _keyMenuPanel.SetText(player, row, text);
            _keyMenuPanel.SetClass(player, row, "info", info);
            _keyMenuPanel.SetClass(player, row, "empty", empty);
        }
        _keyMenuPanel.Show(player);

        // Opened by chat or a bind: the keys only work once the radio panel is open.
        if (!_keyMenuRadioUsers.Contains(player.Slot) && _keyMenuHintShown.Add(player.Slot))
            player.PrintToChat(" \x04[SM]\x01 Press \x04Z\x01 (your radio key) once: then the number keys 1-9 work in this menu, 0 closes it. No binds needed.");
    }

    // CloseMenu: a radio-key player chose something that ends the menu. The game's key catcher (a
    // panel with the bare numbers 1-9) stays open on his screen until it times out, and the server
    // cannot close it. So the page does not vanish: the main menu takes its place, unless the
    // choice opened another page, and runs out together with the catcher. True: keep the page.
    private void OnKeyMenuCallKey(CCSPlayerController player, int number)
    {
        var slot = player.Slot;
        MarkWorkshopVerified(player);
        // A page the server has open covers the calls of the radio panel: the key is for that page.
        if (_openMenus.ContainsKey(slot))
        {
            if (KeyMenuOn) _keyMenuRadioUsers.Add(slot);
            OnMenuNumberKey(player, number, "radio");
            return;
        }
        if (KeyMenuOn)
        {
            _keyMenuRadioUsers.Add(slot);
            if (KeyMenuRepeated(slot, number, "radio")) return;
            KeyMenuTouch(slot);
        }
        Logger.LogInformation("[SM2DIAG] menu_key source=radio_call number={Number} slot={Slot} team={Team} hasOpenMenu=False", number, slot, player.Team);
        if (number <= FootballCalls.Length) SendFootballCall(player, FootballCalls[number - 1]);
        KeyMenuCallsCover(player, KeyMenuCallSeconds);
    }

    // The key catcher that follows a call shows bare numbers: the calls page lies over it for as
    // long as it is open (another call is one key away), then both are gone.
    private void KeyMenuCallsCover(CCSPlayerController player, double seconds)
    {
        if (!UsesKeyMenu(player)) return;
        OpenCallsMenu(player);
        if (_openMenus.ContainsKey(player.Slot)) _menuExpiryBySlot[player.Slot] = Server.TickedTime + seconds;
    }

    private bool KeyMenuCoverAfterClose(CCSPlayerController player, string reason, string? closingKey)
    {
        var slot = player.Slot;
        if (reason != "option_selected" || !KeyMenuOn || !UsesKeyMenu(player) || !_keyMenuRadioUsers.Contains(slot)
            || !_keyMenuTouched.TryGetValue(slot, out var touched) || Server.TickedTime - touched > 0.5) return false;
        Server.NextFrame(() =>
        {
            if (!player.IsValid || _openMenus.ContainsKey(slot)) return;
            if (!UsesKeyMenu(player)) { HideKeyMenu(player); return; }
            // after a call from the calls page: the calls again, as long as the catcher stays
            if (closingKey == "calls") KeyMenuCallsCover(player, KeyMenuTimeoutSeconds);
            else OpenMainMenu(player);
        });
        return true;
    }

    // CloseMenu: a radio-key player chose something that ends the menu. The game's key catcher (a
    // panel with the bare numbers 1-9) stays open on his screen until it times out, and the server
    // cannot close it. So the page does not vanish: the main menu takes its place, unless the
    // choice opened another page, and runs out together with the catcher. True: keep the page.
    private bool KeyMenuCoverAfterClose(CCSPlayerController player, string reason)
    {
        var slot = player.Slot;
        if (reason != "option_selected" || !KeyMenuOn || !UsesKeyMenu(player) || !_keyMenuRadioUsers.Contains(slot)
            || !_keyMenuTouched.TryGetValue(slot, out var touched) || Server.TickedTime - touched > 0.5) return false;
        Server.NextFrame(() =>
        {
            if (!player.IsValid || _openMenus.ContainsKey(slot)) return;
            if (!UsesKeyMenu(player)) { HideKeyMenu(player); return; }
            OpenMainMenu(player);
        });
        return true;
    }

    private void HideKeyMenu(CCSPlayerController player)
    {
        if (_keyMenuPanel is not null && _keyMenuPanel.IsOpen(player)) _keyMenuPanel.Hide(player);
    }
}
