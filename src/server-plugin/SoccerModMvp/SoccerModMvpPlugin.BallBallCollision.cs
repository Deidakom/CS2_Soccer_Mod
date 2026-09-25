using CounterStrikeSharp.API;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;

// 2026-09-25 owner (screenshot of the match ball and a training ball stuck
// inside each other): balls must not pass through each other. Every ball is
// in the player collision group (ApplyBallCollisionGroup, tuned for body
// contact), and that group does not collide with itself, so the engine lets
// two balls overlap. Changing the group would change the tuned ball-player
// physics; instead two overlapping balls are separated here and bounce off
// each other like two equal footballs.
public sealed partial class SoccerModMvpPlugin
{
    private const float BallBallRestitution = 0.7f;

    private void UpdateBallBallCollisions()
    {
        if (_trainingBalls.Count == 0 || _pausedBallHandle != 0) return;
        var balls = PlayableBalls().ToArray();
        if (balls.Length < 2) return;
        var minDistance = BallCollisionRadius * 2;
        for (var i = 0; i < balls.Length; i++)
        for (var j = i + 1; j < balls.Length; j++)
        {
            var a = balls[i];
            var b = balls[j];
            var delta = N(b.Origin) - N(a.Origin);
            var distance = delta.Length();
            if (distance >= minDistance) continue;
            var normal = distance > 0.01f ? delta / distance : V3.UnitX;
            var (velocityA, velocityB, push) = BallContactMath.BallBallResponse(
                N(a.Inherited), N(b.Inherited), normal, distance, minDistance, BallBallRestitution);
            a.Ball.AcceptInput("Wake");
            b.Ball.AcceptInput("Wake");
            a.Ball.Teleport(position: C(N(a.Origin) - normal * push), velocity: C(velocityA));
            b.Ball.Teleport(position: C(N(b.Origin) + normal * push), velocity: C(velocityB));
        }
    }
}
