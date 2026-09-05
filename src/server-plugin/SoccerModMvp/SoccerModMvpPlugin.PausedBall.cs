using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    private uint _pausedBallHandle;
    private Vector? _pausedBallVelocity;
    private bool _pausedBallPreviouslyFrozen;
    private void FreezeBallForPause()
    {
        if (_pausedBallHandle != 0 || _ball is not { IsValid: true } ball) return;
        _pausedBallHandle = ball.EntityHandle.Raw;
        // VPhysics reports a zero AbsVelocity on this server even while moving.
        // Use the same sampled velocity as kicks and player contacts.
        _pausedBallVelocity = new Vector(_derivedBallVelocity.X, _derivedBallVelocity.Y, _derivedBallVelocity.Z);
        _pausedBallPreviouslyFrozen = _ballMotionFrozen;
        NewBallContact(ball); // Invalidate deferred wall corrections.
        ball.AcceptInput("DisableMotion");
        _ballMotionFrozen = true;
        ResetDerivedMotion(clearTouchHistory: false);
    }
    private void ReleasePausedBall(bool restoreMotion)
    {
        if (_pausedBallHandle == 0) return;
        if (_ball is { IsValid: true } ball && ball.EntityHandle.Raw == _pausedBallHandle)
        {
            if (!_pausedBallPreviouslyFrozen)
            {
                _ballMotionFrozen = false;
                ball.AcceptInput("EnableMotion"); ball.AcceptInput("Wake");
                if (restoreMotion && _pausedBallVelocity is not null) ball.Teleport(velocity: _pausedBallVelocity);
            }
            ResetDerivedMotion(clearTouchHistory: false);
        }
        _pausedBallHandle = 0; _pausedBallVelocity = null;
    }
}
