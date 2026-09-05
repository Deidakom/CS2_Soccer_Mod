using System.Numerics;

namespace SoccerModMvp;
internal static class SprintBarView
{
    internal static string Text(float stamina)
    {
        var amount = float.IsFinite(stamina) ? Math.Clamp(stamina, 0, 100) : 0;
        var filled = Math.Clamp((int)MathF.Floor(amount / 10), 0, 10);
        return $"[{new string('|', filled)}{new string('.', 10 - filled)}] {MathF.Floor(amount):0}%";
    }
    internal static bool Visible(int mode, bool active, float stamina, bool eligible, bool menuOpen, bool suppressed)
        => eligible && !menuOpen && !suppressed && mode != 2
            && (mode == 0 || active || stamina < 99.95f);

    // Camera-local placement, below the crosshair; no world-axis drift when looking up/down.
    internal static Vector3 Position(Vector3 eye, float pitch, float yaw)
    {
        var p = pitch * MathF.PI / 180; var y = yaw * MathF.PI / 180;
        var forward = new Vector3(MathF.Cos(p) * MathF.Cos(y), MathF.Cos(p) * MathF.Sin(y), -MathF.Sin(p));
        var up = new Vector3(MathF.Sin(p) * MathF.Cos(y), MathF.Sin(p) * MathF.Sin(y), MathF.Cos(p));
        return eye + forward * 10 - up * 3.5f;
    }
}
