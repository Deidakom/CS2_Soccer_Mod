using System;
using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// EXPERIMENT, 2026-08-30 user request: the user showed the real CS:S
// SoMoE menu, which uses SourceMod's classic SetHudTextParams/
// ShowSyncHudText (positioned, coloured, channel-based HUD text - the
// same mechanism drives its smooth "SPRINT [####] 100%" bar with zero
// flicker). Neither PrintToCenter (no styling) nor PrintToCenterHtml
// (ticks) is that.
//
// Reflection found CS2/CSSharp still exposes the underlying pieces:
// game_text (CGameText) with a real hudtextparms_t (X, Y, Color1, Color2,
// Effect, Channel) - the exact same struct name as SourceMod's version.
// UNVERIFIED whether it actually renders in CS2's Panorama-based HUD or
// is a vestigial Source 1 leftover, same caution as everything else this
// session that "looks right" via reflection alone. This command exists to
// find out empirically, tunable live so this doesn't need another
// redeploy per attempt.
public sealed partial class SoccerModMvpPlugin
{
    private CGameText? _gameTextTestEntity;

    private void GameTextTestOnLoad()
    {
        AddCommand(
            "css_sm2_gametext_test",
            "Server only: spawn/update a game_text HUD panel and display it to you. <x> <y> <effect> <channel> <holdSeconds>",
            OnGameTextTestCommand);
    }

    private void OnGameTextTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            command.ReplyToCommand("[SM] this command is for in-game players (needs a real activator to target)");
            return;
        }

        var x = ParseArgOr(command, 1, -1.0f);
        var y = ParseArgOr(command, 2, 0.35f);
        var effect = (byte)ParseArgOr(command, 3, 2.0f);
        var channel = (byte)ParseArgOr(command, 4, 1.0f);
        var holdSeconds = ParseArgOr(command, 5, 5.0f);

        if (_gameTextTestEntity is not { IsValid: true })
        {
            _gameTextTestEntity = Utilities.CreateEntityByName<CGameText>("game_text");
            if (_gameTextTestEntity is null)
            {
                command.ReplyToCommand("[SM2DIAG] game_text create failed");
                return;
            }

            _gameTextTestEntity.DispatchSpawn();
        }

        _gameTextTestEntity.Message = "SoccerMod Menu\n1. Cap\n2. Match\n3. Position\n4. Spectate\n5. Help\n0. Close";
        var parms = _gameTextTestEntity.TextParms;
        parms.X = x;
        parms.Y = y;
        parms.Effect = effect;
        parms.Channel = channel;
        parms.Color1 = Color.FromArgb(255, 255, 200, 0);
        parms.Color2 = Color.FromArgb(255, 255, 255, 255);

        _gameTextTestEntity.AcceptInput("Display", player, player);

        Logger.LogInformation(
            "[SM2DIAG] gametext_test slot={Slot} x={X:F2} y={Y:F2} effect={Effect} channel={Channel}",
            player.Slot, x, y, effect, channel);
        command.ReplyToCommand(
            $"[SM] game_text displayed: x={x:F2} y={y:F2} effect={effect} channel={channel} "
            + "(tell me if you see it and what it looks like)");
    }

    private static float ParseArgOr(CommandInfo command, int index, float fallback) =>
        command.ArgCount > index
        && float.TryParse(command.GetArg(index), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : fallback;
}
