using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

[MinimumApiVersion(373)]
public sealed partial class SoccerModMvpPlugin : BasePlugin
{
    private const string BallDesignerName = "prop_physics_multiplayer";
    private const string BallTargetName = "filter_ball";
    private const string OwnedBallTargetName = "sm2_xsl_native_hull_ball";
    private const string BallVisualDesignerName = "prop_dynamic";
    private const string BallVisualTargetName = "sm2_xsl_native_hull_visual";
    private const string CtLegacyKillTriggerName = "ct_killer";
    private const string TLegacyKillTriggerName = "terro_killer";
    // Physics and rendering share one map-authored entity. Keeping the
    // Workshop Jabulani as the authoritative object preserves the client's
    // round-baseline entity and removes the hidden-body/visual-shell overlap
    // that caused fresh-round invisibility. The Workshop package outranks a
    // loose server-side model alias, so the tuned custom hull is intentionally
    // inactive in this single-entity visibility architecture.
    //
    // The previous model fed the raw 316-face XSL hull to the Source 2 hull
    // cooker, which silently simplified it: the compiled shape lost 18% of its
    // volume and its centroid moved off-centre, so the ball was measurably not
    // round.  80 faces is the finest sphere the cooker reproduces exactly; see
    // tools/make-ball-hull.cs.
    private const string DefaultBallPhysicsModel = "models/soccermod/soccer_ball_physics.vmdl";

    // Ball candidates, switchable at runtime with css_sm2ball_model so the
    // reference trials can compare them without a redeploy.  All four carry the
    // same Source 1 XSL mass; they differ only in shape and surface property.
    //
    //   hull          80-face geodesic, surfaceprop glass  (faceted, dead bounce)
    //   hull_bouncy   80-face geodesic, surfaceprop weapon (faceted, live bounce)
    //   sphere        true sphere,      surfaceprop glass  (smooth, dead bounce)
    //   sphere_bouncy true sphere,      surfaceprop weapon (smooth, live bounce)
    //
    // The faceted hull has a rolling barrier of circumradius - inradius = 0.977
    // units, so it cannot roll slower than sqrt(2*g*h) = 39.5 u/s.  The CS:S
    // ball coasted at ~30 u/s, which is why the sphere variants exist.
    private static readonly Dictionary<string, string> BallPhysicsModelCandidates = new()
    {
        ["hull"] = DefaultBallPhysicsModel,
        ["hull_bouncy"] = "models/soccermod/ball_hull_bouncy.vmdl",
        ["sphere"] = "models/soccermod/ball_sphere_glass.vmdl",
        ["sphere_bouncy"] = "models/soccermod/ball_sphere_bouncy.vmdl",
        ["sphere_tile"] = "models/soccermod/ball_sphere_tile.vmdl",
        ["sphere_soccerball"] = "models/soccermod/ball_sphere_soccerball.vmdl",
        ["sphere_popcan"] = "models/soccermod/ball_sphere_popcan.vmdl",
        ["combo450"] = "models/soccermod/ball_combo_450.vmdl",
        ["combo460"] = "models/soccermod/ball_combo_460.vmdl",
        ["combo500"] = "models/soccermod/ball_combo_500.vmdl",
        ["combo450mix"] = "models/soccermod/ball_combo_450mix.vmdl",
        ["large1850"] = "models/soccermod/ball_large_1850.vmdl",
        ["large1860"] = "models/soccermod/ball_large_1860.vmdl",
        ["large_popcan"] = "models/soccermod/ball_large_popcan.vmdl",
        ["large_bouncy"] = "models/soccermod/ball_large_metal_bouncy.vmdl",
    };

    private const string BallVisualModelName = "models/ball/jabulani_edit.vmdl";
    // The Workshop map references an overview and a 1080p loading image but
    // does not package either resource. Keep their source-style names in the
    // download manifest; Source 2 resolves them to the deployed *_c files.
    private const string StadiumRadarTextureResource = "panorama/images/overheadmaps/soccer_cssl_stadium_v8_radar_psd.vtex";
    private const string StadiumLoadingScreenResource = "panorama/images/map_icons/screenshots/1080p/soccer_cssl_stadium_v8_png.vtex";
    // Full CSF map ball size: the Jabulani visual is 37.61 units across.
    // Runtime SceneNode scaling changes only rendering, not Rubikon's physics
    // shape, so collision-aware gameplay constants must retain the real hull.
    private const float BallCollisionRadius = 18.805f;
    private const float BallCollisionInradius = 17.567f;
    private const string FoundationMapName = "soccer_cssl_stadium_v8";
    // These affect CS2's native Rubikon body.  The compiled model already
    // carries the exact Source 1 XSL mass (60.694092), so the mass scale is a
    // true 1.0 rather than a fudge factor.  Friction and elasticity are the
    // "glass" surface-property values the XSL ball actually used in CS:S
    // (elasticity 0.2, friction 0.5).  The CS:S reference captures in
    // artifacts/css-reference independently measure a restitution of ~0.18,
    // which confirms them.
    private const float DefaultGameplayMassScale = 1.0f;
    private const float DefaultGameplayFriction = 0.5f;
    private const float DefaultGameplayElasticity = 0.2f;
    private const float DefaultGameplayGravityScale = 1.0f;
    // 2026-08-29: was (7.730350, 2.597906) - copied from wherever the CSF
    // map's own ball happened to be sitting, which was itself off-centre by
    // mapper imprecision. User reported the reset ball visibly isn't in the
    // middle of the pitch. The arena is measured symmetric (side walls at
    // x=+-1280, goal mouths at y=+-1460 - see css_sm2ball_trace_arena), so
    // the true centre spot is (0, 0), not a copied, slightly-off constant.
    // 2026-09-01: promoted from consts to persisted tunables. BOTH previous
    // hard-coded guesses drew the same complaint from the user - the map's
    // own ball placement (7.73, 2.60) AND the geometric arena centre (0, 0)
    // each looked visibly off the PAINTED centre spot. The painted marking
    // is a texture, not something the code can trace or derive, so stop
    // guessing: the ball rolls onto the spot by eye once, and
    // `css_sm2ball_center here` captures that exact position forever.
    private const float DefaultBallResetX = 0.0f;
    private const float DefaultBallResetY = 0.0f;
    private float _ballResetX = DefaultBallResetX;
    private float _ballResetY = DefaultBallResetY;
    // Stadium pitch plane is approximately Z=-31.997691.  The hull is now
    // symmetric, so spawn one circumradius above the plane: worst case the
    // ball is resting on a vertex, best case it settles the last unit onto a
    // face.  Either way it never spawns embedded in the pitch.
    private const float StadiumPitchPlaneZ = -31.997691f;
    private const float BallResetZ = StadiumPitchPlaneZ + BallCollisionRadius;
    private const float NearBallRange = 80.0f;
    // 2026-08-30: user reported the ball was not pushable/rollable by simply
    // walking into it - native Rubikon body-vs-body contact between a
    // player capsule and a MOVETYPE_VPHYSICS prop apparently doesn't impart
    // enough force to matter here, collision-group fix or not. This adds an
    // explicit, gentle push: while a real (non-bot) player's capsule
    // overlaps the ball and is moving toward its centre, the ball's velocity
    // along that approach axis is driven UP TO (never added past) a target
    // derived from the player's own speed - it can't runaway or compound
    // because it clamps to a target every tick instead of accumulating.
    private const float PlayerCapsuleRadius = 16.0f;
    private const float BallPushContactDistance = BallCollisionRadius + PlayerCapsuleRadius + 2.0f;
    // 2026-08-30: user reported the FIRST push on a fully dead/asleep ball
    // is really hard, then gets easy once it's already rolling. Root cause:
    // a sleeping ~60kg VPhysics body has real inertial resistance in the
    // engine's own collision solver on first contact, which eats into the
    // player's own measured velocity for that contact tick - so
    // approachSpeed on the very first touch can land well under a
    // meaningful threshold even though the player is visibly walking into
    // it, while an already-moving ball never has this problem. Dropped the
    // gate near zero so first contact isn't gated out, and added a kickstart
    // floor so a dead ball's first nudge is never weaker than an
    // already-rolling ball's.
    private const float BallPushMinApproachSpeed = 5.0f;
    private const float BallPushKickstartSpeedThreshold = 15.0f;
    // 2026-09-01: raised 50% alongside the ratio/max below, same request.
    private const float BallPushKickstartMinTarget = 135.0f;
    // 2026-08-30: user wanted to push the ball harder by body contact -
    // raised both the transfer ratio and the speed cap 20%. Promoted to
    // tunable fields (css_sm2ball_push) for the CS:S dribble-speed
    // calibration pass - these two numbers were invented, not measured.
    // 2026-09-01: raised another 50% on top of that (0.84->1.26,
    // 264->396) per live user request.
    private const float DefaultBallPushTransferRatio = 1.26f;
    private const float DefaultBallPushMaxSpeed = 396.0f;
    private const float BallPushHeightGate = 80.0f;
    // 2026-09-02 user report: jumping over the ball never worked - a jump
    // apex (~57u, from sv_jump_impulse/sv_gravity) clears the ball's own
    // 37.6u diameter easily, but this push kept firing anywhere within the
    // old +-80u BALL PUSH HEIGHT GATE, including while the jumping player
    // was fully airborne above the ball - so the ball kept getting shoved
    // back in front of their feet before they landed. Fix: once the
    // player's FEET rise above the ball's own top surface they're clear of
    // it and get no push, regardless of the old symmetric band. The lower
    // bound (player well below the ball, e.g. stairs/ramps) is unchanged.
    private const float BallPushFeetClearance = 2.0f;
    private const float DefaultImpulseSpeed = 1336.0f;
    private const float DefaultImpulseLift = 250.0f;
    private const float MaximumProbeImpulseSpeed = 2500.0f;
    private const float MaximumProbeImpulseLift = 800.0f;
    // Measured eye to ball CENTRE, so it has to grow with the ball radius to
    // keep the reach to the ball SURFACE constant as the ball size changes.
    private const float KickSurfaceReach = 81.5f;
    private const float KickMaximumReach = KickSurfaceReach + BallCollisionRadius;
    // 2026-08-29: widened from 55 degrees (0.574) after live play - what
    // felt like input delay was actually silent outside_aim_cone rejects.
    // Logged real attempts: misses clustered at aimDot 0.36-0.57, and
    // several ACCEPTED kicks were already down at 0.58-0.63, i.e. players
    // expect a much wider cone than 55 degrees to land a kick. 70 degrees
    // covers every logged miss with margin.
    private const float KickMinimumAimDot = 0.34202014f; // cos(70 degrees)
    // 2026-08-30: CS:S-parity plan Phase 1 - measured on the live CS:S
    // reference server via the probe's engine-time-precise sample
    // timestamps: two consecutive real knife-ball hits (controlled test,
    // ball barely moved between them) landed 0.484s apart. Our own 0.35 was
    // an invented value letting CS2 kicks come noticeably faster than
    // CS:S's own knife rhythm ever allowed - aligned to the measurement.
    private const double KickCooldownSeconds = 0.48;
    // 2026-08-30: user request - trapping the ball against a wall and
    // knifing it while looking down should have a random chance to pop it
    // up above the player instead of the usual grounded/lofted shot, in one
    // of three directions (straight/left/right relative to where the
    // player is facing). Wall-trapped detection is a short horizontal trace
    // continuing the kick direction from the ball's centre - if solid
    // geometry is right there, the ball has nowhere to roll that way.
    private const float WallPopMinLookDownDegrees = 25.0f;
    private const float WallPopWallProbeDistance = BallCollisionRadius + 20.0f;
    private const float WallPopTriggerChance = 0.30f;
    private const float WallPopVerticalSpeed = 850.0f;
    private const float WallPopLateralSpeed = 220.0f;
    // CS:S measured a clean kick at ~1336 u/s planar with ~250 u/s of lift,
    // i.e. a delta-velocity of 1359 u/s launched about 10.6 degrees above the
    // horizontal.  This is a delta, not a target speed: the ball keeps its own
    // momentum, the way a real VPhysics impulse works.
    // CS:S measured 1359 u/s, but the CS2 ball is 30% larger and sheds speed
    // faster, so the default sits above the reference.  Tune live with
    // css_sm2ball_power; the CS:S value is 1359.2.
    // 2026-08-29: user reported the kick felt ~20% too strong; dropped the
    // default from 1800 to 1440 (still tunable, and persisted via
    // soccermod_settings.json). Same day, later: raised 8% to 1555, then a
    // further 3% to 1602 after trying it live - 1440 undershot.
    private const float DefaultKickDeltaVelocity = 1602.0f;
    // 2026-08-30: a "left-click on an airborne ball = 30% power" scale was
    // tried and shipped, then reverted the same session on user feedback
    // ("its bad now") - do not re-add without the user asking again.
    // Never kick downward: aiming at a ball on the pitch means looking down, and
    // a downward impulse there only wastes shot power into the ground.
    private const float KickMinimumElevationDegrees = 0.0f;
    private const float KickMaximumElevationDegrees = 60.0f;
    private const float KickLiftAngleLevelDegrees = 10.605f;
    private const float KickLiftAngleFlatDegrees = 2.0f;
    private const float KickLiftAngleLoftedDegrees = 35.0f;
    // 2026-08-30 user request: how much a kick's launch angle follows raw
    // view pitch, for ordinary (non-overhead) kicks - see the elevation
    // computation in TryApplyPrimaryKnifeKick for why this exists and why
    // it fades back to 1.0 near a headed ball rather than applying flatly.
    // Tunable live with css_sm2ball_elevation.
    private const float DefaultKickElevationSensitivity = 0.5f;
    private float _kickElevationSensitivity = DefaultKickElevationSensitivity;
    private const float DefaultKickMaximumBallSpeed = 3500.0f;
    // 2026-09-01 spin: topspin about the horizontal axis perpendicular to
    // the launch direction, fired through the soccermod_native metamod
    // bridge (sm2_native_angular_impulse -> the real typed-variant_t
    // CEntityInstance::AcceptInput; CSSharp's own string AcceptInput is a
    // silent no-op for this input). Empirically validated 2026-09-01 via
    // css_sm2ball_trial flightspin vs flight at identical launches: the
    // in-AIR path stayed pure ballistics (no Magnus in Rubikon - Y held
    // ~0.00 through the whole flight), the difference showed up at
    // CONTACTS: landing roll-through, roll-out length and the wall rebound
    // diverged >100 units. 1.0 = spin exactly at the rolling rate for the
    // ball's own post-kick speed (2026-09-01 live verdict: default);
    // 0 = off = exact pre-spin behaviour (the fallback the user asked
    // for). Tunable live with css_sm2ball_spinfactor.
    // 2026-09-02: dropped from 1.0 to the value the user settled on live
    // (Ball menu "Restore defaults" now targets THIS number, not the old
    // launch default - see RestoreBallDefaults).
    private const float DefaultBallSpinFactor = 0.5f;
    private float _ballSpinFactor = DefaultBallSpinFactor;
    // 2026-09-01 user request: after the opposing-motion-cancel fix above,
    // a ball met IN THE AIR (volley) got noticeably more powerful than
    // before (opposing inherited velocity is no longer just subtracted
    // out). Scales deltaSpeed for airborne contacts only; ground kicks
    // (ballGrounded) are untouched. 1.0 = off. Tunable with
    // css_sm2ball_airkick.
    // 2026-09-02: raised from 0.85 to the value the user settled on live.
    private const float DefaultKickAirborneDeltaScale = 0.92f;
    private float _kickAirborneDeltaScale = DefaultKickAirborneDeltaScale;
    // 2026-09-01 user request: an audible kick sound like CS:S SoMoE's, now
    // that the ball's own physics roll noise is hash-blocked (SoundBlock.cs).
    // Server-side EmitSound of a BASE-GAME sound event from the ball entity
    // - 3D positional for everyone, no custom content needed. The default is
    // only a first candidate; the name is a live tunable
    // (css_sm2ball_kicksound) precisely so candidates can be A/B'd over RCON
    // until one feels right. Empty string = off. Once the Workshop-addon
    // route (MultiAddonManager, Round 3) is armed later, the real CS:S
    // kick.wav becomes a custom sound event set through this same dial.
    private const string DefaultKickSoundName = "Weapon_Knife.HitWall";
    private string _kickSoundName = DefaultKickSoundName;
    private const float BallMassKilograms = 60.694092f; // matches mass_override in the vmdl
    // phys_thruster: Start On (1) + Apply Force (2) + Apply Torque (4).
    // Deliberately not "Ignore Pos" (32) and not "Ignore Mass" (16): the
    // off-centre position is exactly what produces the spin we want, and the
    // ball's mass is known and correct.
    private const string ThrusterDesignerName = "phys_thruster";
    private const string ThrusterTargetName = "sm2_ball_kick_thruster";
    private const uint ThrusterSpawnFlags = 1 | 2 | 4;
    private const float DefaultKickThrusterScale = 1.0f;
    private const float DefaultKickThrusterSeconds = 0.05f;
    private const float DefaultKickBackspinBias = 0.35f;
    // Phase A (direct ApplyAbsVelocityImpulse / ApplyLocalAngularVelocityImpulse
    // entity inputs) measured as dead in this CS2 build: AcceptInput accepted
    // the call, logged no error, and produced no directed motion.  Spin comes
    // from a torque-ONLY phys_thruster instead: Start On (1) | Apply Torque (4),
    // deliberately WITHOUT Apply Force (2) so it never adds linear velocity —
    // that stays on the existing Teleport-velocity kick, which is exact.
    // See docs/ball-foundation/2026-08-29-implementation-plan.md Phase A/B.
    private const string SpinThrusterTargetName = "sm2_ball_spin_thruster";
    private const uint SpinThrusterSpawnFlags = 1 | 4;
    // Rolling angular speed for a ball of radius BallCollisionRadius moving at
    // the kick's planar speed: omega = k * v / r.  k=1.0 is pure rolling spin;
    // tunable because CS:S kicks were likely sub-rolling.
    // Sign of the offset (+1 = thruster above ball centre, -1 = below) that
    // produces forward topspin.  Unverified by derivation alone per the plan;
    // calibrate live with css_sm2ball_torque_test, then flip this if needed.
    private const float DefaultSpinThrusterZSign = 1.0f;
    private const float DefaultSpinThrusterSeconds = 0.10f;
    private const float ArenaProbeDistance = 4096.0f;
    // Reference-trial parameters, chosen to mirror the CS:S captures in
    // artifacts/css-reference so the two data sets can be diffed directly.
    private const float TrialSampleInterval = 0.1f;
    private const int TrialSampleCount = 200;
    private const float TrialRollSpeed = 400.0f;
    private const float TrialWallSpeed = 600.0f;
    // 2026-08-30: CS:S-parity plan Phase 1 - mirrors sm_xslref_trial flight
    // on the CS:S reference server exactly (same defaults, same log-and-
    // diff-after approach as roll/wall/drop) so apex/hangtime/range can be
    // compared directly instead of tuning gravity/lift by feel.
    private const float TrialFlightSpeed = 1359.2f;
    private const float TrialFlightAngleDegrees = 10.6f;
    private const float TrialDropHeight = 244.0f;
    // Wall-assist fallback for the CS:S "hochbuggen" hop.  This is NOT a
    // return to v2's aim-heuristic wall pop: it fires only in response to a
    // real, already-computed physics collision (a measured planar-velocity
    // reversal), and only adds a vertical component sized from the actual
    // speed the collision removed.  See
    // docs/ball-foundation/2026-08-29-implementation-plan.md Phase C — the
    // native spin route (phys_thruster Apply Torque) measured completely
    // inert without Apply Force also set, and force+torque combined measured
    // wildly nonlinear and did not reliably respect its own forcetime
    // auto-shutoff, so it is not a controllable spin source in this CS2 build.
    // CS:S measured -334 -> +61 u/s normal rebound plus +43 u/s vertical at
    // the wall: ~0.18 normal-speed retention, and a vertical conversion ratio
    // of ~0.129 of the speed actually lost in the bounce.
    private const float WallAssistMinimumApproachSpeed = 150.0f;
    private const float WallAssistReversalDotThreshold = -0.30f;
    private const float DefaultWallAssistConversionRatio = 0.129f;
    private const float DefaultWallAssistMaxAddedVertical = 200.0f;
    private const float DefaultWallAssistMinimumNormalRetention = 0.18f;
    private const double WallAssistCooldownSeconds = 0.35;
    // Rubikon can spend more than four ticks compressing/sliding in a wall
    // contact before it exposes the small outgoing velocity.  Keep enough
    // history to retain the real pre-contact speed through that solver phase.
    // The nearby-solid trace below remains the collision gate, so the longer
    // window cannot turn ordinary rolling deceleration into a wall bounce.
    private const int WallAssistHistoryTicks = 12;
    // A single velocity write during Rubikon's contact frame can be consumed
    // while the ball is still touching the wall. Hold the already-calibrated
    // rebound for roughly one ball-radius of travel (4 * 1/64 s) so the ball
    // actually separates from the surface instead of looking glued to it.
    // This reasserts a target velocity; it does not add four impulses.
    private const int WallAssistSeparationFrames = 4;
    private const float WallAssistContactSpeedRatio = 0.50f;
    private const float WallAssistContactProbeExtraDistance = 6.0f;
    // Read-only arena traces measured these interior wall planes on
    // soccer_cssl_stadium_v8. The low midfield collision edge is invisible to
    // CounterStrikeSharp traces, so position supplies its map-specific normal.
    private const float FoundationWallPlaneX = 1279.97f;
    private const float FoundationWallPlaneY = 1663.97f;
    // Ball settle deadband (2026-08-29): the compound hull + zero-damping
    // architecture is deliberate (see the hull-compiler root-cause doc), but
    // it means a resting ball never mathematically reaches zero on its own -
    // it was still measured coasting at ~5 u/s after 4+ seconds.  This is a
    // one-shot, edge-triggered zeroing, NOT a per-tick re-teleport: once the
    // ball is grounded and below the speed threshold for SettleTicks in a
    // row, its velocity is teleported to exactly zero once and latched
    // "settled".  The latch only clears when a real kick/push produces a
    // measured speed above 2x the threshold, which a kick's hundreds-of-u/s
    // delta-velocity clears on the very next tick with no special-casing
    // needed at the kick site.
    private const bool DefaultSettleEnabled = true;
    private const float DefaultSettleSpeedThreshold = 8.0f;
    private const int DefaultSettleTicks = 16;
    private const float SettleGroundToleranceZ = 2.5f;

    private readonly HashSet<int> _playersNearBall = new();
    private readonly HashSet<int> _playersPushingBall = new();
    private readonly Dictionary<int, double> _lastAcceptedKickTimeBySlot = new();
    private CPhysicsPropMultiplayer? _ball;
    private CDynamicProp? _ballVisual;
    private CPhysicsPropMultiplayer? _parkedMapBall;
    private Vector? _parkedMapBallOrigin;
    private QAngle? _parkedMapBallAngles;
    private ulong _parkedMapBallInteractsAs;
    private ulong _parkedMapBallInteractsWith;
    private ulong _parkedMapBallInteractsExclude;
    private Vector? _previousBallOrigin;
    private double _previousBallSampleTime;
    private Vector _derivedBallVelocity = new(0.0f, 0.0f, 0.0f);
    private string _currentMapName = string.Empty;
    private BallProbeMode _mode = BallProbeMode.Baseline;
    private int _nextBallBindTick;
    private int _nextPeriodicSnapshotTick;
    private float _gameplayMassScale = DefaultGameplayMassScale;
    private float _gameplayFriction = DefaultGameplayFriction;
    private float _gameplayElasticity = DefaultGameplayElasticity;
    private float _gameplayGravityScale = DefaultGameplayGravityScale;
    private string _ballPhysicsModel = "models/soccermod/ball_large_1850.vmdl";
    private string _ballPhysicsModelKey = "large1850";
    private float _kickDeltaVelocity = DefaultKickDeltaVelocity;
    private float _kickMaximumBallSpeed = DefaultKickMaximumBallSpeed;
    // 2026-08-29: user reported CS:S gave a noticeably faster hit when the
    // ball landed right on top of your own head and you knifed it there -
    // our fixed-delta kick doesn't scale with anything, so it never
    // reproduced that. Root cause found: aiming at a ball resting on your
    // own head means forward.Z is close to 1 (looking almost straight up),
    // which drives aimElevation to the KickMaximumElevationDegrees clamp
    // (60 degrees) every time - so an overhead ball always launches at the
    // same clamped angle with the same delta, regardless of how it's hit.
    // This adds power (not angle) back in for that specific geometry,
    // scaled by how far above your own eye level the ball's centre is.
    // 2026-08-30: user reported airborne kicks got noticeably too strong
    // after this was added at 0.5 (any ball even a bit above eye level got
    // real extra power, not just a literal ball-on-head volley) - dropped
    // to 0.15, then another 5% relative (0.1425 -> 0.14) after still
    // feeling too strong. Both changes applied live via css_sm2ball_power,
    // default updated to match.
    private const float DefaultKickOverheadBonusMax = 0.14f;
    // Soft pass: aim-below-centre distance (in ball radii) at which power
    // starts dropping, where it bottoms out, and the floor it bottoms out
    // at. Tunable live with css_sm2ball_softpass.
    private const float DefaultSoftPassStartRatio = 0.35f;
    private const float DefaultSoftPassFullRatio = 1.60f;
    private const float DefaultSoftPassMinPowerScale = 0.25f;
    private float _softPassStartRatio = DefaultSoftPassStartRatio;
    private float _softPassFullRatio = DefaultSoftPassFullRatio;
    private float _softPassMinPowerScale = DefaultSoftPassMinPowerScale;
    // Soft PITCH (2026-08-30 user request): looking further down should
    // make a left-click kick progressively softer, independent of where the
    // aim ray actually lands relative to the ball's centre (that's soft
    // PASS, above - the two stack when looking down at a ball also puts the
    // aim ray below its centre, which is intentional: a very steep look-down
    // toe-tap should be gentle from both effects). Same blend shape as soft
    // pass. Tunable live with css_sm2ball_softpitch.
    private const float DefaultSoftPitchStartDegrees = 30.0f;
    private const float DefaultSoftPitchFullDegrees = 85.0f;
    private const float DefaultSoftPitchMinPowerScale = 0.35f;
    private float _softPitchStartDegrees = DefaultSoftPitchStartDegrees;
    private float _softPitchFullDegrees = DefaultSoftPitchFullDegrees;
    private float _softPitchMinPowerScale = DefaultSoftPitchMinPowerScale;
    // Right-click kick (2026-08-30 user request): same aim/lift/soft-pass/
    // soft-pitch logic as the primary knife kick, at a reduced power. This
    // supersedes the earlier "knife left-click only" rule - the user
    // explicitly asked for a lighter secondary tap. Tunable live with
    // css_sm2ball_rightclick.
    // 2026-09-02: raised from 0.5 to the value the user settled on live.
    private const float DefaultRightClickPowerScale = 0.6f;
    private float _rightClickPowerScale = DefaultRightClickPowerScale;
    // Left-click kick power scale (2026-08-30 user request): "twice as
    // strong as current right-click" - right-click was 0.50 at the time,
    // so 1.0 (i.e. unchanged from the original hardcoded value). Kept as
    // its own independent, persisted tunable rather than derived from
    // _rightClickPowerScale, matching how every other kick scale in this
    // file works - tuning right-click later should NOT silently move
    // left-click too. Tunable live with css_sm2ball_leftclick.
    // 2026-09-02: dropped from 1.0 to the value the user settled on live.
    private const float DefaultLeftClickPowerScale = 0.9f;
    private float _leftClickPowerScale = DefaultLeftClickPowerScale;
    // 2026-08-30 user request: a crouched left-click kick felt weaker than
    // standing (it was inheriting the flat 0.85 left-click scale like any
    // other stance) - crouch should be a deliberate full-power strike, not
    // penalised. Independent tunable rather than a hardcoded bypass so it
    // can be tuned/disabled the same way as every other kick scale.
    // Tunable live with css_sm2ball_leftclick_crouch.
    private const float DefaultLeftClickCrouchPowerScale = 1.0f;
    private float _leftClickCrouchPowerScale = DefaultLeftClickCrouchPowerScale;
    // 2026-08-31 user request: while crouching, left-click and right-click
    // should feel identical (button doesn't matter) - kept as its own
    // independent tunable rather than reusing _leftClickCrouchPowerScale for
    // both, matching every other kick scale in this file (see the
    // left-click-power-scale comment above for why). Both crouch scales are
    // simply set to the same value by default. Tunable live with
    // css_sm2ball_rightclick_crouch.
    private const float DefaultRightClickCrouchPowerScale = 1.0f;
    private float _rightClickCrouchPowerScale = DefaultRightClickCrouchPowerScale;
    private float _ballPushTransferRatio = DefaultBallPushTransferRatio;
    private float _ballPushMaxSpeed = DefaultBallPushMaxSpeed;
    private float _kickOverheadBonusMax = DefaultKickOverheadBonusMax;
    private KickMode _kickMode = KickMode.Velocity;
    private float _kickThrusterScale = DefaultKickThrusterScale;
    private float _kickThrusterSeconds = DefaultKickThrusterSeconds;
    private float _kickBackspinBias = DefaultKickBackspinBias;
    private float _spinThrusterZSign = DefaultSpinThrusterZSign;
    private float _spinThrusterSeconds = DefaultSpinThrusterSeconds;
    private float _torqueTestForce = 5000.0f;
    private float _wallAssistConversionRatio = DefaultWallAssistConversionRatio;
    private float _wallAssistMaxAddedVertical = DefaultWallAssistMaxAddedVertical;
    private float _wallAssistMinimumNormalRetention = DefaultWallAssistMinimumNormalRetention;
    private bool _wallAssistEnabled = true;
    private double _lastWallAssistTime = double.NegativeInfinity;
    private readonly Queue<Vector> _recentBallVelocities = new();
    private int _wallAssistGeneration;
    private int _ballCollisionGroup = 0;
    private bool _settleEnabled = DefaultSettleEnabled;
    private float _settleSpeedThreshold = DefaultSettleSpeedThreshold;
    private int _settleTicks = DefaultSettleTicks;
    private int _settleLowSpeedTicks;
    private bool _ballSettled;
    private CounterStrikeSharp.API.Modules.Timers.Timer? _trialTimer;
    private string _trialKind = string.Empty;
    private int _trialSeq;
    private int _trialSample;
    private Vector? _trialPreviousOrigin;
    private double _trialPreviousTime;
    private double _trialStartTime;

    public override string ModuleName => "CS2 SoccerMod";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Sergi + Codex";
    public override string ModuleDescription =>
        "Native CS2 VPhysics ball on a symmetric hull with the Source 1 XSL mass, glass surface values and an impulse-based knife kick.";

    public override void Load(bool hotReload)
    {
        _currentMapName = Server.MapName;
        AdminOnLoad();
        BallSettingsOnLoad();
        MatchSettingsOnLoad();
        ApplyDeadChatMode();
        AddCommand("css_sm2_reload_settings", "Server only: re-read soccermod_settings.json from disk.", OnReloadSettingsCommand);
        AddCommand("css_sm2ball_softpass", "Admin: tune the aim-below-centre soft pass (start, full, minScale).", OnBallSoftPassCommand);
        AddCommand("css_sm2ball_softpitch", "Admin: tune the look-down soft kick (startDeg, fullDeg, minScale).", OnBallSoftPitchCommand);
        AddCommand("css_sm2ball_rightclick", "Admin: tune the right-click kick power scale.", OnBallRightClickCommand);
        AddCommand("css_sm2ball_leftclick", "Admin: tune the left-click kick power scale.", OnBallLeftClickCommand);
        AddCommand("css_sm2ball_leftclick_crouch", "Admin: tune the crouched left-click kick power scale.", OnBallLeftClickCrouchCommand);
        AddCommand("css_sm2ball_rightclick_crouch", "Admin: tune the crouched right-click kick power scale.", OnBallRightClickCrouchCommand);
        AddCommand("css_sm2ball_spinfactor", "Admin: tune kick spin/curve strength (0.0-2.0, 1.0=pure rolling, or off).", OnBallSpinFactorCommand);
        AddCommand("css_sm2ball_elevation", "Admin: tune how much view pitch drives kick launch angle (0.1-1.0).", OnBallElevationCommand);
        AddCommand("css_sm2ball_push", "Admin: tune body-push transfer ratio and max speed.", OnBallPushCommand);
        AddCommand("css_sm2ball_airkick", "Admin: tune airborne (volley) kick power scale (0.1-1.0, 1.0=off).", OnBallAirKickCommand);
        AddCommand("css_sm2ball_kicksound", "Admin: set the sound event played on every kick (soundEventName or off).", OnBallKickSoundCommand);
        AddCommand("css_sm2ball_center", "Admin: calibrate the kickoff spot (here|x y|default).", OnBallCenterCommand);
        AddCommand("css_sm2_high_geometry", "Server only: log brush geometry above a height, to find the sky path.", OnHighGeometryCommand);
        AddCommand("css_sm2_button_probe", "Server only: log every func_button and the roof scoreboard digits' fade state.", OnButtonProbeCommand);
        SprintOnLoad();
        LandingSoundOnLoad();
        SoundBlockOnLoad();
        HealthOnLoad();
        ChatSettingsOnLoad();
        DuckJumpBlockOnLoad();
        AfkOnLoad();
        RefereeOnLoad();
        GkAreasOnLoad();
        StatsOnLoad();
        MoveProbeOnLoad();
        GameTextTestOnLoad();
        BodyImpactOnLoad();
        MatchOnLoad();
        WebsiteCapOnLoad();
        TeamColorOnLoad();
        KillOnLoad();
        TeamJoinOnLoad();
        GkSkinOnLoad();
        ThirdPersonOnLoad();
        RegisterListener<Listeners.OnClientDisconnect>(MenuOnPlayerDisconnect);
        RegisterListener<Listeners.OnClientDisconnect>(AfkOnPlayerDisconnect);
        RegisterListener<Listeners.OnClientDisconnect>(BodyImpactOnPlayerDisconnect);
        RegisterListener<Listeners.OnClientDisconnect>(GkSkinOnPlayerDisconnect);
        RegisterListener<Listeners.OnClientDisconnect>(ThirdPersonOnPlayerDisconnect);
        RegisterListener<Listeners.OnClientDisconnect>(TeamColorOnPlayerDisconnect);
        MenuOnLoad();
        SocialOnLoad();
        ChatInputOnLoad();
        CapOnLoad();
        TrainingOnLoad();
        RegisterListener<Listeners.OnMapStart>(OnMapStart);
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            // The authoritative ball uses the Jabulani resource already
            // packaged with the Workshop map. No models/soccermod resource is
            // advertised to clients, eliminating the missing-model cascade.
            manifest.AddResource(BallVisualModelName);
            manifest.AddResource(StadiumRadarTextureResource);
            manifest.AddResource(StadiumLoadingScreenResource);
            // Stock base-game character models used by TeamColor's uniform-model
            // mode. Despite shipping in every client's base VPKs, SetModel() on a
            // pawn still requires the resource to be resident in THIS map's
            // manifest once the stadium Workshop addon is the active content
            // context, or it fails at runtime with RESOURCE_TYPE_MODEL "not
            // resident" instead of silently falling back.
            manifest.AddResource(ModelPathT);
            manifest.AddResource(ModelPathCt);
            if (_menuRenderMode == MenuRenderMode.Classic)
            {
                manifest.AddResource(ClassicHudLayoutResource);
                manifest.AddResource(ClassicHudStyleResource);
                manifest.AddResource(ClassicHudScriptResource);
            }
        });
        RegisterListener<Listeners.OnTick>(OnTick);
        RegisterListener<Listeners.OnEntitySpawned>(OnEntitySpawned);
        RegisterListener<Listeners.OnPlayerButtonsChanged>(OnPlayerButtonsChanged);
        RegisterListener<Listeners.OnPlayerTakeDamagePre>(OnPlayerTakeDamagePre);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);

        AddCommand("css_sm2ball_status", "Print the current SoccerMod ball state.", OnBallStatusCommand);
        AddCommand("css_sm2inventory_status", "Print player inventory state.", OnInventoryStatusCommand);
        AddCommand("css_sm2ball_mode", "Server only: set baseline or wake probe mode.", OnBallModeCommand);
        AddCommand("css_sm2ball_impulse", "Server only: apply one controlled ball impulse.", OnBallImpulseCommand);
        AddCommand("css_sm2ball_trial", "Server only: run a roll/wall/drop reference trial against the CS:S captures.", OnBallTrialCommand);
        AddCommand("css_sm2ball_kickmode", "Server only: switch between the delta-velocity and phys_thruster kick.", OnBallKickModeCommand);
        AddCommand("css_sm2ball_model", "Server only: switch the ball collision model.", OnBallModelCommand);
        AddCommand("css_sm2ball_thrust", "Server only: fire one phys_thruster kick at the ball.", OnBallThrustCommand);
        AddCommand("css_sm2ball_torque_test", "Server only: fire one torque-only spin thruster to calibrate spin sign.", OnBallTorqueTestCommand);
        AddCommand("css_sm2ball_spin_isolate", "Server only: test whether Teleport(velocity:) preserves angular velocity from a thruster burst.", OnBallSpinIsolateCommand);
        AddCommand("css_sm2ball_impulse_input", "Server only: probe ApplyAbsVelocityImpulse on the ball (Phase A).", OnBallImpulseInputCommand);
        AddCommand("css_sm2ball_native_handle", "Server only: print the ball CEntityInstance* address for the native bridge.", OnBallNativeHandleCommand);
        AddCommand("css_sm2ball_spin_input", "Server only: probe ApplyLocalAngularVelocityImpulse on the ball (Phase A).", OnBallSpinInputCommand);
        AddCommand("css_sm2ball_power", "Server only: tune kick delta-velocity and speed clamp.", OnBallPowerCommand);
        AddCommand("css_sm2ball_wallassist", "Server only: tune wall assist (on|off|verticalRatio) [maxAdded] [minNormalRetention].", OnBallWallAssistCommand);
        AddCommand("css_sm2ball_collision", "Server only: set the ball collision group (20=PUSHAWAY passes through players).", OnBallCollisionCommand);
        AddCommand("css_sm2ball_settle", "Admin: tune the low-speed settle deadband (on|off|<threshold> [ticks]).", OnBallSettleCommand);
        AddCommand("css_sm2ball_defaults", "Admin: restore spin/air-kick/left-right-click/push/kicksound/impact/settle/elevation to their defaults.", OnBallDefaultsCommand);
        AddCommand("css_sm2ball_physics", "Server only: inspect or tune the live CS2 ball physics profile.", OnBallPhysicsCommand);
        AddCommand("css_sm2ball_replace_test", "Server only: replace the defective map ball with a clean test ball.", OnBallReplaceTestCommand);
        AddCommand("css_sm2ball_reset_center", "Server only: rebuild the clean ball at the known map center.", OnBallResetCenterCommand);
        AddCommand("css_sm2ball_restore_map", "Server only: remove the test ball and restore the map ball.", OnBallRestoreMapCommand);
        AddCommand("css_sm2ball_trace_arena", "Server only: trace the current pitch boundaries without changing physics.", OnBallTraceArenaCommand);
        AddCommand("css_sm2knife_give", "Server only: perform one controlled knife grant.", OnKnifeGiveCommand);

        Logger.LogInformation(
            "[SM2DIAG] load version={Version} hotReload={HotReload} mode={Mode}",
            ModuleVersion,
            hotReload,
            _mode);

        Server.NextFrame(() =>
        {
            BindBall("plugin_load");
            SnapshotBall("plugin_load");
            SnapshotAllPlayers("plugin_load");
            NeutralizeLegacyMapKillTriggers("plugin_load");
            NeutralizeSkyPath("plugin_load");
            RemoveMapScoreboardButtons("plugin_load");
            EnsureBallFoundation("plugin_load");
            EnsureAllPlayerKnives("plugin_load");
        });
    }

    public override void Unload(bool hotReload)
    {
        ThirdPersonOnUnload();
        MenuOnUnload();
        TrainingOnUnload();
        _ball = null;
        RemoveOwnedBallVisual();
        _parkedMapBall = null;
        _parkedMapBallOrigin = null;
        _parkedMapBallAngles = null;
        _playersNearBall.Clear();
        _playersPushingBall.Clear();
        _lastAcceptedKickTimeBySlot.Clear();
        ResetDerivedMotion();
    }

    private void OnMapStart(string mapName)
    {
        MenuOnMapStart();
        TrainingOnMapStart();
        _currentMapName = mapName;
        _ball = null;
        // 2026-08-30 fix: this used to just drop the reference
        // (_ballVisual = null), leaking the actual CDynamicProp entity -
        // every map load left the PREVIOUS visual alive and orphaned.
        // RemoveOwnedBallVisual actually kills it (and sweeps up any
        // other stragglers by name, cleaning up prior leaks too).
        RemoveOwnedBallVisual();
        _parkedMapBall = null;
        _parkedMapBallOrigin = null;
        _parkedMapBallAngles = null;
        _playersNearBall.Clear();
        _playersPushingBall.Clear();
        _lastAcceptedKickTimeBySlot.Clear();
        ResetDerivedMotion();
        _mode = BallProbeMode.Baseline;
        _nextBallBindTick = 0;
        _nextPeriodicSnapshotTick = 0;
        _skyPathNeutralized = false;
        _mapScoreboardRemoved = false;

        Logger.LogInformation(
            "[SM2DIAG] map_start map={MapName} mode={Mode}",
            mapName,
            _mode);

        Server.NextFrame(() =>
        {
            NeutralizeLegacyMapKillTriggers("map_start");
            NeutralizeSkyPath("map_start");
            RemoveMapScoreboardButtons("map_start");
            BindBall("map_start");
            SnapshotBall("map_start");
        });
        AddTimer(0.25f, () => NeutralizeLegacyMapKillTriggers("map_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(0.25f, () => NeutralizeSkyPath("map_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(0.25f, () => RemoveMapScoreboardButtons("map_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(0.25f, () => FixMapScoreboardVisibility("map_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(0.25f, () => EnsureBallFoundation("map_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.0f, () => EnsureBallFoundation("map_start_plus_1_00s"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        _ball = null;
        // The engine recreates the map-authored Jabulani before this event.
        // EnsureBallFoundation promotes that same baseline entity into the
        // authoritative ball; no late-spawned proxy or overlapping body.
        RemoveOwnedBallVisual();
        _parkedMapBall = null;
        _parkedMapBallOrigin = null;
        _parkedMapBallAngles = null;
        _playersNearBall.Clear();
        _playersPushingBall.Clear();
        _lastAcceptedKickTimeBySlot.Clear();
        ResetDerivedMotion();
        // Rebuild the ball SYNCHRONOUSLY, in this same frame. CS2's round
        // restart wipes our runtime-spawned ball and restores the map's own
        // (defective, and never meant to be seen) one; doing the swap a
        // frame later meant clients briefly rendered the map ball and then
        // watched ours pop in on top of it. Same frame = no visible spawn-in.
        // The NextFrame/timer passes below are idempotent safety nets for
        // the case where the entity isn't ready yet at this exact point.
        NeutralizeLegacyMapKillTriggers("round_start_immediate");
        NeutralizeSkyPath("round_start_immediate");
        RemoveMapScoreboardButtons("round_start_immediate");
        FixMapScoreboardVisibility("round_start_immediate");
        BindBall("round_start_immediate");
        EnsureBallFoundation("round_start_immediate");
        ForceBallFullStop("round_start_immediate");
        Server.NextFrame(() =>
        {
            NeutralizeLegacyMapKillTriggers("round_start");
            NeutralizeSkyPath("round_start");
            RemoveMapScoreboardButtons("round_start");
            BindBall("round_start");
            SnapshotBall("round_start");
            SnapshotAllPlayers("round_start");
            EnsureBallFoundation("round_start_next_frame");
            EnsureAllPlayerKnives("round_start_next_frame");
        });
        AddTimer(0.25f, () => EnsureBallFoundation("round_start_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(0.25f, () =>
        {
            EnsureAllPlayerKnives("round_start_plus_0_25s");
            ApplyAllTeamAppearances("round_start_plus_0_25s");
        }, TimerFlags.STOP_ON_MAPCHANGE);
        TeamColorOnRoundStart();
        SprintOnRoundStart();
        MatchOnRoundStart();
        CapOnRoundStart();
        TrainingOnRoundStart();
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player is null || !player.IsValid)
        {
            return HookResult.Continue;
        }

        ResetSprint(player);
        HealthOnPlayerSpawn(player);
        WebsiteCapOnPlayerSpawn(player);
        RefereeEnforceOnSpawn(player);
        TeamColorOnPlayerSpawn(player);
        ThirdPersonOnPlayerSpawn(player);
        MenuMaybeSendBindReminder(player);
        SnapshotPlayer(player, "spawn_event");
        Server.NextFrame(() => SnapshotPlayerIfValid(player, "spawn_next_frame"));
        AddTimer(0.25f, () =>
        {
            SnapshotPlayerIfValid(player, "spawn_plus_0_25s_pre_grant");
            EnsurePlayerKnife(player, "spawn_plus_0_25s");
            ApplyTeamAppearance(player, "spawn_plus_0_25s");
            ThirdPersonReassertAfterSpawn(player);
        }, TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.0f, () => SnapshotPlayerIfValid(player, "spawn_plus_1_00s"), TimerFlags.STOP_ON_MAPCHANGE);
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        // A player death must never reset a live football. The legacy Stadium
        // kill triggers are neutralized separately and are not gameplay goals.
        Server.NextFrame(() => NeutralizeLegacyMapKillTriggers("player_death"));
        CapFightOnPlayerDeath(@event);
        return HookResult.Continue;
    }

    private void OnEntitySpawned(CEntityInstance entity)
    {
        if (!string.Equals(entity.DesignerName, "trigger_hurt", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Server.NextFrame(() => NeutralizeLegacyMapKillTriggers("trigger_hurt_spawned"));
    }

    private void OnTick()
    {
        if (Server.TickCount >= _nextBallBindTick)
        {
            _nextBallBindTick = Server.TickCount + 64;
            NeutralizeLegacyMapKillTriggers("maintenance");
            if (!_skyPathNeutralized)
            {
                NeutralizeSkyPath("maintenance");
            }
            if (!_mapScoreboardRemoved)
            {
                RemoveMapScoreboardButtons("maintenance");
            }
            if (!_mapScoreboardCullFixed)
            {
                FixMapScoreboardVisibility("maintenance");
            }
            BindBall("maintenance");
            EnsureBallFoundation("maintenance");
        }

        if (_mode == BallProbeMode.Wake && _ball is { IsValid: true })
        {
            _ball.AcceptInput("Wake");
        }

        UpdateDerivedMotion();
        UpdateTrainingBallMotion();
        SprintOnTick();
        MuteLandingOnTick();
        DuckJumpBlockOnTick();
        AfkOnTick();
        MatchOnTick();
        WebsiteCapOnTick();
        MenuOnTick();
        ThirdPersonOnTick();

        if (Server.TickCount >= _nextPeriodicSnapshotTick)
        {
            _nextPeriodicSnapshotTick = Server.TickCount + (64 * 10);
            SnapshotBall("periodic_10s");
        }

        ObservePlayerProximity();
        ApplyPlayerBallPush();
        ApplyBallPlayerImpact();
    }

    // The user does not want the ball to be able to kill or hurt anyone --
    // pushing and bouncing off players is the whole point, damage is not.
    // Fixing the collision group (see ApplyBallCollisionGroup) made the ball
    // a real solid body to players, which also switched on the engine's
    // default physics-impact damage for a fast, ~60kg MOVETYPE_VPHYSICS prop
    // hitting a hitbox.  Block that specifically when our ball is the
    // inflictor; every other damage source is untouched.
    private HookResult OnPlayerTakeDamagePre(CCSPlayerPawn victim, CTakeDamageInfo info)
    {
        var inflictor = info.Inflictor.Value;
        if (inflictor is { IsValid: true }
            && ((_ball is { IsValid: true } && inflictor.Index == _ball.Index)
                || IsTrainingBallIndex(inflictor.Index)))
        {
            return HookResult.Stop;
        }

        // Godmode (SoMoE health.sp parity) is enforced HERE, not by setting
        // pawn.TakesDamage = false.
        //
        // 2026-08-30, learned the hard way: TakesDamage=false does NOT just
        // block damage in this CS2 build - it left players unable to MOVE,
        // unable to be slain (css_slay silently did nothing), and swinging
        // the knife at an uncapped rate. The engine evidently gates
        // movement/weapon logic on that same flag. Blocking damage at the
        // hook is surgical and cannot touch movement.
        //
        // CORRECTION 2026-09-01: the original comment here claimed
        // CommitSuicide produces an INVALID Attacker and therefore always
        // passed through godmode untouched - empirically false. Verified
        // live via a new css_sm2_playerstatus probe: with godmode ON,
        // css_slay reported success but the target's LifeState stayed
        // ALIVE; with godmode OFF, the identical call actually killed them.
        // CommitSuicide sets Attacker to the VICTIM ITSELF (a real, valid
        // entity), not null - so the old "no valid attacker" check was
        // silently blocking every self-inflicted kill (!kill, css_slay,
        // and the goal-punish conceding-team kill) the entire time godmode
        // was on, which is the default. The correct signal for "this is a
        // self-inflicted kill, let it through" is attacker == victim, not
        // attacker == null.
        // Cap fight (Cap.cs, SoMoE cap.sp duel): the ONE sanctioned window
        // for player-vs-player damage, scoped to the fight's participants.
        if (_capFightStarted
            && info.Attacker.Value is { IsValid: true } fightAttacker
            && _capFightPawnIndexes.Contains(fightAttacker.Index)
            && _capFightPawnIndexes.Contains(victim.Index))
        {
            return HookResult.Continue;
        }

        if (_healthGodmodeEnabled
            && info.Attacker.Value is { IsValid: true } attacker
            && attacker.Index != victim.Index)
        {
            return HookResult.Stop;
        }

        return HookResult.Continue;
    }

    private void OnPlayerButtonsChanged(
        CCSPlayerController player,
        PlayerButtons pressed,
        PlayerButtons released)
    {
        // 2026-08-30 user request: right-click (Attack2) is now a sanctioned
        // secondary kick at reduced power, sharing every bit of the primary
        // kick's aim/lift/soft-pass/soft-pitch logic. This supersedes the
        // older "knife left-click only" rule.
        var isPrimary = (pressed & PlayerButtons.Attack) != 0;
        var isSecondary = (pressed & PlayerButtons.Attack2) != 0;
        if (!isPrimary && !isSecondary)
        {
            return;
        }

        var pawn = player.PlayerPawn.Value;
        var activeWeapon = pawn?.WeaponServices?.ActiveWeapon.Value;
        var ballDistance = GetBallDistance(pawn);
        Logger.LogInformation(
            "[SM2DIAG] primary_input slot={Slot} name={Name} team={Team} alive={Alive} active={ActiveWeapon} ballDistance={BallDistance} button={Button}",
            player.Slot,
            player.PlayerName,
            player.Team,
            IsAlive(pawn),
            activeWeapon?.DesignerName ?? "<none>",
            FormatNullable(ballDistance),
            isPrimary ? "primary" : "secondary");

        if (isPrimary)
        {
            var leftClickScale = IsPlayerCrouching(pawn) ? _leftClickCrouchPowerScale : _leftClickPowerScale;
            TryApplyPrimaryKnifeKick(player, pawn, activeWeapon, leftClickScale, "primary");
        }
        else
        {
            var rightClickScale = IsPlayerCrouching(pawn) ? _rightClickCrouchPowerScale : _rightClickPowerScale;
            TryApplyPrimaryKnifeKick(player, pawn, activeWeapon, rightClickScale, "secondary");
        }
    }

    private void TryApplyPrimaryKnifeKick(
        CCSPlayerController player,
        CCSPlayerPawn? pawn,
        CBasePlayerWeapon? activeWeapon,
        float powerScale,
        string kickInputMode)
    {
        if (!IsEligiblePlayer(player) || pawn?.AbsOrigin is not { } playerOrigin)
        {
            LogKickRejected(player, "player_ineligible");
            return;
        }

        if (activeWeapon is null
            || !activeWeapon.IsValid
            || !activeWeapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase))
        {
            LogKickRejected(player, "active_weapon_not_knife");
            return;
        }

        // 2026-09-01 training balls (Training.cs): the kick targets the
        // nearest playable ball that is inside reach AND the aim cone (the
        // match ball wins a tie). Everything after the selection is the
        // unchanged kick model; the match-ball-only side effects (stats/GK
        // touch, freeze, thruster) are gated on target.IsMatchBall.
        BindBall("primary_kick");
        var candidates = PlayableBalls().ToList();
        if (candidates.Count == 0)
        {
            LogKickRejected(player, "clean_ball_unavailable");
            return;
        }

        var now = Server.TickedTime;
        if (_lastAcceptedKickTimeBySlot.TryGetValue(player.Slot, out var lastAcceptedTime)
            && now - lastAcceptedTime < KickCooldownSeconds)
        {
            LogKickRejected(player, "cooldown");
            return;
        }

        var viewOffset = pawn.ViewOffset;
        var eyePosition = new Vector(
            playerOrigin.X + viewOffset.X,
            playerOrigin.Y + viewOffset.Y,
            playerOrigin.Z + viewOffset.Z);

        var eyeAngles = pawn.EyeAngles;
        var pitchRadians = eyeAngles.X * (MathF.PI / 180.0f);
        var yawRadians = eyeAngles.Y * (MathF.PI / 180.0f);
        var cosPitch = MathF.Cos(pitchRadians);
        var forward = new Vector(
            cosPitch * MathF.Cos(yawRadians),
            cosPitch * MathF.Sin(yawRadians),
            -MathF.Sin(pitchRadians));

        PlayableBall? selected = null;
        Vector? selectedEyeToBall = null;
        var distance = float.MaxValue;
        var aimDot = 0.0f;
        var rejectReason = "out_of_reach";
        float? rejectDistance = null;
        float? rejectAimDot = null;
        foreach (var candidate in candidates)
        {
            var toBall = new Vector(
                candidate.Origin.X - eyePosition.X,
                candidate.Origin.Y - eyePosition.Y,
                candidate.Origin.Z - eyePosition.Z);
            var candidateDistance = VectorSpeed(toBall);
            if (!float.IsFinite(candidateDistance) || candidateDistance <= 0.0001f || candidateDistance > KickMaximumReach)
            {
                if (candidate.IsMatchBall)
                {
                    rejectReason = "out_of_reach";
                    rejectDistance = candidateDistance;
                }
                continue;
            }

            var candidateAimDot = Dot(forward, toBall) / candidateDistance;
            if (!float.IsFinite(candidateAimDot) || candidateAimDot < KickMinimumAimDot)
            {
                if (candidate.IsMatchBall)
                {
                    rejectReason = "outside_aim_cone";
                    rejectDistance = candidateDistance;
                    rejectAimDot = candidateAimDot;
                }
                continue;
            }

            if (candidateDistance < distance)
            {
                selected = candidate;
                selectedEyeToBall = toBall;
                distance = candidateDistance;
                aimDot = candidateAimDot;
            }
        }

        if (selected is not { } target || selectedEyeToBall is not { } eyeToBall)
        {
            LogKickRejected(player, rejectReason, rejectDistance, rejectAimDot);
            return;
        }

        var ball = target.Ball;
        var ballOrigin = target.Origin;

        var lineOfSight = Trace.TraceEndShape(
            eyePosition,
            ballOrigin,
            pawn,
            new TraceOptions { InteractsWith = Masks.SolidBrushOnly });
        var lineOfSightHit = lineOfSight.DidHit() ? lineOfSight.HitEntity() : null;
        var lineOfSightHitBall = lineOfSightHit is { IsValid: true }
            && lineOfSightHit.Index == ball.Index;
        var lineOfSightHitBallVisual = lineOfSightHit is { IsValid: true }
            && (lineOfSightHit.Index == _ballVisual?.Index
                || lineOfSightHit.Entity?.Name == BallVisualTargetName);
        if (lineOfSight.DidHit()
            && lineOfSight.Fraction < 0.999f
            && !lineOfSightHitBall
            && !lineOfSightHitBallVisual)
        {
            Logger.LogInformation(
                "[SM2DIAG] kick_obstruction slot={Slot} fraction={Fraction:F4} end={End} normal={Normal} hitClass={HitClass}",
                player.Slot,
                lineOfSight.Fraction,
                FormatVector(lineOfSight.EndPos),
                FormatVector(lineOfSight.Normal),
                TraceHitClass(lineOfSight));
            LogKickRejected(player, "line_of_sight_blocked", distance, aimDot);
            return;
        }

        // Wall-pop launches a fixed 850 u/s regardless of power scale, which
        // would make a "gentle" right-click kick randomly violent - only
        // the full-power primary kick can trigger it.
        if (powerScale >= 1.0f && TryApplyWallPopKick(player, target, eyePosition, forward, yawRadians, now))
        {
            return;
        }

        // CS:S never set the ball's velocity.  The knife hit reached the ball as
        // a VPhysics impulse, so the ball's own momentum survived the kick: a
        // ball rolling towards you reverses, a ball rolling away only gains the
        // difference, and a dead ball gains the full delta.  Add a delta here
        // instead of overwriting, which is what the previous build did and why
        // every kick came out identical.
        //
        // The native hull owns wall contact.  Do not inject an artificial
        // reflection or lift: the facets produce their own rebound.
        // Where the aim ray passes relative to the ball's centre decides the
        // shot height, exactly as the knife's contact point did in CS:S: under
        // the centre lifts, over the centre drives it down.  Deriving this from
        // raw view pitch instead was wrong for any ball off the ground — you
        // have to look up just to aim at it, so every airborne ball was launched
        // steeply upward and went nowhere.
        // CS:S drove the impulse along the player's aim, and the contact point on
        // the ball added lift on top of that.  Deriving the launch angle from
        // the contact point ALONE threw the aim elevation away, so a ball met in
        // the air always left at the same shallow angle no matter where the
        // player was looking, and simply dropped.  Aim sets the direction; the
        // contact point only bends it upward.
        var alongRay = Dot(eyeToBall, forward);
        var contactOffsetZ = eyePosition.Z + forward.Z * alongRay - ballOrigin.Z;
        var contactRatio = Math.Clamp(contactOffsetZ / BallCollisionRadius, -1.0f, 1.0f);
        var liftDegrees = ComputeKickLiftDegrees(contactRatio);
        var aimElevation = MathF.Asin(Math.Clamp(forward.Z, -1.0f, 1.0f));
        // Ball centre a full radius or more above the player's own eyes ->
        // full overhead bonus; at or below eye level -> none. A ball resting
        // on your own head is close to the full-radius case.
        var overheadRatio = Math.Clamp((ballOrigin.Z - eyePosition.Z) / BallCollisionRadius, 0.0f, 1.0f);
        // 2026-08-31 user report: a self-lofted ball met OVERHEAD while
        // looking forward still launched near-vertically instead of the way
        // the player was facing. Cause: with the ball above the eyes, the aim
        // ray geometrically HAS to pass below the ball's centre, so
        // contactRatio saturated at -1 and injected the maximum lofted lift
        // angle - the contact point was reporting "deliberate scoop" for
        // what is really just overhead geometry. Fade the artificial
        // contact-point lift out as the ball rises above eye level so aim
        // elevation owns the launch angle up there. Ground/chest-height
        // kicks (overheadRatio 0) are untouched, and the 2026-08-30 "look
        // straight up = ball goes straight up" behaviour still works because
        // that case is carried by aimElevation, not liftDegrees.
        liftDegrees *= 1.0f - overheadRatio;
        // 2026-08-30 user report: with a ball resting on your own head, aim
        // and shot direction diverged - looking straight up (aimElevation
        // near 90 degrees) still launched forward-ish, because the fixed
        // 60-degree elevation clamp below was built for ground-level kicks
        // (aiming a little up at a rolling ball should NOT send it
        // vertical) and had no exception for a genuinely overhead ball.
        // Relax the clamp toward 90 degrees in proportion to how overhead
        // the contact actually is, so "look straight up" means "ball goes
        // straight up" without loosening the normal ground-kick clamp.
        var maxElevationDegrees = KickMaximumElevationDegrees + (90.0f - KickMaximumElevationDegrees) * overheadRatio;
        // 2026-08-30 user request: "aim higher on the ball -> lifts too
        // easily" for ordinary kicks. The ball is small and close, so a
        // small upward tilt of the VIEW (aimElevation) swings the crosshair
        // from the bottom of the ball to the top, and aimElevation was the
        // dominant term in the elevation sum - hence the oversensitivity.
        // Scale aimElevation's contribution down (default half) to fix
        // that, but blend back to full (1.0) as overheadRatio rises so the
        // earlier "look straight up at a headed ball = ball goes straight
        // up" fix is NOT undone for that specific case - only ordinary
        // ground-level/eye-level kicks get the requested reduction.
        // Shared grounded test (same one UpdateBallSettleState uses for its
        // settle latch), needed both for the elevation shaping right here
        // and for the soft-pass/soft-pitch gates further down.
        var ballGrounded = ballOrigin.Z <= StadiumPitchPlaneZ + BallCollisionRadius + SettleGroundToleranceZ;
        // 2026-09-01 user report: volleys sometimes left far flatter than
        // the crosshair. The 0.5 damping exists for GROUND kicks only
        // ("aiming a little up at a rolling ball should NOT send it
        // vertical") - for an airborne ball the aim owns the launch
        // direction outright, so halving its pitch there was pure error.
        // The old overheadRatio blend is subsumed: airborne covers every
        // overhead contact, and a grounded ball can never be above eye
        // level (overheadRatio 0), so grounded keeps the exact old value.
        var elevationSensitivity = ballGrounded
            ? _kickElevationSensitivity + (1.0f - _kickElevationSensitivity) * overheadRatio
            : 1.0f;
        // Airborne balls may also be smashed DOWNWARD along the aim
        // (volley/spike); only grounded kicks keep the 0-degree floor so
        // nobody kicks the ball into the grass.
        var minElevationDegrees = ballGrounded ? KickMinimumElevationDegrees : -KickMaximumElevationDegrees;
        var elevation = Math.Clamp(
            aimElevation * elevationSensitivity + liftDegrees * (MathF.PI / 180.0f),
            minElevationDegrees * (MathF.PI / 180.0f),
            maxElevationDegrees * (MathF.PI / 180.0f));
        // Both soft-pass and soft-pitch only soften a kick ON THE GROUND.
        // 2026-08-30 user report (corrected from an earlier, wrong read of
        // this same request): an overhead kick was still coming out weak
        // and short whenever the player wasn't aiming dead-on-centre -
        // measured live via kick_accepted: softPassScale was dropping to
        // 0.25-0.87 on real overhead kicks purely from where the aim ray
        // crossed the ball, even though the ball was fully airborne. Soft
        // pass punishing an imprecise volley/header the same way it
        // punishes a deliberate grass-level toe-tap was never the intent -
        // on the ground it means "you meant to hit it soft", in the air it
        // just means "the ball wasn't dead centre in a fast-moving contact
        // you don't fully control". So both effects are gated the same way:
        // grounded = full soft-pass/soft-pitch behaviour as before, airborne
        // = neither reduces power, only contact point still bends the
        // launch ANGLE (liftDegrees, below). ballGrounded is the shared
        // test declared with the elevation shaping above.
        // Soft pass (CS:S behaviour, user report 2026-08-30): aiming BELOW
        // the ball's centre should scale the kick down, so putting the
        // crosshair on the grass near the ball gives a gentle pass instead
        // of a full-power blast. contactOffsetZ is how far above (+) or
        // below (-) the ball centre the aim ray passes; measured in ball
        // radii, unclamped, so aiming right past the bottom edge and lower
        // keeps getting softer until the floor.
        var belowCentreRatio = MathF.Max(0.0f, -contactOffsetZ / BallCollisionRadius);
        var softPassBlend = ballGrounded && _softPassFullRatio > _softPassStartRatio
            ? Math.Clamp((belowCentreRatio - _softPassStartRatio) / (_softPassFullRatio - _softPassStartRatio), 0.0f, 1.0f)
            : 0.0f;
        var softPassScale = 1.0f - softPassBlend * (1.0f - _softPassMinPowerScale);
        // Soft PITCH (2026-08-30 user request): the steeper the player looks
        // down, the softer a left-click kick should be, independent of
        // where the aim ray lands on the ball. aimElevation is negative
        // when looking down (same convention as the wall-pop check above).
        var lookDownDegrees = MathF.Max(0.0f, -aimElevation * (180.0f / MathF.PI));
        var softPitchBlend = ballGrounded && _softPitchFullDegrees > _softPitchStartDegrees
            ? Math.Clamp((lookDownDegrees - _softPitchStartDegrees) / (_softPitchFullDegrees - _softPitchStartDegrees), 0.0f, 1.0f)
            : 0.0f;
        var softPitchScale = 1.0f - softPitchBlend * (1.0f - _softPitchMinPowerScale);
        var deltaSpeed = _kickDeltaVelocity * ComputeGameplayMassResponse()
            * (1.0f + overheadRatio * _kickOverheadBonusMax)
            * softPassScale
            * softPitchScale
            * powerScale
            * (ballGrounded ? 1.0f : _kickAirborneDeltaScale);
        if (target.IsMatchBall)
        {
            RecordBallTouch(player, ballOrigin);
        }
        var launchDirection = new Vector(
            MathF.Cos(yawRadians) * MathF.Cos(elevation),
            MathF.Sin(yawRadians) * MathF.Cos(elevation),
            MathF.Sin(elevation));
        // A real contact impulse first stops the motion INTO the leg, then
        // launches - purely ADDING the delta understates exactly that
        // opposing case. Live symptom (2026-09-01): a falling ball volleyed
        // upward kept its -Z, so the shot left far flatter than aimed; the
        // planar twin is a ball rolled at you leaving weaker than the same
        // kick on a dead ball (CS:S: "a ball rolling towards you
        // reverses"). Cancel only the inherited component OPPOSING the
        // launch direction; perpendicular drift is kept, and a ball already
        // moving WITH the kick still just gains the delta on top.
        var inherited = target.Inherited;
        var opposingSpeed = MathF.Max(0.0f, -Dot(inherited, launchDirection));
        var requestedVelocity = new Vector(
            inherited.X + launchDirection.X * (opposingSpeed + deltaSpeed),
            inherited.Y + launchDirection.Y * (opposingSpeed + deltaSpeed),
            inherited.Z + launchDirection.Z * (opposingSpeed + deltaSpeed));
        var requestedSpeed = VectorSpeed(requestedVelocity);
        var scale = requestedSpeed > _kickMaximumBallSpeed
            ? _kickMaximumBallSpeed / requestedSpeed
            : 1.0f;
        var finalVelocity = new Vector(
            requestedVelocity.X * scale,
            requestedVelocity.Y * scale,
            requestedVelocity.Z * scale);

        if (target.IsMatchBall)
        {
            UnfreezeBallForPlay("primary_kick");
        }
        ball.AcceptInput("Wake");
        var thrusterApplied = false;
        // The thruster binds to the match ball by name (attach1) - training
        // balls always take the velocity path.
        if (target.IsMatchBall && _kickMode == KickMode.Thruster)
        {
            thrusterApplied = ApplyThrusterKick(ballOrigin, launchDirection);
        }

        if (!thrusterApplied)
        {
            ball.Teleport(velocity: finalVelocity);
        }

        if (_ballSpinFactor != 0.0f)
        {
            ApplyKickSpin(ball, finalVelocity, yawRadians);
        }

        PlayKickSound(ball);
        _lastAcceptedKickTimeBySlot[player.Slot] = now;
        Logger.LogInformation(
            "[SM2DIAG] kick_accepted slot={Slot} name={Name} inputMode={InputMode} powerScale={PowerScale:F2} mode={Mode} thruster={Thruster} distance={Distance:F2} aimDot={AimDot:F3} eyeAngles={EyeAngles} liftDegrees={LiftDegrees:F2} overheadRatio={OverheadRatio:F2} maxElevationDegrees={MaxElevationDegrees:F1} ballGrounded={BallGrounded} softPassScale={SoftPassScale:F2} softPitchScale={SoftPitchScale:F2} deltaSpeed={DeltaSpeed:F1} inheritedVelocity={InheritedVelocity} inheritedSpeed={InheritedSpeed:F1} opposingCancelled={OpposingCancelled:F1} requestedVelocity={RequestedVelocity} finalVelocity={FinalVelocity} finalSpeed={FinalSpeed:F2} clamped={Clamped}",
            player.Slot,
            player.PlayerName,
            kickInputMode,
            powerScale,
            _kickMode,
            thrusterApplied,
            distance,
            aimDot,
            FormatAngle(eyeAngles),
            liftDegrees,
            overheadRatio,
            maxElevationDegrees,
            ballGrounded,
            softPassScale,
            softPitchScale,
            deltaSpeed,
            FormatVector(inherited),
            VectorSpeed(inherited),
            opposingSpeed,
            FormatVector(requestedVelocity),
            FormatVector(finalVelocity),
            VectorSpeed(finalVelocity),
            scale < 1.0f);

        Server.NextFrame(() => SnapshotBall("primary_kick_next_frame"));
        AddTimer(0.25f, () => SnapshotBall("primary_kick_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    // Topspin about the horizontal axis perpendicular to the launch
    // direction: for launch yaw psi the axis is (-sin(psi), cos(psi), 0),
    // scaled to the rolling rate for the ball's OWN post-kick planar speed
    // (r = BallCollisionRadius). Fired through the native bridge, same as
    // the css_sm2ball_spin_input probe and the flightspin trial that
    // validated this axis/sign convention.
    private void ApplyKickSpin(CPhysicsPropMultiplayer ball, Vector finalVelocity, float yawRadians)
    {
        if (!ball.IsValid)
        {
            return;
        }

        var planarSpeed = MathF.Sqrt(finalVelocity.X * finalVelocity.X + finalVelocity.Y * finalVelocity.Y);
        if (planarSpeed < 1.0f)
        {
            return;
        }

        var omegaDegPerSec = _ballSpinFactor * (planarSpeed / BallCollisionRadius) * (180.0f / MathF.PI);
        var axisX = -MathF.Sin(yawRadians) * omegaDegPerSec;
        var axisY = MathF.Cos(yawRadians) * omegaDegPerSec;
        var ptrHex = ball.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        Server.ExecuteCommand(
            $"sm2_native_angular_impulse {ptrHex} {axisX.ToString("F2", CultureInfo.InvariantCulture)} {axisY.ToString("F2", CultureInfo.InvariantCulture)} 0.00");
    }

    // Shared with ApplyKickSpin's formula, but derives yaw from the given
    // velocity's OWN direction instead of taking it as a separate parameter
    // - correct for a wall rebound (spin should match the new travel
    // direction, not any player aim) and kept deliberately separate from
    // ApplyKickSpin so the already-tuned, user-approved kick behaviour
    // (yaw from eye angles, not from finalVelocity) is never touched.
    // Fired for every accepted kick (primary/secondary/crouch variants and
    // the wall-pop). An unknown/misspelled sound-event name must never take
    // the kick itself down, hence the catch-all: EmitSound on a bogus name
    // is expected to no-op, but this code path runs on every single kick.
    private void PlayKickSound(CPhysicsPropMultiplayer ball)
    {
        if (!ball.IsValid || string.IsNullOrEmpty(_kickSoundName))
        {
            return;
        }

        try
        {
            ball.EmitSound(_kickSoundName);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[SM2DIAG] kick_sound_failed sound={Sound}", _kickSoundName);
        }
    }

    private void ApplyBallTopspin(CPhysicsPropMultiplayer ball, Vector velocity, float strengthFactor)
    {
        if (!ball.IsValid || strengthFactor <= 0.0f)
        {
            return;
        }

        var planarSpeed = MathF.Sqrt(velocity.X * velocity.X + velocity.Y * velocity.Y);
        if (planarSpeed < 1.0f)
        {
            return;
        }

        var yawRadians = MathF.Atan2(velocity.Y, velocity.X);
        var omegaDegPerSec = strengthFactor * (planarSpeed / BallCollisionRadius) * (180.0f / MathF.PI);
        var axisX = -MathF.Sin(yawRadians) * omegaDegPerSec;
        var axisY = MathF.Cos(yawRadians) * omegaDegPerSec;
        var ptrHex = ball.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        Server.ExecuteCommand(
            $"sm2_native_angular_impulse {ptrHex} {axisX.ToString("F2", CultureInfo.InvariantCulture)} {axisY.ToString("F2", CultureInfo.InvariantCulture)} 0.00");
    }

    private void OnBallSpinFactorCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1);
            if (arg.Equals("off", StringComparison.OrdinalIgnoreCase))
            {
                _ballSpinFactor = 0.0f;
                SaveBallSettings("spinfactor_command");
            }
            else if (float.TryParse(arg, NumberStyles.Float, CultureInfo.InvariantCulture, out var factor)
                && factor is >= 0.0f and <= 2.0f)
            {
                _ballSpinFactor = factor;
                SaveBallSettings("spinfactor_command");
            }
        }

        command.ReplyToCommand(
            $"[SM] ball spin factor: {(_ballSpinFactor > 0.0f ? _ballSpinFactor.ToString("F2", CultureInfo.InvariantCulture) : "off")} "
            + "(usage: css_sm2ball_spinfactor <0.0-2.0|off>; 1.0 = pure rolling rate)");
    }

    // Returns true if the wall-pop fired (and the normal kick path should be
    // skipped). Only even considered when the player is looking steeply down
    // AND a wall/solid brush is immediately behind the ball along the kick
    // direction - a ball with nowhere else to go. Even then it's a
    // random-chance bonus, not guaranteed, per the user's "randomized chance"
    // request.
    private bool TryApplyWallPopKick(
        CCSPlayerController player,
        PlayableBall target,
        Vector eyePosition,
        Vector forward,
        float yawRadians,
        double now)
    {
        var ball = target.Ball;
        var ballOrigin = target.Origin;
        if (!ball.IsValid)
        {
            return false;
        }

        var aimElevation = MathF.Asin(Math.Clamp(forward.Z, -1.0f, 1.0f));
        if (aimElevation > -WallPopMinLookDownDegrees * (MathF.PI / 180.0f))
        {
            return false;
        }

        var probeEnd = new Vector(
            ballOrigin.X + MathF.Cos(yawRadians) * WallPopWallProbeDistance,
            ballOrigin.Y + MathF.Sin(yawRadians) * WallPopWallProbeDistance,
            ballOrigin.Z);
        var wallProbe = Trace.TraceEndShape(
            ballOrigin,
            probeEnd,
            ball,
            new TraceOptions { InteractsWith = Masks.SolidBrushOnly });
        if (!wallProbe.DidHit() || wallProbe.Fraction >= 0.95f)
        {
            return false;
        }

        if (Random.Shared.NextDouble() >= WallPopTriggerChance)
        {
            return false;
        }

        var directionRoll = Random.Shared.Next(3); // 0=straight, 1=left, 2=right
        var lateralSign = directionRoll switch { 1 => -1.0f, 2 => 1.0f, _ => 0.0f };
        var rightX = MathF.Sin(yawRadians);
        var rightY = -MathF.Cos(yawRadians);
        var inherited = target.Inherited;
        var popVelocity = new Vector(
            inherited.X + rightX * lateralSign * WallPopLateralSpeed,
            inherited.Y + rightY * lateralSign * WallPopLateralSpeed,
            inherited.Z + WallPopVerticalSpeed);
        var popSpeed = VectorSpeed(popVelocity);
        var scale = popSpeed > _kickMaximumBallSpeed ? _kickMaximumBallSpeed / popSpeed : 1.0f;
        var finalVelocity = new Vector(popVelocity.X * scale, popVelocity.Y * scale, popVelocity.Z * scale);

        if (target.IsMatchBall)
        {
            UnfreezeBallForPlay("wall_pop_kick");
        }
        ball.AcceptInput("Wake");
        ball.Teleport(velocity: finalVelocity);
        PlayKickSound(ball);
        _lastAcceptedKickTimeBySlot[player.Slot] = now;
        if (target.IsMatchBall)
        {
            RecordBallTouch(player, ballOrigin);
        }

        var directionName = directionRoll switch { 1 => "left", 2 => "right", _ => "straight" };
        Logger.LogInformation(
            "[SM2DIAG] kick_wall_pop slot={Slot} name={Name} direction={Direction} finalVelocity={FinalVelocity} finalSpeed={FinalSpeed:F2}",
            player.Slot,
            player.PlayerName,
            directionName,
            FormatVector(finalVelocity),
            VectorSpeed(finalVelocity));

        Server.NextFrame(() => SnapshotBall("wall_pop_kick_next_frame"));
        return true;
    }

    // CS:S applied the knife hit as an off-centre VPhysics impulse, which is
    // where the ball's spin and therefore its curve came from.  CS2 exposes the
    // same mechanic natively: the FGD for phys_thruster states that "the force
    // and torque is calculated using the position and direction of the thruster
    // as an impulse.  So moving those off the object's center will cause torque
    // as well."  Place the thruster behind and slightly below the ball so the
    // push carries backspin, run it for a couple of ticks, then remove it.
    //
    // Whether attach1 binds for a runtime-spawned entity is the open question,
    // so this returns false on any failure and the caller falls back to the
    // delta-velocity kick rather than swallowing the input.
    private bool ApplyThrusterKick(Vector ballOrigin, Vector launchDirection)
    {
        if (_ball is not { IsValid: true })
        {
            return false;
        }

        var contact = new Vector(
            ballOrigin.X - launchDirection.X * BallCollisionRadius,
            ballOrigin.Y - launchDirection.Y * BallCollisionRadius,
            ballOrigin.Z - launchDirection.Z * BallCollisionRadius
                - BallCollisionRadius * _kickBackspinBias);

        var thruster = Utilities.CreateEntityByName<CBaseEntity>(ThrusterDesignerName);
        if (thruster is null || !thruster.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] thruster_spawn_failed class={Class}", ThrusterDesignerName);
            return false;
        }

        // Force is integrated over forcetime, so the impulse the ball receives is
        // force * forcetime.  Express the knob as the delta-velocity we want and
        // convert through the known Source 1 XSL mass.
        var force = (_kickDeltaVelocity * BallMassKilograms * _kickThrusterScale) / _kickThrusterSeconds;
        var yaw = MathF.Atan2(launchDirection.Y, launchDirection.X) * (180.0f / MathF.PI);
        var pitch = -MathF.Asin(Math.Clamp(launchDirection.Z, -1.0f, 1.0f)) * (180.0f / MathF.PI);

        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", ThrusterTargetName);
        keyValues.SetString("attach1", OwnedBallTargetName);
        keyValues.SetString("force", force.ToString("F1", CultureInfo.InvariantCulture));
        keyValues.SetString("forcetime", _kickThrusterSeconds.ToString("F4", CultureInfo.InvariantCulture));
        keyValues.SetUInt("spawnflags", ThrusterSpawnFlags);
        keyValues.SetVector("origin", contact);
        keyValues.SetAngle("angles", new QAngle(pitch, yaw, 0.0f));
        thruster.DispatchSpawn(keyValues);

        if (!thruster.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] thruster_spawn_invalid class={Class}", ThrusterDesignerName);
            return false;
        }

        thruster.AcceptInput("Activate");
        var index = thruster.Index;
        Logger.LogInformation(
            "[SM2DIAG] thruster_kick index={Index} contact={Contact} angles=({Pitch:F2},{Yaw:F2}) force={Force:F0} forcetime={ForceTime:F4} backspinBias={Backspin:F2}",
            index,
            FormatVector(contact),
            pitch,
            yaw,
            force,
            _kickThrusterSeconds,
            _kickBackspinBias);

        AddTimer(
            _kickThrusterSeconds + 0.2f,
            () =>
            {
                var stale = Utilities.GetEntityFromIndex<CBaseEntity>((int)index);
                if (stale is { IsValid: true } && stale.Entity?.Name == ThrusterTargetName)
                {
                    stale.AcceptInput("Deactivate");
                    stale.AcceptInput("Kill");
                }
            },
            TimerFlags.STOP_ON_MAPCHANGE);
        return true;
    }


    // Torque-only phys_thruster: spawnflags Start On | Apply Torque, NOT Apply
    // Force, so it changes only angular velocity and never touches the ball's
    // linear velocity (that stays on the Teleport-velocity kick).
    //
    // Placing a thruster offset from the ball centre and firing it along a
    // direction produces a torque of r x F (offset cross force), per the
    // phys_thruster FGD.  For rolling topspin in planar direction d=(cosψ,sinψ,0)
    // the required angular velocity axis works out to (-sinψ, cosψ, 0) — see
    // docs/ball-foundation/2026-08-29-implementation-plan.md Phase B.  Offsetting
    // the thruster by +BallCollisionRadius along Z and firing along d gives
    // r x F = R*(zHat x d) = R*(-sinψ, cosψ, 0), which matches; zSign flips this
    // if live testing shows the opposite roll direction.
    //
    // Takes a RAW force value rather than deriving it from a target angular
    // speed via a moment-of-inertia formula: the linear thruster measured
    // force*forcetime behaving nonlinearly (scale 1/5/20 identical, forcetime
    // 0.05s/0.20s/0.50s wildly different), so a physics-derived force number is
    // not trustworthy here.  Calibrate empirically with css_sm2ball_torque_test
    // instead, per the plan's explicit "tune by measurement, not by formula".
    private bool ApplySpinKick(Vector ballOrigin, Vector planarDirection, float force, float forcetime, uint spawnflags = SpinThrusterSpawnFlags)
    {
        if (_ball is not { IsValid: true } || MathF.Abs(force) < 0.01f || forcetime <= 0.0f)
        {
            return false;
        }

        var offset = new Vector(0.0f, 0.0f, BallCollisionRadius * _spinThrusterZSign);
        var position = new Vector(
            ballOrigin.X + offset.X,
            ballOrigin.Y + offset.Y,
            ballOrigin.Z + offset.Z);

        var thruster = Utilities.CreateEntityByName<CBaseEntity>(ThrusterDesignerName);
        if (thruster is null || !thruster.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] spin_thruster_spawn_failed class={Class}", ThrusterDesignerName);
            return false;
        }

        var yaw = MathF.Atan2(planarDirection.Y, planarDirection.X) * (180.0f / MathF.PI);

        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", SpinThrusterTargetName);
        keyValues.SetString("attach1", OwnedBallTargetName);
        keyValues.SetString("force", force.ToString("F1", CultureInfo.InvariantCulture));
        keyValues.SetString("forcetime", forcetime.ToString("F4", CultureInfo.InvariantCulture));
        keyValues.SetUInt("spawnflags", spawnflags);
        keyValues.SetVector("origin", position);
        keyValues.SetAngle("angles", new QAngle(0.0f, yaw, 0.0f));
        thruster.DispatchSpawn(keyValues);

        if (!thruster.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] spin_thruster_spawn_invalid class={Class}", ThrusterDesignerName);
            return false;
        }

        thruster.AcceptInput("Activate");
        var index = thruster.Index;
        Logger.LogInformation(
            "[SM2DIAG] spin_thruster_fired index={Index} position={Position} yaw={Yaw:F2} zSign={ZSign:F1} force={Force:F1} forcetime={ForceTime:F4}",
            index,
            FormatVector(position),
            yaw,
            _spinThrusterZSign,
            force,
            forcetime);

        AddTimer(
            forcetime + 0.2f,
            () =>
            {
                var stale = Utilities.GetEntityFromIndex<CBaseEntity>((int)index);
                if (stale is { IsValid: true } && stale.Entity?.Name == SpinThrusterTargetName)
                {
                    stale.AcceptInput("Deactivate");
                    stale.AcceptInput("Kill");
                }
            },
            TimerFlags.STOP_ON_MAPCHANGE);
        return true;
    }

    // Phase A probe: server.dll contains the strings ApplyAbsVelocityImpulse
    // and ApplyLocalAngularVelocityImpulse — the classic Source CBaseEntity
    // inputs, not listed in the FGD (which omits base-entity inputs).  These
    // two commands exist only to find out whether AcceptInput still reaches
    // them in CS2.  If they work, they replace both the Teleport-velocity kick
    // and the unreliable phys_thruster path with the exact impulse mechanism
    // CS:S-era plugins used.  Throwaway once Phase A/B lands; see
    // docs/ball-foundation/2026-08-29-implementation-plan.md.
    // Diagnostic: prints the ball's native CEntityInstance* address so it can
    // be handed to the sm2native Metamod plugin's ConCommands, which need a
    // raw pointer (not an entity index) because CGameEntitySystem::
    // GetEntityIdentity is not a dynamically-exported symbol in libserver.so
    // and cannot be linked against directly from a native plugin.
    private void OnBallNativeHandleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("native_handle_command"))
        {
            return;
        }

        command.ReplyToCommand($"[SM2DIAG] ball native handle: {_ball!.Handle}");
    }

    private void OnBallImpulseInputCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("impulse_input_probe"))
        {
            return;
        }

        if (command.ArgCount < 4
            || !TryParseProfileValue(command.GetArg(1), -6000.0f, 6000.0f, out var x)
            || !TryParseProfileValue(command.GetArg(2), -6000.0f, 6000.0f, out var y)
            || !TryParseProfileValue(command.GetArg(3), -6000.0f, 6000.0f, out var z))
        {
            command.ReplyToCommand("[SM2DIAG] usage: css_sm2ball_impulse_input <x> <y> <z>");
            return;
        }

        // 2026-09-01: native bridge, same rationale as the spin probe below.
        UnfreezeBallForPlay("native_impulse_probe");
        _ball!.AcceptInput("Wake");
        var ptrHex = _ball.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        var nativeCommand = $"sm2_native_impulse {ptrHex} {x.ToString("F2", CultureInfo.InvariantCulture)} {y.ToString("F2", CultureInfo.InvariantCulture)} {z.ToString("F2", CultureInfo.InvariantCulture)}";
        Server.ExecuteCommand(nativeCommand);
        Logger.LogInformation("[SM2DIAG] impulse_input_probe native={Native}", nativeCommand);
        command.ReplyToCommand($"[SM2DIAG] native linear impulse sent (ptr={ptrHex}); check css_sm2ball_status");
    }

    private void OnBallSpinInputCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("spin_input_probe"))
        {
            return;
        }

        if (command.ArgCount < 4
            || !TryParseProfileValue(command.GetArg(1), -36000.0f, 36000.0f, out var x)
            || !TryParseProfileValue(command.GetArg(2), -36000.0f, 36000.0f, out var y)
            || !TryParseProfileValue(command.GetArg(3), -36000.0f, 36000.0f, out var z))
        {
            command.ReplyToCommand("[SM2DIAG] usage: css_sm2ball_spin_input <x> <y> <z> (deg/s, local axes)");
            return;
        }

        // 2026-09-01: route through the soccermod_native metamod bridge (real
        // typed-variant_t CEntityInstance::AcceptInput) instead of CSSharp's
        // string AcceptInput, which is a silent no-op for this input. The
        // native scanner bug (matched Metamod's proxy libserver.so) is fixed,
        // so g_fnAcceptInput now resolves.
        UnfreezeBallForPlay("native_spin_probe");
        _ball!.AcceptInput("Wake");
        var ptrHex = _ball.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
        var nativeCommand = $"sm2_native_angular_impulse {ptrHex} {x.ToString("F2", CultureInfo.InvariantCulture)} {y.ToString("F2", CultureInfo.InvariantCulture)} {z.ToString("F2", CultureInfo.InvariantCulture)}";
        Server.ExecuteCommand(nativeCommand);
        Logger.LogInformation("[SM2DIAG] spin_input_probe native={Native}", nativeCommand);
        command.ReplyToCommand($"[SM2DIAG] native angular impulse sent (ptr={ptrHex}); check css_sm2ball_status and the [SM2NATIVE] server console line");
    }

    // Fires one thruster kick along +X without needing a player, so the spin
    // route can be validated from RCON against the wall and roll trials.
    // Fires one torque-only spin thruster along +X without a player, to
    // calibrate _spinThrusterZSign and force scaling from RCON before wiring
    // spin into the live kick (Phase B).
    // Diagnostic: Apply Torque alone is inert in this CS2 build (measured —
    // force 500..100000 with spawnflags Start On|Apply Torque produced no
    // motion at all, while the same force with Apply Force added launched the
    // ball instantly).  This fires a combined force+torque thruster for spin,
    // then overwrites the ball's LINEAR velocity back to zero one frame after
    // the thruster's forcetime elapses, to test whether Teleport(velocity:)
    // touches angular velocity or only linear.  If the ball keeps creeping
    // after the zero-out, angular velocity survived the overwrite and this
    // is the mechanism Phase B needs: fire a spin thruster, then Teleport the
    // ball to the intended KICK velocity instead of zero.
    // Live tuning: css_sm2ball_wallassist <on|off|verticalRatio>
    // [maxAdded] [minNormalRetention].
    private void OnBallWallAssistCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1).ToLowerInvariant();
            if (arg is "on" or "off")
            {
                _wallAssistEnabled = arg == "on";
            }
            else if (TryParseProfileValue(arg, 0.0f, 2.0f, out var ratio))
            {
                _wallAssistConversionRatio = ratio;
            }
        }

        if (command.ArgCount >= 3 && TryParseProfileValue(command.GetArg(2), 0.0f, 2000.0f, out var maxAdded))
        {
            _wallAssistMaxAddedVertical = maxAdded;
        }

        if (command.ArgCount >= 4 && TryParseProfileValue(command.GetArg(3), 0.0f, 2.0f, out var normalRetention))
        {
            _wallAssistMinimumNormalRetention = normalRetention;
        }

        if (command.ArgCount >= 2)
        {
            SaveBallSettings("wall_assist_command");
        }

        command.ReplyToCommand(
            $"[SM2DIAG] wall assist enabled={_wallAssistEnabled} verticalRatio={_wallAssistConversionRatio:F3} maxAdded={_wallAssistMaxAddedVertical:F1} minNormalRetention={_wallAssistMinimumNormalRetention:F3} (CS:S references ~0.129 vertical, ~0.18 normal)");
    }

    private void OnBallSpinIsolateCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("spin_isolate_command"))
        {
            return;
        }

        var force = 8000.0f;
        if (command.ArgCount >= 2 && TryParseProfileValue(command.GetArg(1), 1.0f, 2000000.0f, out var forceArg))
        {
            force = forceArg;
        }

        var forcetime = 0.05f;
        if (command.ArgCount >= 3 && TryParseProfileValue(command.GetArg(2), 0.01f, 1.0f, out var timeArg))
        {
            forcetime = timeArg;
        }

        if (command.ArgCount >= 4 && TryParseProfileValue(command.GetArg(3), -1.0f, 1.0f, out var zSign))
        {
            _spinThrusterZSign = zSign;
        }

        if (_ball?.AbsOrigin is not { } origin)
        {
            command.ReplyToCommand("[SM2DIAG] no ball origin");
            return;
        }

        _ball.AcceptInput("Wake");
        ApplySpinKick(origin, new Vector(1.0f, 0.0f, 0.0f), force, forcetime, spawnflags: 1 | 2 | 4);

        AddTimer(
            forcetime + 0.05f,
            () =>
            {
                if (_ball is { IsValid: true })
                {
                    _ball.Teleport(velocity: new Vector(0.0f, 0.0f, 0.0f));
                    Logger.LogInformation(
                        "[SM2DIAG] spin_isolate_velocity_zeroed origin={Origin}",
                        FormatVector(_ball.AbsOrigin ?? new Vector(0.0f, 0.0f, 0.0f)));
                }
            },
            TimerFlags.STOP_ON_MAPCHANGE);

        command.ReplyToCommand(
            $"[SM2DIAG] spin isolate fired: force {force:F0}, forcetime {forcetime:F3}, zSign {_spinThrusterZSign:F1}. Velocity will be zeroed at t+{forcetime + 0.05f:F3}s — watch css_sm2ball_status AFTER that for creep.");
    }

    private void OnBallTorqueTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("torque_test_command"))
        {
            return;
        }

        if (command.ArgCount >= 2 && TryParseProfileValue(command.GetArg(1), -1.0f, 1.0f, out var zSign))
        {
            _spinThrusterZSign = zSign;
        }

        var force = _torqueTestForce;
        if (command.ArgCount >= 3
            && TryParseProfileValue(command.GetArg(2), 1.0f, 2000000.0f, out var forceArg))
        {
            force = forceArg;
            _torqueTestForce = forceArg;
        }


        var includeForce = command.ArgCount >= 5 && command.GetArg(4) == "1";
        var spawnflagsToUse = includeForce ? (uint)(1 | 2 | 4) : SpinThrusterSpawnFlags;
        if (command.ArgCount >= 4 && TryParseProfileValue(command.GetArg(3), 0.01f, 1.0f, out var seconds))
        {
            _spinThrusterSeconds = seconds;
        }

        if (_ball?.AbsOrigin is not { } origin)
        {
            command.ReplyToCommand("[SM2DIAG] no ball origin");
            return;
        }

        _ball.AcceptInput("Wake");
        var applied = ApplySpinKick(origin, new Vector(1.0f, 0.0f, 0.0f), force, _spinThrusterSeconds, spawnflagsToUse);
        command.ReplyToCommand(
            applied
                ? $"[SM2DIAG] torque test fired: zSign {_spinThrusterZSign:F1}, force {force:F0}, seconds {_spinThrusterSeconds:F3}, includeForce {includeForce}. Watch css_sm2ball_status for roll direction."
                : "[SM2DIAG] spin thruster FAILED to spawn");
    }

    private void OnBallThrustCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("thrust_command"))
        {
            return;
        }

        if (command.ArgCount >= 2 && TryParseProfileValue(command.GetArg(1), 0.05f, 20.0f, out var scale))
        {
            _kickThrusterScale = scale;
        }

        if (command.ArgCount >= 3 && TryParseProfileValue(command.GetArg(2), 0.01f, 1.0f, out var seconds))
        {
            _kickThrusterSeconds = seconds;
        }

        if (command.ArgCount >= 4 && TryParseProfileValue(command.GetArg(3), -1.0f, 1.0f, out var backspin))
        {
            _kickBackspinBias = backspin;
        }

        if (_ball?.AbsOrigin is not { } origin)
        {
            command.ReplyToCommand("[SM2DIAG] no ball origin");
            return;
        }

        _ball.AcceptInput("Wake");
        var applied = ApplyThrusterKick(origin, new Vector(1.0f, 0.0f, 0.0f));
        command.ReplyToCommand(
            applied
                ? $"[SM2DIAG] thruster fired: scale {_kickThrusterScale:F2}, seconds {_kickThrusterSeconds:F3}, backspin {_kickBackspinBias:F2}"
                : "[SM2DIAG] thruster kick FAILED to spawn");
    }

    // Live power tuning over RCON: css_sm2ball_power <deltaVelocity> [maxSpeed].
    // Deliberately a server command, not an in-game menu.
    private void OnBallPowerCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        var changed = false;
        if (command.ArgCount >= 2 && TryParseProfileValue(command.GetArg(1), 100.0f, 6000.0f, out var delta))
        {
            _kickDeltaVelocity = delta;
            changed = true;
        }

        if (command.ArgCount >= 3 && TryParseProfileValue(command.GetArg(2), 500.0f, 8000.0f, out var maximum))
        {
            _kickMaximumBallSpeed = maximum;
            changed = true;
        }

        if (command.ArgCount >= 4 && TryParseProfileValue(command.GetArg(3), 0.0f, 2.0f, out var overheadBonus))
        {
            _kickOverheadBonusMax = overheadBonus;
            changed = true;
        }

        if (changed)
        {
            SaveBallSettings("kick_power_command");
        }

        Logger.LogInformation(
            "[SM2DIAG] kick_power delta={Delta:F1} max={Max:F1} overheadBonus={OverheadBonus:F2}",
            _kickDeltaVelocity,
            _kickMaximumBallSpeed,
            _kickOverheadBonusMax);
        command.ReplyToCommand(
            $"[SM2DIAG] kick delta {_kickDeltaVelocity:F0} u/s, clamp {_kickMaximumBallSpeed:F0} u/s, overhead bonus +{_kickOverheadBonusMax * 100:F0}% max (CS:S reference delta was 1359)");
    }

    // Swaps the ball's collision model and rebuilds it in place, so the roll /
    // wall / drop trials can be run against each candidate back to back.
    private void OnBallModelCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        var known = string.Join("|", BallPhysicsModelCandidates.Keys);
        command.ReplyToCommand(
            $"[SM2DIAG] active model={BallVisualModelName} (Workshop collision); tuned build selection={_ballPhysicsModelKey} ({_ballPhysicsModel}) is inactive while single-entity mode is enabled (known builds: {known})");
    }

    private void OnBallKickModeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2)
        {
            switch (command.GetArg(1).ToLowerInvariant())
            {
                case "velocity":
                    _kickMode = KickMode.Velocity;
                    break;
                case "thruster":
                    _kickMode = KickMode.Thruster;
                    break;
                default:
                    command.ReplyToCommand("[SM2DIAG] usage: css_sm2ball_kickmode velocity|thruster [scale] [seconds] [backspin]");
                    return;
            }
        }

        if (command.ArgCount >= 3 && TryParseProfileValue(command.GetArg(2), 0.05f, 20.0f, out var scale))
        {
            _kickThrusterScale = scale;
        }

        if (command.ArgCount >= 4 && TryParseProfileValue(command.GetArg(3), 0.01f, 1.0f, out var seconds))
        {
            _kickThrusterSeconds = seconds;
        }

        if (command.ArgCount >= 5 && TryParseProfileValue(command.GetArg(4), -1.0f, 1.0f, out var backspin))
        {
            _kickBackspinBias = backspin;
        }

        SaveBallSettings("kick_mode_command");
        Logger.LogInformation(
            "[SM2DIAG] kick_mode mode={Mode} scale={Scale:F2} seconds={Seconds:F4} backspin={Backspin:F2}",
            _kickMode,
            _kickThrusterScale,
            _kickThrusterSeconds,
            _kickBackspinBias);
        command.ReplyToCommand(
            $"[SM2DIAG] kick mode {_kickMode}; scale {_kickThrusterScale:F2}, seconds {_kickThrusterSeconds:F4}, backspin {_kickBackspinBias:F2}");
    }

    private void LogKickRejected(
        CCSPlayerController player,
        string reason,
        float? distance = null,
        float? aimDot = null)
    {
        Logger.LogInformation(
            "[SM2DIAG] kick_rejected slot={Slot} name={Name} reason={Reason} distance={Distance} aimDot={AimDot}",
            player.Slot,
            player.PlayerName,
            reason,
            FormatNullable(distance),
            FormatNullable(aimDot));
    }

    private void OnBallStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        BindBall("status_command");
        var summary = BuildBallSummary();
        command.ReplyToCommand($"[SM2DIAG] mode={_mode} {summary}");
        SnapshotBall("status_command");
    }

    private void OnInventoryStatusCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (player is not null)
        {
            command.ReplyToCommand($"[SM2DIAG] {BuildPlayerSummary(player)}");
            SnapshotPlayer(player, "inventory_status_command");
            return;
        }

        var players = Utilities.GetPlayers().Where(candidate => candidate.IsValid).ToList();
        command.ReplyToCommand($"[SM2DIAG] players={players.Count}");
        foreach (var candidate in players)
        {
            command.ReplyToCommand($"[SM2DIAG] {BuildPlayerSummary(candidate)}");
            SnapshotPlayer(candidate, "inventory_status_command");
        }
    }

    private void OnBallModeCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        if (command.ArgCount < 2)
        {
            command.ReplyToCommand($"[SM2DIAG] mode={_mode}; usage: css_sm2ball_mode baseline|wake");
            return;
        }

        var requested = command.GetArg(1).Trim().ToLowerInvariant();
        _mode = requested switch
        {
            "baseline" => BallProbeMode.Baseline,
            "wake" => BallProbeMode.Wake,
            _ => _mode
        };

        if (requested is not ("baseline" or "wake"))
        {
            command.ReplyToCommand("[SM2DIAG] rejected mode; use baseline or wake");
            return;
        }

        if (_mode == BallProbeMode.Wake && BindBall("mode_wake"))
        {
            _ball!.AcceptInput("Wake");
        }

        Logger.LogInformation("[SM2DIAG] mode_change mode={Mode}", _mode);
        command.ReplyToCommand($"[SM2DIAG] mode={_mode}");
        SnapshotBall("mode_change");
    }

    private void OnBallImpulseCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command) || !BindBall("impulse_command"))
        {
            return;
        }

        var speed = DefaultImpulseSpeed;
        var lift = DefaultImpulseLift;
        if (command.ArgCount >= 2
            && (!float.TryParse(command.GetArg(1), NumberStyles.Float, CultureInfo.InvariantCulture, out speed)
                || !float.IsFinite(speed)
                || speed <= 0.0f
                || speed > MaximumProbeImpulseSpeed))
        {
            command.ReplyToCommand(
                $"[SM2DIAG] invalid speed; use 0 < speed <= {MaximumProbeImpulseSpeed.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        if (command.ArgCount >= 3
            && (!float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out lift)
                || !float.IsFinite(lift)
                || lift < 0.0f
                || lift > MaximumProbeImpulseLift))
        {
            command.ReplyToCommand(
                $"[SM2DIAG] invalid lift; use 0 <= lift <= {MaximumProbeImpulseLift.ToString(CultureInfo.InvariantCulture)}");
            return;
        }

        SnapshotBall("pre_impulse");
        UnfreezeBallForPlay("impulse_probe");
        _ball!.AcceptInput("Wake");
        var impulseVelocity = new Vector(speed, 0.0f, lift);
        _ball.Teleport(velocity: impulseVelocity);
        Logger.LogInformation(
            "[SM2DIAG] controlled_impulse velocity=({Speed:F1},0.0,{Lift:F1})",
            speed,
            lift);
        command.ReplyToCommand(
            $"[SM2DIAG] controlled impulse applied: {speed:F1} u/s, lift {lift:F1} u/s");

        Server.NextFrame(() => SnapshotBall("impulse_next_frame"));
        AddTimer(0.25f, () => SnapshotBall("impulse_plus_0_25s"), TimerFlags.STOP_ON_MAPCHANGE);
        AddTimer(1.0f, () => SnapshotBall("impulse_plus_1_00s"), TimerFlags.STOP_ON_MAPCHANGE);
    }

    // Reproduces the three CS:S probe trials (roll, wall, drop) on the CS2 ball
    // and logs them in the same shape as artifacts/css-reference, so the decay
    // curve, rebound restitution and settling behaviour can be diffed against
    // the original instead of judged by eye.
    private void OnBallTrialCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var kind = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : string.Empty;
        if (kind is not ("roll" or "wall" or "drop" or "flight" or "flightspin"))
        {
            command.ReplyToCommand("[SM2CSSREF] usage: css_sm2ball_trial roll|wall|drop|flight|flightspin [speed] [wall: startYOffset | flight/flightspin: angleDegrees] [flightspin: spinDegPerSec]");
            return;
        }

        if (!TryActivateOwnedBall($"trial_{kind}", out var activation) || _ball is not { IsValid: true })
        {
            command.ReplyToCommand($"[SM2CSSREF] trial aborted: {activation}");
            return;
        }

        _trialTimer?.Kill();
        _trialKind = kind;
        _trialSeq++;
        _trialSample = 0;
        _trialPreviousOrigin = null;

        var origin = CreateBallResetOrigin();
        if (command.ArgCount >= 4
            && kind == "wall"
            && float.TryParse(command.GetArg(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var startYOffset)
            && float.IsFinite(startYOffset))
        {
            origin = new Vector(origin.X, origin.Y + startYOffset, origin.Z);
        }
        var speed = kind switch
        {
            "roll" => TrialRollSpeed,
            "wall" => TrialWallSpeed,
            "flight" or "flightspin" => TrialFlightSpeed,
            _ => 0.0f,
        };
        if (command.ArgCount >= 3
            && float.TryParse(command.GetArg(2), NumberStyles.Float, CultureInfo.InvariantCulture, out var requested)
            && float.IsFinite(requested)
            && requested > 0.0f
            && requested <= 6000.0f)
        {
            speed = requested;
        }

        // Same defaults as the CS:S probe's flight trial (sm_xslref_trial
        // flight), so the two logs are directly diffable: 1359.2 u/s @ 10.6
        // degrees is the CS:S-measured clean-kick reference.
        var flightAngleDegrees = TrialFlightAngleDegrees;
        if (kind is "flight" or "flightspin"
            && command.ArgCount >= 4
            && float.TryParse(command.GetArg(3), NumberStyles.Float, CultureInfo.InvariantCulture, out var requestedAngle)
            && float.IsFinite(requestedAngle))
        {
            flightAngleDegrees = requestedAngle;
        }

        // 2026-09-01 spin-curve trial: reuses the flight trial's EXACT
        // Teleport-velocity launch (deliberately not the weak native linear
        // impulse - Phase A measured that as barely coupling to the ball,
        // and this trial's whole point is to isolate the ANGULAR impulse's
        // effect on trajectory, not re-litigate the linear one). Spin rate
        // is arg 5 (deg/s about the axis perpendicular to the launch
        // direction, same sign convention as css_sm2ball_spin_input) so
        // `trial flightspin <speed> <angle> <spinDegPerSec>` is directly
        // diffable against `trial flight <speed> <angle>` in the logs - any
        // Y-axis (lateral) drift that appears only in the spin run is curve.
        var spinDegPerSec = 0.0f;
        if (kind == "flightspin"
            && command.ArgCount >= 5
            && float.TryParse(command.GetArg(4), NumberStyles.Float, CultureInfo.InvariantCulture, out var requestedSpin)
            && float.IsFinite(requestedSpin))
        {
            spinDegPerSec = requestedSpin;
        }

        var launch = kind switch
        {
            "roll" => new Vector(speed, 0.0f, 0.0f),
            "wall" => new Vector(-speed, 0.0f, 0.0f),
            "flight" or "flightspin" => new Vector(
                MathF.Cos(flightAngleDegrees * (MathF.PI / 180.0f)) * speed,
                0.0f,
                MathF.Sin(flightAngleDegrees * (MathF.PI / 180.0f)) * speed),
            _ => new Vector(0.0f, 0.0f, 0.0f),
        };
        if (kind == "drop")
        {
            origin = new Vector(origin.X, origin.Y, origin.Z + TrialDropHeight);
        }

        UnfreezeBallForPlay("trial_launch");
        _ball.AcceptInput("Wake");
        _ball.Teleport(position: origin, angles: new QAngle(0.0f, 0.0f, 0.0f), velocity: launch);
        ResetDerivedMotion();

        if (kind == "flightspin" && spinDegPerSec != 0.0f)
        {
            // Topspin about the horizontal axis perpendicular to travel
            // (launch is in the XZ plane here, so that axis is +Y) - the
            // same axis convention Phase B's live kick spin will use.
            var ptrHex = _ball.Handle.ToInt64().ToString("X", CultureInfo.InvariantCulture);
            Server.ExecuteCommand($"sm2_native_angular_impulse {ptrHex} 0.00 {spinDegPerSec.ToString("F2", CultureInfo.InvariantCulture)} 0.00");
        }

        _trialStartTime = Server.TickedTime;
        _trialPreviousTime = _trialStartTime;
        Logger.LogInformation(
            "[SM2CSSREF] trial_start seq={Seq} kind={Kind} model={Model} samples={Samples} interval={Interval} position={Position} launch={Launch} {Profile}",
            _trialSeq,
            kind,
            _ballPhysicsModel,
            TrialSampleCount,
            TrialSampleInterval,
            FormatVector(origin),
            FormatVector(launch),
            BuildGameplayPhysicsProfileSummary());

        _trialTimer = AddTimer(
            TrialSampleInterval,
            SampleTrial,
            TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        command.ReplyToCommand(
            $"[SM2CSSREF] trial {kind} seq {_trialSeq} started; {TrialSampleCount} samples to the server log");
    }

    private void SampleTrial()
    {
        if (_ball is not { IsValid: true } || _ball.AbsOrigin is not { } origin)
        {
            StopTrial("ball_lost");
            return;
        }

        var now = (double)Server.TickedTime;
        var elapsed = now - _trialPreviousTime;
        var derived = new Vector(0.0f, 0.0f, 0.0f);
        if (_trialPreviousOrigin is { } previous && elapsed > 0.000001)
        {
            derived = new Vector(
                (float)((origin.X - previous.X) / elapsed),
                (float)((origin.Y - previous.Y) / elapsed),
                (float)((origin.Z - previous.Z) / elapsed));
        }

        _trialSample++;
        Logger.LogInformation(
            "[SM2CSSREF] trial_sample seq={Seq} kind={Kind} n={N} time={Time:F6} dt={Dt:F6} position={Position} derived={Derived} speed={Speed:F6}",
            _trialSeq,
            _trialKind,
            _trialSample,
            now - _trialStartTime,
            elapsed,
            FormatVector(origin),
            FormatVector(derived),
            VectorSpeed(derived));

        _trialPreviousOrigin = new Vector(origin.X, origin.Y, origin.Z);
        _trialPreviousTime = now;

        if (_trialSample >= TrialSampleCount)
        {
            StopTrial("completed");
        }
    }

    private void StopTrial(string reason)
    {
        _trialTimer?.Kill();
        _trialTimer = null;
        Logger.LogInformation(
            "[SM2CSSREF] trial_end seq={Seq} kind={Kind} samples={Samples} reason={Reason}",
            _trialSeq,
            _trialKind,
            _trialSample,
            reason);
    }

    private void OnBallPhysicsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount == 1)
        {
            command.ReplyToCommand($"[SM2DIAG] {BuildGameplayPhysicsProfileSummary()}; usage: css_sm2ball_physics <massScale> <friction> <elasticity> <gravityScale>");
            return;
        }

        if (command.ArgCount != 5
            || !TryParseProfileValue(command.GetArg(1), 0.05f, 2.0f, out var massScale)
            || !TryParseProfileValue(command.GetArg(2), 0.0f, 2.0f, out var friction)
            || !TryParseProfileValue(command.GetArg(3), 0.0f, 1.5f, out var elasticity)
            || !TryParseProfileValue(command.GetArg(4), 0.1f, 2.0f, out var gravityScale))
        {
            command.ReplyToCommand("[SM2DIAG] usage: css_sm2ball_physics <massScale 0.05..2> <friction 0..2> <elasticity 0..1.5> <gravityScale 0.1..2>");
            return;
        }

        _gameplayMassScale = massScale;
        _gameplayFriction = friction;
        _gameplayElasticity = elasticity;
        _gameplayGravityScale = gravityScale;
        if (BindBall("physics_profile_command") && _ball is { IsValid: true })
        {
            ApplyGameplayPhysicsProfile(_ball, "physics_profile_command");
            _ball.AcceptInput("Wake");
        }

        SaveBallSettings("physics_profile_command");
        var summary = BuildGameplayPhysicsProfileSummary();
        Logger.LogInformation("[SM2DIAG] physics_profile_changed {Summary}", summary);
        command.ReplyToCommand($"[SM2DIAG] {summary}");
        SnapshotBall("physics_profile_changed");
    }

    private static bool TryParseProfileValue(string value, float minimum, float maximum, out float parsed)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out parsed)
            && float.IsFinite(parsed)
            && parsed >= minimum
            && parsed <= maximum;
    }

    private void NeutralizeLegacyMapKillTriggers(string reason)
    {
        var neutralized = 0;
        foreach (var trigger in Utilities.FindAllEntitiesByDesignerName<CBaseEntity>("trigger_hurt"))
        {
            if (!trigger.IsValid
                || trigger.Entity?.Name is not { } targetName
                || (!string.Equals(targetName, CtLegacyKillTriggerName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(targetName, TLegacyKillTriggerName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            trigger.AcceptInput("Disable");
            trigger.AcceptInput("Kill");
            neutralized++;
        }

        if (neutralized > 0 && reason != "maintenance")
        {
            Logger.LogInformation(
                "[SM2DIAG] legacy_map_kill_triggers_neutralized reason={Reason} count={Count}",
                reason,
                neutralized);
        }
    }

    private void ResetBallForGoalSafety(string reason)
    {
        if (!BindBall(reason) || _ball is not { IsValid: true })
        {
            return;
        }

        _ball.AcceptInput("Wake");
        _ball.Teleport(
            position: CreateBallResetOrigin(),
            angles: new QAngle(0.0f, 0.0f, 0.0f),
            velocity: new Vector(0.0f, 0.0f, 0.0f));
        ResetDerivedMotion();
        Logger.LogInformation("[SM2DIAG] ball_reset_for_goal_safety reason={Reason}", reason);
    }

    private void ApplyCurrentGameplayPhysicsProfile(string reason)
    {
        if (BindBall(reason) && _ball is { IsValid: true })
        {
            ApplyGameplayPhysicsProfile(_ball, reason);
            _ball.AcceptInput("Wake");
        }

        Logger.LogInformation("[SM2DIAG] physics_profile_changed reason={Reason} {Summary}", reason, BuildGameplayPhysicsProfileSummary());
    }

    private void OnBallReplaceTestCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var activated = TryActivateOwnedBall("replace_test_command", out var result);
        command.ReplyToCommand($"[SM2DIAG] {result}");
        if (activated)
        {
            SnapshotBall("replace_test_active");
        }
    }

    private void OnBallTraceArenaCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command)
            || !BindBall("trace_arena_command")
            || _ball?.AbsOrigin is not { } origin)
        {
            return;
        }

        var options = new TraceOptions { InteractsWith = Masks.SolidBrushOnly };
        const int radialTraceCount = 16;
        for (var index = 0; index < radialTraceCount; index++)
        {
            var angleDegrees = index * (360.0f / radialTraceCount);
            var angleRadians = angleDegrees * (MathF.PI / 180.0f);
            var direction = new Vector(
                MathF.Cos(angleRadians),
                MathF.Sin(angleRadians),
                0.0f);
            LogArenaTrace($"radial_{angleDegrees:F1}", origin, direction, options);
        }

        LogArenaTrace("vertical_down", origin, new Vector(0.0f, 0.0f, -1.0f), options);
        LogArenaTrace("vertical_up", origin, new Vector(0.0f, 0.0f, 1.0f), options);
        command.ReplyToCommand(
            $"[SM2DIAG] arena trace emitted {radialTraceCount + 2} read-only samples to the server log");
    }











    private void LogArenaTrace(
        string label,
        Vector origin,
        Vector direction,
        TraceOptions options)
    {
        var end = new Vector(
            origin.X + direction.X * ArenaProbeDistance,
            origin.Y + direction.Y * ArenaProbeDistance,
            origin.Z + direction.Z * ArenaProbeDistance);
        var trace = Trace.TraceEndShape(origin, end, _ball, options);
        Logger.LogInformation(
            "[SM2DIAG] arena_trace label={Label} hit={Hit} allSolid={AllSolid} fraction={Fraction:F6} distance={Distance:F2} end={End} normal={Normal} hitClass={HitClass}",
            label,
            trace.DidHit(),
            trace.IsAllSolid,
            trace.Fraction,
            Distance(origin, trace.EndPos),
            FormatVector(trace.EndPos),
            FormatVector(trace.Normal),
            TraceHitClass(trace));
    }

    private void EnsureBallFoundation(string reason)
    {
        if (!string.Equals(_currentMapName, FoundationMapName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryActivateOwnedBall(reason, out _);
    }

    private bool TryActivateOwnedBall(string reason, out string result)
    {
        if (!BindBall(reason))
        {
            result = "map ball is not available yet";
            return false;
        }

        if (_ball?.Entity?.Name == OwnedBallTargetName)
        {
            ApplyGameplayPhysicsProfile(_ball, reason);
            result = "single visible authoritative ball is already active";
            return true;
        }

        var mapBall = _ball!;
        var origin = mapBall.AbsOrigin;
        var angles = mapBall.AbsRotation;
        if (origin is null || angles is null)
        {
            result = "map ball has no usable transform; replacement aborted";
            return false;
        }

        // Deliberately do not preserve the map ball's own X/Y: the CSF map's
        // placed slightly off the true pitch centre (mapper imprecision,
        // measured (7.73, 2.60) against a symmetric arena whose true centre
        // is (0, 0) - see BallResetX/Y above).
        var resetOrigin = CreateBallResetOrigin();

        // Keep the map-authored entity itself. It is part of every client's
        // round baseline and therefore visible immediately. One networked
        // entity supplies both rendering and collision with no proxy, no
        // overlapping hidden hull, and no CheckTransmit filter.
        RemoveOwnedBallVisual();
        _parkedMapBallOrigin = new Vector(origin.X, origin.Y, origin.Z);
        _parkedMapBallAngles = new QAngle(angles.X, angles.Y, angles.Z);
        var collision = mapBall.Collision;
        var collisionAttribute = collision.CollisionAttribute;
        _parkedMapBallInteractsAs = collisionAttribute.InteractsAs;
        _parkedMapBallInteractsWith = collisionAttribute.InteractsWith;
        _parkedMapBallInteractsExclude = collisionAttribute.InteractsExclude;
        mapBall.Entity!.Name = OwnedBallTargetName;
        mapBall.AcceptInput("EnableCollision");
        mapBall.AcceptInput("EnableMotion");
        ApplyGameplayPhysicsProfile(mapBall, reason);
        mapBall.Teleport(
            position: resetOrigin,
            angles: new QAngle(angles.X, angles.Y, angles.Z),
            velocity: new Vector(0.0f, 0.0f, 0.0f));
        mapBall.AcceptInput("Wake");
        _parkedMapBall = mapBall;
        _ball = mapBall;
        ResetDerivedMotion();
        Logger.LogInformation(
            "[SM2DIAG] single_ball_activated reason={Reason} index={Index} model={Model} solidType={SolidType} solidFlags={SolidFlags} enablePhysics={EnablePhysics} collisionMins={CollisionMins} collisionMaxs={CollisionMaxs}",
            reason,
            mapBall.Index,
            BallVisualModelName,
            collision.SolidType,
            collision.SolidFlags,
            collision.EnablePhysics,
            FormatVector(collision.Mins),
            FormatVector(collision.Maxs));
        result = "map-authored Jabulani promoted to the single visible authoritative ball";
        SnapshotBall($"single_ball_activated_{reason}");
        return true;
    }

    private void OnBallResetCenterCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (!TryActivateOwnedBall("center_reset", out var result)
            || _ball is not { IsValid: true })
        {
            command.ReplyToCommand($"[SM2DIAG] center reset failed: {result}");
            return;
        }

        // Same frozen-at-rest end state as every other reset path (see
        // ForceBallFullStop) - first touch unfreezes.
        ForceBallFullStop("center_reset");
        command.ReplyToCommand($"[SM2DIAG] center reset: {result}");
        SnapshotBall("center_reset_complete");
    }

    private void OnBallRestoreMapCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        RemoveOwnedBallVisual();

        var mapBall = _ball is { IsValid: true }
            ? _ball
            : _parkedMapBall is { IsValid: true }
            ? _parkedMapBall
            : FindMapBall();
        if (mapBall is null)
        {
            _ball = null;
            command.ReplyToCommand("[SM2DIAG] original map ball is unavailable; reload the map for a full reset");
            return;
        }

        var restoreOrigin = _parkedMapBallOrigin is not null
            ? new Vector(_parkedMapBallOrigin.X, _parkedMapBallOrigin.Y, _parkedMapBallOrigin.Z)
            : CreateBallResetOrigin();
        var restoreAngles = _parkedMapBallAngles is not null
            ? new QAngle(_parkedMapBallAngles.X, _parkedMapBallAngles.Y, _parkedMapBallAngles.Z)
            : new QAngle(0.0f, 0.0f, 0.0f);
        var restoreCollision = mapBall.Collision;
        var restoreCollisionAttribute = restoreCollision.CollisionAttribute;
        restoreCollisionAttribute.InteractsAs = _parkedMapBallInteractsAs;
        restoreCollisionAttribute.InteractsWith = _parkedMapBallInteractsWith;
        restoreCollisionAttribute.InteractsExclude = _parkedMapBallInteractsExclude;
        restoreCollision.SolidType = SolidType_t.SOLID_VPHYSICS;
        restoreCollision.EnablePhysics = 1;
        mapBall.Entity!.Name = BallTargetName;
        mapBall.Teleport(
            position: restoreOrigin,
            angles: restoreAngles,
            velocity: new Vector(0.0f, 0.0f, 0.0f));
        mapBall.AcceptInput("EnableCollision");
        mapBall.AcceptInput("EnableMotion");
        mapBall.AcceptInput("Wake");
        _ball = mapBall;
        _parkedMapBall = null;
        _parkedMapBallOrigin = null;
        _parkedMapBallAngles = null;
        ResetDerivedMotion();
        command.ReplyToCommand("[SM2DIAG] clean test ball removed and map ball restored");
        SnapshotBall("map_ball_restored");
    }

    private Vector CreateBallResetOrigin() =>
        new(_ballResetX, _ballResetY, BallResetZ);

    // 2026-09-01 user report (two rounds of it): after any reset the ball
    // kept drifting slightly instead of sitting perfectly still. First
    // theory (angular-velocity carryover) was disproven by the journal:
    // entity indexes CHANGE across round restarts (the ball is recreated,
    // nothing carries over), and settle kept logging 1-7 u/s of wander for
    // ~9s after the stop - the documented zero-damping creep plus the
    // circumradius spawn settling onto a hull facet. The user's actual
    // requirement is literal: "stand perfectly still till someone hits it
    // or touches it". So do exactly that: freeze the physics body
    // (DisableMotion, the same input OnBallResetCenterCommand already
    // uses) and unfreeze on the first real touch (UnfreezeBallForPlay,
    // called from every kick/push/launch path). A frozen body cannot
    // creep, roll off a facet, or keep spin - by construction.
    private bool _ballMotionFrozen;

    private void ForceBallFullStop(string reason)
    {
        if (_ball is not { IsValid: true })
        {
            return;
        }

        _ball.Teleport(
            position: CreateBallResetOrigin(),
            angles: new QAngle(0.0f, 0.0f, 0.0f),
            velocity: new Vector(0.0f, 0.0f, 0.0f));
        _ball.AcceptInput("DisableMotion");
        _ballMotionFrozen = true;
        ResetDerivedMotion();
        Logger.LogInformation("[SM2DIAG] ball_full_stop reason={Reason} frozen=True", reason);
    }

    // First real interaction re-enables physics. Callers: primary/wall-pop
    // kicks, the body push, trials/probes and the goal test - anything that
    // is about to impart motion.
    private void UnfreezeBallForPlay(string reason)
    {
        if (!_ballMotionFrozen)
        {
            return;
        }

        _ballMotionFrozen = false;
        if (_ball is { IsValid: true })
        {
            _ball.AcceptInput("EnableMotion");
            _ball.AcceptInput("Wake");
        }

        Logger.LogInformation("[SM2DIAG] ball_unfrozen reason={Reason}", reason);
    }

    private static CPhysicsPropMultiplayer? FindMapBall() => Utilities
        .FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>(BallDesignerName)
        .FirstOrDefault(candidate => candidate.IsValid && candidate.Entity?.Name == BallTargetName);

    private void OnKnifeGiveCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequireServerConsole(player, command))
        {
            return;
        }

        var eligiblePlayers = Utilities.GetPlayers().Where(IsEligiblePlayer).ToList();
        command.ReplyToCommand($"[SM2DIAG] controlled knife grant players={eligiblePlayers.Count}");
        foreach (var candidate in eligiblePlayers)
        {
            SnapshotPlayer(candidate, "pre_knife_grant");
            EnsurePlayerKnife(candidate, "controlled_command");
        }
    }

    private void EnsureAllPlayerKnives(string reason)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            EnsurePlayerKnife(player, reason);
        }
    }

    private bool EnsurePlayerKnife(CCSPlayerController player, string reason)
    {
        if (!IsEligiblePlayer(player))
        {
            return false;
        }

        var weapons = player.PlayerPawn.Value?.WeaponServices?.MyWeapons;
        var hasKnife = weapons is not null && weapons.Any(handle =>
        {
            var weapon = handle.Value;
            return weapon is { IsValid: true }
                && weapon.DesignerName.Contains("knife", StringComparison.OrdinalIgnoreCase);
        });
        if (hasKnife)
        {
            return true;
        }

        var knifeName = player.Team == CsTeam.Terrorist ? "weapon_knife_t" : "weapon_knife";
        var grantResult = player.GiveNamedItem(knifeName);
        Logger.LogInformation(
            "[SM2DIAG] knife_ensure reason={Reason} slot={Slot} name={Name} item={Item} result=0x{Result:X}",
            reason,
            player.Slot,
            player.PlayerName,
            knifeName,
            grantResult.ToInt64());
        Server.NextFrame(() => SnapshotPlayerIfValid(player, $"knife_ensure_{reason}_next_frame"));
        return grantResult != IntPtr.Zero;
    }

    private static bool RequireServerConsole(CCSPlayerController? player, CommandInfo command)
    {
        if (player is null)
        {
            return true;
        }

        command.ReplyToCommand("[SM2DIAG] this probe command is server-console/RCON only");
        return false;
    }

    private bool BindBall(string reason)
    {
        if (_ball is { IsValid: true })
        {
            return true;
        }

        _ball = Utilities
            .FindAllEntitiesByDesignerName<CPhysicsPropMultiplayer>(BallDesignerName)
            .Where(candidate => candidate.IsValid)
            .OrderByDescending(candidate => candidate.Entity?.Name == OwnedBallTargetName)
            .FirstOrDefault(candidate =>
                candidate.Entity?.Name is OwnedBallTargetName or BallTargetName);

        if (_ball is null || !_ball.IsValid)
        {
            if (reason != "maintenance")
            {
                Logger.LogWarning(
                    "[SM2DIAG] ball_not_found class={ClassName} target={TargetName} reason={Reason}",
                    BallDesignerName,
                    BallTargetName,
                    reason);
            }
            return false;
        }

        Logger.LogInformation(
            "[SM2DIAG] ball_bound index={Index} class={ClassName} target={TargetName} reason={Reason}",
            _ball.Index,
            _ball.DesignerName,
            _ball.Entity?.Name ?? "<none>",
            reason);
        return true;
    }

    private string BuildBallSummary()
    {
        if (_ball is null || !_ball.IsValid)
        {
            return $"ball=<not-found> class={BallDesignerName} target={BallTargetName}";
        }

        var origin = _ball.AbsOrigin;
        var velocity = _ball.AbsVelocity;
        var collision = _ball.Collision;
        var physicsCollision = collision.CollisionAttribute;
        var sceneScale = _ball.CBodyComponent?.SceneNode?.Scale;
        return
            $"ball=index:{_ball.Index},class:{_ball.DesignerName},target:{_ball.Entity?.Name ?? "<none>"} " +
            $"spawnflags:{_ball.Spawnflags} collisionGroup:{collision.CollisionGroup} " +
            $"physicsCollisionGroup:{physicsCollision.CollisionGroup} interactsAs:0x{physicsCollision.InteractsAs:X16} " +
            $"interactsWith:0x{physicsCollision.InteractsWith:X16} interactsExclude:0x{physicsCollision.InteractsExclude:X16} " +
            $"solidType:{collision.SolidType} solidFlags:{collision.SolidFlags} enablePhysics:{collision.EnablePhysics} " +
            $"sceneScale:{FormatNullable(sceneScale)} collisionMins:{FormatVector(collision.Mins)} collisionMaxs:{FormatVector(collision.Maxs)} boundingRadius:{collision.BoundingRadius:F3} " +
            $"moveType:{_ball.MoveType} actualMoveType:{_ball.ActualMoveType} massScale:{_ball.MassScale:F3} friction:{_ball.Friction:F3} elasticity:{_ball.Elasticity:F3} gravityScale:{_ball.GravityScale:F3} " +
            $"awake:{_ball.Awake} hasBeenAwakened:{_ball.HasBeenAwakened} touchedByPlayer:{_ball.TouchedByPlayer} " +
            $"origin:{FormatVector(origin)} absVelocity:{FormatVector(velocity)} absSpeed:{VectorSpeed(velocity):F2} " +
            $"derivedVelocity:{FormatVector(_derivedBallVelocity)} derivedSpeed:{VectorSpeed(_derivedBallVelocity):F2}";
    }

    private void SnapshotBall(string reason)
    {
        BindBall(reason);
        Logger.LogInformation("[SM2DIAG] ball_snapshot reason={Reason} mode={Mode} {Summary}", reason, _mode, BuildBallSummary());
    }

    private void SnapshotAllPlayers(string reason)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player.IsValid)
            {
                SnapshotPlayer(player, reason);
            }
        }
    }

    private void SnapshotPlayerIfValid(CCSPlayerController player, string reason)
    {
        if (player.IsValid)
        {
            SnapshotPlayer(player, reason);
        }
    }

    private void SnapshotPlayer(CCSPlayerController player, string reason)
    {
        Logger.LogInformation(
            "[SM2DIAG] player_snapshot reason={Reason} {Summary}",
            reason,
            BuildPlayerSummary(player));
    }

    private static string BuildPlayerSummary(CCSPlayerController player)
    {
        var pawn = player.PlayerPawn.Value;
        if (pawn is null || !pawn.IsValid)
        {
            return
                $"slot={player.Slot} name={player.PlayerName} team={player.Team} pawn=<invalid>";
        }

        var weaponServices = pawn.WeaponServices;
        var weaponNames = weaponServices?.MyWeapons
            .Select(handle => handle.Value)
            .Where(weapon => weapon is { IsValid: true })
            .Select(weapon => weapon!.DesignerName)
            .ToArray() ?? Array.Empty<string>();
        var activeWeapon = weaponServices?.ActiveWeapon.Value;

        return
            $"slot={player.Slot} name={player.PlayerName} team={player.Team} alive={IsAlive(pawn)} " +
            $"origin={FormatVector(pawn.AbsOrigin)} velocity={FormatVector(pawn.AbsVelocity)} " +
            $"active={activeWeapon?.DesignerName ?? "<none>"} weapons=[{string.Join(',', weaponNames)}]";
    }

    private void ObservePlayerProximity()
    {
        if (_ball is null || !_ball.IsValid || _ball.AbsOrigin is not { } ballOrigin)
        {
            _playersNearBall.Clear();
            return;
        }

        var currentlyNear = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value?.AbsOrigin is not { } playerOrigin)
            {
                continue;
            }

            var distance = Distance(ballOrigin, playerOrigin);
            if (distance > NearBallRange)
            {
                continue;
            }

            currentlyNear.Add(player.Slot);
            if (_playersNearBall.Add(player.Slot))
            {
                Logger.LogInformation(
                    "[SM2DIAG] proximity_enter slot={Slot} name={Name} distance={Distance:F2} playerVelocity={PlayerVelocity} ballVelocity={BallVelocity} touchedByPlayer={TouchedByPlayer}",
                    player.Slot,
                    player.PlayerName,
                    distance,
                    FormatVector(player.PlayerPawn.Value!.AbsVelocity),
                    FormatVector(_ball.AbsVelocity),
                    _ball.TouchedByPlayer);
            }
        }

        foreach (var slot in _playersNearBall.Where(slot => !currentlyNear.Contains(slot)).ToArray())
        {
            _playersNearBall.Remove(slot);
            Logger.LogInformation(
                "[SM2DIAG] proximity_exit slot={Slot} ballVelocity={BallVelocity} touchedByPlayer={TouchedByPlayer}",
                slot,
                FormatVector(_ball.AbsVelocity),
                _ball.TouchedByPlayer);
        }
    }

    private void ApplyPlayerBallPush()
    {
        // Match ball + training balls (Training.cs) through one seam; the
        // match ball's own state (_playersPushingBall, stats/GK touches,
        // kickoff release, freeze) is only ever touched on IsMatchBall.
        foreach (var target in PlayableBalls().ToList())
        {
            ApplyPlayerBallPushFor(target);
        }
    }

    private void ApplyPlayerBallPushFor(PlayableBall target)
    {
        var ball = target.Ball;
        var origin = target.Origin;
        var inherited = target.Inherited;
        var pushing = target.PushingSlots;

        var currentlyPushing = new HashSet<int>();
        foreach (var player in Utilities.GetPlayers())
        {
            if (!IsEligiblePlayer(player) || player.PlayerPawn.Value is not { IsValid: true } pawn
                || pawn.AbsOrigin is not { } playerOrigin)
            {
                continue;
            }

            var ballTopZ = origin.Z + BallCollisionRadius - BallPushFeetClearance;
            if (playerOrigin.Z >= ballTopZ || playerOrigin.Z < origin.Z - BallPushHeightGate)
            {
                continue;
            }

            var dx = origin.X - playerOrigin.X;
            var dy = origin.Y - playerOrigin.Y;
            var planarDistance = MathF.Sqrt(dx * dx + dy * dy);
            if (planarDistance > BallPushContactDistance || planarDistance < 0.001f)
            {
                continue;
            }

            var dirX = dx / planarDistance;
            var dirY = dy / planarDistance;
            var playerVelocity = pawn.AbsVelocity;
            var approachSpeed = playerVelocity.X * dirX + playerVelocity.Y * dirY;
            if (approachSpeed < BallPushMinApproachSpeed)
            {
                continue;
            }

            var targetAlongDir = Math.Min(approachSpeed * _ballPushTransferRatio, _ballPushMaxSpeed);
            var ballSpeed = VectorSpeed(inherited);
            if (ballSpeed < BallPushKickstartSpeedThreshold)
            {
                // A dead/asleep ball's first nudge must never be weaker than
                // an already-rolling ball's push, even though the engine's
                // own collision response ate more of the player's speed on
                // first contact (see comment on the consts above).
                targetAlongDir = Math.Max(targetAlongDir, BallPushKickstartMinTarget);
            }

            var currentAlongDir = inherited.X * dirX + inherited.Y * dirY;
            if (currentAlongDir >= targetAlongDir)
            {
                // Ball is already moving away/faster along this axis (e.g.
                // right after a kick) - don't fight it with a weaker push.
                continue;
            }

            var perpX = inherited.X - currentAlongDir * dirX;
            var perpY = inherited.Y - currentAlongDir * dirY;
            var pushedVelocity = new Vector(
                perpX + targetAlongDir * dirX,
                perpY + targetAlongDir * dirY,
                inherited.Z);

            if (target.IsMatchBall)
            {
                UnfreezeBallForPlay("body_push");
            }
            ball.AcceptInput("Wake");
            ball.Teleport(velocity: pushedVelocity);
            currentlyPushing.Add(player.Slot);
            if (pushing.Add(player.Slot))
            {
                // Genuinely a NEW push starting - this is a real touch for
                // GK save / stats purposes (RecordBallTouch), same as a
                // kick. ApplyPlayerBallPush runs every tick while contact
                // continues, so this must stay gated to push-START, or a
                // 3-second dribble would count as hundreds of touches.
                if (target.IsMatchBall)
                {
                    RecordBallTouch(player, origin);
                }
                Logger.LogInformation(
                    "[SM2DIAG] ball_push_start slot={Slot} name={Name} approachSpeed={ApproachSpeed:F1} targetAlongDir={TargetAlongDir:F1} matchBall={MatchBall}",
                    player.Slot,
                    player.PlayerName,
                    approachSpeed,
                    targetAlongDir,
                    target.IsMatchBall);
            }
            else if (target.IsMatchBall)
            {
                // Still pushing continuously - own-goal attribution
                // (Match.cs) is by last TOUCHER, so this must stay
                // current, but it is NOT a new touch for stats/GK.
                _lastKickerSlot = player.Slot;
                _lastKickerTeam = player.Team;
                ClearKickoffRestrictionOnTouch(player.Team);
            }
        }

        pushing.IntersectWith(currentlyPushing);
    }

    private void UpdateDerivedMotion()
    {
        if (_ball is null || !_ball.IsValid || _ball.AbsOrigin is not { } origin)
        {
            ResetDerivedMotion();
            return;
        }

        var now = (double)Server.TickedTime;
        if (_previousBallOrigin is not null)
        {
            // A goal fires a kickoff reset (ResetBallForGoalSafety ->
            // ResetDerivedMotion) from INSIDE this same call, which nulls
            // _previousBallOrigin/_ball out from under this still-running
            // method. Bail out immediately when that happens instead of
            // falling through to code that dereferences the now-null field
            // (measured live: NullReferenceException on every goal).
            if (MatchCheckGoalCrossing(_previousBallOrigin, origin))
            {
                return;
            }

            var elapsed = now - _previousBallSampleTime;
            if (elapsed > 0.000001)
            {
                _derivedBallVelocity = new Vector(
                    (float)((origin.X - _previousBallOrigin.X) / elapsed),
                    (float)((origin.Y - _previousBallOrigin.Y) / elapsed),
                    (float)((origin.Z - _previousBallOrigin.Z) / elapsed));
                UpdateBallSettleState(origin, _derivedBallVelocity);
                if (!_ballSettled)
                {
                    TryApplyWallAssist(_derivedBallVelocity, now);
                }
            }
        }

        _previousBallOrigin = new Vector(origin.X, origin.Y, origin.Z);
        _previousBallSampleTime = now;
    }

    // Edge-triggered settle with re-arming: a single Teleport(velocity: 0)
    // does not reliably hold at an exact zero forever on this compound hull
    // (measured live: it can resume a small ~2 u/s constant creep seconds
    // after being zeroed, on a different facet than where it first settled -
    // this is the same zero-damping "never truly stops" behavior documented
    // in the root-cause doc, just re-appearing after a reset instead of
    // being prevented by it). So this does NOT latch permanently: it keeps
    // counting consecutive below-threshold ticks continuously, and re-fires
    // the zeroing Teleport every time a fresh low-speed streak completes -
    // in practice a handful of times a second at worst if the residual creep
    // is persistent, which is far below the "unconditional every tick"
    // write pattern that caused the original jitter bug (see
    // ApplyGameplayPhysicsProfile/ApplyBallCollisionGroup above). _ballSettled
    // is purely a status flag (wall-assist skip, css_sm2ball_settle display):
    // true while the ball is currently sitting inside a below-threshold
    // streak, false the instant a real kick/push pushes it back over the
    // threshold.
    private void UpdateBallSettleState(Vector origin, Vector velocity)
    {
        if (!_settleEnabled || _ball is not { IsValid: true })
        {
            return;
        }

        var speed = MathF.Sqrt(
            velocity.X * velocity.X + velocity.Y * velocity.Y + velocity.Z * velocity.Z);

        var grounded = origin.Z <= StadiumPitchPlaneZ + BallCollisionRadius + SettleGroundToleranceZ;
        if (!grounded || speed >= _settleSpeedThreshold)
        {
            _settleLowSpeedTicks = 0;
            _ballSettled = false;
            return;
        }

        _ballSettled = true;
        // Already at a true, exact standstill from a previous zeroing - no
        // need to re-teleport or restart the countdown.
        if (speed < 0.05f)
        {
            _settleLowSpeedTicks = 0;
            return;
        }

        _settleLowSpeedTicks++;
        if (_settleLowSpeedTicks < _settleTicks)
        {
            return;
        }

        _ball.Teleport(velocity: new Vector(0.0f, 0.0f, 0.0f));
        _ballSettled = true;
        _settleLowSpeedTicks = 0;
        Logger.LogInformation(
            "[SM2DIAG] ball_settled speed={Speed:F2} ticks={Ticks}",
            speed,
            _settleTicks);
    }

    // Fallback for the CS:S wall "hochbuggen" hop — see the constants block
    // above for why this exists instead of native spin.  Detects a real wall
    // bounce from the ball's OWN already-computed motion (a planar-velocity
    // reversal, or a trace-confirmed contact slowdown) rather than predicting
    // one from aim. It restores the minimum wall-normal rebound measured in
    // CS:S while preserving Rubikon's real tangential component/collision
    // angle, then adds a vertical component sized from the speed the bounce
    // actually removed.
    //
    // Comparing only ADJACENT ticks misses real bounces: measured directly
    // against the arena wall, an incoming ~745 u/s tick is followed by a
    // near-zero tick during the actual contact frame, then a ~196 u/s
    // rebound tick — the contact frame's near-zero speed fails a simple
    // "was the previous tick fast" gate.  Instead this keeps a short rolling
    // window of recent samples and uses the FASTEST one in the window as the
    // approach reference, which bridges over that near-zero contact frame.
    private void TryApplyWallAssist(Vector current, double now)
    {
        _recentBallVelocities.Enqueue(current);
        while (_recentBallVelocities.Count > WallAssistHistoryTicks)
        {
            _recentBallVelocities.Dequeue();
        }

        if (!_wallAssistEnabled
            || _ball is not { IsValid: true }
            || now - _lastWallAssistTime < WallAssistCooldownSeconds)
        {
            return;
        }

        var approach = current;
        var approachPlanarSpeed = 0.0f;
        foreach (var sample in _recentBallVelocities)
        {
            var speed = MathF.Sqrt(sample.X * sample.X + sample.Y * sample.Y);
            if (speed > approachPlanarSpeed)
            {
                approachPlanarSpeed = speed;
                approach = sample;
            }
        }

        if (approachPlanarSpeed < WallAssistMinimumApproachSpeed)
        {
            return;
        }

        var currentPlanarSpeed = MathF.Sqrt(current.X * current.X + current.Y * current.Y);
        var dot = currentPlanarSpeed > 0.01f
            ? (approach.X * current.X + approach.Y * current.Y) / (approachPlanarSpeed * currentPlanarSpeed)
            : 0.0f;

        // A wall collision may be a clean reversal, a near-zero contact
        // frame, or a glancing hit whose tangential speed makes its overall
        // direction look unchanged.  The common signal is a large speed loss
        // while the ball is physically within one radius of a solid brush.
        var isStrongReversal = currentPlanarSpeed > 0.01f
            && dot <= WallAssistReversalDotThreshold;
        if (!isStrongReversal
            && currentPlanarSpeed / approachPlanarSpeed > WallAssistContactSpeedRatio)
        {
            return;
        }

        if (_ball.AbsOrigin is not { } ballOrigin)
        {
            return;
        }

        var approachUnitX = approach.X / approachPlanarSpeed;
        var approachUnitY = approach.Y / approachPlanarSpeed;
        var contactProbeEnd = new Vector(
            ballOrigin.X + approachUnitX * (BallCollisionRadius + WallAssistContactProbeExtraDistance),
            ballOrigin.Y + approachUnitY * (BallCollisionRadius + WallAssistContactProbeExtraDistance),
            ballOrigin.Z);
        // The midfield curb participates in Rubikon's general solid layer but
        // is absent from SolidBrushOnly. Filter the result back down to static
        // map/world classes below so players and movable props cannot qualify.
        var traceOptions = new TraceOptions { InteractsWith = Masks.Solid };
        var contactProbe = Trace.TraceEndShape(
            ballOrigin,
            contactProbeEnd,
            _ball,
            traceOptions);

        // Use the actual surface normal, not the ball's full approach vector.
        // On a glancing hit those differ: the approach contains the along-wall
        // component that must remain untouched.
        var wallNormalX = 0.0f;
        var wallNormalY = 0.0f;
        var surfaceSource = "trace";
        if (IsStaticWallSurface(contactProbe) && contactProbe.Fraction < 0.999f)
        {
            var wallNormalPlanarLength = MathF.Sqrt(
                contactProbe.Normal.X * contactProbe.Normal.X
                + contactProbe.Normal.Y * contactProbe.Normal.Y);
            if (wallNormalPlanarLength < 0.70f)
            {
                return;
            }

            wallNormalX = contactProbe.Normal.X / wallNormalPlanarLength;
            wallNormalY = contactProbe.Normal.Y / wallNormalPlanarLength;
        }
        else if (string.Equals(_currentMapName, FoundationMapName, StringComparison.OrdinalIgnoreCase)
            && TryGetFoundationBoundaryNormal(ballOrigin, out wallNormalX, out wallNormalY))
        {
            surfaceSource = "measured_boundary";
        }
        else
        {
            return;
        }

        var incomingNormalSpeed = -(approach.X * wallNormalX + approach.Y * wallNormalY);
        if (incomingNormalSpeed < WallAssistMinimumApproachSpeed)
        {
            return;
        }

        var speedLost = approachPlanarSpeed - currentPlanarSpeed;
        if (speedLost <= 0.0f)
        {
            return;
        }

        // Restore only the component normal to the detected approach. Scaling
        // the whole planar vector would also amplify a glancing wall hit's
        // tangential motion and distort its angle. The CS:S reference wall
        // capture retained about 61 / 334 = 0.18 of the incoming normal speed.
        var currentNormalRebound = current.X * wallNormalX + current.Y * wallNormalY;
        var targetNormalRebound = incomingNormalSpeed * _wallAssistMinimumNormalRetention;
        var addedNormalRebound = Math.Max(0.0f, targetNormalRebound - currentNormalRebound);

        var addedVertical = Math.Min(speedLost * _wallAssistConversionRatio, _wallAssistMaxAddedVertical);
        var boosted = new Vector(
            current.X + wallNormalX * addedNormalRebound,
            current.Y + wallNormalY * addedNormalRebound,
            current.Z + addedVertical);
        _ball.Teleport(velocity: boosted);
        // 2026-09-01 user report: shooting flat into a wall rarely leaves
        // the ball dead-stopped right after an otherwise normal-looking
        // bounce. Diagnosis: Teleport only ever rewrites LINEAR velocity -
        // the ball's angular velocity (spin) survives the bounce unchanged.
        // A kicked ball carries forward topspin; after a near-180-degree
        // reversal that same absolute spin is now BACKSPIN relative to its
        // new direction of travel, and backspin-while-moving on the pitch
        // bleeds speed to ground friction far faster than normal rolling
        // resistance - reading as "bounced, then just stopped". Re-spin to
        // match the REBOUND direction (same formula/native bridge as a
        // kick's topspin, same global dial so `spinfactor off` also kills
        // this) instead of reinventing a separate wall-contact detector -
        // this function's own reversal/speed-loss/surface-normal logic
        // above is already the verified "this is a wall hit" signal.
        if (_ballSpinFactor > 0.0f)
        {
            ApplyBallTopspin(_ball, boosted, _ballSpinFactor);
        }
        ScheduleWallAssistSeparation(
            _ball.Index,
            _wallAssistGeneration,
            boosted,
            WallAssistSeparationFrames);
        _lastWallAssistTime = now;
        _recentBallVelocities.Clear();
        Logger.LogInformation(
            "[SM2DIAG] wall_assist_applied mode={Mode} surface={Surface} approachSpeed={ApproachSpeed:F1} currentSpeed={CurrentSpeed:F1} dot={Dot:F3} wallNormal=({NormalX:F3},{NormalY:F3}) incomingNormal={IncomingNormal:F1} speedLost={SpeedLost:F1} normalBefore={NormalBefore:F1} normalTarget={NormalTarget:F1} addedNormal={AddedNormal:F1} addedVertical={AddedVertical:F1} separationFrames={SeparationFrames} verticalRatio={VerticalRatio:F3} minNormalRetention={MinNormalRetention:F3}",
            isStrongReversal ? "reversal" : "contact_slowdown",
            surfaceSource,
            approachPlanarSpeed,
            currentPlanarSpeed,
            dot,
            wallNormalX,
            wallNormalY,
            incomingNormalSpeed,
            speedLost,
            currentNormalRebound,
            targetNormalRebound,
            addedNormalRebound,
            addedVertical,
            WallAssistSeparationFrames,
            _wallAssistConversionRatio,
            _wallAssistMinimumNormalRetention);
    }

    private void ScheduleWallAssistSeparation(
        uint ballIndex,
        int generation,
        Vector reboundVelocity,
        int framesRemaining)
    {
        if (framesRemaining <= 0)
        {
            return;
        }

        Server.NextFrame(() =>
        {
            if (_wallAssistGeneration != generation
                || _ball is not { IsValid: true }
                || _ball.Index != ballIndex)
            {
                return;
            }

            _ball.AcceptInput("Wake");
            _ball.Teleport(velocity: new Vector(
                reboundVelocity.X,
                reboundVelocity.Y,
                reboundVelocity.Z));
            ScheduleWallAssistSeparation(
                ballIndex,
                generation,
                reboundVelocity,
                framesRemaining - 1);
        });
    }

    // A kick is an impulse, so the resulting delta-velocity is inversely
    // proportional to mass, not to its square root.
    private float ComputeGameplayMassResponse() => Math.Clamp(
        DefaultGameplayMassScale / _gameplayMassScale,
        0.25f,
        4.00f);

    // Maps the vertical contact offset (-1 = struck at the bottom of the ball,
    // 0 = through the centre, +1 = struck on top) onto the launch angle.  A
    // centre hit reproduces the launch angle measured in the CS:S captures.
    private static float ComputeKickLiftDegrees(float contactRatio)
    {
        if (contactRatio <= 0.0f)
        {
            var low = Math.Clamp(-contactRatio, 0.0f, 1.0f);
            return KickLiftAngleLevelDegrees
                + (KickLiftAngleLoftedDegrees - KickLiftAngleLevelDegrees) * low;
        }

        var high = Math.Clamp(contactRatio, 0.0f, 1.0f);
        return KickLiftAngleLevelDegrees
            + (KickLiftAngleFlatDegrees - KickLiftAngleLevelDegrees) * high;
    }

    private void ResetDerivedMotion()
    {
        _previousBallOrigin = null;
        _previousBallSampleTime = 0.0;
        _derivedBallVelocity = new Vector(0.0f, 0.0f, 0.0f);
        _wallAssistGeneration++;
        _recentBallVelocities.Clear();
        ResetBodyImpactMotionTracking();
        _ballSettled = false;
        _settleLowSpeedTicks = 0;
    }

    private static bool IsEligiblePlayer(CCSPlayerController? player)
    {
        if (player is null || !player.IsValid || player.IsBot)
        {
            return false;
        }

        if (player.Team is not (CsTeam.Terrorist or CsTeam.CounterTerrorist))
        {
            return false;
        }

        return IsAlive(player.PlayerPawn.Value);
    }

    private static bool IsAlive(CCSPlayerPawn? pawn) =>
        pawn is { IsValid: true } && pawn.LifeState == (byte)LifeState_t.LIFE_ALIVE;

    // Same MovementServices -> CCSPlayer_MovementServices cast MoveProbe.cs
    // uses to read Ducked/Ducking. Checks both: Ducked is the settled
    // in-stance flag, Ducking is the mid-transition flag - a player who just
    // tapped crouch right as they swing should still count.
    private static bool IsPlayerCrouching(CCSPlayerPawn? pawn)
    {
        var movement = pawn?.MovementServices;
        if (movement is null)
        {
            return false;
        }

        var humanoid = new CCSPlayer_MovementServices(movement.Handle);
        return humanoid.Ducked || humanoid.Ducking;
    }

    private float? GetBallDistance(CCSPlayerPawn? pawn)
    {
        if (pawn?.AbsOrigin is not { } playerOrigin
            || _ball is null
            || !_ball.IsValid
            || _ball.AbsOrigin is not { } ballOrigin)
        {
            return null;
        }

        return Distance(playerOrigin, ballOrigin);
    }

    private static float Distance(Vector first, Vector second)
    {
        var dx = first.X - second.X;
        var dy = first.Y - second.Y;
        var dz = first.Z - second.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private static float VectorSpeed(Vector value) =>
        MathF.Sqrt(value.X * value.X + value.Y * value.Y + value.Z * value.Z);

    private static float Dot(Vector first, Vector second) =>
        first.X * second.X + first.Y * second.Y + first.Z * second.Z;

    // The user reports the ball rolling THROUGH players.  Cause: the spawned
    // prop_physics_multiplayer comes up in collision group 20
    // (COLLISION_GROUP_PUSHAWAY), which in Source is explicitly non-solid to
    // players — the player pushaway code shoves it around, but there is no real
    // contact.  The compiled model itself declares "default"
    // (m_CollisionGroupString), so this override happens at entity spawn.
    //
    // CS:S's ball was a func_physbox with a normal solid collision group, so
    // bodies blocked it physically.  Force the group back to a solid one here.
    // Group index is tunable live (css_sm2ball_collision) because the CS2
    // CollisionGroup_t enum ordering is not documented anywhere we can verify
    // offline — 0 is the conventional "none/solid-against-everything" value.
    private void ApplyBallCollisionGroup(CPhysicsPropMultiplayer ball, string reason)
    {
        if (!ball.IsValid || _ballCollisionGroup < 0)
        {
            return;
        }

        var collision = ball.Collision;
        var before = collision.CollisionGroup;
        if (before == (byte)_ballCollisionGroup)
        {
            // Already correct.  EnsureBallFoundation runs this every maintenance
            // tick (about once a second) for as long as the ball exists, so an
            // unconditional Wake here forced the ball awake every second
            // forever -- it could never finish settling, which is why a resting
            // ball kept jittering slightly in every direction.  Only touch the
            // entity (and only then wake it) on an actual change.
            return;
        }

        collision.CollisionGroup = (byte)_ballCollisionGroup;
        ball.AcceptInput("Wake");
        Logger.LogInformation(
            "[SM2DIAG] ball_collision_group_applied reason={Reason} before={Before} after={After}",
            reason,
            before,
            collision.CollisionGroup);
    }

    // Live tuning: css_sm2ball_collision <groupIndex|-1 to leave alone>.
    private void OnBallCollisionCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        if (command.ArgCount >= 2
            && int.TryParse(command.GetArg(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var group)
            && group >= -1
            && group <= 63)
        {
            _ballCollisionGroup = group;
            if (BindBall("collision_command") && _ball is { IsValid: true })
            {
                ApplyBallCollisionGroup(_ball, "collision_command");
            }
            SaveBallSettings("collision_command");
        }

        var live = _ball is { IsValid: true } ? _ball.Collision.CollisionGroup.ToString() : "<no ball>";
        command.ReplyToCommand(
            $"[SM2DIAG] requested collision group {_ballCollisionGroup} (-1 = leave engine default); live group now {live}. 20 = PUSHAWAY = non-solid to players.");
    }

    // Live tuning: css_sm2ball_defaults - restores the Ball menu's own
    // fields (see RestoreBallDefaults comment for exactly what's included).
    private void OnBallDefaultsCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        RestoreBallDefaults("defaults_command");

        command.ReplyToCommand(
            $"[SM] Ball settings restored to defaults: spin={BallMenuNumber(_ballSpinFactor)} "
            + $"airkick={BallMenuNumber(_kickAirborneDeltaScale)} left={BallMenuNumber(_leftClickPowerScale)} "
            + $"right={BallMenuNumber(_rightClickPowerScale)} push={BallMenuNumber(_ballPushTransferRatio)}/{_ballPushMaxSpeed:F0} "
            + $"kicksound={(string.IsNullOrEmpty(_kickSoundName) ? "off" : _kickSoundName)} impact={(_ballImpactEnabled ? "on" : "off")} "
            + $"settle={(_settleEnabled ? "on" : "off")} elevation={BallMenuNumber(_kickElevationSensitivity)}");
    }

    // Live tuning: css_sm2ball_settle <on|off|threshold> [ticks].
    private void OnBallSettleCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "ball"))
        {
            return;
        }

        var changed = false;
        if (command.ArgCount >= 2)
        {
            var arg = command.GetArg(1).ToLowerInvariant();
            if (arg is "on" or "off")
            {
                _settleEnabled = arg == "on";
                changed = true;
            }
            else if (TryParseProfileValue(arg, 0.0f, 200.0f, out var threshold))
            {
                _settleSpeedThreshold = threshold;
                changed = true;
            }
        }

        if (command.ArgCount >= 3 && int.TryParse(command.GetArg(2), out var ticks) && ticks is >= 1 and <= 640)
        {
            _settleTicks = ticks;
            changed = true;
        }

        if (changed)
        {
            SaveBallSettings("settle_command");
        }

        command.ReplyToCommand(
            $"[SM2DIAG] settle enabled={_settleEnabled} threshold={_settleSpeedThreshold:F1} u/s ticks={_settleTicks} settled={_ballSettled}");
    }

    private void ApplyGameplayPhysicsProfile(CPhysicsPropMultiplayer ball, string reason)
    {
        if (!ball.IsValid)
        {
            return;
        }

        // EnsureBallFoundation calls this every maintenance tick (about once a
        // second) for as long as the ball exists.  A resting ball never fully
        // settled and kept creeping because these writes ran unconditionally
        // every time, and CS2's physics-prop property setters appear to
        // nudge/re-simulate the Rubikon body on ANY write, even one that
        // rewrites the same value it already had (the same class of bug the
        // collision-group fix below addresses).  Only touch a field when the
        // value is actually changing.
        if (ball.MassScale != _gameplayMassScale)
        {
            ball.MassScale = _gameplayMassScale;
        }

        if (ball.Friction != _gameplayFriction)
        {
            ball.Friction = _gameplayFriction;
        }

        if (ball.Elasticity != _gameplayElasticity)
        {
            ball.Elasticity = _gameplayElasticity;
        }

        if (ball.GravityScale != _gameplayGravityScale)
        {
            ball.GravityScale = _gameplayGravityScale;
        }

        ApplyBallCollisionGroup(ball, reason);
        Logger.LogDebug(
            "[SM2DIAG] physics_profile_applied reason={Reason} index={Index} {Summary}",
            reason,
            ball.Index,
            BuildGameplayPhysicsProfileSummary());
    }

    private string BuildGameplayPhysicsProfileSummary() =>
        $"massScale={_gameplayMassScale:F3} friction={_gameplayFriction:F3} elasticity={_gameplayElasticity:F3} gravityScale={_gameplayGravityScale:F3}";

    private void RemoveOwnedBallVisual()
    {
        if (_ballVisual is { IsValid: true })
        {
            _ballVisual.AcceptInput("Kill");
        }

        foreach (var visual in Utilities.FindAllEntitiesByDesignerName<CDynamicProp>(BallVisualDesignerName))
        {
            if (visual.IsValid && visual.Entity?.Name == BallVisualTargetName)
            {
                visual.AcceptInput("Kill");
            }
        }

        _ballVisual = null;
    }

    private static string TraceHitClass(TraceResult trace)
    {
        if (!trace.DidHit())
        {
            return "<none>";
        }

        var hit = trace.HitEntity();
        return hit.IsValid ? hit.DesignerName : "<world>";
    }

    private static bool IsStaticWallSurface(TraceResult trace)
    {
        if (!trace.DidHit())
        {
            return false;
        }

        var hit = trace.HitEntity();
        if (!hit.IsValid)
        {
            return true;
        }

        return hit.DesignerName is "worldent" or "func_wall" or "func_brush" or "func_detail" or "prop_static";
    }

    private static bool TryGetFoundationBoundaryNormal(
        Vector origin,
        out float normalX,
        out float normalY)
    {
        normalX = 0.0f;
        normalY = 0.0f;
        var maximumPlaneDistance = BallCollisionRadius + WallAssistContactProbeExtraDistance;
        var sidePlaneDistance = FoundationWallPlaneX - MathF.Abs(origin.X);
        var endPlaneDistance = FoundationWallPlaneY - MathF.Abs(origin.Y);
        var nearSide = sidePlaneDistance >= -WallAssistContactProbeExtraDistance
            && sidePlaneDistance <= maximumPlaneDistance;
        var nearEnd = endPlaneDistance >= -WallAssistContactProbeExtraDistance
            && endPlaneDistance <= maximumPlaneDistance;
        if (!nearSide && !nearEnd)
        {
            return false;
        }

        if (nearSide && (!nearEnd || sidePlaneDistance <= endPlaneDistance))
        {
            normalX = origin.X < 0.0f ? 1.0f : -1.0f;
        }
        else
        {
            normalY = origin.Y < 0.0f ? 1.0f : -1.0f;
        }

        return true;
    }

    private static string FormatVector(Vector? value) => value is null
        ? "<null>"
        : $"({value.X:F2},{value.Y:F2},{value.Z:F2})";

    private static string FormatNullable(float? value) => value.HasValue
        ? value.Value.ToString("F2", CultureInfo.InvariantCulture)
        : "<null>";

    private static string FormatAngle(QAngle value) =>
        $"({value.X:F2},{value.Y:F2},{value.Z:F2})";

    private enum KickMode
    {
        Velocity,
        Thruster
    }

    private enum BallProbeMode
    {
        Baseline,
        Wake
    }
}
