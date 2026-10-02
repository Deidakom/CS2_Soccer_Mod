using System.Drawing;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

// 2026-10-01 owner: "a visual representation of the kick cone so I can adjust better".
//
// The kick test (TryApplyPrimaryKnifeKick, KickGeometry) is: the ball's centre is at most
// reach + ball radius from the eye, and inside the aim cone (the ball's near edge counts,
// BallContactMath.KickSphereInCone / HorizontalKickAim). That is a shape in the air in front of
// the eye and hard to picture, so this draws what matters for play: the outline, on the pitch,
// of where a ball lying on the ground can be kicked from RIGHT NOW - for the way the player is
// standing and looking. It follows the view, so looking down, up or sideways shows at once what
// that does to the reach, and a changed "Kick reach" or "Kick cone width" changes the outline
// live. The outline is green while the match ball is inside the kick area, white otherwise.
//
// Switch: Ball settings - "Show kick area" (root, per player). The outline is made of beams,
// which everyone on the server sees (hiding entities per player needs CheckTransmit) - it is a
// tuning aid, not something to leave on in a match. Only the CS2 kick uses this cone; with the
// CS:S original ball the knife of the CS:S helper decides.
public sealed partial class SoccerModMvpPlugin
{
    private const int KickConeSegments = 48;
    private const float KickConeStep = 1.5f;      // search step along a direction, units
    private const float KickConeLift = 3.0f;      // above the floor (and the 3D grass)
    private const string KickConeTargetName = "sm2_kick_cone";
    private static readonly Color KickConeIdle = Color.FromArgb(255, 255, 255, 255);
    private static readonly Color KickConeHit = Color.FromArgb(255, 60, 255, 90);

    private sealed class KickConeView
    {
        public readonly List<CBeam> Beams = new();
        public bool Green;
    }

    private readonly Dictionary<int, KickConeView> _kickConeViews = new();

    private bool KickConeShown(CCSPlayerController player) => _kickConeViews.ContainsKey(player.Slot);

    private void SetKickConeShown(CCSPlayerController player, bool on)
    {
        if (on) { _kickConeViews.TryAdd(player.Slot, new KickConeView()); return; }
        KickConeRemove(player.Slot);
    }

    private void KickConeRemove(int slot)
    {
        if (!_kickConeViews.Remove(slot, out var view)) return;
        foreach (var beam in view.Beams) if (beam.IsValid) beam.Remove();
    }

    private void KickConeRemoveAll()
    {
        foreach (var slot in _kickConeViews.Keys.ToArray()) KickConeRemove(slot);
    }

    // Is a ball with its centre at `ball` inside the kick area of an eye at `eye` looking along
    // `forward` (yaw in radians)? The geometry part of KickGeometry, nothing else.
    private bool KickConeContains(V3 eye, V3 forward, float yaw, V3 ball) =>
        KickAreaContains(eye, forward, yaw, ball, _kickAimConeDegrees, stab: false);   // KickFeel.cs: reach + cone, or the CS:S knife area

    // Called every tick from OnTick; redraws at 32 Hz.
    private void KickConeOnTick()
    {
        if (_kickConeViews.Count == 0 || Server.TickCount % 2 != 0) return;
        foreach (var (slot, view) in _kickConeViews.ToArray())
        {
            if (Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player)
            {
                KickConeRemove(slot);
                continue;
            }
            if (player.PlayerPawn.Value is not { IsValid: true } pawn || !IsAlive(pawn) || pawn.AbsOrigin is not { } feet)
            {
                KickConeDraw(view, new List<(V3 From, V3 To)>(), V3.Zero, false);
                continue;
            }

            var eye = new V3(feet.X + pawn.ViewOffset.X, feet.Y + pawn.ViewOffset.Y, feet.Z + pawn.ViewOffset.Z);
            var pitch = pawn.EyeAngles.X * (MathF.PI / 180.0f);
            var yaw = pawn.EyeAngles.Y * (MathF.PI / 180.0f);
            var forward = new V3(MathF.Cos(pitch) * MathF.Cos(yaw), MathF.Cos(pitch) * MathF.Sin(yaw), -MathF.Sin(pitch));
            // a ball lying on the floor the player stands on
            var ballZ = feet.Z + BallCollisionRadius;
            var drawZ = feet.Z + KickConeLift;
            // far enough for either hit area (the CS:S knife reaches 48 + its box + the ball)
            var far = MathF.Max(_kickSurfaceReach, CssKnifeArea.SlashRange + 24.0f) + BallCollisionRadius;

            // per direction round the player: the nearest and the farthest kickable spot
            var inner = new float[KickConeSegments];
            var outer = new float[KickConeSegments];
            for (var i = 0; i < KickConeSegments; i++)
            {
                var a = i * (2.0f * MathF.PI / KickConeSegments);
                float dx = MathF.Cos(a), dy = MathF.Sin(a);
                inner[i] = outer[i] = -1.0f;
                for (var r = far; r >= 0.0f; r -= KickConeStep)
                {
                    if (!KickConeContains(eye, forward, yaw, new V3(feet.X + dx * r, feet.Y + dy * r, ballZ))) continue;
                    if (outer[i] < 0.0f) outer[i] = r;
                    inner[i] = r;
                }
            }

            V3 Point(int i, float r)
            {
                var a = i * (2.0f * MathF.PI / KickConeSegments);
                return new V3(feet.X + MathF.Cos(a) * r, feet.Y + MathF.Sin(a) * r, drawZ);
            }
            var lines = new List<(V3 From, V3 To)>();
            for (var i = 0; i < KickConeSegments; i++)
            {
                var j = (i + 1) % KickConeSegments;
                var here = outer[i] >= 0.0f;
                var next = outer[j] >= 0.0f;
                if (here && next)
                {
                    lines.Add((Point(i, outer[i]), Point(j, outer[j])));
                    // the near edge only where it is not at the player's feet
                    if (inner[i] > KickConeStep * 2.0f || inner[j] > KickConeStep * 2.0f) lines.Add((Point(i, inner[i]), Point(j, inner[j])));
                }
                else if (here) lines.Add((Point(i, inner[i]), Point(i, outer[i])));   // side edge of the area
                else if (next) lines.Add((Point(j, inner[j]), Point(j, outer[j])));
            }

            var green = _ball is { IsValid: true } && _ball.AbsOrigin is { } b && KickConeContains(eye, forward, yaw, N(b));
            KickConeDraw(view, lines, new V3(feet.X, feet.Y, feet.Z - 2000.0f), green);
        }
    }

    private void KickConeDraw(KickConeView view, List<(V3 From, V3 To)> lines, V3 parked, bool green)
    {
        view.Beams.RemoveAll(b => !b.IsValid);   // a round restart takes the beams with it
        var color = green ? KickConeHit : KickConeIdle;
        var recolor = green != view.Green;
        view.Green = green;
        for (var i = 0; i < Math.Max(lines.Count, view.Beams.Count); i++)
        {
            // a beam that is not needed right now waits far under the pitch
            var (from, to) = i < lines.Count ? lines[i] : (parked, parked);
            if (i >= view.Beams.Count)
            {
                var created = Utilities.CreateEntityByName<CBeam>("beam");
                if (created is not { IsValid: true }) return;
                created.Render = color; created.Width = 1.6f; created.EndWidth = 1.6f;
                created.Teleport(C(from));
                created.EndPos.X = to.X; created.EndPos.Y = to.Y; created.EndPos.Z = to.Z;
                created.DispatchSpawn();
                created.Render = color;
                created.Entity!.Name = KickConeTargetName;
                view.Beams.Add(created);
                continue;
            }
            var beam = view.Beams[i];
            beam.Teleport(C(from));
            beam.EndPos.X = to.X; beam.EndPos.Y = to.Y; beam.EndPos.Z = to.Z;
            Utilities.SetStateChanged(beam, "CBeam", "m_vecEndPos");
            if (recolor)
            {
                beam.Render = color;
                Utilities.SetStateChanged(beam, "CBaseModelEntity", "m_clrRender");
            }
        }
    }
}
