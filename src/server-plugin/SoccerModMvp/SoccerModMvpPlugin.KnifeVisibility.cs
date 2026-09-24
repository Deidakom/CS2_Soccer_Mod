using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    // The knife is only the kick input here, not a prop anyone should see. Its
    // world model does not follow the hand on the football animations, so other
    // players see it standing in the grass under the owner's feet. A fully
    // transparent render colour (plus no shadow) hides that world model from
    // everyone; the owner's first-person knife is the separate view model
    // and is not touched.
    //
    // No CheckTransmit on purpose: that hook has crashed this server before
    // (see MapCleanup.cs), and a transparent weapon stays networked, so knife
    // contact and lag compensation keep working unchanged.
    //
    // Knives are re-created by GiveNamedItem, WeaponPaints skins and the cap
    // code, so this re-checks every owned knife a few times a second instead
    // of hooking each grant path. Already-hidden knives cost one alpha read.
    private const int KnifeVisibilityCheckEveryTicks = 16;

    private void KnifeVisibilityOnTick()
    {
        if (Server.TickCount % KnifeVisibilityCheckEveryTicks != 0)
        {
            return;
        }

        foreach (var player in Utilities.GetPlayers())
        {
            if (player is not { IsValid: true } || player.PlayerPawn.Value is not { IsValid: true } pawn)
            {
                continue;
            }

            var weapons = pawn.WeaponServices?.MyWeapons;
            if (weapons is null)
            {
                continue;
            }

            foreach (var handle in weapons)
            {
                if (handle.Value is { IsValid: true } weapon
                    && weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase)
                    && weapon.Render.A != 0)
                {
                    HideKnifeWorldModel(weapon);
                }
            }
        }
    }

    private void HideKnifeWorldModel(CBasePlayerWeapon knife)
    {
        knife.RenderMode = RenderMode_t.kRenderTransAlpha;
        knife.Render = Color.FromArgb(0, 255, 255, 255);
        knife.ShadowStrength = 0.0f;
        Utilities.SetStateChanged(knife, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(knife, "CBaseModelEntity", "m_clrRender");
        Utilities.SetStateChanged(knife, "CBaseModelEntity", "m_flShadowStrength");
        Logger.LogInformation(
            "[SM2DIAG] knife_hidden index={Index} item={Item}",
            knife.Index,
            knife.DesignerName);
    }
}
