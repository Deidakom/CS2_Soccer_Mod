using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private sealed record SprintBarHud(uint Controller);
    private readonly Dictionary<int, SprintBarHud> _sprintBars = new();
    private void SprintBarOnLoad()
    {
        AddCommand("css_sprintbar", "Your sprint bar: on|off|always (default: during activity).", (player, command) =>
        {
            if (player is not { IsValid: true }) return;
            var pref = SprintPreference(player);
            var value = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : pref.Hud == 2 ? "on" : "off";
            if (value is not ("on" or "off" or "always")) { command.ReplyToCommand("Use !sprintbar on|off|always."); return; }
            var before = pref.Hud; pref.Hud = value == "always" ? 0 : value == "on" ? 1 : 2;
            if (!SaveJsonAtomic(SprintPrefsFileName, _sprintPrefsStore))
            { pref.Hud = before; command.ReplyToCommand("Could not save your preference; unchanged."); return; }
            RemoveSprintBar(player.Slot);
            command.ReplyToCommand($"[SM] Sprint bar: {value}.");
        });
        RegisterListener<Listeners.OnClientDisconnect>(RemoveSprintBar);
    }
    private void RemoveSprintBar(int slot)
    {
        if (_sprintBars.Remove(slot, out var bar)
            && Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player
            && player.EntityHandle.Raw == bar.Controller)
            player.PrintToCenterHtml(" ");
    }
    private void ClearSprintBars()
    {
        foreach (var slot in _sprintBars.Keys.ToArray()) RemoveSprintBar(slot);
    }
    private void SprintBarOnTick()
    {
        var seen = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!player.IsValid || player.IsBot) continue;
            seen.Add(player.Slot);
            var pawn = player.PlayerPawn.Value;
            var eligible = IsEligiblePlayer(player) && pawn is { IsValid: true };
            var pref = SprintPreference(player);
            float amount = 100; bool active = false;
            if (eligible)
            {
                if (_menuParity.SprintStamina)
                { var state = StaminaFor(pawn!); amount = state.Stamina; active = state.Active; }
                else
                {
                    var state = GetSprintState(player.Slot); active = state.Phase == SprintPhase.Sprinting;
                    var remaining = Math.Max(0, state.PhaseEndTime - Server.TickedTime);
                    amount = active ? (float)(remaining / SprintDurationSeconds * 100)
                        : state.Phase == SprintPhase.Cooldown ? (float)((1 - remaining / SprintCooldownSeconds) * 100) : 100;
                }
            }
            if (!SprintBarView.Visible(pref.Hud, active, amount, eligible, _openMenus.ContainsKey(player.Slot), _sprintSuppressed))
            {
                if (_openMenus.ContainsKey(player.Slot)) _sprintBars.Remove(player.Slot);
                else RemoveSprintBar(player.Slot);
                continue;
            }
            if (_sprintBars.TryGetValue(player.Slot, out var existing) && existing.Controller != player.EntityHandle.Raw)
                RemoveSprintBar(player.Slot);
            _sprintBars.TryAdd(player.Slot, new(player.EntityHandle.Raw));
            // The client renders this in screen space. No eye-angle sampling or
            // world-entity teleporting, so mouse movement cannot drag the bar.
            var score = _matchPhase == MatchPhase.Live ? MatchScoreboardText(Server.TickedTime) : "";
            player.PrintToCenterHtml(SprintBarView.Html(amount, active, score), 1);

        }
        foreach (var slot in _sprintBars.Keys.Where(slot => !seen.Contains(slot)).ToArray()) RemoveSprintBar(slot);
    }
}
