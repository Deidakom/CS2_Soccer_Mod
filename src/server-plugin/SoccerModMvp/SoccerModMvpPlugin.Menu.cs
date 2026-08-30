using System.Linq;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// !menu, replicating the SoMoE-19 menu (Cap / Match / Admin submenus).
//
// Menu input is REAL NUMBER KEYS, not typed chat. Neither of CSSharp's own
// menu types does that: ChatMenu makes the player type "!1"/"!2", and
// CenterHtmlMenu scrolls with W/S/E and re-renders its HTML every tick
// (which visibly flickers). Both were tried and rejected on those grounds.
//
// What actually works: in CS2 the number keys 1-9 are bound to the engine
// commands slot1..slot9, so a command LISTENER on those names receives the
// keypress directly and returns HookResult.Handled to swallow the weapon
// switch. The menu body itself is printed to chat exactly once when it
// opens - static text, so there is nothing to flicker.
//
// Every option re-invokes the existing chat command AS the selecting player
// via ExecuteClientCommandFromServer, so all the real logic (state
// machines, permission gates, logging) stays in one place - the menu is
// pure presentation. Training/Referee are SoMoE menu entries with no CS2
// implementation yet (deferred per the MVP plan) so they're left off rather
// than shown as dead buttons.
public sealed partial class SoccerModMvpPlugin
{
    private const double MenuTimeoutSeconds = 30.0;

    private sealed class NumberMenu
    {
        public required string Title { get; init; }
        public List<(string Text, Action<CCSPlayerController> OnSelect)> Options { get; } = new();

        public void Add(string text, Action<CCSPlayerController> onSelect) => Options.Add((text, onSelect));
    }

    // 2026-08-30 user question: how does every future player get working
    // 1-9/0 navigation without being told the bind commands individually?
    //
    // Answer, and it is a hard engine limit, not something we can code
    // around: a SERVER CANNOT SET A CLIENT'S KEYBINDS. That is deliberate
    // Source-engine behaviour (a server forcing input mappings onto a
    // client would be a real exploit vector), and it applies just as much
    // to a workshop addon as to us - a map/addon cannot ship a keybind
    // override either. The default number-key binds (slot1..slot9) DO
    // exist out of the box, but on this build+loadout they were measured
    // dead last session (0 events logged, ever - see the handoff doc) and
    // must not be relied on.
    // So a bind command run once, by each player, in their own client, is
    // unavoidable for real-keypress input. What CAN be automated is the
    // reminder: every real (non-bot) player gets the exact command block
    // printed to their own chat/console once, on their first spawn, so
    // nobody has to be told this by hand or dig it out of a doc. See
    // MenuMaybeSendBindReminder, called from OnPlayerSpawn.
    private readonly HashSet<int> _bindReminderShownBySlot = new();

    private readonly Dictionary<int, NumberMenu> _openMenus = new();
    private readonly Dictionary<int, double> _menuExpiryBySlot = new();
    private readonly Dictionary<int, double> _menuNextRedrawBySlot = new();
    private readonly Dictionary<int, int> _menuPageBySlot = new();

    // The menu is drawn as a centre-screen panel (radio-menu style), NOT
    // chat. Redraw cadence is tunable live because the right value can only
    // be judged on screen: css_sm2menu_hud <seconds>.
    //
    // 2026-08-30, measured: with PrintToCenterHtml the server was confirmed
    // drawing the panel every 1.0s for 20+ seconds straight (menu_draw in
    // the log, no menu_close), while the player saw it flash once and
    // vanish - so the HTML panel's own lifetime is far shorter than the
    // duration argument claims, and CSSharp's CenterHtmlMenu redraws every
    // tick for exactly this reason, not out of sloppiness.
    // Plain PrintToCenter, by contrast, is visibly stable at a 1.0s cadence
    // on this same server (the match score ticker uses it). Hence two
    // render modes, switchable live with css_sm2menu_mode, and a much
    // faster default redraw for the HTML one.
    // Per-mode, because the two panels have very different lifetimes:
    // plain centre text visibly survives a full second (the match score
    // ticker runs at 1.0s and is stable), while the HTML panel needs
    // near-per-tick refreshing to stay up at all.
    // The HTML panel has its own fade-in animation, and every redraw
    // restarts it. That gives a narrow usable band, confirmed in-game:
    // at 1.0s the panel expires outright (gone), at 0.1s the animation
    // gets far enough to be visible before restarting, so it pulses.
    // 0 = redraw every tick, which is what CSSharp's own CenterHtmlMenu
    // does - the animation never advances past its first frame, so it
    // holds at full opacity instead of cycling.
    // CORRECTED 2026-08-30, later the same day: the html-is-stable claim
    // that used to be here was wrong - even at per-tick (0.00s) redraw,
    // the user still saw it pulsing/ticking in-game. The panel's fade-in
    // animation is apparently intrinsic to PrintToCenterHtml itself in
    // this build, not something a redraw-rate tweak can fully suppress.
    // plain is the ONLY mode that structurally cannot animate (no fade,
    // plain text) and is therefore the default. html stays available via
    // css_sm2menu_mode for the nicer look, at the cost of that tick.
    //
    // BUG THAT COST REAL TIME, do not repeat: this used to be a plain
    // in-memory field, so an RCON-only css_sm2menu_mode switch was silently
    // wiped by the next redeploy's service restart back to whatever was
    // hardcoded here - the user saw the "wrong" menu return with no
    // apparent cause. Mode + both redraw intervals are now persisted via
    // BallSettingsStore (Config.cs) specifically so that can't happen
    // again. If you add another live-tunable menu field, persist it too.
    private float _menuRedrawPlainSeconds = 0.8f;
    private float _menuRedrawHtmlSeconds = 0.0f;
    private bool _menuUsePlainCenterText = true;

    private float MenuRedrawIntervalSeconds =>
        _menuUsePlainCenterText ? _menuRedrawPlainSeconds : _menuRedrawHtmlSeconds;

    private void MenuOnLoad()
    {
        AddCommand("css_menu", "Open the SoccerMod menu.", OnMenuCommand);
        AddCommand("css_sm2menu_hud", "Admin: tune the menu panel redraw interval in seconds.", OnMenuHudCommand);
        AddCommand("css_sm2menu_mode", "Admin: switch the menu panel between plain and html rendering.", OnMenuModeCommand);

        for (var i = 1; i <= 9; i++)
        {
            var number = i;

            // Path A: slot1..slot9 are what CS2's default number-key binds
            // invoke. Whether those actually reach the server is build- and
            // context-dependent (a knife-only loadout has nothing in most
            // slots), so this is attempted but NOT relied on.
            AddCommandListener($"slot{number}", (player, _) => OnMenuNumberKey(player, number, "slot"), HookMode.Pre);

            // Path B: real plugin commands, which are proven to reach the
            // server from a client keybind (that is exactly how the F10 ->
            // css_menu bind works). Players bind 1-9 to these once; on a
            // knife-only server the number keys have no other job anyway.
            AddCommand($"css_{number}", $"Select menu option {number}.", (player, _) => { OnMenuNumberKey(player, number, "command"); });
        }

        // 0 closes the menu, same two input paths as 1-9 (2026-08-30 user
        // request).
        AddCommandListener("slot0", (player, _) => OnMenuCloseKey(player, "slot"), HookMode.Pre);
        AddCommand("css_0", "Close the SoccerMod menu.", (player, _) => { OnMenuCloseKey(player, "command"); });
    }

    private HookResult OnMenuCloseKey(CCSPlayerController? player, string source)
    {
        if (player is null || !player.IsValid || !_openMenus.ContainsKey(player.Slot))
        {
            return HookResult.Continue;
        }

        Logger.LogInformation("[SM2DIAG] menu_key source={Source} number=0 slot={Slot} hasOpenMenu=True", source, player.Slot);
        CloseMenu(player.Slot, "closed_by_zero_key");
        // Swallow the keypress so it doesn't also switch weapon slots.
        return HookResult.Handled;
    }

    private HookResult OnMenuNumberKey(CCSPlayerController? player, int number, string source)
    {
        if (player is not null && player.IsValid)
        {
            // Diagnostic: tells us from the log which of the two input paths
            // actually delivers a keypress on this build.
            Logger.LogInformation(
                "[SM2DIAG] menu_key source={Source} number={Number} slot={Slot} hasOpenMenu={HasOpenMenu}",
                source,
                number,
                player.Slot,
                _openMenus.ContainsKey(player.Slot));
        }

        if (player is null || !player.IsValid || !_openMenus.TryGetValue(player.Slot, out var menu))
        {
            return HookResult.Continue;
        }

        if (_menuExpiryBySlot.TryGetValue(player.Slot, out var expiry) && Server.TickedTime > expiry)
        {
            CloseMenu(player.Slot, "expired_on_keypress");
            return HookResult.Continue;
        }

        var pages = BuildMenuPages(menu);
        var pageIndex = NormalizePageIndex(player.Slot, pages.Count);
        var page = pages[pageIndex];

        if (page.HasPrev && number == page.BackKey)
        {
            var prevPage = pageIndex - 1;
            _menuPageBySlot[player.Slot] = prevPage;
            DrawMenu(player, menu);
            Logger.LogInformation(
                "[SM2DIAG] menu_page_back slot={Slot} page={Page} totalPages={Total}",
                player.Slot,
                prevPage + 1,
                pages.Count);
            return HookResult.Handled;
        }

        if (page.HasNext && number == page.NextKey)
        {
            var nextPage = pageIndex + 1;
            _menuPageBySlot[player.Slot] = nextPage;
            DrawMenu(player, menu);
            Logger.LogInformation(
                "[SM2DIAG] menu_page_advanced slot={Slot} page={Page} totalPages={Total}",
                player.Slot,
                nextPage + 1,
                pages.Count);
            return HookResult.Handled;
        }

        if (number < 1 || number > page.Items.Count)
        {
            return HookResult.Continue;
        }

        var option = page.Items[number - 1];
        CloseMenu(player.Slot, "option_selected");
        option.OnSelect(player);
        // Swallow the keypress so it doesn't also switch weapon slots.
        return HookResult.Handled;
    }

    private int NormalizePageIndex(int slot, int pageCount)
    {
        var requested = _menuPageBySlot.TryGetValue(slot, out var p) ? p : 0;
        var normalized = ((requested % pageCount) + pageCount) % pageCount;
        _menuPageBySlot[slot] = normalized;
        return normalized;
    }

    // A stale entry here would otherwise survive until its 30s expiry after
    // the player who owned it has already left.
    private void MenuOnPlayerDisconnect(int slot)
    {
        _openMenus.Remove(slot);
        _menuExpiryBySlot.Remove(slot);
        _menuNextRedrawBySlot.Remove(slot);
        _menuPageBySlot.Remove(slot);
        _bindReminderShownBySlot.Remove(slot);
    }

    // Single source of truth for the bind instructions - printed both on
    // first spawn and from !help (2026-08-30 user request), so the two can
    // never drift apart.
    // NOTE the space after \x01 on the paste line: without it the client
    // eats the first character and the player pastes "ind 1 css_1", which
    // silently fails. Do not remove it.
    internal void MenuSendBindInstructions(CCSPlayerController player)
    {
        player.PrintToChat(" \x04[SoccerMod]\x01 Open console (~) and paste this once:");
        player.PrintToChat(" \x01 bind 1 css_1;bind 2 css_2;bind 3 css_3;bind 4 css_4;bind 5 css_5;bind 6 css_6;bind 7 css_7;bind 8 css_8;bind 9 css_9;bind 0 css_0;bind F10 css_menu");
        player.PrintToChat(" \x04[SoccerMod]\x01 Saved in your own config. F10 opens the menu, 1-9 pick, 0 closes.");
    }

    // Called from OnPlayerSpawn. One-time, real players only - see the
    // field comment above for why this can't just be pushed to the client.
    private void MenuMaybeSendBindReminder(CCSPlayerController player)
    {
        if (player.IsBot || !_bindReminderShownBySlot.Add(player.Slot))
        {
            return;
        }

        player.PrintToChat(" \x04[SoccerMod]\x01 First time here?");
        MenuSendBindInstructions(player);
    }

    private void CloseMenu(int slot, string reason = "unspecified")
    {
        Logger.LogInformation("[SM2DIAG] menu_close slot={Slot} reason={Reason}", slot, reason);
        _openMenus.Remove(slot);
        _menuExpiryBySlot.Remove(slot);
        _menuNextRedrawBySlot.Remove(slot);
        _menuPageBySlot.Remove(slot);
        // Blank the panel immediately so it doesn't linger after a choice.
        if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
        {
            if (_menuUsePlainCenterText)
            {
                player.PrintToCenter(" ");
            }
            else
            {
                player.PrintToCenterHtml(" ");
            }
        }
    }

    private void OpenNumberMenu(CCSPlayerController player, NumberMenu menu)
    {
        _openMenus[player.Slot] = menu;
        _menuExpiryBySlot[player.Slot] = Server.TickedTime + MenuTimeoutSeconds;
        _menuNextRedrawBySlot[player.Slot] = 0.0;
        _menuPageBySlot[player.Slot] = 0;
        Logger.LogInformation(
            "[SM2DIAG] menu_open slot={Slot} name={Name} title={Title} optionCount={OptionCount} now={Now:F2}",
            player.Slot,
            player.PlayerName,
            menu.Title,
            menu.Options.Count,
            (double)Server.TickedTime);
        DrawMenu(player, menu);
    }

    // 2026-08-30 user request: real pagination. Plain mode's centre-text
    // panel is measured to clip at ~4 total lines and does NOT just cut
    // the bottom off - a 9-line test earlier the same session showed only
    // the MIDDLE four lines rendering. Page 0 keeps the title (1 line) +
    // up to 3 content lines; every later page drops the title to buy a 4th
    // content line instead.
    //
    // 2026-08-30 later: HTML mode was ALSO found to clip (the "stable up to
    // 9 options" claim this comment used to make was wrong, same class of
    // mistake as an earlier wrong "html is stable" claim this session - see
    // Menu.cs history). A screenshot pinned the real number: with a fixed
    // "8=Back, 9=Next" scheme, title + 6 items + "9. Next" (8 rendered
    // lines) all showed fully, with only the small-font page-count hint on
    // a 9th line getting clipped. That fixed-key scheme was ALSO rejected
    // live - the user saw "1..6" then a bare jump to "9. Next" with no 7/8
    // and read it as broken. So both problems get fixed together: Back/Next
    // are now placed CONTIGUOUSLY right after the real items (dynamic keys,
    // like the very first "last slot = Next" design, just now with a real
    // Back too), and the hint line is dropped entirely so nothing rides
    // past the proven-safe 8-line budget. HTML capacities below are set to
    // stay comfortably under that measured edge, not right at it.
    private const int MenuFirstPageCapacity = 3;
    private const int MenuLaterPageCapacity = 4;
    private const int MenuHtmlFirstPageCapacity = 6;
    private const int MenuHtmlLaterPageCapacity = 7;

    private sealed class MenuPage
    {
        public bool ShowTitle;
        public required List<(string Text, Action<CCSPlayerController> OnSelect)> Items;
        public bool HasPrev;
        public bool HasNext;
        public int PageIndex;
        public int TotalPages;

        // Contiguous placement: real items are 1..Items.Count, Back (if
        // any) is the very next number, Next (if any) is the one after
        // that - never a fixed key, so there's never a gap for the player
        // to be confused by.
        public int BackKey => Items.Count + 1;
        public int NextKey => Items.Count + (HasPrev ? 2 : 1);
    }

    private List<MenuPage> BuildMenuPages(NumberMenu menu)
    {
        // HTML mode paginates too now (user report: it clips as well, just
        // at a larger size) - only its capacity differs from plain mode.
        var singlePageCapacity = _menuUsePlainCenterText ? MenuFirstPageCapacity : MenuHtmlFirstPageCapacity;
        if (menu.Options.Count <= singlePageCapacity)
        {
            return new List<MenuPage>
            {
                new() { ShowTitle = true, Items = menu.Options, HasPrev = false, HasNext = false, PageIndex = 0, TotalPages = 1 },
            };
        }

        var pages = new List<MenuPage>();
        var index = 0;
        while (index < menu.Options.Count)
        {
            var showTitle = pages.Count == 0;
            var hasPrev = !showTitle;
            var baseCapacity = _menuUsePlainCenterText
                ? (showTitle ? MenuFirstPageCapacity : MenuLaterPageCapacity)
                : (showTitle ? MenuHtmlFirstPageCapacity : MenuHtmlLaterPageCapacity);
            var remaining = menu.Options.Count - index;
            // Tentative: could this page hold everything remaining while
            // reserving a slot only for Back (never Next)? If so it IS the
            // last page - the lookahead that avoids over-reserving nav
            // slots on boundary pages.
            var capacityIfLast = baseCapacity - (hasPrev ? 1 : 0);
            var isLast = remaining <= capacityIfLast;
            var capacity = baseCapacity - (hasPrev ? 1 : 0) - (isLast ? 0 : 1);
            var take = Math.Min(capacity, remaining);
            pages.Add(new MenuPage
            {
                ShowTitle = showTitle,
                Items = menu.Options.GetRange(index, take),
                HasPrev = hasPrev,
                HasNext = !isLast,
                PageIndex = pages.Count,
            });
            index += take;
        }

        foreach (var page in pages)
        {
            page.TotalPages = pages.Count;
        }

        return pages;
    }

    // Shared (key, text) list for both renderers below: real items keep
    // their 1-based position, Prev/Next get whatever key comes right after
    // them (page.BackKey/page.NextKey) - contiguous, never a gap.
    //
    // 2026-08-30 user report (screenshot): most of our menus already end
    // with their OWN "Back" item that returns to the PARENT menu (e.g.
    // Match Menu -> Admin Menu). This pagination control does something
    // different - go to the PREVIOUS PAGE of the SAME menu - but sharing
    // the label "Back" made a paginated page showing both look like a
    // literal duplicate ("2. Back" / "3. Back") with no way to tell them
    // apart. Labelled "Prev" here so the two are never confusable, without
    // touching the actual per-menu Back items (still real, unrelated
    // options wired by each Open*Menu method).
    private static List<(int Key, string Text)> BuildMenuDisplayLines(MenuPage page)
    {
        var lines = new List<(int Key, string Text)>();
        for (var i = 0; i < page.Items.Count; i++)
        {
            lines.Add((i + 1, page.Items[i].Text));
        }

        if (page.HasPrev)
        {
            lines.Add((page.BackKey, "Prev"));
        }

        if (page.HasNext)
        {
            lines.Add((page.NextKey, "Next"));
        }

        return lines;
    }

    private static string BuildMenuHtml(string title, MenuPage page)
    {
        var lines = BuildMenuDisplayLines(page);

        // The panel centres every line, so a ragged-right list looks
        // centre-aligned. Padding each line out to the width of the
        // longest one makes the whole block read as left-aligned. The font
        // is proportional, so this is approximate by nature - it lines the
        // numbers up, which is the part that matters.
        var widest = page.ShowTitle ? title.Length : 0;
        foreach (var (_, text) in lines)
        {
            var lineWidth = text.Length + 3; // "N. "
            if (lineWidth > widest)
            {
                widest = lineWidth;
            }
        }

        static string Pad(int count) => string.Concat(Enumerable.Repeat("&nbsp;", Math.Max(0, count)));

        var html = page.ShowTitle
            ? $"<font class='fontSize-m' color='#ff9900'>{title}{Pad(widest - title.Length)}</font><br>"
            : string.Empty;
        for (var i = 0; i < lines.Count; i++)
        {
            var (key, text) = lines[i];
            var isLastLine = i == lines.Count - 1;
            html += $"<font class='fontSize-sm' color='#ffffff'>{key}.</font> "
                + $"<font class='fontSize-sm' color='#bfff00'>{text}{Pad(widest - text.Length - 3)}</font>"
                + (isLastLine ? string.Empty : "<br>");
        }

        return html;
    }

    // One option per line (2026-08-30 user request), now inside the
    // pagination scheme above - a page's Items list is already sized to
    // fit, so this just lays out whatever page it's given.
    private static string BuildMenuPlainText(string title, MenuPage page)
    {
        var lines = new List<string>();
        if (page.ShowTitle)
        {
            lines.Add(title);
        }

        foreach (var (key, text) in BuildMenuDisplayLines(page))
        {
            lines.Add($"{key}. {text}");
        }

        return string.Join("\n", lines);
    }

    // Explicit duration comfortably longer than the redraw interval, so the
    // panel can't expire client-side between two redraws.
    private const int MenuPanelDurationSeconds = 5;

    private void DrawMenu(CCSPlayerController player, NumberMenu menu)
    {
        if (!player.IsValid)
        {
            Logger.LogInformation("[SM2DIAG] menu_draw_skipped_invalid_player slot={Slot}", player.Slot);
            return;
        }

        var pages = BuildMenuPages(menu);
        var page = pages[NormalizePageIndex(player.Slot, pages.Count)];

        if (_menuUsePlainCenterText)
        {
            player.PrintToCenter(BuildMenuPlainText(menu.Title, page));
        }
        else
        {
            player.PrintToCenterHtml(BuildMenuHtml(menu.Title, page), MenuPanelDurationSeconds);
        }
    }

    // Called every tick from the main OnTick.
    private void MenuOnTick()
    {
        if (_openMenus.Count == 0)
        {
            return;
        }

        var now = (double)Server.TickedTime;
        foreach (var slot in _openMenus.Keys.ToArray())
        {
            if (_menuExpiryBySlot.TryGetValue(slot, out var expiry) && now > expiry)
            {
                CloseMenu(slot, "expired_on_tick");
                continue;
            }

            if (_menuNextRedrawBySlot.TryGetValue(slot, out var nextRedraw) && now < nextRedraw)
            {
                continue;
            }

            _menuNextRedrawBySlot[slot] = now + MenuRedrawIntervalSeconds;
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player
                && _openMenus.TryGetValue(slot, out var menu))
            {
                DrawMenu(player, menu);
            }
            else
            {
                CloseMenu(slot, "player_or_menu_missing_on_tick");
            }
        }
    }

    private void OnMenuHudCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            // 0 means "every tick", which is what CSSharp's own
            // CenterHtmlMenu does - see the blinking note on the fields.
            && seconds is >= 0.0f and <= 10.0f)
        {
            // Tunes whichever mode is currently active.
            if (_menuUsePlainCenterText)
            {
                _menuRedrawPlainSeconds = seconds;
            }
            else
            {
                _menuRedrawHtmlSeconds = seconds;
            }
            SaveBallSettings("menu_hud_command");
        }

        command.ReplyToCommand(
            $"[SM] menu HUD redraw interval ({(_menuUsePlainCenterText ? "plain" : "html")} mode): "
            + $"{MenuRedrawIntervalSeconds:F2}s");
    }

    private void OnMenuModeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            if (string.Equals(arg, "plain", StringComparison.OrdinalIgnoreCase))
            {
                _menuUsePlainCenterText = true;
            }
            else if (string.Equals(arg, "html", StringComparison.OrdinalIgnoreCase))
            {
                _menuUsePlainCenterText = false;
            }
            SaveBallSettings("menu_mode_command");
        }

        command.ReplyToCommand(
            $"[SM] menu render mode: {(_menuUsePlainCenterText ? "plain" : "html")} "
            + "(usage: css_sm2menu_mode <plain|html>)");
    }

    private void OnMenuCommand(CCSPlayerController? player, CommandInfo command)
    {
        Logger.LogInformation(
            "[SM2DIAG] menu_command_received slot={Slot} playerIsNull={PlayerIsNull}",
            player?.Slot ?? -1,
            player is null);

        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        OpenMainMenu(player);
    }

    // 2026-08-30 user request: arrange root/submenus the same way as the
    // real SoMoE-19 menu (menus.sp OpenMenuSoccer/OpenMenuAdmin) - there,
    // Match/Cap/Referee live INSIDE the Admin submenu, not on the root; the
    // root is just Admin/Ranking/Statistics/Positions/Help/Settings/Shouts/
    // Credits. Ranking, Statistics, client Settings and Shouts don't exist
    // in this port yet (no stats/ranking engine, no workshop addon
    // pipeline - see the reconstruction plan), so they're left off rather
    // than shown as dead buttons; everything else follows SoMoE's layout.
    // Explicit user choice: Match/Cap/Referee now require the "admin" flag
    // to even see the Admin submenu, same as real SoMoE - regular players
    // lose one-click menu access to them (chat commands !cap/!match still
    // work directly) in exchange for matching the real structure.
    private void OpenMainMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "SoccerMod Menu" };
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "admin"))
        {
            menu.Add("Admin", OpenAdminMenu);
        }
        menu.Add("Position", p => p.ExecuteClientCommandFromServer("css_pos"));
        menu.Add("Spectate", p => p.ExecuteClientCommandFromServer("css_spec me"));
        menu.Add("Help", PrintHelp);
        menu.Add("Credits", OpenCreditsMenu);
        // Sprint is deliberately NOT here (2026-08-30 user request): it is
        // a mid-play action bound to a key / +use, so opening a menu to
        // reach it makes no sense. css_sprint still exists.
        OpenNumberMenu(player, menu);
    }

    private void OpenCreditsMenu(CCSPlayerController player)
    {
        player.PrintToChat($" \x04[SoccerMod]\x01 {ModuleName} v{ModuleVersion}");
        player.PrintToChat(" \x04[SoccerMod]\x01 A CS2 port of SoMoE-19 (github.com/MK99MA/SoMoE-19)");
        player.PrintToChat(" \x04[SoccerMod]\x01 Port by Natsu");
    }

    private void OpenCapMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Cap Menu" };
        menu.Add("Open / Status", p => p.ExecuteClientCommandFromServer("css_cap"));
        menu.Add("Join", p => p.ExecuteClientCommandFromServer("css_join"));
        menu.Add("Leave", p => p.ExecuteClientCommandFromServer("css_leave"));
        menu.Add("Draft (owner)", p => p.ExecuteClientCommandFromServer("css_draft"));
        menu.Add("Cancel", p => p.ExecuteClientCommandFromServer("css_capcancel"));
        menu.Add("Back", OpenAdminMenu);
        OpenNumberMenu(player, menu);
    }

    private void OpenMatchMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Match Menu" };
        menu.Add("Status", p => p.ExecuteClientCommandFromServer("css_match status"));
        menu.Add("Start", p => p.ExecuteClientCommandFromServer("css_match start"));
        menu.Add("Stop", p => p.ExecuteClientCommandFromServer("css_match stop"));
        menu.Add("Pause", p => p.ExecuteClientCommandFromServer("css_match pause"));
        menu.Add("Unpause", p => p.ExecuteClientCommandFromServer("css_match unpause"));
        menu.Add("Restart Round", p => p.ExecuteClientCommandFromServer("css_rr"));
        menu.Add("Back", OpenAdminMenu);
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Admin Menu" };
        menu.Add("Match", OpenMatchMenu);
        menu.Add("Cap", OpenCapMenu);
        // Referee keeps its own extra "match" flag check on top of the
        // "admin" flag that already gates this whole submenu - unchanged
        // from before, just relocated to match SoMoE's layout.
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "match"))
        {
            menu.Add("Referee", OpenRefereeMenu);
        }
        menu.Add("Kick a player", OpenKickPlayerMenu);
        menu.Add("Ban a player", OpenBanPlayerMenu);
        menu.Add("Admin List", p => p.ExecuteClientCommandFromServer("css_admin_list"));
        menu.Add("Ban List", p => p.ExecuteClientCommandFromServer("css_banlist"));
        menu.Add("Reload map (workshop-safe)", p => p.ExecuteClientCommandFromServer("css_maprr"));
        menu.Add("Back", OpenMainMenu);
        OpenNumberMenu(player, menu);
    }

    private void OpenKickPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Kick Player" };
        // 8 targets max so "Back" always fits inside the 1-9 key range.
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null).Take(8))
        {
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName, p => p.ExecuteClientCommandFromServer($"css_kick #{userId}"));
        }
        menu.Add("Back", OpenAdminMenu);
        OpenNumberMenu(player, menu);
    }

    private void OpenBanPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Ban Player (permanent)" };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null).Take(8))
        {
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName, p => p.ExecuteClientCommandFromServer($"css_ban #{userId} 0 menu_ban"));
        }
        menu.Add("Back", OpenAdminMenu);
        OpenNumberMenu(player, menu);
    }
}
