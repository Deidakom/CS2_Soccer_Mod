using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-24 owner request: enemy names always visible, like teammates'.
// CS2 only draws overhead names for teammates (sv_teamid_overhead); enemies
// show a name only under the crosshair (mp_playerid). So every living player
// gets a point_worldtext name above the head that turns to face the viewer,
// and each client is sent only the ENEMY tags: its own and its teammates' are
// dropped in CheckTransmit (the native teammate names stay). Spectators see
// none (2026-09-25 owner). Only TransmitEntities.Remove is used - Add has crashed this server
// (MapCleanup.cs); Remove is the kind the old ball plugin used safely.
// The tags are not parented to the pawn (no hierarchy for CheckTransmit to
// break): they follow by Teleport every tick. Round restarts delete them and
// they are recreated. Toggle: css_sm2nametags on|off (persisted).
public sealed partial class SoccerModMvpPlugin
{
    // 2026-09-25 owner: 50% bigger and closer to the head. The tag sits a
    // little above the eyes (view offset: 64 standing, ~46 crouched), so it
    // stays just over the head in both stances.
    private const float NameTagAboveEyes = 12.0f;
    private const float NameTagFontSize = 48.0f;
    private const float NameTagWorldUnitsPerPx = 0.09f;
    // A free point_worldtext starts lying on its side (2026-09-24 screenshot:
    // vertical and mirrored). Roll 90 stands it up; AROUND_UP then turns it to
    // the viewer. Live-tunable with css_sm2nametags_angles <p> <y> <r>.
    private QAngle _nameTagAngles = new(0.0f, 270.0f, 90.0f);

    private sealed class NameTag
    {
        public required uint Pawn;
        public required CPointWorldText Text;
        public required string Name;
        public required CsTeam Team;
    }

    private readonly Dictionary<int, NameTag> _nameTags = new();

    private void NameTagsOnLoad()
    {
        AddCommand("css_sm2nametags", "Admin: always show enemy names over their heads (on|off).", OnNameTagsCommand);
        AddCommand("css_sm2nametags_angles", "Admin: name tag base angles <pitch> <yaw> <roll> (runtime only).", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            if (command.ArgCount >= 4
                && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var pitch)
                && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var yaw)
                && float.TryParse(command.GetArg(3), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var roll))
            {
                _nameTagAngles = new QAngle(pitch, yaw, roll);
            }
            command.ReplyToCommand($"[SM] Name tag angles: {_nameTagAngles.X:F0} {_nameTagAngles.Y:F0} {_nameTagAngles.Z:F0}");
        });
        RegisterListener<Listeners.CheckTransmit>(NameTagsCheckTransmit);
        RegisterListener<Listeners.OnClientDisconnect>(slot => RemoveNameTag(slot));
        RegisterListener<Listeners.OnMapEnd>(() => _nameTags.Clear());
    }

    private void NameTagsOnUnload()
    {
        foreach (var slot in _nameTags.Keys.ToList()) RemoveNameTag(slot);
    }

    private void OnNameTagsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        if (command.ArgCount >= 2 && TryParseTeamAppearanceToggle(command.GetArg(1), out var enabled))
        {
            var before = _menuParity.EnemyNameTags;
            _menuParity.EnemyNameTags = enabled;
            if (!SaveJsonAtomic(MenuParityFile, _menuParity)) _menuParity.EnemyNameTags = before;
            if (!_menuParity.EnemyNameTags) NameTagsOnUnload();
        }
        command.ReplyToCommand($"[SM] Enemy name tags: {(_menuParity.EnemyNameTags ? "on" : "off")} (usage: css_sm2nametags <on|off>)");
    }

    private static Color NameTagColor(CsTeam team) =>
        team == CsTeam.Terrorist ? Color.FromArgb(255, 255, 110, 110) : Color.FromArgb(255, 120, 175, 255);

    private void NameTagsOnTick()
    {
        if (!_menuParity.EnemyNameTags)
        {
            return;
        }

        var seen = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn
                || !IsAlive(pawn) || pawn.AbsOrigin is not { } origin)
            {
                continue;
            }

            seen.Add(player.Slot);
            var name = player.PlayerName ?? string.Empty;
            if (!_nameTags.TryGetValue(player.Slot, out var tag) || !tag.Text.IsValid || tag.Pawn != pawn.EntityHandle.Raw)
            {
                RemoveNameTag(player.Slot);
                if (CreateNameTag(player, pawn, name) is not { } created) continue;
                tag = created;
            }
            else if (tag.Name != name || tag.Team != player.Team)
            {
                tag.Name = name;
                tag.Team = player.Team;
                tag.Text.MessageText = name;
                tag.Text.Color = NameTagColor(player.Team);
                Utilities.SetStateChanged(tag.Text, "CPointWorldText", "m_messageText");
                Utilities.SetStateChanged(tag.Text, "CPointWorldText", "m_Color");
            }

            var eyes = pawn.ViewOffset.Z > 1.0f ? pawn.ViewOffset.Z : 64.0f;
            tag.Text.Teleport(new Vector(origin.X, origin.Y, origin.Z + eyes + NameTagAboveEyes), _nameTagAngles);
        }

        foreach (var slot in _nameTags.Keys.Where(s => !seen.Contains(s)).ToList()) RemoveNameTag(slot);
    }

    private NameTag? CreateNameTag(CCSPlayerController player, CCSPlayerPawn pawn, string name)
    {
        var text = Utilities.CreateEntityByName<CPointWorldText>("point_worldtext");
        if (text is null || !text.IsValid) return null;
        text.Entity!.Name = $"sm2_nametag_{player.Slot}";
        text.MessageText = name;
        text.FontName = "Arial";
        text.FontSize = NameTagFontSize;
        text.WorldUnitsPerPx = NameTagWorldUnitsPerPx;
        text.DepthOffset = 0.0f;
        text.Fullbright = true;
        text.Enabled = true;
        text.DrawBackground = false;
        text.JustifyHorizontal = PointWorldTextJustifyHorizontal_t.POINT_WORLD_TEXT_JUSTIFY_HORIZONTAL_CENTER;
        text.JustifyVertical = PointWorldTextJustifyVertical_t.POINT_WORLD_TEXT_JUSTIFY_VERTICAL_BOTTOM;
        text.ReorientMode = PointWorldTextReorientMode_t.POINT_WORLD_TEXT_REORIENT_AROUND_UP;
        text.Color = NameTagColor(player.Team);
        try
        {
            text.DispatchSpawn();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] nametag_spawn_failed slot={Slot}", player.Slot);
            if (text.IsValid) text.Remove();
            return null;
        }

        var tag = new NameTag { Pawn = pawn.EntityHandle.Raw, Text = text, Name = name, Team = player.Team };
        _nameTags[player.Slot] = tag;
        return tag;
    }

    private void RemoveNameTag(int slot)
    {
        if (!_nameTags.Remove(slot, out var tag)) return;
        try
        {
            if (tag.Text.IsValid) tag.Text.Remove();
        }
        catch
        {
            // Best effort during map end/unload; the state is gone either way.
        }
    }

    // Each client receives only enemy tags: never its own, never its
    // teammates' (CS2 already names teammates). Spectators receive all.
    private void NameTagsCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_nameTags.Count == 0) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true }) continue;
            var receiverTeam = receiver.Team;
            foreach (var (slot, tag) in _nameTags)
            {
                if (!tag.Text.IsValid) continue;
                // 2026-09-25 owner: spectators see no tags (only useful for
                // players); players see only the other team's tags.
                if (slot == receiver.Slot
                    || receiverTeam is not (CsTeam.Terrorist or CsTeam.CounterTerrorist)
                    || tag.Team == receiverTeam)
                {
                    info.TransmitEntities.Remove(tag.Text);
                }
            }
        }
    }
}
