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

    private float _thirdPersonDistance = DefaultThirdPersonDistance;
    private float _thirdPersonHeight = DefaultThirdPersonHeight;

    private readonly HashSet<int> _thirdPersonSlots = new();
    private readonly Dictionary<int, CDynamicProp> _thirdPersonCamBySlot = new();

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
    }

    private void ThirdPersonOnTick()
    {
        foreach (var (slot, camProp) in _thirdPersonCamBySlot.ToArray())
        {
            if (!camProp.IsValid)
            {
                _thirdPersonCamBySlot.Remove(slot);
                continue;
            }

            var player = Utilities.GetPlayerFromSlot(slot);
            var pawn = player?.PlayerPawn.Value;
            if (player is not { IsValid: true }
                || pawn is not { IsValid: true }
                || !IsAlive(pawn)
                || !TryGetThirdPersonCameraTransform(pawn, out var targetPosition, out var targetAngles))
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
            camProp.Teleport(smoothedPosition, targetAngles, new Vector());
        }
    }

    private void OnThirdPersonToggleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not { IsValid: true })
        {
            command.ReplyToCommand("[SM] third person is available to in-game players only");
            return;
        }

        if (_thirdPersonSlots.Contains(player.Slot))
        {
            DisableThirdPerson(player);
            command.ReplyToCommand("[SM] third-person camera: off");
            return;
        }

        _thirdPersonSlots.Add(player.Slot);
        if (!AttachThirdPersonCamera(player, recreate: true, "toggle_on"))
        {
            _thirdPersonSlots.Remove(player.Slot);
            command.ReplyToCommand("[SM] join a team and spawn before enabling third person");
            return;
        }

        command.ReplyToCommand("[SM] third-person camera: on (type !tp again to disable)");
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

        var cameraServices = pawn.CameraServices;
        if (cameraServices is null
            || !TryGetThirdPersonCameraTransform(pawn, out var position, out var angles))
        {
            return false;
        }

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

        SetThirdPersonView(pawn, cameraServices, camProp);
        Logger.LogDebug(
            "[SM2DIAG] thirdperson_camera_attached slot={Slot} camera={Camera} reason={Reason}",
            player.Slot,
            camProp.Index,
            reason);
        return true;
    }

    private void DisableThirdPerson(CCSPlayerController player)
    {
        ResetThirdPersonView(player);
        RemoveThirdPersonCamera(player.Slot);
        _thirdPersonSlots.Remove(player.Slot);
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

    private bool TryGetThirdPersonCameraTransform(
        CCSPlayerPawn pawn,
        out Vector position,
        out QAngle angles)
    {
        position = new Vector();
        angles = new QAngle(0.0f, 0.0f, 0.0f);
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

        position = new Vector(
            eyePosition.X - forward.X * _thirdPersonDistance,
            eyePosition.Y - forward.Y * _thirdPersonDistance,
            eyePosition.Z - forward.Z * _thirdPersonDistance + _thirdPersonHeight);
        angles = new QAngle(eyeAngles.X, eyeAngles.Y, 0.0f);
        return true;
    }

    private static Vector LerpThirdPersonPosition(Vector current, Vector target, float factor) => new(
        current.X + (target.X - current.X) * factor,
        current.Y + (target.Y - current.Y) * factor,
        current.Z + (target.Z - current.Z) * factor);
}
