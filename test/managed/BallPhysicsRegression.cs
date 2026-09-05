using SoccerModMvp;
using System.Numerics;

internal static class BallPhysicsRegression
{
    internal static void Run()
    {
        var bottom = new Vector3(0, 0, 16);
        var top = new Vector3(0, 0, 56);
        const float radius = 34.805f;
        void Check(bool condition, string error) { if (!condition) throw new Exception(error); }
        var vertical = BallContactMath.SweepCapsule(new(0, 0, 150), new(0, 0, 40), bottom, top, radius);
        Check(vertical is { } v && v.Normal.Z > .99f && Math.Abs(v.Fraction - (150 - 56 - radius) / 110) < .0001f,
            "Pure vertical header must hit the top at the analytic entry time.");
        var crossing = BallContactMath.SweepCapsule(new(-100, 0, 40), new(100, 0, 40), bottom, top, radius);
        Check(crossing is { } c && c.Normal.X < -.99f && c.Fraction < .5,
            "Crossing beyond the player's centre must still enter from the incoming side.");
        Check(BallContactMath.SweepCapsule(new(-100, 36, 40), new(100, 36, 40), bottom, top, radius) is null,
            "Near miss must not become a hit.");
        Check(BallContactMath.SweepCapsule(new(-100, 0, 105), new(100, 0, 105), bottom, top, radius) is null,
            "Ball above the actual pawn hull must not hit.");
        Check(BallContactMath.SweepCapsule(new(-100, 0, 80), new(100, 0, 80), bottom, new(0,0,20), radius) is null,
            "Crouching must let a high ball clear the shorter capsule.");
        var first = BallContactMath.SweepCapsule(new(-100,0,40),new(200,0,40),bottom,top,radius);
        var later = BallContactMath.SweepCapsule(new(-100,0,40),new(200,0,40),bottom + new Vector3(100,0,0),top + new Vector3(100,0,0),radius);
        Check(first!.Value.Fraction < later!.Value.Fraction, "Front player must have the earlier contact.");
        var overlap = BallContactMath.SweepCapsule(new(30,0,40),new(20,0,40),bottom,top,radius);
        Check(overlap is { Fraction: 0 } && overlap.Value.Normal.X > .99f, "Initial overlap must preserve the closing normal.");
        var incoming = new Vector3(12, -7, 30);
        var pushes = new[] { (2, new Vector3(0,135,0)), (1, new Vector3(135,0,0)) };
        var combined = BallContactMath.CombinePushes(incoming, pushes);
        Check(combined == BallContactMath.CombinePushes(incoming, pushes.Reverse()) && combined.Z == 30,
            "Simultaneous dribble must be order independent and retain vertical momentum.");
        Check(BallContactMath.CombinePushes(Vector3.Zero, new[]{(1,new Vector3(135,0,0)),(2,new Vector3(135,0,0))}).X == 135,
            "Two aligned pushers must not double the ball impulse.");
        var separation = BallContactMath.Separate(new(2, 150, -47), Vector3.UnitX, 50);
        Check(separation == new Vector3(50,150,-47), "Wall separation must retain tangential velocity and gravity.");
        Check(BallContactMath.Separate(new(100,150,-47),Vector3.UnitX,50) == new Vector3(100,150,-47),
            "Wall correction must never reduce an already sufficient rebound.");
        var side = BallContactMath.ContactSide(new(0,10,0),0,20);
        Check(side == -.5f && BallContactMath.ContactSide(new(0,-10,0),0,20) == -side,
            "Mirrored contact offsets must produce mirrored curve directions.");
        var shot = new Vector3(1000,0,100);
        var curved = BallContactMath.CurveStep(shot,1,1f/64);
        var mirrored = BallContactMath.CurveStep(shot,-1,1f/64);
        Check(Math.Abs(curved.Length() - shot.Length()) < .001f && curved.Z == 100 && curved.Y == -mirrored.Y,
            "Optional curve must conserve speed and mirror without changing vertical motion.");
        Check(BallContactMath.CurveStep(shot,0,1f/64) == shot, "Centre strikes must not curve.");
        Console.WriteLine("Production ball physics regression checks passed (14 scenarios).");
    }
}
