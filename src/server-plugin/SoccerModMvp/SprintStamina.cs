namespace SoccerModMvp;

// SoMoE Sprint 2.0: 3s full drain, 1s recovery delay, 7.5s full recharge.
// Hold and Toggle feed the same state machine and therefore have equal cost.
internal sealed class SprintStamina
{
    internal float Stamina = 100;
    internal bool Active;
    internal bool Exhausted;
    internal bool RequireRelease;
    internal bool InputDown;
    internal double RegenAt;
    internal double LastUpdate = double.NaN;
    internal bool Unlimited { get; private set; }

    internal bool TryStart(double now)
    {
        if (Active || (!Unlimited && (Exhausted || Stamina <= .01f || now < RegenAt))) return false;
        Active = true; return true;
    }
    internal void Stop(double now, bool exhausted = false)
    {
        if (Active && !Unlimited) RegenAt = now + 1;
        Active = false;
        if (exhausted) { Stamina = 0; Exhausted = true; RequireRelease = true; }
    }
    internal void Input(double now, bool down, bool hold)
    {
        if (RequireRelease && !down) RequireRelease = false;
        if (down && !InputDown && !RequireRelease)
        {
            if (!hold && Active) Stop(now); else TryStart(now);
        }
        else if (hold && !down && InputDown && Active) Stop(now);
        InputDown = down;
    }
    internal void Update(double now, bool unlimited = false)
    {
        var elapsed = double.IsNaN(LastUpdate) ? 0 : Math.Clamp(now - LastUpdate, 0, .25);
        LastUpdate = now;
        if (Active && !Unlimited)
        {
            Stamina -= (float)(100 / 3.0 * elapsed);
            if (Stamina <= .001f) Stop(now, true);
        }
        else if (!Active && now >= RegenAt && Stamina < 100)
        {
            Stamina = Math.Min(100, Stamina + (float)(100 / 7.5 * elapsed));
            if (Stamina >= 99.999f) { Stamina = 100; Exhausted = false; }
        }
        if (unlimited == Unlimited) return;
        Unlimited = unlimited;
        // The box can be used even with exhausted normal stamina, but must
        // not refill it or clear exhaustion for a sprint outside the box.
        if (Unlimited && Exhausted) RequireRelease = false;
        if (!Unlimited && Active && (Exhausted || Stamina <= .01f || now < RegenAt))
        {
            Active = false;
            RequireRelease = true;
        }
    }
}
