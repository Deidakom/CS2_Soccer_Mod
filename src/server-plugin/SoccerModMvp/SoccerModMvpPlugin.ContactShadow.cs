using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-28 owner: "the shadow of the ball is hard to see ... the ball looks
// floating" (floor and 3D grass). The ball is a runtime prop: it casts only
// the sharp sun shadow, 45 degrees off to the side, and gets no contact shadow;
// the unlit 3D grass receives no dynamic shadow at all. Two Feature Package
// models (tools/ball/generate-ball-shadow.mjs) follow every playable ball:
//   contact_shadow - soft dark disc straight under the ball, for everyone.
//     Skin 0 (darkest) .. 7 (faintest) by the gap under the ball, gone above
//     BallShadowContactMaxHeight.
//   sun_shadow - the sun shadow for players with 3D grass on (others see the
//     engine's own shadow on the floor): where the ball's shadow falls (sun
//     angles from the map's light_environment), hidden when the ball is not in
//     the sun (trace towards the sun hits the roof).
// The floor under the ball is traced, so the contact shadow works on any map.
// Server flag file; without it or the models in the mounted package nothing
// is spawned.
public sealed partial class SoccerModMvpPlugin
{
    private const string BallShadowFlagFile = "soccermod_ball_shadow.enabled";
    private const string BallShadowContactModel = "models/soccermod/ball/contact_shadow.vmdl";
    private const string BallShadowSunModel = "models/soccermod/ball/sun_shadow.vmdl";
    private const string BallShadowTargetName = "sm2_ball_shadow";
    private const int BallShadowContactSkins = 8;
    private const float BallShadowContactMaxHeight = 120.0f;
    private const int BallShadowMaxBalls = 8;
    private const int BallShadowSunCheckTicks = 4;

    private sealed class BallShadowState
    {
        public CDynamicProp? Contact;
        public CDynamicProp? Sun;
        public int ContactSkin = -1;
        public bool SunLit;
        public int SunCheckedTick = -1000;
    }

    private bool _ballShadowPrecached;
    private readonly Dictionary<uint, BallShadowState> _ballShadows = new();
    private (float X, float Y, float Z, float Pitch)? _sunDirection;

    private bool BallShadowActive => _ballShadowPrecached && FlagFileOn(BallShadowFlagFile);

    private void BallShadowOnLoad(bool hotReload)
    {
        // Only on a hot reload: at server start the entity system does not exist yet.
        if (hotReload)
        {
            foreach (var old in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>("prop_dynamic")
                         .Where(e => e.IsValid && e.Entity?.Name == BallShadowTargetName).ToList())
                old.Remove();
            AddTimer(1.0f, ReadSunDirection);
        }
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _ballShadowPrecached = false;
            if (!File.Exists(ConfigPath(BallShadowFlagFile))) return;
            var mounted = MountedAddonFiles();
            if (!mounted.Contains(BallShadowContactModel + "_c") || !mounted.Contains(BallShadowSunModel + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] ball_shadow_unavailable reason=model_not_in_mounted_workshop_items model={Model}", BallShadowContactModel);
                return;
            }
            manifest.AddResource(BallShadowContactModel);
            manifest.AddResource(BallShadowSunModel);
            _ballShadowPrecached = true;
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _ballShadows.Clear();
            _sunDirection = null;
            AddTimer(1.0f, ReadSunDirection, CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterListener<Listeners.OnTick>(BallShadowTick);
        RegisterListener<Listeners.CheckTransmit>(BallShadowCheckTransmit);
        AddCommand("css_sm2shadow", "Admin: ball shadow status.", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            command.ReplyToCommand($"[SM] Ball shadow: flag={File.Exists(ConfigPath(BallShadowFlagFile))} precached={_ballShadowPrecached} balls={_ballShadows.Count} sun={(_sunDirection is { } s ? $"pitch {s.Pitch:F1} dir {s.X:F2},{s.Y:F2}" : "none")}");
        });
    }

    private void BallShadowOnUnload() => RemoveBallShadows();

    // The direction the sunlight travels: the light_environment's forward
    // vector. soccer_cssl_stadium_v8 has "45 45 0" (fallback on that pitch).
    private void ReadSunDirection()
    {
        QAngle? angles = null;
        var light = Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("light_environment").FirstOrDefault(e => e.IsValid);
        if (light?.AbsRotation is { } rotation) angles = rotation;
        else if (IsFoundationMap(_currentMapName)) angles = new QAngle(45.0f, 45.0f, 0.0f);
        if (angles is null || angles.X < 10.0f)
        {
            _sunDirection = null;
            return;
        }
        var pitch = angles.X * MathF.PI / 180.0f;
        var yaw = angles.Y * MathF.PI / 180.0f;
        _sunDirection = (MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch), angles.X);
        Logger.LogInformation("[SM2DIAG] ball_shadow_sun source={Source} pitch={Pitch:F1} yaw={Yaw:F1}",
            light is not null ? "light_environment" : "fallback", angles.X, angles.Y);
    }

    private void BallShadowTick()
    {
        if (!BallShadowActive)
        {
            if (_ballShadows.Count > 0) RemoveBallShadows();
            return;
        }
        var seen = new HashSet<uint>();
        var tick = Server.TickCount;
        var options = new TraceOptions { InteractsWith = Masks.SolidBrushOnly };
        foreach (var ball in PlayableBalls().Take(BallShadowMaxBalls))
        {
            var index = ball.Ball.Index;
            seen.Add(index);
            if (!_ballShadows.TryGetValue(index, out var state)) _ballShadows[index] = state = new BallShadowState();
            var o = ball.Origin;
            var down = Trace.TraceEndShape(o, new Vector(o.X, o.Y, o.Z - 600.0f), ball.Ball, options);
            if (!down.DidHit() || down.Normal.Z < 0.7f)
            {
                BallShadowHide(state.Contact);
                BallShadowHide(state.Sun);
                continue;
            }
            var floorZ = down.EndPos.Z;
            // above the 3D grass blades (top shell 2.25 units) where they exist
            var lift = GrassSpawned ? 2.6f : 0.6f;

            var gap = o.Z - BallCollisionRadius - floorZ;
            if (gap > BallShadowContactMaxHeight) BallShadowHide(state.Contact);
            else
            {
                var skin = Math.Clamp((int)(Math.Max(0.0f, gap) / (BallShadowContactMaxHeight / BallShadowContactSkins)), 0, BallShadowContactSkins - 1);
                var before = state.Contact;
                state.Contact = BallShadowPlace(state.Contact, BallShadowContactModel, new Vector(o.X, o.Y, floorZ + lift), 0.0f);
                if (!ReferenceEquals(before, state.Contact)) state.ContactSkin = -1; // new prop: skin 0
                if (state.Contact is not null && state.ContactSkin != skin)
                {
                    state.ContactSkin = skin;
                    state.Contact.AcceptInput("Skin", value: skin.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            }

            if (_sunDirection is not { } sun || !GrassSpawned)
            {
                BallShadowHide(state.Sun);
                continue;
            }
            if (tick - state.SunCheckedTick >= BallShadowSunCheckTicks)
            {
                state.SunCheckedTick = tick;
                var toSun = new Vector(o.X - sun.X * 6000.0f, o.Y - sun.Y * 6000.0f, o.Z - sun.Z * 6000.0f);
                state.SunLit = !Trace.TraceEndShape(o, toSun, ball.Ball, options).DidHit();
            }
            if (!state.SunLit)
            {
                BallShadowHide(state.Sun);
                continue;
            }
            // The ball centre's shadow lands where the sun ray through it meets the floor.
            var height = o.Z - floorZ;
            var run = height / -sun.Z;
            var yaw = MathF.Atan2(sun.Y, sun.X) * 180.0f / MathF.PI;
            state.Sun = BallShadowPlace(state.Sun, BallShadowSunModel, new Vector(o.X + sun.X * run, o.Y + sun.Y * run, floorZ + lift + 0.05f), yaw);
        }
        if (_ballShadows.Count > seen.Count)
        {
            foreach (var gone in _ballShadows.Keys.Where(k => !seen.Contains(k)).ToList())
            {
                var state = _ballShadows[gone];
                if (state.Contact is { IsValid: true }) state.Contact.Remove();
                if (state.Sun is { IsValid: true }) state.Sun.Remove();
                _ballShadows.Remove(gone);
            }
        }
    }

    // Spawns the prop if needed (a round restart deletes it), shows it and
    // moves it only when it really moved.
    private CDynamicProp? BallShadowPlace(CDynamicProp? prop, string model, Vector at, float yaw)
    {
        if (prop is not { IsValid: true })
        {
            prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
            if (prop is null || !prop.IsValid) return null;
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", BallShadowTargetName);
            keyValues.SetString("model", model);
            keyValues.SetInt("solid", 0);
            keyValues.SetInt("disableshadows", 1);
            keyValues.SetVector("origin", at);
            keyValues.SetAngle("angles", new QAngle(0.0f, yaw, 0.0f));
            prop.DispatchSpawn(keyValues);
            if (!prop.IsValid) return null;
            prop.Entity!.Name = BallShadowTargetName;
            prop.AcceptInput("DisableCollision");
            return prop;
        }
        if ((prop.Effects & EffectNoDraw) != 0)
        {
            prop.Effects &= ~EffectNoDraw;
            Utilities.SetStateChanged(prop, "CBaseEntity", "m_fEffects");
        }
        if (prop.AbsOrigin is not { } current
            || MathF.Abs(current.X - at.X) + MathF.Abs(current.Y - at.Y) + MathF.Abs(current.Z - at.Z) > 0.1f)
            prop.Teleport(at, new QAngle(0.0f, yaw, 0.0f), new Vector(0.0f, 0.0f, 0.0f));
        return prop;
    }

    private static void BallShadowHide(CDynamicProp? prop)
    {
        if (prop is not { IsValid: true } || (prop.Effects & EffectNoDraw) != 0) return;
        prop.Effects |= EffectNoDraw;
        Utilities.SetStateChanged(prop, "CBaseEntity", "m_fEffects");
    }

    // Sun shadows only for players who see the 3D grass.
    private void BallShadowCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_ballShadows.Count == 0) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            if (receiver is not { IsValid: true } || GrassOn(receiver)) continue;
            foreach (var state in _ballShadows.Values)
                if (state.Sun is { IsValid: true } sun) info.TransmitEntities.Remove(sun);
        }
    }

    private void RemoveBallShadows()
    {
        foreach (var state in _ballShadows.Values)
        {
            if (state.Contact is { IsValid: true }) state.Contact.Remove();
            if (state.Sun is { IsValid: true }) state.Sun.Remove();
        }
        _ballShadows.Clear();
    }
}
