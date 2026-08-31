using System.Linq;
using System.Text;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// !menu, replicating the SoMoE-19 hierarchy and number-key interaction.
//
// Menu input is REAL NUMBER KEYS, not typed chat. Neither of CSSharp's own
// menu types does that: ChatMenu makes the player type "!1"/"!2", and
// CenterHtmlMenu scrolls with W/S/E and re-renders its HTML every tick
// (which visibly flickers). Both were tried and rejected on those grounds.
//
// The default slot1..slot9 listeners are attempted, while the proven
// css_1..css_9/css_0 client bindings provide the reliable path. The menu
// model is renderer-independent: stable plain centre text is the default,
// HTML remains available, and a companion addon supplies the CS:S-style
// custom_hud_layout renderer.
//
// Every option re-invokes the existing chat command AS the selecting player
// via ExecuteClientCommandFromServer, so all the real logic (state
// machines, permission gates, logging) stays in one place - the menu is
// pure presentation. Entries without a working CS2 backend are omitted rather
// than shown as dead buttons.
public sealed partial class SoccerModMvpPlugin
{
    private const double MenuTimeoutSeconds = 30.0;

    private enum MenuRenderMode
    {
        Plain,
        Html,
        Classic,
    }

    private sealed class NumberMenuOption
    {
        public required string Text { get; init; }
        public required Action<CCSPlayerController> OnSelect { get; init; }
    }

    private sealed class NumberMenu
    {
        public required string Title { get; init; }
        public Action<CCSPlayerController>? OnBack { get; init; }
        public List<NumberMenuOption> Options { get; } = new();

        public void Add(string text, Action<CCSPlayerController> onSelect) =>
            Options.Add(new NumberMenuOption { Text = text, OnSelect = onSelect });
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
    private MenuRenderMode _menuRenderMode = MenuRenderMode.Plain;

    // Valve added custom_hud_layout in August 2026. CounterStrikeSharp
    // 1.0.373 does not yet expose its native per-player methods safely, so
    // the companion content addon's cs_script is the bridge. It acknowledges
    // a successful load through css_sm2menu_classic_ready. Until that happens,
    // Classic deliberately falls back to the stable plain renderer.
    private const string ClassicHudLayoutTargetName = "sm2_classic_menu_layout";
    private const string ClassicHudScriptTargetName = "sm2_classic_menu_script";
    private const string ClassicHudPayloadTargetPrefix = "sm2h|";
    internal const string ClassicHudLayoutResource = "panorama/layout/custom_game/soccermod_classic_menu.vxml";
    internal const string ClassicHudStyleResource = "panorama/styles/custom_game/soccermod_classic_menu.vcss";
    internal const string ClassicHudScriptResource = "maps/scripts/soccermod_classic_menu.vjs";
    private CBaseEntity? _classicHudLayoutEntity;
    private CBaseEntity? _classicHudScriptEntity;
    private readonly Dictionary<int, CBaseEntity> _classicHudPayloadEntities = new();
    private bool _classicHudReady;

    private bool UseClassicMenuRenderer =>
        _menuRenderMode == MenuRenderMode.Classic && _classicHudReady;

    private MenuRenderMode EffectiveMenuRenderMode =>
        UseClassicMenuRenderer ? MenuRenderMode.Classic
        : _menuRenderMode == MenuRenderMode.Html ? MenuRenderMode.Html
        : MenuRenderMode.Plain;

    private float MenuRedrawIntervalSeconds =>
        EffectiveMenuRenderMode == MenuRenderMode.Html
            ? _menuRedrawHtmlSeconds
            : _menuRedrawPlainSeconds;

    private void MenuOnLoad()
    {
        AddCommand("css_menu", "Open the SoccerMod menu.", OnMenuCommand);
        AddCommand("css_sm2menu_hud", "Admin: tune the menu panel redraw interval in seconds.", OnMenuHudCommand);
        AddCommand("css_sm2menu_mode", "Admin: switch the menu panel between plain, html, and classic rendering.", OnMenuModeCommand);
        AddCommand("css_sm2menu_classic_ready", "Internal: classic HUD script readiness handshake.", OnClassicHudReadyCommand);

        if (_menuRenderMode == MenuRenderMode.Classic)
        {
            Server.NextFrame(() => MenuTryInitializeClassicHud("plugin_load"));
        }

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

        if (page.HasBack && number == page.BackKey)
        {
            if (pageIndex > 0)
            {
                var prevPage = pageIndex - 1;
                _menuPageBySlot[player.Slot] = prevPage;
                DrawMenu(player, menu);
                Logger.LogInformation(
                    "[SM2DIAG] menu_page_back slot={Slot} page={Page} totalPages={Total}",
                    player.Slot,
                    prevPage + 1,
                    pages.Count);
            }
            else if (menu.OnBack is { } onBack)
            {
                CloseMenu(player.Slot, "parent_back_selected");
                onBack(player);
            }
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
            ClearMenuSurface(player, EffectiveMenuRenderMode);
        }
    }

    private void ClearMenuSurface(CCSPlayerController player, MenuRenderMode renderMode)
    {
        switch (renderMode)
        {
            case MenuRenderMode.Classic:
                SendClassicHudCommand(player.Slot, $"close|{player.Slot}");
                break;
            case MenuRenderMode.Html:
                player.PrintToCenterHtml(" ");
                break;
            default:
                player.PrintToCenter(" ");
                break;
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
    private const int MenuClassicPageCapacity = 7;

    private sealed class MenuPage
    {
        public bool ShowTitle;
        public required List<NumberMenuOption> Items;
        public bool HasBack;
        public bool BackGoesToParent;
        public bool HasNext;
        public bool UsesClassicKeys;
        public int PageIndex;
        public int TotalPages;

        // The OG SourceMod menu reserves 8 for Back and 9 for Next. The
        // proven plain/HTML fallback keeps contiguous keys because its tiny
        // panel made fixed gaps look broken during live tests.
        public int BackKey => UsesClassicKeys ? 8 : Items.Count + 1;
        public int NextKey => UsesClassicKeys ? 9 : Items.Count + (HasBack ? 2 : 1);
    }

    private List<MenuPage> BuildMenuPages(NumberMenu menu)
    {
        if (UseClassicMenuRenderer)
        {
            var classicPages = new List<MenuPage>();
            for (var classicIndex = 0; classicIndex < menu.Options.Count; classicIndex += MenuClassicPageCapacity)
            {
                var pageIndex = classicPages.Count;
                classicPages.Add(new MenuPage
                {
                    ShowTitle = true,
                    Items = menu.Options.GetRange(classicIndex, Math.Min(MenuClassicPageCapacity, menu.Options.Count - classicIndex)),
                    HasBack = pageIndex > 0 || menu.OnBack is not null,
                    BackGoesToParent = pageIndex == 0 && menu.OnBack is not null,
                    HasNext = classicIndex + MenuClassicPageCapacity < menu.Options.Count,
                    UsesClassicKeys = true,
                    PageIndex = pageIndex,
                });
            }

            if (classicPages.Count == 0)
            {
                classicPages.Add(new MenuPage
                {
                    ShowTitle = true,
                    Items = new List<NumberMenuOption>(),
                    HasBack = menu.OnBack is not null,
                    BackGoesToParent = menu.OnBack is not null,
                    UsesClassicKeys = true,
                    PageIndex = 0,
                });
            }

            foreach (var page in classicPages)
            {
                page.TotalPages = classicPages.Count;
            }

            return classicPages;
        }

        // HTML mode paginates too (it clips as well, just at a larger size).
        var isPlain = EffectiveMenuRenderMode == MenuRenderMode.Plain;
        var singlePageCapacity = isPlain ? MenuFirstPageCapacity : MenuHtmlFirstPageCapacity;
        var singleHasBack = menu.OnBack is not null;
        if (menu.Options.Count + (singleHasBack ? 1 : 0) <= singlePageCapacity)
        {
            return new List<MenuPage>
            {
                new()
                {
                    ShowTitle = true,
                    Items = menu.Options,
                    HasBack = singleHasBack,
                    BackGoesToParent = singleHasBack,
                    HasNext = false,
                    PageIndex = 0,
                    TotalPages = 1,
                },
            };
        }

        var pages = new List<MenuPage>();
        var index = 0;
        while (index < menu.Options.Count)
        {
            var showTitle = pages.Count == 0;
            var hasBack = !showTitle || menu.OnBack is not null;
            var baseCapacity = isPlain
                ? (showTitle ? MenuFirstPageCapacity : MenuLaterPageCapacity)
                : (showTitle ? MenuHtmlFirstPageCapacity : MenuHtmlLaterPageCapacity);
            var remaining = menu.Options.Count - index;
            // Tentative: could this page hold everything remaining while
            // reserving a slot only for Back (never Next)? If so it IS the
            // last page - the lookahead that avoids over-reserving nav
            // slots on boundary pages.
            var capacityIfLast = baseCapacity - (hasBack ? 1 : 0);
            var isLast = remaining <= capacityIfLast;
            var capacity = baseCapacity - (hasBack ? 1 : 0) - (isLast ? 0 : 1);
            var take = Math.Min(capacity, remaining);
            pages.Add(new MenuPage
            {
                ShowTitle = showTitle,
                Items = menu.Options.GetRange(index, take),
                HasBack = hasBack,
                BackGoesToParent = showTitle && menu.OnBack is not null,
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

    // Shared (key, text) list for every renderer. Parent navigation and page
    // navigation are model state now, not fake content options. "Back" means
    // parent on page one; "Prev" means an earlier page of the same menu.
    private static List<(int Key, string Text)> BuildMenuDisplayLines(MenuPage page)
    {
        var lines = new List<(int Key, string Text)>();
        for (var i = 0; i < page.Items.Count; i++)
        {
            lines.Add((i + 1, page.Items[i].Text));
        }

        if (page.HasBack)
        {
            lines.Add((page.BackKey, page.BackGoesToParent ? "Back" : "Prev"));
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

        switch (EffectiveMenuRenderMode)
        {
            case MenuRenderMode.Classic:
                DrawClassicMenu(player, menu.Title, page);
                break;
            case MenuRenderMode.Html:
                player.PrintToCenterHtml(BuildMenuHtml(menu.Title, page), MenuPanelDurationSeconds);
                break;
            default:
                player.PrintToCenter(BuildMenuPlainText(menu.Title, page));
                break;
        }
    }

    private static string EncodeClassicHudField(string value) =>
        value.Length == 0 ? "-" : Convert.ToHexString(Encoding.UTF8.GetBytes(value));

    private void DrawClassicMenu(CCSPlayerController player, string title, MenuPage page)
    {
        var labels = Enumerable.Repeat(string.Empty, 9).ToArray();
        foreach (var (key, text) in BuildMenuDisplayLines(page))
        {
            if (key is >= 1 and <= 9)
            {
                labels[key - 1] = text;
            }
        }

        SendClassicHudCommand(
            player.Slot,
            $"begin|{player.Slot}|{page.PageIndex + 1}|{page.TotalPages}|{EncodeClassicHudField(title)}");
        for (var index = 0; index < labels.Length; index++)
        {
            SendClassicHudCommand(
                player.Slot,
                $"line|{player.Slot}|{index + 1}|{EncodeClassicHudField(labels[index])}");
        }
        SendClassicHudCommand(player.Slot, $"show|{player.Slot}");
    }

    private void SendClassicHudCommand(int playerSlot, string command)
    {
        if (!_classicHudReady || _classicHudScriptEntity is not { IsValid: true } script)
        {
            return;
        }

        if (!_classicHudPayloadEntities.TryGetValue(playerSlot, out var payload)
            || !payload.IsValid)
        {
            payload = Utilities.CreateEntityByName<CBaseEntity>("info_target");
            if (payload is null || !payload.IsValid)
            {
                Logger.LogWarning("[SM2DIAG] classic_menu_payload_spawn_failed slot={Slot}", playerSlot);
                return;
            }

            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", $"{ClassicHudPayloadTargetPrefix}noop");
            payload.DispatchSpawn(keyValues);
            _classicHudPayloadEntities[playerSlot] = payload;
        }

        // RegisterCheatCommand cannot be used on a production server with
        // sv_cheats=0. RunScriptInput is the supported entity-I/O bridge; the
        // caller's targetname carries one compact command to the cs_script.
        payload.Entity!.Name = $"{ClassicHudPayloadTargetPrefix}{command}";
        script.AcceptInput("RunScriptInput", caller: payload, value: "Apply");
    }

    private void OnClassicHudReadyCommand(CCSPlayerController? player, CommandInfo command)
    {
        // The acknowledgement must come from the server-side cs_script, not
        // from a player who happens to type the internal command.
        if (player is not null)
        {
            return;
        }

        _classicHudReady = true;
        Logger.LogInformation("[SM2DIAG] classic_menu_ready layout={Layout}", ClassicHudLayoutResource);
        foreach (var (slot, menu) in _openMenus.ToArray())
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } menuPlayer)
            {
                menuPlayer.PrintToCenter(" ");
                DrawMenu(menuPlayer, menu);
            }
        }
    }

    private void MenuTryInitializeClassicHud(string reason)
    {
        if (_menuRenderMode != MenuRenderMode.Classic)
        {
            return;
        }

        if (_classicHudLayoutEntity is { IsValid: true }
            && _classicHudScriptEntity is { IsValid: true })
        {
            return;
        }

        _classicHudReady = false;
        MenuRemoveClassicHudEntities();

        var layout = Utilities.CreateEntityByName<CBaseEntity>("custom_hud_layout");
        if (layout is null || !layout.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] classic_menu_layout_spawn_failed reason={Reason}", reason);
            return;
        }

        using (var keyValues = new CEntityKeyValues())
        {
            keyValues.SetString("targetname", ClassicHudLayoutTargetName);
            keyValues.SetString("layout", ClassicHudLayoutResource);
            layout.DispatchSpawn(keyValues);
        }
        _classicHudLayoutEntity = layout;

        var script = Utilities.CreateEntityByName<CBaseEntity>("point_script");
        if (script is null || !script.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] classic_menu_script_spawn_failed reason={Reason}", reason);
            MenuRemoveClassicHudEntities();
            return;
        }

        using (var keyValues = new CEntityKeyValues())
        {
            keyValues.SetString("targetname", ClassicHudScriptTargetName);
            keyValues.SetString("cs_script", ClassicHudScriptResource);
            script.DispatchSpawn(keyValues);
        }
        _classicHudScriptEntity = script;
        Logger.LogInformation(
            "[SM2DIAG] classic_menu_initializing reason={Reason} layout={Layout} script={Script}",
            reason,
            ClassicHudLayoutResource,
            ClassicHudScriptResource);

        // Top-level ServerCommand can run before CSS command dispatch is ready
        // during a workshop changelevel. Probe through entity I/O after the
        // point_script is active; this also verifies the C# -> cs_script path.
        Server.NextFrame(() => MenuProbeClassicHud(script, reason));
        AddTimer(0.5f, () => MenuProbeClassicHud(script, $"{reason}_retry"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void MenuProbeClassicHud(CBaseEntity script, string reason)
    {
        if (_classicHudReady
            || _menuRenderMode != MenuRenderMode.Classic
            || !script.IsValid
            || _classicHudScriptEntity?.Index != script.Index)
        {
            return;
        }

        Logger.LogInformation("[SM2DIAG] classic_menu_probe reason={Reason}", reason);
        script.AcceptInput("RunScriptInput", value: "ReadyProbe");
    }

    private void MenuRemoveClassicHudEntities()
    {
        foreach (var payload in _classicHudPayloadEntities.Values)
        {
            if (payload.IsValid)
            {
                payload.AcceptInput("Kill");
            }
        }
        _classicHudPayloadEntities.Clear();

        if (_classicHudScriptEntity is { IsValid: true })
        {
            _classicHudScriptEntity.AcceptInput("Kill");
        }
        if (_classicHudLayoutEntity is { IsValid: true })
        {
            _classicHudLayoutEntity.AcceptInput("Kill");
        }
        _classicHudScriptEntity = null;
        _classicHudLayoutEntity = null;
        _classicHudReady = false;
    }

    private void MenuOnMapStart()
    {
        _openMenus.Clear();
        _menuExpiryBySlot.Clear();
        _menuNextRedrawBySlot.Clear();
        _menuPageBySlot.Clear();
        _classicHudPayloadEntities.Clear();
        _classicHudScriptEntity = null;
        _classicHudLayoutEntity = null;
        _classicHudReady = false;
        if (_menuRenderMode == MenuRenderMode.Classic)
        {
            AddTimer(
                0.25f,
                () => MenuTryInitializeClassicHud("map_start_plus_0_25s"),
                TimerFlags.STOP_ON_MAPCHANGE);
        }
    }

    private void MenuOnUnload()
    {
        foreach (var slot in _openMenus.Keys.ToArray())
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                ClearMenuSurface(player, EffectiveMenuRenderMode);
            }
        }
        _openMenus.Clear();
        MenuRemoveClassicHudEntities();
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

            // custom_hud_layout state persists client-side. Re-sending it on
            // a timer only wastes commands; updates happen on open/page/back.
            if (UseClassicMenuRenderer)
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
            // Tunes whichever legacy fallback mode is currently active.
            if (EffectiveMenuRenderMode == MenuRenderMode.Plain)
            {
                _menuRedrawPlainSeconds = seconds;
            }
            else if (EffectiveMenuRenderMode == MenuRenderMode.Html)
            {
                _menuRedrawHtmlSeconds = seconds;
            }
            SaveBallSettings("menu_hud_command");
        }

        command.ReplyToCommand(
            EffectiveMenuRenderMode == MenuRenderMode.Classic
                ? "[SM] classic HUD persists without timed redraws"
                : $"[SM] menu HUD redraw interval ({EffectiveMenuRenderMode.ToString().ToLowerInvariant()} mode): "
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
            MenuRenderMode? requestedMode = null;
            if (string.Equals(arg, "plain", StringComparison.OrdinalIgnoreCase))
            {
                requestedMode = MenuRenderMode.Plain;
            }
            else if (string.Equals(arg, "html", StringComparison.OrdinalIgnoreCase))
            {
                requestedMode = MenuRenderMode.Html;
            }
            else if (string.Equals(arg, "classic", StringComparison.OrdinalIgnoreCase))
            {
                requestedMode = MenuRenderMode.Classic;
            }

            if (requestedMode is { } mode && mode != _menuRenderMode)
            {
                var previousRenderer = EffectiveMenuRenderMode;
                foreach (var slot in _openMenus.Keys.ToArray())
                {
                    if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } menuPlayer)
                    {
                        ClearMenuSurface(menuPlayer, previousRenderer);
                    }
                }
                _openMenus.Clear();
                _menuExpiryBySlot.Clear();
                _menuNextRedrawBySlot.Clear();
                _menuPageBySlot.Clear();

                _menuRenderMode = mode;
                if (mode == MenuRenderMode.Classic)
                {
                    MenuTryInitializeClassicHud("mode_command");
                }
                else
                {
                    MenuRemoveClassicHudEntities();
                }
                SaveBallSettings("menu_mode_command");
            }
            else if (requestedMode == MenuRenderMode.Classic && !_classicHudReady)
            {
                MenuTryInitializeClassicHud("mode_command_retry");
            }
        }

        var fallback = _menuRenderMode == MenuRenderMode.Classic && !_classicHudReady
            ? " (content addon not ready; currently falling back to plain)"
            : string.Empty;
        command.ReplyToCommand(
            $"[SM] menu render mode: {_menuRenderMode.ToString().ToLowerInvariant()}{fallback} "
            + "(usage: css_sm2menu_mode <plain|html|classic>)");
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

    // Root order follows SoMoE-19's OpenMenuSoccer exactly for every feature
    // whose backend exists in this port. Shouts stay absent until their sound
    // and preference backend exists.
    private void OpenMainMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod" };
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "admin"))
        {
            menu.Add("Admin", OpenAdminMenu);
        }
        menu.Add("Ranking", OpenRankingMenu);
        menu.Add("Statistics", OpenStatisticsMenu);
        menu.Add("Positions", p => p.ExecuteClientCommandFromServer("css_pos"));
        menu.Add("Help", OpenHelpMenu);
        menu.Add("Settings", OpenClientSettingsMenu);
        menu.Add("Credits", OpenCreditsMenu);
        OpenNumberMenu(player, menu);
    }

    private void OpenRankingMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Ranking", OnBack = OpenMainMenu };
        menu.Add("Match Ranking", p => p.ExecuteClientCommandFromServer("css_rank"));
        menu.Add("Public Ranking", p => p.ExecuteClientCommandFromServer("css_prank"));
        menu.Add("Public Top 10", p => p.ExecuteClientCommandFromServer("css_top"));
        OpenNumberMenu(player, menu);
    }

    private void OpenStatisticsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Statistics", OnBack = OpenMainMenu };
        menu.Add("Personal Statistics", p => p.ExecuteClientCommandFromServer("css_stats"));
        OpenNumberMenu(player, menu);
    }

    private void OpenHelpMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Help", OnBack = OpenMainMenu };
        menu.Add("Commands", PrintHelp);
        menu.Add("Menu key binds", MenuSendBindInstructions);
        menu.Add("Connect order", p => p.ExecuteClientCommandFromServer("css_lc"));
        menu.Add("Move me to Spectator", p => p.ExecuteClientCommandFromServer("css_spec me"));
        menu.Add("Project links", PrintProjectLinks);
        OpenNumberMenu(player, menu);
    }

    private void OpenClientSettingsMenu(CCSPlayerController player)
    {
        var messages = SprintMessagesEnabled(player) ? "Enabled" : "Disabled";
        var menu = new NumberMenu { Title = "Soccer Mod - Client Settings", OnBack = OpenMainMenu };
        menu.Add($"Sprint messages: {messages}", p =>
        {
            p.ExecuteClientCommandFromServer("css_sprintset");
            Server.NextFrame(() =>
            {
                if (p.IsValid)
                {
                    OpenClientSettingsMenu(p);
                }
            });
        });
        menu.Add("Menu key binds", MenuSendBindInstructions);
        OpenNumberMenu(player, menu);
    }

    private void OpenCreditsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Credits", OnBack = OpenMainMenu };
        menu.Add("View Credits", PrintCredits);
        OpenNumberMenu(player, menu);
    }

    private void PrintCredits(CCSPlayerController player)
    {
        player.PrintToChat($" \x04[SoccerMod]\x01 {ModuleName} v{ModuleVersion}");
        player.PrintToChat(" \x04[SoccerMod]\x01 A CS2 port of SoMoE-19 (github.com/MK99MA/SoMoE-19)");
        player.PrintToChat(" \x04[SoccerMod]\x01 Port by Natsu");
    }

    private static void PrintProjectLinks(CCSPlayerController player)
    {
        player.PrintToChat(" \x04[SoccerMod]\x01 CS2 port: github.com/Deidakom/CS2_Soccer_Mod");
        player.PrintToChat(" \x04[SoccerMod]\x01 Original SoMoE-19: github.com/MK99MA/SoMoE-19");
    }

    private void OpenMatchMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Match", OnBack = OpenAdminMenu };
        menu.Add("Status", p => p.ExecuteClientCommandFromServer("css_match status"));
        menu.Add("Start", p => p.ExecuteClientCommandFromServer("css_match start"));
        menu.Add("Stop", p => p.ExecuteClientCommandFromServer("css_match stop"));
        menu.Add("Pause", p => p.ExecuteClientCommandFromServer("css_match pause"));
        menu.Add("Unpause", p => p.ExecuteClientCommandFromServer("css_match unpause"));
        menu.Add("Restart Round", p => p.ExecuteClientCommandFromServer("css_rr"));
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin", OnBack = OpenMainMenu };
        menu.Add("Match", OpenMatchMenu);
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "match"))
        {
            menu.Add("Referee", OpenRefereeMenu);
        }
        menu.Add("Spec Player", OpenSpecPlayerMenu);
        menu.Add("Reload Map", p => p.ExecuteClientCommandFromServer("css_maprr"));
        menu.Add("Settings", OpenServerSettingsMenu);
        OpenNumberMenu(player, menu);
    }

    private void OpenServerSettingsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Settings", OnBack = OpenAdminMenu };
        menu.Add("Kick Player", OpenKickPlayerMenu);
        menu.Add("Ban Player", OpenBanPlayerMenu);
        menu.Add("Admin List", p => p.ExecuteClientCommandFromServer("css_admin_list"));
        menu.Add("Ban List", p => p.ExecuteClientCommandFromServer("css_banlist"));
        OpenNumberMenu(player, menu);
    }

    private void OpenSpecPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Spec Player", OnBack = OpenAdminMenu };
        menu.Add("All Players", p => p.ExecuteClientCommandFromServer("css_spec all"));
        foreach (var target in Utilities.GetPlayers().Where(t =>
                     t.IsValid && t.UserId is not null && t.Team is CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName, p => p.ExecuteClientCommandFromServer($"css_spec #{userId}"));
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenKickPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Kick Player", OnBack = OpenServerSettingsMenu };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null))
        {
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName, p => p.ExecuteClientCommandFromServer($"css_kick #{userId}"));
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenBanPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Ban Player", OnBack = OpenServerSettingsMenu };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null))
        {
            var userId = target.UserId!.Value;
            menu.Add(target.PlayerName, p => p.ExecuteClientCommandFromServer($"css_ban #{userId} 0 menu_ban"));
        }
        OpenNumberMenu(player, menu);
    }
}
