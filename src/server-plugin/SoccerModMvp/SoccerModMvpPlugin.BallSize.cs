using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-25 owner: a Ball size submenu (CS2 Legacy = the full 37.6 u map
// ball, 10%, 12.5%, 15%, 20% smaller); 12.5% smaller is the default.
// The ball is the map's Jabulani, one entity for rendering and physics. The
// SetScale input resizes a live ball's physics AND look together; measured
// with css_sm2ball_sizetest at 0.65 (control: rest height 18.70, side trace
// 18.79; SetScale: 12.23 / 12.12; scale before spawn: the same). Setting only
// the scene-node scale changes the look, and SetModel resets the scale.
// The size is a workbench dial, so presets and undo carry it; every ball
// (match ball after each round restart, training balls) gets it through
// ApplyGameplayPhysicsProfile. Everything radius-dependent reads
// BallCollisionRadius, which follows _ballSize.
public sealed partial class SoccerModMvpPlugin
{
    private const string BallSizeProbePrefix = "sm2_sizeprobe_";

    private static readonly (string Name, float Size)[] BallSizePresets =
    {
        ("CS2 Legacy", 1f),
        ("10% smaller", 0.9f),
        ("Default (12.5% smaller)", DefaultBallSize),
        ("15% smaller", 0.85f),
        ("20% smaller", 0.8f),
    };

    private void BallSizeOnLoad()
    {
        // Console-only diagnostic: T0 unscaled control, T1 scale before
        // DispatchSpawn, T2 the SetScale input, T3 scene scale + rebuild.
        AddCommand("css_sm2ball_sizetest", "Server only: measure whether scaling the ball changes its physics.", OnBallSizeTestCommand);
    }

    // Only acts when the ball's size differs: ApplyGameplayPhysicsProfile runs
    // every maintenance tick and repeated writes wake a resting ball.
    private void ApplyBallSize(CPhysicsPropMultiplayer ball, string reason)
    {
        if (!ball.IsValid || ball.CBodyComponent?.SceneNode is not { } node) return;
        var current = node.Scale;
        if (MathF.Abs(current - _ballSize) < 0.001f) return;
        var oldRadius = DefaultBallCollisionRadius * current;
        ball.AcceptInput("SetScale", value: _ballSize.ToString("0.###", CultureInfo.InvariantCulture));
        // A ball resting on the pitch (also the frozen kickoff ball) keeps
        // resting on it: a shrunk one would hang in the air until touched, a
        // grown one would sit inside the pitch and be popped out.
        if (ball.AbsOrigin is { } origin && origin.Z <= StadiumPitchPlaneZ + oldRadius + 2f)
            ball.Teleport(position: new Vector(origin.X, origin.Y, StadiumPitchPlaneZ + BallCollisionRadius));
        Logger.LogInformation("[SM2DIAG] ball_size_applied reason={Reason} index={Index} from={From:F2} to={To:F2}",
            reason, ball.Index, current, _ballSize);
    }

    private static string BallDiameterText(float size) => $"{BallMenuNumber(2 * DefaultBallCollisionRadius * size)} u";

    private string BallSizeLabel()
    {
        foreach (var (name, size) in BallSizePresets)
            if (MathF.Abs(_ballSize - size) < 0.001f) return $"{name} ({BallDiameterText(size)})";
        return $"custom ({BallDiameterText(_ballSize)})";
    }

    private void OpenBallSizeMenu(CCSPlayerController player)
    {
        if (!BallWorkbenchAccess(player)) return;
        var menu = new NumberMenu { Title = $"Ball size: {BallSizeLabel()}", Key = "ball-size", OnBack = OpenBallAdminMenu };
        foreach (var (name, size) in BallSizePresets)
        {
            var active = MathF.Abs(_ballSize - size) < 0.001f;
            var note = size == 0.8f ? " (about the CS:S ball)" : "";
            menu.Add($"{(active ? "* " : "")}{name} - {BallDiameterText(size)}{note}", p =>
            {
                if (!BallWorkbenchAccess(p)) return;
                var tuning = CaptureBallTuning();
                tuning.Values["ballSize"] = size;
                p.PrintToChat(ApplyBallTuning(tuning)
                    ? $" [SM] Ball size: {name} ({BallDiameterText(size)}, saved)"
                    : " [SM] Not changed: settings could not be saved.");
                OpenBallSizeMenu(p);
            });
        }
        menu.AddInfo("Physics and look change together, right away.");
        OpenNumberMenu(player, menu);
    }

    private void OnBallSizeTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command)) return;
        var scale = 0.65f;
        if (command.ArgCount >= 2 && float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            scale = Math.Clamp(parsed, 0.3f, 1.5f);
        var model = _ball is { IsValid: true } ? _ball.CBodyComponent?.SceneNode?.GetSkeletonInstance().ModelState.ModelName : null;
        if (string.IsNullOrEmpty(model)) model = BallVisualModelName;

        RemoveBallSizeProbes();
        var probes = new List<(string Name, CPhysicsPropMultiplayer Ball)>();
        foreach (var (name, y, preSpawnScale) in new[] { ("T0_control", 0f, 1f), ("T1_prespawn", 150f, scale), ("T2_setscale", 300f, 1f), ("T3_rebuild", 450f, 1f) })
        {
            if (SpawnBallSizeProbe(name, model, new Vector(600f, y, StadiumPitchPlaneZ + 80f), preSpawnScale) is { } probe)
                probes.Add((name, probe));
        }
        foreach (var (name, probe) in probes)
        {
            if (name == "T2_setscale")
            {
                probe.AcceptInput("SetScale", value: scale.ToString(CultureInfo.InvariantCulture));
            }
            else if (name == "T3_rebuild")
            {
                SetBallSceneScale(probe, scale);
                probe.AcceptInput("DisableMotion");
                probe.AcceptInput("EnableMotion");
                probe.SetModel(model);
                SetBallSceneScale(probe, scale);
                probe.AcceptInput("Wake");
            }
        }
        command.ReplyToCommand($"[SM2DIAG] ball size probes spawned={probes.Count} scale={scale:F2} model={model}; results in 2.5 s");
        AddTimer(2.5f, () =>
        {
            foreach (var (name, probe) in probes)
            {
                if (!probe.IsValid || probe.AbsOrigin is not { } origin)
                {
                    Logger.LogInformation("[SM2DIAG] ball_size_test probe={Probe} missing", name);
                    continue;
                }
                var collision = probe.Collision;
                Logger.LogInformation(
                    "[SM2DIAG] ball_size_test probe={Probe} scale={Scale:F2} restHeight={Rest:F2} defaultRadius={Default:F2} scaledRadius={Scaled:F2} sceneScale={SceneScale:F3} mins={Mins} maxs={Maxs} boundingRadius={Bounding:F2} sideTraceRadius={Side}",
                    name, scale, origin.Z - StadiumPitchPlaneZ, DefaultBallCollisionRadius, DefaultBallCollisionRadius * scale,
                    probe.CBodyComponent?.SceneNode?.Scale ?? float.NaN, FormatVector(collision.Mins), FormatVector(collision.Maxs),
                    collision.BoundingRadius, MeasureBallProbeRadius(probe, origin));
            }
            RemoveBallSizeProbes();
        }, TimerFlags.STOP_ON_MAPCHANGE);
    }

    private CPhysicsPropMultiplayer? SpawnBallSizeProbe(string name, string model, Vector origin, float preSpawnScale)
    {
        var probe = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>(BallDesignerName);
        if (probe is null || !probe.IsValid) return null;
        if (preSpawnScale != 1f) SetBallSceneScale(probe, preSpawnScale);
        using (var keyValues = new CEntityKeyValues())
        {
            keyValues.SetString("targetname", BallSizeProbePrefix + name);
            keyValues.SetString("model", model);
            keyValues.SetUInt("spawnflags", 0u);
            keyValues.SetInt("physicsmode", 1);
            keyValues.SetVector("origin", origin);
            keyValues.SetAngle("angles", new QAngle(0.0f, 0.0f, 0.0f));
            if (preSpawnScale != 1f) keyValues.SetVector("scales", new Vector(preSpawnScale, preSpawnScale, preSpawnScale));
            probe.DispatchSpawn(keyValues);
        }
        if (!probe.IsValid) return null;
        probe.AcceptInput("Wake");
        return probe;
    }

    private static void SetBallSceneScale(CPhysicsPropMultiplayer ball, float scale)
    {
        if (ball.CBodyComponent?.SceneNode is not { } node) return;
        node.Scale = scale;
        node.ClientLocalScale = scale;
    }

    // Horizontal trace from 80 u away towards the centre; only a hit on the
    // probe itself counts.
    private static string MeasureBallProbeRadius(CPhysicsPropMultiplayer probe, Vector origin)
    {
        const float distance = 80f;
        var start = new Vector(origin.X + distance, origin.Y, origin.Z);
        var trace = Trace.TraceEndShape(start, origin, null, new TraceOptions { InteractsWith = Masks.Solid });
        if (!trace.DidHit() || !trace.HitEntity().IsValid || trace.HitEntity().Index != probe.Index) return "n/a";
        return (distance * (1f - trace.Fraction)).ToString("F2", CultureInfo.InvariantCulture);
    }

    private void RemoveBallSizeProbes()
    {
        foreach (var probe in Utilities.FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>(BallDesignerName)
                     .Where(p => p.IsValid && (p.Entity?.Name ?? "").StartsWith(BallSizeProbePrefix, StringComparison.Ordinal)))
            probe.Remove();
    }
}
