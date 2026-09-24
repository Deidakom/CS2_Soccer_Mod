using SoccerModMvp;

internal static class MenuNavigationChecks
{
    internal static void Run()
    {
        static void Check(bool condition, string why) { if (!condition) throw new Exception(why); }

        Check(MenuText.EscapeHtml("<font color='red'>Bob & Co</font>") == "&lt;font color='red'&gt;Bob &amp; Co&lt;/font&gt;",
            "Markup characters in names must be escaped.");
        Check(MenuText.EscapeHtml("Jörg ⚽ Müller") == "Jörg ⚽ Müller" && MenuText.EscapeHtml("Admin") == "Admin",
            "Umlauts, emoji and plain labels must stay exactly as typed.");
        Check(MenuText.EscapeHtml(null) == "" && MenuText.EscapeHtml("") == "", "Missing text renders as empty.");
        Check(MenuText.EscapeHtml("&amp;") == "&amp;amp;", "Escaping is literal, never interpreted.");

        var memory = new MenuPageMemory();
        Check(memory.Enter("Unknown") == 0, "A menu never visited opens on page one.");
        memory.Leave("Misc", 2);
        Check(memory.Enter("Misc") == 2, "A toggle that re-opens its menu returns to the same page.");
        Check(memory.Enter("Misc") == 0, "A remembered page is used once, not forever.");

        memory.Leave("Main", 1);
        memory.Leave("Admin", 2);
        memory.Leave("Settings", 3);
        Check(memory.Enter("Admin") == 2, "Back returns to the parent's page.");
        Check(memory.Enter("Settings") == 0, "Returning to a parent forgets the pages visited below it.");
        Check(memory.Enter("Main") == 1 && memory.Depth == 0, "Back to the root restores its page and empties the trail.");

        memory.Leave("A", 1);
        memory.Leave("B", 1);
        memory.Leave("A", 3);
        Check(memory.Depth == 2 && memory.Enter("A") == 3 && memory.Enter("B") == 1,
            "Leaving a menu again replaces its older entry.");

        for (var i = 0; i < 40; i++) memory.Leave("menu" + i, i);
        Check(memory.Depth == MenuPageMemory.MaximumDepth && memory.Enter("menu0") == 0 && memory.Enter("menu39") == 39,
            "The trail is bounded and keeps the most recent menus.");
        memory.Leave("negative", -4);
        Check(memory.Enter("negative") == 0, "Invalid pages are clamped to page one.");
        memory.Leave("x", 1);
        memory.Clear();
        Check(memory.Depth == 0 && memory.Enter("x") == 0, "Clearing forgets the whole trail.");
        Console.WriteLine("Menu text and page memory checks passed (14 scenarios).");
    }
}
