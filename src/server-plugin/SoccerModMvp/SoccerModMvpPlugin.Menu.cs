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
        // SoMoE ITEMDRAW_DISABLED parity: an information row. It keeps its
        // slot in the numbering (exactly like the SourceMod radio menu) but
        // is drawn without the number and ignored when its number key is
        // pressed.
        public bool Enabled { get; init; } = true;
    }

    private sealed class NumberMenu
    {
        public required string Title { get; init; }
        public Action<CCSPlayerController>? OnBack { get; init; }
        public List<NumberMenuOption> Options { get; } = new();
        // Optional periodic rebuild while the menu stays open (SoMoE "Match
        // Log (Refreshes every 5 seconds)"): the opener is re-invoked in
        // place so live rows update without any keypress.
        public Action<CCSPlayerController>? AutoRefresh { get; init; }
        public double AutoRefreshSeconds { get; init; }

        public void Add(string text, Action<CCSPlayerController> onSelect) =>
            Options.Add(new NumberMenuOption { Text = text, OnSelect = onSelect });

        public void AddInfo(string text) =>
            Options.Add(new NumberMenuOption { Text = text, OnSelect = _ => { }, Enabled = false });
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
    private readonly HashSet<int> _spectatorMenuHintShownBySlot = new();
    internal const string SpectatorMenuKeysCommand = "spec_usenumberkeys_nobinds 0";

    private readonly Dictionary<int, NumberMenu> _openMenus = new();
    private readonly Dictionary<int, double> _menuExpiryBySlot = new();
    private readonly Dictionary<int, double> _menuNextRedrawBySlot = new();
    private readonly Dictionary<int, int> _menuPageBySlot = new();
    private readonly Dictionary<int, double> _menuNextRefreshBySlot = new();

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
        AddCommand("css_menukeys", "Show menu key setup, including spectator controls.", (player, _) =>
        { if (player is { IsValid: true, IsBot: false }) MenuSendBindInstructions(player); });
        MenuAuditOnLoad();
        AddCommand("css_admin", "Open the admin menu directly (admin flag required).", OnAdminMenuCommand);
        AddCommand("css_sm2menu_hud", "Admin: tune the menu panel redraw interval in seconds.", OnMenuHudCommand);
        AddCommand("css_sm2menu_mode", "Admin: switch the menu panel between plain, html, and classic rendering.", OnMenuModeCommand);
        AddCommand("css_sm2menu_classic_ready", "Internal: classic HUD script readiness handshake.", OnClassicHudReadyCommand);
        AddCommand("css_sm2publicmode", "Admin: toggle the public !menu (Help/Settings/Credits only for non-admins).", OnPublicModeCommand);

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
        AddCommandListener("slot10", (player, _) => OnMenuCloseKey(player, "slot10"), HookMode.Pre);
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
                "[SM2DIAG] menu_key source={Source} number={Number} slot={Slot} team={Team} hasOpenMenu={HasOpenMenu}",
                source,
                number,
                player.Slot,
                player.Team,
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
        if (!option.Enabled)
        {
            // Information row (SoMoE ITEMDRAW_DISABLED): owns the number,
            // does nothing.
            return HookResult.Handled;
        }

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
        _menuNextRefreshBySlot.Remove(slot);
        _bindReminderShownBySlot.Remove(slot);
        _spectatorMenuHintShownBySlot.Remove(slot);
    }

    // Single source of truth for the bind instructions - printed both on
    // first spawn and from !help (2026-08-30 user request), so the two can
    // never drift apart.
    // NOTE the space after \x01 on the paste line: without it the client
    // eats the first character and the player pastes "ind 1 css_1", which
    // silently fails. Do not remove it.
    internal void MenuSendBindInstructions(CCSPlayerController player)
    {
        player.PrintToChat(" \x04[SoccerMod]\x01 Open console (~) and paste both lines once:");
        player.PrintToChat($" \x01 {SpectatorMenuKeysCommand}");
        player.PrintToChat(" \x01 bind 1 css_1;bind 2 css_2;bind 3 css_3;bind 4 css_4;bind 5 css_5;bind 6 css_6;bind 7 css_7;bind 8 css_8;bind 9 css_9;bind 0 css_0;bind F10 css_menu");
        player.PrintToChat(" \x04[SoccerMod]\x01 F10 opens the menu, 1-9 pick, 0 closes. The first line allows your binds while spectating.");
        player.PrintToChat(" \x04[SoccerMod]\x01 While a menu is open, chat !1 to !9 also selects; !0 closes. !menukeys repeats this setup.");
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
        // 2026-09-02 user request: the sprint burst is easy to miss since
        // it has no on-screen prompt of its own.
        player.PrintToChat(" \x04[SoccerMod]\x01 Type !sprint or hold your +use key for a burst of speed.");
    }

    private void CloseMenu(int slot, string reason = "unspecified")
    {
        Logger.LogInformation("[SM2DIAG] menu_close slot={Slot} reason={Reason}", slot, reason);
        _openMenus.Remove(slot);
        _menuExpiryBySlot.Remove(slot);
        _menuNextRedrawBySlot.Remove(slot);
        _menuPageBySlot.Remove(slot);
        _menuNextRefreshBySlot.Remove(slot);
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
        // Spectator camera UI can consume raw digits before plugin bindings.
        // This client-only archived setting cannot be reliably forced by a server.
        // No alive/pawn requirement: admins must operate CAP after speccing everyone.
        if (player.Team == CsTeam.Spectator && !player.IsBot && _spectatorMenuHintShownBySlot.Add(player.Slot))
        {
            player.PrintToChat($" [SM] Spectator number keys: run {SpectatorMenuKeysCommand} in your own console once.");
            player.PrintToChat(" [SM] You can also select the shown number through chat (!1 to !9, !0 to close). !menukeys shows setup.");
        }
        _openMenus[player.Slot] = menu;
        _menuExpiryBySlot[player.Slot] = _menuParity.KeepMenusOpen ? double.PositiveInfinity : Server.TickedTime + MenuTimeoutSeconds;
        _menuNextRedrawBySlot[player.Slot] = 0.0;
        _menuPageBySlot[player.Slot] = 0;
        _menuNextRefreshBySlot[player.Slot] = menu.AutoRefresh is not null && menu.AutoRefreshSeconds > 0.0
            ? Server.TickedTime + menu.AutoRefreshSeconds
            : double.MaxValue;
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
    // Enabled=false rows (info rows) keep their number slot but are drawn
    // without it.
    private static List<(int Key, string Text, bool Enabled)> BuildMenuDisplayLines(MenuPage page)
    {
        var lines = new List<(int Key, string Text, bool Enabled)>();
        for (var i = 0; i < page.Items.Count; i++)
        {
            lines.Add((i + 1, page.Items[i].Text, page.Items[i].Enabled));
        }

        if (page.HasBack)
        {
            lines.Add((page.BackKey, page.BackGoesToParent ? "Back" : "Prev", true));
        }

        if (page.HasNext)
        {
            lines.Add((page.NextKey, "Next", true));
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
        foreach (var (_, text, _) in lines)
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
            var (key, text, enabled) = lines[i];
            var isLastLine = i == lines.Count - 1;
            if (!enabled)
            {
                html += $"<font class='fontSize-sm' color='#9a9a9a'>{text}{Pad(widest - text.Length)}</font>";
            }
            else
            {
                html += $"<font class='fontSize-sm' color='#ffffff'>{key}.</font> "
                    + $"<font class='fontSize-sm' color='#bfff00'>{text}{Pad(widest - text.Length - 3)}</font>";
            }
            html += isLastLine ? string.Empty : "<br>";
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

        foreach (var (key, text, enabled) in BuildMenuDisplayLines(page))
        {
            lines.Add(enabled ? $"{key}. {text}" : text);
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

        RemoveSprintBar(player.Slot);
        var pages = BuildMenuPages(menu);
        var pageIndex = NormalizePageIndex(player.Slot, pages.Count);
        var page = pages[pageIndex];

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
        foreach (var (key, text, _) in BuildMenuDisplayLines(page))
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
        _menuGameRulesProxy = null;
        _menuFlickerSuppressionActive = false;
        _openMenus.Clear();
        _menuExpiryBySlot.Clear();
        _menuNextRedrawBySlot.Clear();
        _menuPageBySlot.Clear();
        _menuNextRefreshBySlot.Clear();
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

    // 2026-09-01 flicker fix, ported from SwiftlyS2's MenuFlickeringFix
    // (source-verified: it is a plain gamerules schema write, NOT a binary
    // patch): while CCSGameRules.m_bGameRestart is true the client keeps a
    // PrintToCenterHtml panel steady instead of running its fade/pulse
    // animation. The reference's own guard doubles as our GoalPause safety:
    // when a REAL mp_restartgame is pending (RestartRoundTime >= now) the
    // engine owns the flag and we never touch it, so the goal-restart flow
    // is unaffected. Known reference limitation: no effect during warmup.
    private bool _menuFlickerSuppressionActive;
    private CCSGameRulesProxy? _menuGameRulesProxy;

    private void MenuApplyHtmlFlickerSuppression()
    {
        var wantActive = _sprintBars.Count > 0
            || (_openMenus.Count > 0 && EffectiveMenuRenderMode == MenuRenderMode.Html);
        if (!wantActive && !_menuFlickerSuppressionActive)
        {
            return;
        }

        if (_menuGameRulesProxy is not { IsValid: true })
        {
            _menuGameRulesProxy = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault(p => p.IsValid);
        }

        if (_menuGameRulesProxy?.GameRules is not { } rules)
        {
            return;
        }

        // A genuine restart (mp_restartgame from the goal flow) sets
        // RestartRoundTime in the future - hands off entirely until the
        // engine has finished and cleared it.
        if (rules.RestartRoundTime >= Server.CurrentTime)
        {
            _menuFlickerSuppressionActive = false;
            return;
        }

        if (rules.GameRestart != wantActive)
        {
            rules.GameRestart = wantActive;
            Utilities.SetStateChanged(_menuGameRulesProxy, "CCSGameRulesProxy", "m_pGameRules");
        }

        _menuFlickerSuppressionActive = wantActive;
    }

    // Called every tick from the main OnTick.
    private void MenuOnTick()
    {
        // Must run before the early return below: turning suppression OFF
        // after the last menu closes is exactly the _openMenus.Count == 0
        // case.
        MenuApplyHtmlFlickerSuppression();

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

            if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } inputPlayer
                || !_openMenus.TryGetValue(slot, out var inputMenu))
            {
                CloseMenu(slot, "player_or_menu_missing_on_tick");
                continue;
            }

            // Periodic in-place rebuild (Match Log): re-run the opener, then
            // put the page back where it was.
            if (inputMenu.AutoRefresh is { } refresh
                && _menuNextRefreshBySlot.TryGetValue(slot, out var nextRefresh)
                && now >= nextRefresh)
            {
                var keepPage = _menuPageBySlot.TryGetValue(slot, out var p) ? p : 0;
                refresh(inputPlayer);
                if (_openMenus.TryGetValue(slot, out var refreshed))
                {
                    _menuPageBySlot[slot] = keepPage;
                    _menuNextRefreshBySlot[slot] = now + refreshed.AutoRefreshSeconds;
                }
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
                _menuNextRefreshBySlot.Clear();

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
        var hasAdmin = HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "admin");

        // 2026-09-02 user request: a "public" mode that shrinks !menu down
        // to just Help/Settings/Credits for everyone WITHOUT the admin
        // flag, toggled from !menu -> Admin -> Settings. Admins always see
        // the full menu regardless - this only ever narrows what non-admins
        // see, it grants nothing and revokes nothing (every hidden entry's
        // own command keeps its own permission gate either way).
        if (_publicModeEnabled && !hasAdmin)
        {
            var publicMenu = new NumberMenu { Title = "Soccer Mod" };
            publicMenu.Add("Help", OpenHelpMenu);
            publicMenu.Add("Settings", OpenClientSettingsMenu);
            publicMenu.Add("Credits", OpenCreditsMenu);
            OpenNumberMenu(player, publicMenu);
            return;
        }

        var menu = new NumberMenu { Title = "Soccer Mod" };
        if (hasAdmin)
        {
            menu.Add("Admin", OpenAdminMenu);
        }
        // 2026-09-01 user request: Match and Reload Map moved out of the
        // Admin section - everyone can see them (the commands behind them
        // keep their own permission gates: css_match's privileged actions
        // stay "match"-flag gated, css_rr/css_maprr are already open to
        // everyone, and the self-service !rdy/!forfeit items inside the
        // Match menu were never gated in the first place).
        if (HasPublicControl(player)) menu.Add("Match", OpenMatchMenu);
        if (HasPublicControl(player, true)) menu.Add("Reload Map", p => p.ExecuteClientCommandFromServer("css_maprr"));
        // Cap: the SoMoE cap menu (Cap.cs). Hidden only while the KICKOFF
        // website has a cap active - it is already enforcing team
        // assignments (WebCap.cs), so an in-game cap would just fight it.
        if (_menuParity.IngameCap && !IsWebsiteCapActive() && HasPublicControl(player))
        {
            menu.Add("Cap", OpenCapMenu);
        }
        menu.Add("Ranking", OpenRankingMenu);
        menu.Add("Statistics", OpenStatisticsMenu);
        menu.Add("Positions", OpenCapPositionMenu);
        menu.Add("Help", OpenHelpMenu);
        menu.Add("Settings", OpenClientSettingsMenu);
        menu.Add("Credits", OpenCreditsMenu);
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
        menu.Add("Sprintsettings", OpenSprintSettingsMenu);
        menu.Add("Toggle first-person legs", p => RunBallMenuCommand(p, "css_legs", OpenClientSettingsMenu));
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
        player.PrintToChat(" \x04[SoccerMod]\x01 Official community: steamcommunity.com/groups/cs2soccermod");
    }

    // --- Match menu, 1:1 SoMoE match.sp OpenMatchMenu (2026-09-01) --------
    // "Start / Stop" and "Pause / Unpause" are single toggles, "Match
    // Settings" opens the settings tree, "Match Log" shows the live log,
    // and three disabled info rows mirror the current configuration.
    // Open to everyone (SoMoE publicmode 2, the live CS:S server's setting).
    private bool MatchRunning => _matchPhase is not (MatchPhase.Warmup or MatchPhase.Finished);

    private void ReopenNextFrame(CCSPlayerController player, Action<CCSPlayerController> opener)
    {
        Server.NextFrame(() =>
        {
            if (player.IsValid)
            {
                opener(player);
            }
        });
    }

    private void OpenMatchMenu(CCSPlayerController player)
    {
        if (!RequirePublicControl(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Match", OnBack = OpenMainMenu };
        menu.Add("Start / Stop", p =>
        {
            if (!RequirePublicControl(p)) return;
            if (MatchRunning)
            {
                StopMatch(p.PlayerName);
            }
            else
            {
                var halfSeconds = TryGetWebsiteCapReference(out var capHalfSeconds) ? capHalfSeconds : _periodLengthSeconds;
                StartMatch(halfSeconds, capHalfSeconds > 0.0f ? "cap_reference" : "default");
                AnnounceAll($" \x04[SM]\x01 {p.PlayerName} has started a match");
                AnnounceAll($" \x04[SM]\x01 {_teamNameCt} (CT) will face {_teamNameT} (T)");
            }
            ReopenNextFrame(p, OpenMatchMenu);
        });
        menu.Add("Pause / Unpause", p =>
        {
            if (!RequirePublicControl(p)) return;
            if (_matchPhase == MatchPhase.Paused)
            {
                ResumeFromPause("menu");
                AnnounceAll($" \x04[SM]\x01 {p.PlayerName} has unpaused the match");
            }
            else if (PauseMatch(out var failure))
            {
                AnnounceAll($" \x04[SM]\x01 {p.PlayerName} has paused the match");
            }
            else
            {
                p.PrintToChat($" \x04[SM]\x01 {failure}");
            }
            ReopenNextFrame(p, OpenMatchMenu);
        });
        if (_matchPhase == MatchPhase.Paused && _menuParity.ReadyMode != 0) menu.Add("Ready Check", OpenReadyMenu);
        menu.Add("Match Settings", p =>
        {
            if (MatchRunning)
            {
                p.PrintToChat(" \x04[SM]\x01 You can not use this option during a match");
                OpenMatchMenu(p);
                return;
            }

            OpenMatchSettingsMenu(p);
        });
        if (_menuParity.MatchLogEnabled && _matchLogLines.Count > 0)
        {
            menu.Add("Match Log", OpenMatchLogMenu);
        }
        menu.AddInfo($"Period length: {(int)_periodLengthSeconds} | Break length: {(int)_breakLengthSeconds}");
        menu.AddInfo($"T team name: {_teamNameT} | CT team name: {_teamNameCt}");
        menu.AddInfo($"GoldenGoal: {(_goldenGoalEnabled ? "On" : "Off")} | Match Log: {(_matchLogLines.Count > 0 ? "Yes" : "No")}");
        OpenNumberMenu(player, menu);
    }

    private void OpenMatchSettingsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Match Settings", OnBack = OpenMatchMenu };
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "admin")) menu.Add("Rules / Ready Check", OpenMatchRulesMenu);
        menu.Add("Period Length", p => MatchSettingsGuard(p, OpenMatchPeriodMenu));
        menu.Add("Break Length", p => MatchSettingsGuard(p, OpenMatchBreakMenu));
        menu.Add("Golden Goal", p => MatchSettingsGuard(p, OpenMatchGoldenGoalMenu));
        menu.Add("Team Name settings", p => MatchSettingsGuard(p, OpenMatchNameSettingsMenu));
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0, "admin"))
        {
            menu.Add("Match Log settings", p => MatchSettingsGuard(p, OpenLogSettingsMenu));
            menu.Add("Forfeit Vote settings", p => MatchSettingsGuard(p, OpenForfeitSettingsMenu));
            menu.Add("Match-Info settings", p => MatchSettingsGuard(p, OpenMatchInfoSettingsMenu));
        }
        OpenNumberMenu(player, menu);
    }

    private void MatchSettingsGuard(CCSPlayerController player, Action<CCSPlayerController> opener)
    {
        if (!RequirePublicControl(player, true)) return;
        if (MatchRunning)
        {
            player.PrintToChat(" \x04[SM]\x01 Can't change the settings during a match.");
            OpenMatchMenu(player);
            return;
        }

        opener(player);
    }

    private void SetPeriodLength(CCSPlayerController actor, float seconds)
    {
        if (!RequirePublicControl(actor, true) || MatchRunning) return;
        _periodLengthSeconds = seconds;
        SaveMatchSettings("period_length_menu");
        AnnounceAll($" \x04[SM]\x01 Period length was set to: {(int)seconds}.");
        ReopenNextFrame(actor, OpenMatchSettingsMenu);
    }

    private void OpenMatchPeriodMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Match Settings - Period Length", OnBack = OpenMatchSettingsMenu };
        menu.Add("15 Minutes", p => SetPeriodLength(p, 900.0f));
        menu.Add("10 Minutes", p => SetPeriodLength(p, 600.0f));
        menu.Add("7.5 Minutes", p => SetPeriodLength(p, 450.0f));
        menu.Add("Custom", p => BeginChatNumberInput(
            p,
            $"Type a value for the period length, 0 to stop. Current value is {(int)_periodLengthSeconds}.",
            1.0f,
            7200.0f,
            (pl, value) => SetPeriodLength(pl, MathF.Round(value)),
            pl => OpenMatchSettingsMenu(pl)));
        OpenNumberMenu(player, menu);
    }

    private void SetBreakLength(CCSPlayerController actor, float seconds)
    {
        if (!RequirePublicControl(actor, true) || MatchRunning) return;
        _breakLengthSeconds = seconds;
        SaveMatchSettings("break_length_menu");
        AnnounceAll($" \x04[SM]\x01 Break length was set to: {(int)seconds}.");
        ReopenNextFrame(actor, OpenMatchSettingsMenu);
    }

    private void OpenMatchBreakMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Match Settings - Break Length", OnBack = OpenMatchSettingsMenu };
        menu.Add("60 Seconds", p => SetBreakLength(p, 60.0f));
        menu.Add("30 Seconds", p => SetBreakLength(p, 30.0f));
        menu.Add("15 Seconds", p => SetBreakLength(p, 15.0f));
        menu.Add("5 Seconds", p => SetBreakLength(p, 5.0f));
        menu.Add("Custom", p => BeginChatNumberInput(
            p,
            $"Type a value for the break length, 0 to stop. Current value is {(int)_breakLengthSeconds}.",
            1.0f,
            600.0f,
            (pl, value) => SetBreakLength(pl, MathF.Round(value)),
            pl => OpenMatchSettingsMenu(pl)));
        OpenNumberMenu(player, menu);
    }

    private void OpenMatchGoldenGoalMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Match Settings - Golden Goal", OnBack = OpenMatchSettingsMenu };
        menu.Add("Enable", p =>
        {
            if (!RequirePublicControl(p, true) || MatchRunning) return;
            _goldenGoalEnabled = true;
            SaveMatchSettings("golden_goal_menu");
            p.PrintToChat(" \x04[SM]\x01 Golden Goal was enabled.");
            OpenMatchSettingsMenu(p);
        });
        menu.Add("Disable", p =>
        {
            if (!RequirePublicControl(p, true) || MatchRunning) return;
            _goldenGoalEnabled = false;
            SaveMatchSettings("golden_goal_menu");
            p.PrintToChat(" \x04[SM]\x01 Golden Goal was disabled.");
            OpenMatchSettingsMenu(p);
        });
        OpenNumberMenu(player, menu);
    }

    // SoMoE match.sp OpenMenuNameSettings / OpenMenuTeamName /
    // OpenMenuTeamNameList.
    private void OpenMatchNameSettingsMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Name Settings", OnBack = OpenMatchSettingsMenu };
        menu.Add("[Match] Change Terrorists Name", p => OpenTeamNameListMenu(p, CsTeam.Terrorist, permanent: false));
        menu.Add("[Match] Change CTs Name", p => OpenTeamNameListMenu(p, CsTeam.CounterTerrorist, permanent: false));
        menu.Add("[Perm] Change Terrorists Name", p => OpenTeamNameMenu(p, CsTeam.Terrorist));
        menu.Add("[Perm] Change CTs Name", p => OpenTeamNameMenu(p, CsTeam.CounterTerrorist));
        OpenNumberMenu(player, menu);
    }

    private void OpenTeamNameMenu(CCSPlayerController player, CsTeam team)
    {
        var isCt = team == CsTeam.CounterTerrorist;
        var menu = new NumberMenu { Title = isCt ? "Counter-Terrorists Name" : "Terrorists Name", OnBack = OpenMatchNameSettingsMenu };
        menu.Add("Clan Tag for Name", p => OpenTeamNameListMenu(p, team, permanent: true));
        menu.Add("Custom Name", p => BeginChatTextInput(
            p,
            $"Type in the name of the {(isCt ? "Counter-Terrorists" : "Terrorists")} team, !cancel to stop. Current name is {(isCt ? _teamNameCt : _teamNameT)}.",
            (pl, text) =>
            {
                SetTeamName(team, text, permanent: true, pl);
                ReopenNextFrame(pl, OpenMatchNameSettingsMenu);
            },
            pl => OpenMatchNameSettingsMenu(pl)));
        menu.Add(isCt ? "CT" : "T", p =>
        {
            SetTeamName(team, isCt ? "CT" : "T", permanent: true, p);
            ReopenNextFrame(p, OpenMatchNameSettingsMenu);
        });
        OpenNumberMenu(player, menu);
    }

    private void OpenTeamNameListMenu(CCSPlayerController player, CsTeam team, bool permanent)
    {
        var members = Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.Team == team).ToList();
        if (members.Count == 0)
        {
            player.PrintToChat(" \x04[SM]\x01 No targets found");
            OpenMatchNameSettingsMenu(player);
            return;
        }

        var menu = new NumberMenu
        {
            Title = team == CsTeam.CounterTerrorist ? "Select Name for CT" : "Select Name for T",
            OnBack = OpenMatchNameSettingsMenu,
        };
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            var tag = (member.Clan ?? string.Empty).Trim();
            if (tag.Length == 0)
            {
                menu.AddInfo("Empty Tag");
                continue;
            }

            if (!seen.Add(tag))
            {
                continue;
            }

            menu.Add(tag, p =>
            {
                SetTeamName(team, tag, permanent, p);
                ReopenNextFrame(p, OpenMatchNameSettingsMenu);
            });
        }
        OpenNumberMenu(player, menu);
    }

    // SoMoE match.sp OpenMatchLogMenu: newest first, self-refreshing.
    private void OpenMatchLogMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu
        {
            Title = "Match Log (Refreshes every 5 seconds)",
            OnBack = OpenMatchMenu,
            AutoRefresh = OpenMatchLogMenu,
            AutoRefreshSeconds = 5.0,
        };
        if (_menuParity.MatchLogCards && _refereeCardStore.Cards.Count > 0)
        {
            menu.Add("Card Log", OpenMatchCardLogMenu);
        }
        if (_matchLogLines.Count == 0)
        {
            menu.AddInfo("Nothing to display");
        }
        foreach (var line in _matchLogLines.Take(12))
        {
            menu.AddInfo(line);
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenMatchCardLogMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Match Card Log", OnBack = OpenMatchLogMenu };
        menu.Add("Refresh", OpenMatchCardLogMenu);
        if (_refereeCardStore.Cards.Count == 0)
        {
            menu.AddInfo("Nothing to display");
        }
        foreach (var card in _refereeCardStore.Cards.Take(12))
        {
            menu.AddInfo($"{card.Name}: {(card.Red ? "Red" : card.Yellow ? "Yellow" : "-")}");
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenAdminMenu(CCSPlayerController player)
    {
        // Match and Reload Map moved to the main menu (2026-09-01, open to
        // everyone) - not duplicated here.
        var menu = new NumberMenu { Title = "Soccer Mod - Admin", OnBack = OpenMainMenu };
        // 2026-09-01 user request: the ball tuning menu is root-only (not
        // just anyone holding the "ball" flag) - it's the whole physics
        // feel of the mod, more sensitive than a normal admin action.
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "root"))
        {
            menu.Add("Ball", OpenBallAdminMenu);
        }
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "match"))
        {
            menu.Add("Referee", OpenRefereeMenu);
        }
        // SoMoE OpenMenuAdmin "Training" (training.sp), see Training.cs.
        menu.Add("Training", OpenTrainingMenu);
        menu.Add("Spec Player", OpenSpecPlayerMenu);
        menu.Add("Punish Player", OpenPunishPlayerMenu);
        menu.Add("Settings", OpenServerSettingsMenu);
        // 2026-09-01 user request: root-only, same gate as the Ball entry -
        // only root can create/revoke the "soccermod" admin tier.
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "root"))
        {
            menu.Add("Player Promotion", OpenPlayerPromotionMenu);
        }
        OpenNumberMenu(player, menu);
    }

    // 2026-09-01 user request: direct !admin entry point for the admin
    // section (soccermod tier and up), no detour through !menu.
    private void OnAdminMenuCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] this command is for in-game players");
            return;
        }

        if (!HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "admin"))
        {
            command.ReplyToCommand("[SM] you do not have permission to use this command");
            return;
        }

        OpenAdminMenu(player);
    }

    // --- Punish menu (2026-09-01 user request) -------------------------
    // Kick/Slay/Suspend for the soccermod tier and up; permanent ban is a
    // root-only entry. Suspends are just time-limited bans through the
    // existing css_ban/BanStore machinery (ExpiresAtUtc + the kickid
    // enforcement on connect) - no new store, and the rights matrix is
    // ALSO enforced server-side in OnBanCommand, the menu only mirrors it.
    private void OpenPunishPlayerMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Punish", OnBack = OpenAdminMenu };
        foreach (var target in Utilities.GetPlayers().Where(t =>
                     t.IsValid && t.UserId is not null && t.Slot != player.Slot))
        {
            var userId = target.UserId!.Value;
            var targetName = target.PlayerName;
            menu.Add(targetName, p => OpenPunishActionMenu(p, userId, targetName));
        }
        OpenNumberMenu(player, menu);
    }

    private void OpenPunishActionMenu(CCSPlayerController player, int targetUserId, string targetName)
    {
        var menu = new NumberMenu { Title = $"Punish - {targetName}", OnBack = OpenPunishPlayerMenu };
        menu.Add("Kick", p => p.ExecuteClientCommandFromServer($"css_kick #{targetUserId}"));
        menu.Add("Slay", p => p.ExecuteClientCommandFromServer($"css_slay #{targetUserId}"));
        menu.Add("Suspend 10 min", p => p.ExecuteClientCommandFromServer($"css_ban #{targetUserId} 10 suspended"));
        menu.Add("Suspend 30 min", p => p.ExecuteClientCommandFromServer($"css_ban #{targetUserId} 30 suspended"));
        menu.Add("Suspend 1 hour", p => p.ExecuteClientCommandFromServer($"css_ban #{targetUserId} 60 suspended"));
        menu.Add("Suspend 1 day", p => p.ExecuteClientCommandFromServer($"css_ban #{targetUserId} 1440 suspended"));
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "root"))
        {
            menu.Add("Ban permanent", p => p.ExecuteClientCommandFromServer($"css_ban #{targetUserId} 0 banned"));
        }
        OpenNumberMenu(player, menu);
    }

    // Root-only (gated at the Settings entry): list active bans with their
    // remaining time and lift one per click.
    private void OpenUnbanMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Unban", OnBack = OpenServerSettingsMenu };
        var now = DateTime.UtcNow;
        foreach (var ban in _banStore.Bans
                     .Where(b => b.ExpiresAtUtc is null || b.ExpiresAtUtc > now)
                     .Take(24)
                     .ToList())
        {
            var steamId64 = ban.SteamId64;
            var remaining = ban.ExpiresAtUtc is { } expires
                ? $"{Math.Max(0.0, (expires - now).TotalMinutes):F0}m"
                : "perm";
            menu.Add($"{ban.Name} [{remaining}]", p =>
            {
                p.ExecuteClientCommandFromServer($"css_unban {steamId64}");
                Server.NextFrame(() =>
                {
                    if (p.IsValid)
                    {
                        OpenUnbanMenu(p);
                    }
                });
            });
        }
        OpenNumberMenu(player, menu);
    }

    // --- Player promotion menu (2026-09-01 user request) ---------------
    // Promotes/demotes the "soccermod" admin tier (Admin.cs: implies
    // "admin"+"match", NOT "ball"/"root") directly from the menu, no
    // console command needed. Root-only entry point (OpenAdminMenu above).
    private void OpenPlayerPromotionMenu(CCSPlayerController player)
    {
        if (!RootMenuAccess(player)) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Player Promotion", OnBack = OpenAdminMenu };
        foreach (var target in Utilities.GetPlayers().Where(t => t.IsValid && t.UserId is not null))
        {
            var targetSteamId = target.AuthorizedSteamID?.SteamId64;
            if (targetSteamId is not { } steamId64 || steamId64 == 0UL)
            {
                menu.Add($"{target.PlayerName} (not ready)", _ => { });
                continue;
            }

            if (HasFlag(steamId64, "root"))
            {
                // Display only - the menu can't demote root, and promoting
                // an already-root player would be a no-op anyway.
                menu.Add($"{target.PlayerName} [Root]", _ => { });
                continue;
            }

            var targetName = target.PlayerName;
            var targetCapture = target;
            var isSoccermodAdmin = DemoteSoccermodAdminWouldApply(steamId64);
            menu.Add(isSoccermodAdmin ? $"{targetName} [Admin]" : targetName, p =>
            {
                if (!RootMenuAccess(p)) return;
                if (isSoccermodAdmin)
                {
                    var demoted = DemoteSoccermodAdmin(steamId64);
                    if (demoted)
                    {
                        p.PrintToChat($" \x04[SM]\x01 Revoked SoccerMod admin from {targetName}.");
                        if (targetCapture.IsValid)
                        {
                            targetCapture.PrintToChat(" \x04[SM]\x01 Your SoccerMod admin access was revoked.");
                        }
                    }
                    else
                    {
                        p.PrintToChat($" \x04[SM]\x01 {targetName} has other admin flags - not touched.");
                    }
                }
                else
                {
                    PromoteToSoccermodAdmin(steamId64, targetName);
                    p.PrintToChat($" \x04[SM]\x01 Promoted {targetName} to SoccerMod admin.");
                    if (targetCapture.IsValid)
                    {
                        targetCapture.PrintToChat(" \x04[SM]\x01 You are now a SoccerMod admin (!menu -> Admin).");
                    }
                }

                Server.NextFrame(() =>
                {
                    if (p.IsValid)
                    {
                        OpenPlayerPromotionMenu(p);
                    }
                });
            });
        }

        OpenNumberMenu(player, menu);
    }

    // True exactly when this player's ONLY flag is "soccermod" - i.e. the
    // menu-created tier, safe to demote. Named to mirror DemoteSoccermodAdmin's
    // own protection rule so the label logic can never drift from the
    // actual demote behaviour.
    private bool DemoteSoccermodAdminWouldApply(ulong steamId64)
    {
        var entry = _adminStore.Admins.FirstOrDefault(a => a.SteamId64 == steamId64);
        return entry is { Flags.Count: 1 } && entry.Flags[0].Equals("soccermod", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenServerSettingsMenu(CCSPlayerController player)
    {
        // Kick/Ban moved into the Punish Player menu (2026-09-01) - this
        // submenu keeps the read-only lists plus the root-only unban.
        var menu = new NumberMenu { Title = "Soccer Mod - Admin - Settings", OnBack = OpenAdminMenu };
        menu.Add($"Public access: {new[] { "Admins", "CAP / Match", "Free for all" }[_menuParity.PublicAccess]}", p => EditParity(p, s => s.PublicAccess = (s.PublicAccess + 1) % 3, OpenServerSettingsMenu));
        menu.Add("Admin List", p => p.ExecuteClientCommandFromServer("css_admin_list"));
        menu.Add("Ban List", p => p.ExecuteClientCommandFromServer("css_banlist"));
        menu.Add("Misc Settings", OpenMiscSettingsMenu);
        menu.Add("Skin Settings", OpenSkinSettingsMenu);
        menu.Add("Chat Settings", OpenChatSettingsMenu);
        menu.Add("Sound Control", OpenSoundSettingsMenu);
        menu.Add("Training Settings", OpenTrainingDrillsMenu);
        menu.Add($"Public Mode: {(_publicModeEnabled ? "on" : "off")}", p =>
            RunBallMenuCommand(p, $"css_sm2publicmode {(_publicModeEnabled ? "off" : "on")}", OpenServerSettingsMenu));
        if (HasFlag(player.AuthorizedSteamID?.SteamId64 ?? 0UL, "root"))
        {
            menu.Add("Unban", OpenUnbanMenu);
            menu.Add("Admin flag editor / Offline SteamID", OpenAdminEditor);
        }
        OpenNumberMenu(player, menu);
    }

    // 2026-09-02 user request: everyone without the "admin" flag sees only
    // Help/Settings/Credits in !menu when this is on. Admins are unaffected
    // (OpenMainMenu checks the flag first). Nothing this hides loses its
    // own permission gate - a curious non-admin typing !cap or !training
    // directly still gets exactly the same response as always.
    private bool _publicModeEnabled;

    private void OnPublicModeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            _publicModeEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
            SaveMatchSettings("publicmode_command");
        }

        command.ReplyToCommand($"[SM] public menu mode: {(_publicModeEnabled ? "on" : "off")} (usage: css_sm2publicmode <on|off>)");
    }

    // Shared formatting and command bridge for numbered menus.
    private static string BallMenuNumber(float value) =>
        value.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

    private void RunBallMenuCommand(CCSPlayerController player, string command, Action<CCSPlayerController> reopen)
    {
        player.ExecuteClientCommandFromServer(command);
        Server.NextFrame(() =>
        {
            if (player.IsValid)
            {
                reopen(player);
            }
        });
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

}
