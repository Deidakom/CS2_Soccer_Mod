using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// Map profiles (MapProfiles.cs), 2026-09-27 test server: soccer_multi_indoor
// (port of ka_soccer_multiindoor_b) has four pitches. The plugin works in the
// active pitch's local coordinates; admins pick the pitch in !menu (the map's
// control room is sealed), and after every round restart the chosen pitch is
// switched back on if the map came back on another one.
public sealed partial class SoccerModMvpPlugin
{
    private string? _pitchMode;
    private string? _pitchModeMap;

    private MapProfile? ActiveProfile => MapProfiles.For(_currentMapName);

    private PitchFrame? ActiveFrame
    {
        get
        {
            if (ActiveProfile is not { } profile) return null;
            if (_pitchModeMap != profile.MapName) { _pitchModeMap = profile.MapName; _pitchMode = profile.DefaultMode; }
            return profile.Frame(_pitchMode ?? profile.DefaultMode) ?? profile.Frames[0];
        }
    }

    // A supported non-v8 map: the ball is taken over like on the stadium.
    private bool IsSupportedMap => IsFoundationMap(_currentMapName) || ActiveProfile is not null;

    // The active pitch's floor (v8: the stadium plane).
    private float PitchFloorZ => ActiveFrame?.FloorZ ?? StadiumPitchPlaneZ;

    // A profile ball of another size is scaled to the Jabulani the numbers are
    // tuned for (BallSize.cs); soccer_multi_indoor v3 has the Jabulani itself.
    private float BallModelScale => ActiveProfile is { } p ? DefaultBallCollisionRadius / p.BallRadius : 1.0f;

    private Vector ToPitchLocal(Vector world) =>
        ActiveFrame is { } f ? V(MapProfiles.ToLocal(f, world.X, world.Y, world.Z)) : world;

    private Vector ToPitchWorld(Vector local) =>
        ActiveFrame is { } f ? V(MapProfiles.ToWorld(f, local.X, local.Y, local.Z)) : local;

    private static Vector V((float X, float Y, float Z) p) => new(p.X, p.Y, p.Z);

    // Goal geometry: the active pitch's, else the calibrated v8 values.
    private float GoalLineNow => ActiveFrame?.GoalLineY ?? _goalLineY + GoalShiftY;
    private float GoalHalfWidthNow => ActiveFrame?.GoalHalfWidth ?? _goalHalfWidthX;
    private float GoalApertureMaxNow => ActiveFrame?.GoalHeight ?? _goalApertureMaxZ;
    private float GoalPlaneNow => ActiveFrame is { } f ? f.GoalLineY + BallCollisionRadius : GoalPlaneY;
    private bool GoalsOnThisPitch => ActiveFrame?.HasGoals ?? true;

    // Where the v8 goal extras (GoalFrame, DynamicNet, NetPocket) go: the
    // stadium's two goals on v8, the active pitch's on a profile pitch with v8
    // goals (the posts are centred on the goal line there; the net frame sits
    // 3 u behind it, as on v8 where the posts are centred 3 u in front of
    // y 1384), else none. Key = what they were spawned for.
    private readonly record struct NetGoals(float Cx, float Cy, float LineY, float FloorZ, string Key);

    private NetGoals? NetGoalsHere =>
        IsFoundationMap(_currentMapName) ? new NetGoals(0.0f, 0.0f, GoalFrameLineY + GoalShiftY, StadiumPitchPlaneZ, "v8")
        : ActiveFrame is { V8Goals: true } f ? new NetGoals(f.CenterX, f.CenterY, f.GoalLineY + 3.0f, f.FloorZ, f.Mode)
        : null;

    // World origin of a goal's net frame (side +1 = +y goal), local x offset.
    private static Vector NetGoalOrigin(NetGoals g, float side, float localX = 0.0f) =>
        new(g.Cx + side * localX, g.Cy + side * g.LineY, g.FloorZ);

    private void MapProfileOnLoad()
    {
        // css_sm2pitch belongs to PitchDesign.cs (pitch design status).
        AddCommand("css_sm2pitchsize", "Admin: map pitch size (profile maps): css_sm2pitchsize <mode>.", OnPitchCommand);
    }

    // Round start: seal the control room / spawner and switch the map's
    // spawns/teleports back to the chosen pitch (the round restart runs the
    // map's own start logic again).
    private void MapProfileOnRoundStart()
    {
        if (ActiveProfile is not { } profile) return;
        Server.NextFrame(() => RemoveProfileEntities(profile));
        AddTimer(0.6f, () => ReapplyPitchMode("round_start"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private void RemoveProfileEntities(MapProfile profile)
    {
        var names = profile.RemoveEntityNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = 0;
        foreach (var entity in Utilities.GetAllEntities())
        {
            if (!entity.IsValid || entity.Entity?.Name is not { } name || !names.Contains(name)) continue;
            entity.Remove();
            removed++;
        }
        if (removed > 0) Logger.LogInformation("[SM2DIAG] map_profile_removed map={Map} count={Count}", profile.MapName, removed);
    }

    // The map comes back on its default pitch after a round restart. Pressing
    // the chosen pitch's button again is harmless when it already is on it
    // (its teleports only cover the other pitches), so it is simply pressed.
    private void ReapplyPitchMode(string reason)
    {
        if (ActiveProfile is not { } profile || ActiveFrame is not { } wanted) return;
        PressPitchButton(profile, wanted, resetBall: false);
        Logger.LogInformation("[SM2DIAG] map_profile_reapply reason={Reason} mode={Mode}", reason, wanted.Mode);
    }

    // Switches the map to a pitch through its own button chain (spawns,
    // teleports; v3: the stadium stands at every pitch, the one ball is the
    // plugin's and is put on the new pitch's centre spot).
    private bool SetPitchMode(string mode, string reason)
    {
        if (ActiveProfile is not { } profile || profile.Frame(mode) is not { } frame) return false;
        _pitchModeMap = profile.MapName;
        _pitchMode = frame.Mode;
        PressPitchButton(profile, frame, resetBall: true);
        // goals of the new pitch (v8 frame / moving net / pocket where it has them)
        AddTimer(0.5f, () => PitchGoalsRefresh("pitch_mode"), TimerFlags.STOP_ON_MAPCHANGE);
        Logger.LogInformation("[SM2DIAG] map_profile_mode map={Map} mode={Mode} reason={Reason}", profile.MapName, frame.Mode, reason);
        return true;
    }

    private void PressPitchButton(MapProfile profile, PitchFrame frame, bool resetBall)
    {
        if (resetBall)
        {
            _trainingBalls.Values.ToList().ForEach(t => { if (t.Entity.IsValid) t.Entity.Remove(); });
            _trainingBalls.Clear();
        }
        var pressed = frame.Button.Length == 0;   // a one-pitch map (the indoor hall) has no switch
        foreach (var button in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("func_button"))
        {
            if (!button.IsValid || button.Entity?.Name != frame.Button) continue;
            button.AcceptInput("Unlock");
            button.AcceptInput("Press");
            pressed = true;
        }
        if (!pressed) Logger.LogWarning("[SM2DIAG] map_profile_button_missing map={Map} button={Button}", profile.MapName, frame.Button);
        AddTimer(0.2f, () => MovePlayersToPitch(frame), TimerFlags.STOP_ON_MAPCHANGE);
        if (resetBall) AddTimer(0.3f, () => ResetBallForGoalSafety("pitch_mode"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    // 2026-09-28 owner: switching the pitch left the players where they were.
    // The map's teleports aimed at the stadium spawners v3 removed (and put
    // everyone on one spot): players not on the pitch go to their team's
    // spawns there, one spawn each. The first version moved nobody in the
    // owner's test (no error): every call now logs what it found, and without
    // spawns on the pitch a team stands in its own half (T -y, CT +y).
    private void MovePlayersToPitch(PitchFrame frame)
    {
        var moved = 0;
        var report = new List<string>();
        foreach (var (team, spawnClass, half) in new[] { (CsTeam.Terrorist, "info_player_terrorist", -1.0f), (CsTeam.CounterTerrorist, "info_player_counterterrorist", 1.0f) })
        {
            var all = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(spawnClass).Where(s => s.IsValid).ToList();
            var spawns = all.Where(s => s.AbsOrigin is { } o && frame.Contains(o.X, o.Y)).ToList();
            // the plugin's own test (pawn life state), not the controller's
            // PawnIsAlive flag
            var players = Utilities.GetPlayers().Where(p => IsEligiblePlayer(p) && p.Team == team).ToList();
            var next = 0;
            foreach (var player in players)
            {
                if (player.PlayerPawn.Value is not { IsValid: true } pawn || pawn.AbsOrigin is not { } pos || frame.Contains(pos.X, pos.Y)) continue;
                var slot = next++;
                if (spawns.Count > 0)
                {
                    var spawn = spawns[slot % spawns.Count];
                    pawn.Teleport(spawn.AbsOrigin, spawn.AbsRotation, new Vector(0, 0, 0));
                }
                else
                {
                    var spread = ((slot % 6) - 2.5f) * 64.0f;
                    pawn.Teleport(new Vector(frame.CenterX + spread, frame.CenterY + half * frame.GoalLineY * 0.5f, frame.FloorZ + 8.0f),
                        new QAngle(0.0f, half > 0 ? 270.0f : 90.0f, 0.0f), new Vector(0, 0, 0));
                }
                moved++;
            }
            report.Add($"{team}: spawns {spawns.Count}/{all.Count} players {players.Count} moved {next}");
        }
        Logger.LogInformation("[SM2DIAG] map_profile_players_move mode={Mode} moved={Moved} {Report}", frame.Mode, moved, string.Join("; ", report));
    }

    // The v8 goal extras follow the pitch: drop them now, the Ensure calls put
    // them on the new pitch (or none where it has no v8 goals).
    private void PitchGoalsRefresh(string reason)
    {
        GoalFrameRemove();
        DynamicNetRemove();
        NetPocketRemove();
        GoalFrameEnsure(reason);
        DynamicNetEnsure(reason);
        NetPocketEnsure(reason);
    }

    private CPhysicsPropMultiplayer? FindProfileBall(MapProfile profile)
    {
        var names = profile.BallNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>(profile.BallDesignerName)
            .FirstOrDefault(e => e.IsValid && e.Entity?.Name is { } n && (names.Contains(n) || n == OwnedBallTargetName));
        // The map's ball is a plain prop_physics; the plugin only uses members
        // prop_physics_multiplayer inherits from it.
        return found is null ? null : new CPhysicsPropMultiplayer(found.Handle);
    }

    private void OnPitchCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        if (ActiveProfile is not { } profile)
        {
            command.ReplyToCommand("[SM] This map has only one pitch.");
            return;
        }
        var mode = command.ArgCount > 1 ? command.GetArg(1).ToLowerInvariant() : "";
        if (mode.Length == 0)
        {
            command.ReplyToCommand($"[SM] pitch={ActiveFrame?.Mode} modes={string.Join(",", profile.Frames.Select(f => f.Mode))}");
            return;
        }
        if (MatchRunning || CapRunning)
        {
            command.ReplyToCommand("[SM] Not while a match or cap is running.");
            return;
        }
        command.ReplyToCommand(SetPitchMode(mode, "command") ? $"[SM] Pitch: {mode}" : "[SM] Unknown pitch.");
    }

    // !menu - Admin - Pitch size (profile maps only).
    private void OpenPitchSizeMenu(CCSPlayerController player)
    {
        if (ActiveProfile is not { } profile) return;
        var menu = new NumberMenu { Title = "Soccer Mod - Pitch size", OnBack = OpenAdminMenu };
        foreach (var frame in profile.Frames)
        {
            var f = frame;
            menu.Add($"{(ActiveFrame?.Mode == f.Mode ? "★ " : "")}{f.Label}", p =>
            {
                if (!HasFlag(SteamIdOf(p), "admin")) return;
                if (MatchRunning || CapRunning)
                {
                    p.PrintToChat(" \u0004[SM]\u0001 Pitch size can't change while a match or cap is running.");
                    return;
                }
                SetPitchMode(f.Mode, $"menu:{p.PlayerName}");
                AnnounceAll($" \u0004[SM]\u0001 {p.PlayerName} switched the pitch to \u0004{f.Label}\u0001.");
                OpenPitchSizeMenu(p);
            });
        }
        OpenNumberMenu(player, menu);
    }
}
