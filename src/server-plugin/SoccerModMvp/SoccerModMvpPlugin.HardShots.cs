using CounterStrikeSharp.API.Core;

namespace SoccerModMvp;

// 2026-09-26 player feedback via the owner: "hitting is so easy compared to
// CS:S that the goalkeeper reaches every ball". The easy saves come from
// hard shots: a 50 degree half-cone and up to 100 ms of lag compensation (a
// ball at 1600 u/s travels ~160 units in 100 ms, so a keeper could still hit
// a shot that had practically passed him). The owner chose "step 2": the
// hit window shrinks only for fast balls - from HardShotStartSpeed the aim
// cone narrows linearly to hardShotConeScale and the lag compensation to
// hardShotLagCompensationMs at HardShotFullSpeed. Dribbling, passes and slow
// balls feel exactly as before. Both ends are Ball workbench dials.
public sealed partial class SoccerModMvpPlugin
{
    private const float HardShotStartSpeed = 1000f;
    private const float HardShotFullSpeed = 2000f;
    private const float DefaultHardShotConeScale = 0.67f;
    private const float DefaultHardShotLagCompensationMs = 30f;
    private float _hardShotConeScale = DefaultHardShotConeScale;
    private float _hardShotLagCompensationMs = DefaultHardShotLagCompensationMs;

    // 0 for a ball under HardShotStartSpeed, 1 from HardShotFullSpeed on.
    private float HardShotFactor(CPhysicsPropMultiplayer ball)
    {
        if (_ball is not { IsValid: true } matchBall || ball.Index != matchBall.Index) return 0f;
        var speed = VectorSpeed(_derivedBallVelocity);
        return Math.Clamp((speed - HardShotStartSpeed) / (HardShotFullSpeed - HardShotStartSpeed), 0f, 1f);
    }

    private float HardShotConeDegrees(CPhysicsPropMultiplayer? ball) =>
        ball is null ? _kickAimConeDegrees : _kickAimConeDegrees * (1f - (1f - _hardShotConeScale) * HardShotFactor(ball));

    private float HardShotLagCompensationMs(CPhysicsPropMultiplayer ball)
    {
        var limit = MathF.Min(_kickLagCompensationMs, _hardShotLagCompensationMs);
        return _kickLagCompensationMs - (_kickLagCompensationMs - limit) * HardShotFactor(ball);
    }
}
