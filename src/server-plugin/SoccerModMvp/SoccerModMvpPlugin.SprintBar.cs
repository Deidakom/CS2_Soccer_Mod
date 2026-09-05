using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using System.Drawing;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private sealed record SprintBarEntity(uint Controller, CPointWorldText Text);
    private readonly Dictionary<int, SprintBarEntity> _sprintBars = new();
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
        RegisterListener<Listeners.CheckTransmit>(infoList =>
        {
            foreach (var (info, recipient) in infoList)
                foreach (var bar in _sprintBars.Values)
                    if (bar.Text.IsValid && (recipient is null || !recipient.IsValid || recipient.EntityHandle.Raw != bar.Controller))
                        info.TransmitEntities.Remove(bar.Text);
        });
    }
    private void RemoveSprintBar(int slot)
    {
        if (_sprintBars.Remove(slot, out var bar) && bar.Text.IsValid) bar.Text.Remove();
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
            if (!SprintBarView.Visible(pref.Hud, active, amount, eligible, _openMenus.ContainsKey(player.Slot), _sprintSuppressed)
                || pawn?.AbsOrigin is not { } origin)
            { RemoveSprintBar(player.Slot); continue; }
            if (_sprintBars.TryGetValue(player.Slot, out var existing) && existing.Controller != player.EntityHandle.Raw)
                RemoveSprintBar(player.Slot);
            if (!_sprintBars.TryGetValue(player.Slot, out var bar) || !bar.Text.IsValid)
            {
                var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
                if (text is null || !text.IsValid) continue;
                text.MessageText = SprintBarView.Text(amount); text.FontName = "Consolas";
                text.FontSize = 32; text.WorldUnitsPerPx = .005f;
                text.Fullbright = true; text.Enabled = true; text.DrawBackground = false;
                text.JustifyHorizontal = (PointWorldTextJustifyHorizontal_t)1;
                text.JustifyVertical = (PointWorldTextJustifyVertical_t)1;
                text.Color = Color.FromArgb(220, 220, 220, 220);
                // Track ownership before spawning: only the owner may receive this entity.
                bar = new(player.EntityHandle.Raw, text); _sprintBars[player.Slot] = bar;
                text.DispatchSpawn();
            }
            var angles = pawn.V_angle;
            var eye = N(origin) + new System.Numerics.Vector3(pawn.ViewOffset.X, pawn.ViewOffset.Y, pawn.ViewOffset.Z);
            if (_thirdPersonCamBySlot.TryGetValue(player.Slot, out var camera) && camera.IsValid && camera.AbsOrigin is { } cameraOrigin)
                eye = N(cameraOrigin);
            bar.Text.Teleport(position: C(SprintBarView.Position(eye, angles.X, angles.Y)), angles: new QAngle(0, angles.Y + 270, 90 - angles.X));
            if (Server.TickCount % 8 == 0)
            {
                var label = SprintBarView.Text(amount);
                if (bar.Text.MessageText != label)
                {
                    bar.Text.MessageText = label;
                    Utilities.SetStateChanged(bar.Text, "CPointWorldText", "m_messageText");
                }
            }
        }
        foreach (var slot in _sprintBars.Keys.Where(slot => !seen.Contains(slot)).ToArray()) RemoveSprintBar(slot);
    }
}
