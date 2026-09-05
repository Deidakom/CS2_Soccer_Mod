# Ball workbench — local development queue

> Publication update: the owner authorized deployment and both GitHub pushes.
> This batch is deployed as 1.4.2-dev; see the [deployment report](deployments/2026-09-05-queue-update.md).
> Earlier local/hold statements below record the development history.

Status: **1.4.2-dev, not deployed, committed or pushed**. The owner asked to finish
work on the queue before deployment. The German server remains on the earlier
1.4.1-dev build documented in [the menu queue](menu-queue-2026-09-05.md).
Original custom sounds, jerseys, grass and prop artwork remain deferred.

## In-game entry point

`!menu → Admin → Ball` (root permission required). The numbered menu paginates
through the categories. Each numeric setting offers smaller/larger steps and
an exact chat entry. `cancel` or `!cancel` aborts input; zero is a real setting
where the displayed range permits it. Changes save immediately, recheck the
actor's permission and undo their in-memory change if persistence fails.

The workbench exposes **46 numeric controls**:

- Kick power: base impulse, speed limit, left/right click, crouched left/right
  click, volley scale, surface reach, aim half-cone and cooldown.
- Lift and soft passes: overhead bonus, elevation sensitivity, aim-below-ball
  start/full/minimum power and look-down start/full/minimum power.
- Dribbling and player impact: body push ratio/limit, impact speed threshold,
  player push ratio/limit, falling threshold and bounce restitution, horizontal
  retention and vertical limit.
- Walls and settling: wall lift conversion/limit/normal retention, settling
  speed threshold and tick count.
- Engine physics: mass scale, friction, elasticity, gravity and native spin.
- Kickoff position: X and Y within the stadium's supported central area.
- Optional handling: creative curve strength/duration and first-touch window/
  retained momentum; legacy wall-pop probability/upward/sideways speed.

Separate switches control wall assist, settling, player impact and impact
feedback. Sound choices include existing event names, off and a validated custom
sound-event name. An entered name must already be installed to produce sound.

Existing tuning defaults are preserved. The creative controls only affect the
creative profile, and wall-pop controls only affect legacy. Lower first-touch
retention means stronger cushioning; players arm it with `css_ball_trap`.
The engine's native spin bridge remains unacknowledged. Mass, friction and
elasticity are engine requests whose actual effect requires live measurement.
Model/collision size stays tied to the verified Workshop asset; no decorative
size control pretends to change the collision hull.

## Presets and rollback

Named presets store the 46 values, four switches and kick sound, with a maximum
of 24 presets. Names are bounded and cannot overwrite existing presets. Loading
has a separate confirmation screen and a saved/current value review; deletion
also requires confirmation. Malformed or incompatible presets are rejected.

On first load, the plugin saves a protected **Before workbench** preset if the
current tuning passes validation and the preset file can be written. This is a
checkpoint of the owner's actual tuning, not a new recommended physics profile.
The last ten successful workbench edits/loads/restores can be undone during the
current plugin session. The established-defaults option preserves the previous
Ball menu's subset of defaults and can also be undone.

**Handling profile is explicitly separate from presets and undo**, preserving
its existing independent configuration file. Presets also exclude menu rendering,
landing-sound filtering, administrative data and match settings.

Numeric tuning and switches stay in `soccermod_settings.json`. Version 3 saves
valid zero values instead of silently restoring positive defaults on restart.
New fields are nullable for backward-compatible loading. Named presets live in
`soccermod_ball_presets.json`; a later binary/settings rollback retains that
sidecar. Existing installer backups capture the complete plugin directory and
restore the previous binary and gameplay settings.

Root/server console diagnostics:

```text
css_sm2ball_tune
css_sm2ball_tune kickCooldownSeconds 0.48
css_sm2ball_undo
```

## Live controls and validation limits

Warmup-only main-ball controls freeze/resume motion, stop momentum, reset to
kickoff or place the ball on the pitch under the crosshair. They are blocked
while a match or CAP is active. Crosshair placement uses the calibrated pitch
height and rejects locations close to the stadium walls or goal lines.
The menu also displays position and measured speed.

Local managed tests exercise production validation, independent snapshots,
all 46 settings through save/reload, and restoration of runtime values after a
failed write. Invalid numbers, fractional tick counts, invalid sound syntax,
unknown keys and inverted soft-pass ranges are rejected.

No new runtime or client-visible behavior has been verified on the German
server yet. The remaining integration checks include numbered-menu input,
manual placement/freezing, repeated preset/undo use, and comparison of the
optional handling controls during play. Group CAP/ready/dead-chat checks from
the earlier queue remain open. These are not presented as completed by local
unit tests.

## Local check results

- CS2: 100 Node tests, 15 Python tests and 87 managed scenarios passed.
  The workbench cases round-trip every one of the 46 numeric controls.
- CS:S: 12 Node tests and 10 Python tests passed. The earlier queue's eight
  SourcePawn plugins were already compiled successfully; this Ball workbench
  adds no CS:S source changes.
- Both repositories passed `git diff --check`.
- Ball-workbench snapshot DLL SHA-256 (before subsequent kickoff/sprint fixes): `bfecbf7a244455e4a75afd128fad0fb30b1b969d0228373f162a0808cc13677c`.

Deployment, commits and pushes remain on hold. The local implementation queue
is recorded separately from the outstanding in-game/group validation and the
owner-deferred asset work.
