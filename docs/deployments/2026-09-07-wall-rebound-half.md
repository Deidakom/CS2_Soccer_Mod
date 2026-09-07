# Wall rebound reduced — 2026-09-07

User found the CSS-hybrid wall rebound too strong and requested a 50% reduction.
Halved minimum wall-normal retention from 0.35 to 0.175 using the existing
live command and verified the returned setting:

```text
css_sm2ball_wallassist 0 0 0.175
css_sm2ball_wallassist
```

Persisted by the plugin to `soccermod_settings.json`. No restart, DLL replacement,
map change or client control. Local source default matches the new setting for
future builds. All other tuning is unchanged, including zero added wall lift.

This halves the assisted normal rebound target, not the native tangent or any
native rebound already above that minimum. In-game feel remains user-tested.
