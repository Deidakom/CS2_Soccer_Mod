using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

public sealed partial class SoccerModMvpPlugin
{
    // 2026-08-31 user feedback: the initial Height=70 (on TOP of eye level,
    // which is already ~64 units up) framed the player from a steep drone
    // angle. A proper over-the-shoulder cam wants only a small lift above
    // the eyes. Live-tunable via css_sm2tp_tune so camera feel can be
    // iterated without a service restart per attempt.
    private const float DefaultThirdPersonDistance = 100.0f;
    private const float DefaultThirdPersonHeight = 16.0f;
    private const float ThirdPersonSmoothingFactor = 0.4f;

    // 2026-09-09: diagnostic sample cadence for the front camera investigation
    // (thirdperson_front_sample). 32 ticks @ 64 tick/s = 2 Hz - enough to see
    // whether V_angle/AbsRotation are stable or alternating without flooding
    // the journal. Remove once the live camera is confirmed stable.
    private const int ThirdPersonFrontSampleEveryTicks = 32;

    private float _thirdPersonDistance = DefaultThirdPersonDistance;
    private float _thirdPersonHeight = DefaultThirdPersonHeight;

    private readonly HashSet<int> _thirdPersonSlots = new();
    private readonly HashSet<int> _thirdPersonFrontSlots = new();
    private readonly Dictionary<int, CDynamicProp> _thirdPersonCamBySlot = new();
    private int _thirdPersonFrontSampleTick;

    private void ThirdPersonOnLoad()
    {
        AddCommand(
            "css_sm2thirdperson",
            "Toggle your third-person camera.",
            OnThirdPersonToggleCommand);
        AddCommand(
            "css_tp",
            "Chat alias: !tp toggles your third-person camera.",
            OnThirdPersonToggleCommand);
        AddCommand(
            "css_sm2thirdperson_front",
            "Toggle your front-facing third-person camera.",
            OnThirdPersonFrontToggleCommand);
        AddCommand(
            "css_tpf",
            "Chat alias: !tpf toggles your front-facing third-person camera.",
            OnThirdPersonFrontToggleCommand);
        AddCommand(
            "css_sm2tp_tune",
            "Admin: tune the third-person camera (distance, height above eyes).",
            OnThirdPersonTuneCommand);
    }

    private void OnThirdPersonTuneCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "match")) return;

        if (command.ArgCount >= 3
            && float.TryParse(command.GetArg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var distance)
            && float.TryParse(command.GetArg(2), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var height)
            && distance is >= 30.0f and <= 400.0f
            && height is >= -50.0f and <= 150.0f)
        {
            _thirdPersonDistance = distance;
            _thirdPersonHeight = height;
        }

        command.ReplyToCommand(
            $"[SM] third-person camera: distance={_thirdPersonDistance:F0} height={_thirdPersonHeight:F0} "
            + "(usage: css_sm2tp_tune <distance 30-400> <height -50-150>; applies live to anyone in !tp)");
    }

    private void ThirdPersonOnUnload()
    {
        foreach (var slot in _thirdPersonSlots.ToArray())
        {
            if (Utilities.GetPlayerFromSlot(slot) is { IsValid: true } player)
            {
                ResetThirdPersonView(player);
            }

            RemoveThirdPersonCamera(slot);
        }

        foreach (var slot in _thirdPersonCamBySlot.Keys.ToArray())
        {
            RemoveThirdPersonCamera(slot);
        }

        _thirdPersonSlots.Clear();
        _thirdPersonFrontSlots.Clear();
    }

    private void ThirdPersonOnPlayerSpawn(CCSPlayerController player)
    {
        if (!_thirdPersonSlots.Contains(player.Slot))
        {
            return;
        }

        Server.NextFrame(() => AttachThirdPersonCamera(player, recreate: true, "spawn_next_frame"));
    }

    private void ThirdPersonReassertAfterSpawn(CCSPlayerController player)
    {
        if (_thirdPersonSlots.Contains(player.Slot))
        {
            AttachThirdPersonCamera(player, recreate: false, "spawn_plus_0_25s");
        }
    }

    private void ThirdPersonOnPlayerDisconnect(int slot)
    {
        RemoveThirdPersonCamera(slot);
        _thirdPersonSlots.Remove(slot);
        _thirdPersonFrontSlots.Remove(slot);
    }

    private void ThirdPersonOnTick()
    {
        // 2026-09-09: shared once per tick, not per slot - the front-camera
        // diagnostic sampling below must fire on the same cadence for every
        // slot in front mode, not restart per player.
        var sampleThisTick = _thirdPersonFrontSlots.Count > 0
            && ++_thirdPersonFrontSampleTick % ThirdPersonFrontSampleEveryTicks == 0;

        foreach (var (slot, camProp) in _thirdPersonCamBySlot.ToArray())
        {
            if (!camProp.IsValid)
            {
                _thirdPersonCamBySlot.Remove(slot);
                continue;
            }

            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            var frontFacing = _thirdPersonFrontSlots.Contains(slot);
            if (player is not { IsValid: true }
                || pawn is not { IsValid: true }
                || !IsAlive(pawn)
                || !TryGetThirdPersonCameraTransform(
                    pawn,
                    frontFacing,
                    out var targetPosition,
                    out var lookAtTarget))
            {
                continue;
            }

            if (pawn.CameraServices is { } cameraServices
                && cameraServices.ViewEntity.Raw != camProp.EntityHandle.Raw)
            {
                SetThirdPersonView(pawn, cameraServices, camProp);
            }

            var current = camProp.AbsOrigin;
            var smoothedPosition = current is null
                ? targetPosition
                : LerpThirdPersonPosition(current, targetPosition, ThirdPersonSmoothingFactor);
            // 2026-09-09: front-mode angles are derived from the SMOOTHED
            // (actual) camera position looking at the chest target, not from
            // the raw target position used for the lerp above. Deriving the
            // look-at from the unsmoothed target made the view direction
            // recompute against a position the camera prop hadn't reached
            // yet, so the view snapped every tick while the position eased -
            // this is what "flicks around" looked like. Rear mode is
            // unaffected: its angles already equal the live view angles, not
            // a look-at computation, and were never the smoothed/unsmoothed
            // mismatch source.
            var angles = frontFacing
                ? LookAtAngles(smoothedPosition, lookAtTarget)
                : new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0.0f);
            camProp.Teleport(smoothedPosition, angles, new Vector());

            if (sampleThisTick && frontFacing && pawn.AbsRotation is { } sampleAbsRotation)
            {
                Logger.LogInformation(
                    "[SM2DIAG] thirdperson_front_sample slot={Slot} vangleYaw={VAngleYaw:F1} vanglePitch={VAnglePitch:F1} absYaw={AbsYaw:F1} camYaw={CamYaw:F1} camPos={CamPos}",
                    slot,
                    pawn.V_angle.Y,
                    pawn.V_angle.X,
                    sampleAbsRotation.Y,
                    angles.Y,
                    FormatVector(smoothedPosition));
            }
        }
    }

    private void OnThirdPersonToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] third person is available to in-game players only");
            return;
        }

        // 2026-09-09 user request: !tp while !tpf is active switches to the
        // rear camera instead of turning third person off - only repeating
        // the mode that is CURRENTLY active disables it (mirrors !tpf below).
        // The existing camera prop and ViewEntity binding are left alone;
        // removing the slot from the front set is enough, ThirdPersonOnTick
        // picks up the mode change and eases the camera behind on the next
        // tick via the normal position lerp - no snap, no recreate.
        if (_thirdPersonSlots.Contains(player.Slot) && _thirdPersonFrontSlots.Contains(player.Slot))
        {
            _thirdPersonFrontSlots.Remove(player.Slot);
            command.ReplyToCommand("[SM] third-person camera: rear (type !tp again to disable)");
            return;
        }

        if (_thirdPersonSlots.Contains(player.Slot))
        {
            DisableThirdPerson(player);
            command.ReplyToCommand("[SM] third-person camera: off");
            return;
        }

        _thirdPersonSlots.Add(player.Slot);
        _thirdPersonFrontSlots.Remove(player.Slot);
        if (!AttachThirdPersonCamera(player, recreate: true, "toggle_on"))
        {
            _thirdPersonSlots.Remove(player.Slot);
            command.ReplyToCommand("[SM] join a team and spawn before enabling third person");
            return;
        }

        command.ReplyToCommand("[SM] third-person camera: on (type !tp again to disable)");
    }

    private void OnThirdPersonFrontToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] third person is available to in-game players only");
            return;
        }

        if (_thirdPersonSlots.Contains(player.Slot)
            && _thirdPersonFrontSlots.Contains(player.Slot))
        {
            DisableThirdPerson(player);
            command.ReplyToCommand("[SM] front-facing third-person camera: off");
            return;
        }

        var wasThirdPersonEnabled = _thirdPersonSlots.Contains(player.Slot);
        _thirdPersonSlots.Add(player.Slot);
        _thirdPersonFrontSlots.Add(player.Slot);
        if (!AttachThirdPersonCamera(player, recreate: false, "front_toggle_on"))
        {
            _thirdPersonFrontSlots.Remove(player.Slot);
            if (!wasThirdPersonEnabled)
            {
                _thirdPersonSlots.Remove(player.Slot);
            }

            command.ReplyToCommand("[SM] join a team and spawn before enabling front-facing third person");
            return;
        }

        // 2026-09-09: settles which of the two theories behind the reported
        // "flicks around" is live - a stable V_angle with a jumping absYaw
        // supports the AbsRotation-instability theory the previous revision
        // relied on; a V_angle that itself alternates ~180 degrees between
        // this line and the first thirdperson_front_sample supports a view-
        // angle feedback loop instead. Compare against the sample lines.
        if (player.PlayerPawn.Value is { IsValid: true } samplePawn
            && samplePawn.AbsRotation is { } toggleAbsRotation)
        {
            Logger.LogInformation(
                "[SM2DIAG] thirdperson_front_on slot={Slot} vangleYaw={VAngleYaw:F1} absYaw={AbsYaw:F1}",
                player.Slot,
                samplePawn.V_angle.Y,
                toggleAbsRotation.Y);
        }

        command.ReplyToCommand("[SM] front-facing third-person camera: on (type !tpf again to disable)");
    }

    private bool AttachThirdPersonCamera(CCSPlayerController player, bool recreate, string reason)
    {
        var pawn = player.PlayerPawn.Value;
        if (!player.IsValid
            || pawn is not { IsValid: true }
            || !IsAlive(pawn))
        {
            return false;
        }

        var frontFacing = _thirdPersonFrontSlots.Contains(player.Slot);
        var cameraServices = pawn.CameraServices;
        if (cameraServices is null
            || !TryGetThirdPersonCameraTransform(
                pawn,
                frontFacing,
                out var position,
                out var lookAtTarget))
        {
            return false;
        }

        // Only used for the very first placement below (no prior position to
        // lerp from yet); ThirdPersonOnTick recomputes this every tick from
        // the actual (possibly smoothed) camera position instead.
        var angles = frontFacing
            ? LookAtAngles(position, lookAtTarget)
            : new QAngle(pawn.V_angle.X, pawn.V_angle.Y, 0.0f);

        if (recreate)
        {
            RemoveThirdPersonCamera(player.Slot);
        }

        if (!_thirdPersonCamBySlot.TryGetValue(player.Slot, out var camProp)
            || !camProp.IsValid)
        {
            camProp = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (camProp is null || !camProp.IsValid)
            {
                Logger.LogWarning(
                    "[SM2DIAG] thirdperson_camera_create_failed slot={Slot} reason={Reason}",
                    player.Slot,
                    reason);
                return false;
            }

            // This model-less prop is only a networked camera transform. Its
            // error-model fallback must not render or cast an ERROR shadow.
            SuppressThirdPersonCameraRendering(camProp);
            camProp.DispatchSpawn();
            if (!camProp.IsValid)
            {
                Logger.LogWarning(
                    "[SM2DIAG] thirdperson_camera_spawn_failed slot={Slot} reason={Reason}",
                    player.Slot,
                    reason);
                return false;
            }

            camProp.Teleport(position, angles, new Vector());
            _thirdPersonCamBySlot[player.Slot] = camProp;
        }

        // Spawn can initialize render fields; also repair reused camera props.
        SuppressThirdPersonCameraRendering(camProp);
        Utilities.SetStateChanged(camProp, "CBaseModelEntity", "m_nRenderMode");
        Utilities.SetStateChanged(camProp, "CBaseModelEntity", "m_flShadowStrength");
        SetThirdPersonView(pawn, cameraServices, camProp);
        Logger.LogDebug(
            "[SM2DIAG] thirdperson_camera_attached slot={Slot} camera={Camera} reason={Reason}",
            player.Slot,
            camProp.Index,
            reason);
        return true;
    }

    private static void SuppressThirdPersonCameraRendering(CDynamicProp camProp)
    {
        // Keep the entity transmitted for CameraServices.ViewEntity. EF_NODRAW
        // or transmit filtering can prevent the client receiving its transform.
        camProp.RenderMode = RenderMode_t.kRenderNone;
        camProp.ShadowStrength = 0.0f;
    }

    private void DisableThirdPerson(CCSPlayerController player)
    {
        ResetThirdPersonView(player);
        RemoveThirdPersonCamera(player.Slot);
        _thirdPersonSlots.Remove(player.Slot);
        _thirdPersonFrontSlots.Remove(player.Slot);
    }

    private static void ResetThirdPersonView(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is { IsValid: true } && pawn.CameraServices is { } cameraServices)
        {
            cameraServices.ViewEntity.Raw = uint.MaxValue;
            Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
        }
    }

    private static void SetThirdPersonView(
        CCSPlayerPawn pawn,
        CPlayer_CameraServices cameraServices,
        CDynamicProp camProp)
    {
        cameraServices.ViewEntity.Raw = camProp.EntityHandle.Raw;
        Utilities.SetStateChanged(pawn, "CBasePlayerPawn", "m_pCameraServices");
    }

    private void RemoveThirdPersonCamera(int slot)
    {
        if (_thirdPersonCamBySlot.Remove(slot, out var camProp) && camProp.IsValid)
        {
            camProp.AcceptInput("Kill");
        }
    }

    // 2026-09-09: rewritten after the live report "it flicks around weirdly,
    // doesn't work at all". Two prior bugs, in order:
    //  1. The very first revision drove the front camera off pawn.V_angle
    //     INCLUDING pitch, so looking down at the ball (constant in
    //     football) put the camera underground, and looking up put it over
    //     the head.
    //  2. The live revision "fixed" that by switching to pawn.AbsRotation.Y
    //     (the entity's body yaw) instead. In CS2 the player pawn's entity
    //     rotation is NOT a reliable "which way is the character facing"
    //     value - the animgraph drives visible facing from the eye angles,
    //     it does not feed AbsRotation back in a stable way a server-side
    //     read can rely on tick to tick. Anchoring the orbit position to it
    //     each tick, on top of computing the look-at angles from that same
    //     tick's UNSMOOTHED target position (not the position the camera
    //     prop had actually eased to), is what produced the reported
    //     flicking: two independent sources of tick-to-tick jitter feeding
    //     straight into what the client renders.
    // Fix: use the same flat (pitch-stripped) view yaw the rear camera
    // already uses - it is exactly the value CS2's own movement/animation
    // system treats as "which way the player is facing" (see the 2026-08-31
    // comment on the rear branch below) - and compute the LOOK-AT angles
    // from the camera's actual smoothed position (done in ThirdPersonOnTick/
    // AttachThirdPersonCamera, which call LookAtAngles below), not from the
    // raw orbit target. A wall clamp keeps the camera out of geometry when
    // the player faces a wall, the goal net, or an ad board up close.
    // thirdperson_front_on/thirdperson_front_sample (ThirdPersonOnTick) log
    // vangleYaw next to absYaw specifically to leave a provable trail if the
    // AbsRotation theory needs revisiting instead.
    private bool TryGetThirdPersonCameraTransform(
        CCSPlayerPawn pawn,
        bool frontFacing,
        out Vector position,
        out Vector lookAtTarget)
    {
        position = new Vector();
        lookAtTarget = new Vector();
        if (pawn.AbsOrigin is not { } playerOrigin)
        {
            return false;
        }

        var viewOffset = pawn.ViewOffset;
        var eyePosition = new Vector(
            playerOrigin.X + viewOffset.X,
            playerOrigin.Y + viewOffset.Y,
            playerOrigin.Z + viewOffset.Z);
        // 2026-08-31 user feedback: using EyeAngles here made W-forward walk
        // diagonally relative to what the camera showed. The reference
        // implementation (ThirdPerson-Revamped) drives the camera off
        // PlayerPawn.V_angle instead -- the actual input-facing view angle
        // movement is computed from -- not the (possibly render-only)
        // EyeAngles property. Aligning the camera to the same angle the
        // movement system uses is what keeps W = "the direction the camera
        // is looking".
        var eyeAngles = pawn.V_angle;
        var pitchRadians = eyeAngles.X * (MathF.PI / 180.0f);
        var yawRadians = eyeAngles.Y * (MathF.PI / 180.0f);
        var cosPitch = MathF.Cos(pitchRadians);
        var forward = new Vector(
            cosPitch * MathF.Cos(yawRadians),
            cosPitch * MathF.Sin(yawRadians),
            -MathF.Sin(pitchRadians));

        if (!frontFacing)
        {
            position = new Vector(
                eyePosition.X - forward.X * _thirdPersonDistance,
                eyePosition.Y - forward.Y * _thirdPersonDistance,
                eyePosition.Z - forward.Z * _thirdPersonDistance + _thirdPersonHeight);
            lookAtTarget = eyePosition;
            return true;
        }

        // Same view yaw as the rear camera, flattened (no pitch) - orbiting
        // on pitch would drive the camera underground/overhead exactly like
        // bug 1 above; a flat orbit with the height offset below is enough
        // to frame the chest from slightly above.
        var flatForward = new Vector(MathF.Cos(yawRadians), MathF.Sin(yawRadians), 0.0f);
        lookAtTarget = new Vector(
            playerOrigin.X,
            playerOrigin.Y,
            playerOrigin.Z + MathF.Max(40.0f, viewOffset.Z * 0.65f));
        var desired = new Vector(
            lookAtTarget.X + flatForward.X * _thirdPersonDistance,
            lookAtTarget.Y + flatForward.Y * _thirdPersonDistance,
            lookAtTarget.Z + _thirdPersonHeight);

        // Wall clamp: pull the camera to just short of the first static
        // solid between the chest target and the desired orbit position, so
        // facing a wall/goal net/ad board up close does not put the camera
        // inside or beyond it. IsStaticWallSurface only matches world/static
        // geometry (see its own definition), so a teammate walking through
        // the shot never yanks the camera.
        position = desired;
        var wallTrace = Trace.TraceEndShape(
            lookAtTarget,
            desired,
            pawn,
            new TraceOptions { InteractsWith = Masks.Solid });
        if (wallTrace.DidHit() && IsStaticWallSurface(wallTrace))
        {
            var hitDistance = VectorSpeed(new Vector(
                wallTrace.EndPos.X - lookAtTarget.X,
                wallTrace.EndPos.Y - lookAtTarget.Y,
                wallTrace.EndPos.Z - lookAtTarget.Z));
            var clampedDistance = MathF.Max(30.0f, hitDistance - 8.0f);
            position = new Vector(
                lookAtTarget.X + flatForward.X * clampedDistance,
                lookAtTarget.Y + flatForward.Y * clampedDistance,
                lookAtTarget.Z + _thirdPersonHeight);
        }

        return true;
    }

    private static QAngle LookAtAngles(Vector from, Vector to)
    {
        var toFace = new Vector(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        var horizontalDistance = MathF.Sqrt((toFace.X * toFace.X) + (toFace.Y * toFace.Y));
        var pitch = MathF.Atan2(-toFace.Z, horizontalDistance) * (180.0f / MathF.PI);
        var yaw = MathF.Atan2(toFace.Y, toFace.X) * (180.0f / MathF.PI);
        return new QAngle(pitch, yaw, 0.0f);
    }

    private static Vector LerpThirdPersonPosition(Vector current, Vector target, float factor) => new(
        current.X + (target.X - current.X) * factor,
        current.Y + (target.Y - current.Y) * factor,
        current.Z + (target.Z - current.Z) * factor);
}
