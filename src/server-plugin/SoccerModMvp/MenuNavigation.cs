#nullable enable
using System.Text;

namespace SoccerModMvp;

internal static class MenuText
{
    // Dynamic menu text (player, clan, team, ban and preset names) is placed
    // inside PrintToCenterHtml markup. Escape only the characters the markup
    // gives meaning to; every other character (umlauts, emoji) stays as typed.
    internal static string EscapeHtml(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        if (value.IndexOfAny(['&', '<', '>']) < 0) return value;
        var escaped = new StringBuilder(value.Length + 16);
        foreach (var character in value)
        {
            escaped.Append(character switch
            {
                '&' => "&amp;",
                '<' => "&lt;",
                '>' => "&gt;",
                _ => character.ToString(),
            });
        }
        return escaped.ToString();
    }
}

// The trail of numbered menus one player walked through. When an option
// re-opens its own menu (a toggle, a +/- step, an unban) or Back returns to a
// parent, the player lands on the page they left instead of page one.
internal sealed class MenuPageMemory
{
    internal const int MaximumDepth = 16;
    private readonly List<(string Key, int Page)> _trail = new();

    internal int Depth => _trail.Count;

    // The player selected an option on `page` of the menu identified by `key`.
    internal void Leave(string key, int page)
    {
        _trail.RemoveAll(entry => entry.Key == key);
        _trail.Add((key, Math.Max(0, page)));
        if (_trail.Count > MaximumDepth) _trail.RemoveAt(0);
    }

    // A menu is opening: the remembered page, or 0 for a menu not on the
    // trail. Returning to an earlier menu forgets everything visited after it.
    internal int Enter(string key)
    {
        var index = _trail.FindLastIndex(entry => entry.Key == key);
        if (index < 0) return 0;
        var page = _trail[index].Page;
        _trail.RemoveRange(index, _trail.Count - index);
        return page;
    }

    internal void Clear() => _trail.Clear();
}
