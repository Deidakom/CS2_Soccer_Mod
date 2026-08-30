# Session handoff (2026-08-29, end of day)

Self-contained. A fresh session (any model) can pick this up without any
prior conversation context. Prior docs, still valid and referenced below:

- `docs/ball-foundation/2026-08-29-hull-compiler-root-cause.md` — why the
  ball was lopsided, the compound-hull fix, measured physics values.
- `docs/ball-foundation/2026-08-29-implementation-plan.md` — the spin
  investigation plan and its §9/§10 execution log (Phase A/B/C outcome).

This file covers everything from the rest of the same day that isn't in
those two yet: three more user-reported issues and their fixes, and the
native-plugin build (built, loaded, stable, but blocked on a stale
signature).

## 1. Live server state right now

- Service `cs2-soccermod-test.service`: active.
- Ball model: `large1850` (CSF map size, 37.61u diameter — user's explicit
  choice over the true XSL 28.96u).
- Kick: `delta 1800 u/s, clamp 3500 u/s` (CS:S measured 1359, but this ball
  is 30% larger and sheds speed faster, tunable via `css_sm2ball_power`).
- Wall assist: `enabled=True ratio=0.129 maxAdded=200.0` — this is what
  currently delivers the CS:S "hochbuggen" wall hop (see plan doc §9,
  no native spin needed for this part).
- Ball collision group: `0` (was `20`/PUSHAWAY — non-solid to players; fixed
  today, see §2 below).
- A native Metamod plugin (`soccermod_native`) is built and loaded on the
  live server, but its write path is currently inert (safe no-op) — see §4.

## 2. Three new user-reported issues fixed today

All three are already built, deployed, and live.

### 2a. Ball rolled through players
**Cause:** the ball spawned in Source collision group 20
(`COLLISION_GROUP_PUSHAWAY`), which is explicitly non-solid to players in
the engine — it gets pushed but never truly collides.
**Fix:** `ApplyBallCollisionGroup` in
`src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs` sets the ball's
`Collision.CollisionGroup` to `0` (solid to everything). Tunable live:
`css_sm2ball_collision <groupIndex|-1>`.

### 2b. Ball touch could hurt/kill players
**Cause:** side effect of 2a — once the ball became genuinely solid, CS2's
default physics-impact damage kicked in for a fast ~60kg MOVETYPE_VPHYSICS
prop hitting a player hitbox.
**Fix:** registered `Listeners.OnPlayerTakeDamagePre` (delegate signature
`(CCSPlayerPawn, CTakeDamageInfo) -> HookResult`, discovered via IL
inspection of `CounterStrikeSharp.API.dll` — not documented anywhere, it's
a real member of `CounterStrikeSharp.API.Core.Listeners`). Handler
`OnPlayerTakeDamagePre` compares `info.Inflictor.Value.Index` against the
ball's index and returns `HookResult.Stop` to block only ball-caused
damage; everything else passes through untouched. Push/bounce off the ball
is unaffected — only the HP loss was blocked.

### 2c. Ball never fully settles at rest (root-caused, partially fixed, one
open architectural trade-off)
Three layers were found, in order of discovery:

1. **Self-inflicted, now fixed:** `ApplyBallCollisionGroup` called
   `ball.AcceptInput("Wake")` unconditionally every time it ran.
   `EnsureBallFoundation` (which calls it) runs every ~1s from `OnTick`'s
   maintenance branch, for as long as the ball exists — so the ball was
   being force-woken once a second, forever, and could never complete a
   sleep cycle. Fixed: only writes+wakes on an actual group change.
2. **Same class of bug, also fixed:** `ApplyGameplayPhysicsProfile`
   unconditionally reassigned `MassScale`/`Friction`/`Elasticity`/
   `GravityScale` every maintenance tick even when the values hadn't
   changed. CS2's physics-prop property setters appear to nudge/re-simulate
   the Rubikon body on ANY write, even a no-op one. Fixed: every field is
   now guarded with an equality check before writing.
3. **NOT fixed — open architectural trade-off, needs the user's call.**
   After both fixes, the ball still doesn't come to a hard stop. Measured
   directly: after settling onto a facet of the compound hull, it coasts at
   a small but genuinely constant velocity (~5.4 u/s, confirmed constant
   for 4+ straight seconds via `css_sm2ball_status` polling) before
   eventually changing direction. This is NOT a bug or feedback loop
   (audited every use of `_derivedBallVelocity` — nothing writes it back to
   the ball outside the kick path). It is a direct consequence of the
   **zero linear/angular damping** the whole compound-hull architecture
   deliberately uses (root-cause doc, "The decay curve" section): CS:S's
   own reference log never fully stops either (still ~28-32 u/s at 19s in
   `artifacts/css-reference/roll-seq1.log`). Our low-speed floor is lower
   (~5 u/s vs CS:S's ~30 u/s) but with zero damping it never decays to
   true zero.
   **Decision needed from the user:** add a hard low-speed sleep/deadband
   (snap velocity to zero below some small threshold, e.g. 8-10 u/s,
   touching ONLY that regime) for a harder, more game-conventional stop —
   this would trade a small amount of reference-accuracy for a "feels
   right" resting ball. Was asked, not yet answered as of this handoff.

## 3. Also open, unresolved, needs the user

**Ball looks like it's floating above the ground / shadow looks wrong.**
Raised by the user, not yet diagnosed. Hypothesis (unverified): the visual
`prop_dynamic` (Jabulani model) is scaled at runtime via
`ComputeBallVisualScale()` (`sceneNode.Scale`/`ClientLocalScale`) to match
whatever collision model is active; Source engine's shadow system for
`prop_dynamic` does not always follow a runtime scale change, which could
produce exactly this symptom. Cannot be diagnosed further without a
screenshot — the agent has no way to see the client rendering. **Ask the
user for a screenshot before touching this.**

## 4. Native plugin: built, loaded, stable — blocked on a stale signature

### Why it exists
CounterStrikeSharp's `CEntityInstance.AcceptInput` only accepts a **string**
value (confirmed via IL metadata inspection of
`CounterStrikeSharp.API.dll`: `AcceptInput(String, CEntityInstance,
CEntityInstance, String, Int32)`). The real engine inputs
`ApplyAbsVelocityImpulse` / `ApplyLocalAngularVelocityImpulse` need a typed
`variant_t` carrying a `Vector` (`FIELD_VECTOR`) — a string can't produce
that. This is the actual, root reason no spin has been achievable all day
(see the plan doc §9: direct AcceptInput calls were silent no-ops, and
`phys_thruster`'s force+torque combo was too nonlinear/unreliable to trust
in gameplay). Fixing it needs native code that can call the real
`CEntityInstance::AcceptInput(variant_t*)`, which a C# CounterStrikeSharp
plugin structurally cannot do.

### What was built
A minimal Metamod:Source 2 plugin, `soccermod_native`, source in
`/root/native-build/soccermod_native/src/` **on the server itself** (not
yet copied back into the git repo — see §6 TODO). Two files:

- `plugin.cpp` — resolves `CEntityInstance::AcceptInput`'s real address via
  a byte-signature scan of `libserver.so` at plugin Load(), then exposes
  three RCON `CON_COMMAND_F`s:
  - `sm2_native_selftest <hexPointer>` — read-only: reinterprets the
    pointer as `CEntityInstance*` and prints its classname. **Always run
    this before trusting a new pointer value.**
  - `sm2_native_impulse <hexPointer> <x> <y> <z>` — fires
    `ApplyAbsVelocityImpulse` with a typed Vector.
  - `sm2_native_angular_impulse <hexPointer> <x> <y> <z>` — fires
    `ApplyLocalAngularVelocityImpulse` with a typed Vector.
- `sigscan.h` — a small, self-written (not vendored) `dl_iterate_phdr`-based
  byte-pattern scanner. No external dependency.

**Key design decision, already learned the hard way — do not redo this
work:** entity resolution does NOT go through
`CGameEntitySystem::GetEntityIdentity()`/`GetEntityInstance(CEntityIndex)`.
That symbol is not dynamically exported by `libserver.so` (verified with
`nm -D`), so a native plugin cannot link against it, and reaching it would
require vendoring CS2Fixes' full gamedata+signature infrastructure (an
`IGameResourceService` interface pointer plus a second signature-scanned
offset) for no real benefit. Instead: the C# side already holds a valid
native pointer for the ball on `_ball.Handle` (an `IntPtr`; `CEntityInstance`
inherits it through `NativeEntity`/`NativeObject`). C# passes that pointer's
numeric value straight through as a plain integer; native reinterprets it
directly. Zero extra gamedata dependency for entity resolution, only the one
signature for `AcceptInput` itself is needed.

New C# command added to support this: `css_sm2ball_native_handle` — prints
`_ball.Handle` as a decimal integer (from
`OnBallNativeHandleCommand`/`OnBallImpulseInputCommand` region in
`SoccerModMvpPlugin.cs`). Convert to hex before passing to the native
commands (e.g. `printf "%x\n" <decimal>` — or just have the AI agent do it).

### Provenance (GPLv3 — private-server use only, flag before any public
release)
The **one** signature actually used —
`"CEntityInstance_AcceptInput"` (linux):
`55 48 89 E5 41 56 49 89 FE 41 55 48 8D 7D`
— is sourced from `Source2ZE/CS2Fixes` (github.com/Source2ZE/CS2Fixes,
GPLv3), specifically `gamedata/cs2fixes.jsonc`. Not independently derived.
GPLv3 obligations are about distribution; running this on the user's own
private test server is not distribution and is fine, but if this project is
ever published/redistributed, this specific piece would need to either be
re-licensed under GPLv3-compatible terms or independently re-derived. Flag
this to the user explicitly if that question ever comes up — don't silently
decide it.

### Confirmed working, live, tonight
```
meta load addons/soccermod_native/bin/linuxsteamrt64/soccermod_native
  -> "Plugin ... is already loaded as 2." (loads cleanly, stable, no crash)
css_sm2ball_native_handle
  -> [SM2DIAG] ball native handle: 5746757857280   (example value, changes every ball respawn)
sm2_native_selftest 53a0575f000   (hex of the above)
  -> [SM2NATIVE] selftest OK: pointer=0x53a0575f000 classname="prop_physics_multiplayer"
```
Pointer resolution is proven correct end-to-end.

### The one thing that's blocked
```
sm2_native_impulse 53a04530000 500 0 0
  -> [SM2NATIVE] ApplyAbsVelocityImpulse FAILED: CEntityInstance_AcceptInput was not resolved at load.
```
The signature scan finds `libserver.so`'s executable ranges fine, but the
specific byte pattern isn't found anywhere in it. Root cause, confirmed by
date comparison, not guessed:
- Live server binary: `/home/gameserver/cs2/game/csgo/bin/linuxsteamrt64/libserver.so`,
  mtime **2026-08-28 08:02 UTC**, CS2 patch version `1.41.7.8` (from
  `game/csgo/steam.inf`).
- CS2Fixes' gamedata last touched **2026-08-24** (commit message: "Update
  signatures for 2026-08-24 CS2 update"), confirmed still current as of a
  fresh `git fetch` this session — no newer commit exists upstream yet.

Valve shipped a CS2 update between Aug 24 and Aug 28 that CS2Fixes hasn't
published a matching signature for yet. This is exactly the maintenance-
burden risk flagged from the start of the spin investigation (see plan
doc §2, "the mechanism is real ... but not controllable enough").

**The failure mode is safe.** `g_fnAcceptInput` stays null when the scan
fails; every command checks it and refuses with a clear log line rather
than dereferencing a null/wrong pointer. No crash risk from this state.

### User's decision (as of this handoff): wait
Given the choice between (a) wait for CS2Fixes to publish an updated
signature — historically fast, days not weeks, and then it's a five-minute
fix to paste in the new byte pattern and rebuild — or (b) reverse-engineer
a fresh signature now (real disassembly work, meaningful time cost, real
crash-risk-on-live-server per test iteration since each test needs the
user's manual `meta load`/reload — the Claude Code safety classifier
blocks the agent from running `meta load` itself, twice, on two different
phrasings; this is a deliberate, repeated block on loading native code into
a live process, not a fluke — **do not try to route around it via a
different phrasing or a service restart trick; ask the user to run it, or
ask before trying anything creative**), **the user chose (a): wait.**

### To resume once a fresh signature is available
1. Check for an update: `cd /root/native-build/CS2Fixes && git fetch -q
   origin && git log -1 --format="%ai %s" -- gamedata/cs2fixes.jsonc` — if
   the date has moved past `2026-08-24`, there's a new one.
2. Extract the new `"CEntityInstance_AcceptInput"` → `"linux"` byte string
   from `gamedata/cs2fixes.jsonc`.
3. Edit `/root/native-build/soccermod_native/src/plugin.cpp`, replace the
   pattern string passed to `sm2native::FindPattern`.
4. Rebuild:
   ```
   cd /root/native-build/soccermod_native/build
   /root/native-build/venv/bin/ambuild
   ```
5. Copy the new `.so` over the live one (the game files, NOT the build
   directory):
   ```
   cp /root/native-build/soccermod_native/build/package/cs2/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so \
      /home/gameserver/cs2/game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
   chown gameserver:gameserver /home/gameserver/cs2/game/csgo/addons/soccermod_native/bin/linuxsteamrt64/soccermod_native.so
   ```
6. **Ask the user to run** (agent is blocked from this specific action):
   `meta unload sm2native` then `meta load
   addons/soccermod_native/bin/linuxsteamrt64/soccermod_native` (or just
   restart the service, which will pick it up automatically via the
   already-installed `addons/metamod/soccermod_native.vdf`).
7. Re-run the selftest + a small impulse test (as in "Confirmed working"
   above) to verify before wiring it into the real kick.
8. Once `sm2_native_impulse`/`sm2_native_angular_impulse` both work: follow
   Phase B/C in `docs/ball-foundation/2026-08-29-implementation-plan.md`
   (kick with topspin, then the wallspin trial to see if Rubikon converts
   spin into the hop natively — the wall-assist fallback already covers T4
   regardless, so this would be purely for T5, the rolling/kicked-ball
   sideways curve).

### TODO housekeeping (not urgent, note for later)
The native plugin's full source tree currently lives only on the server at
`/root/native-build/soccermod_native/`, `/root/native-build/hl2sdk-cs2/`,
`/root/native-build/metamod-source/`, etc. — not committed to the git repo.
Worth pulling `src/plugin.cpp`, `src/plugin.h`, `src/sigscan.h` back into
the repo (e.g. `src/native-plugin/soccermod_native/`) at some point so it
isn't only reachable by SSHing into the box. Low priority while the plugin
is still non-functional pending the signature update.

## 5. Full current RCON command reference

Existing (from before today): `css_sm2ball_status`, `_model`, `_power`,
`_trial roll|wall|drop [speed] [wall: startYOffset]`, `_reset_center`,
`_impulse`, `_physics`, `_kickmode`, `_thrust`, `_torque_test`,
`_spin_isolate`, `_trace_arena`, `_replace_test`, `_restore_map`,
`css_sm2knife_give`, `css_sm2inventory_status`, `_impulse_input`,
`_spin_input` (Phase A probes, confirmed dead — direct AcceptInput calls
are silent no-ops in this CS2 build, kept only as harmless probes in case a
future patch changes this).

New today: `css_sm2ball_collision <group|-1>`, `css_sm2ball_wallassist
<on|off|ratio> [maxAdded]`, `css_sm2ball_native_handle`.

Native plugin (separate binary, `sm2native` alias): `sm2_native_selftest
<hexPointer>`, `sm2_native_impulse <hexPointer> <x> <y> <z>`,
`sm2_native_angular_impulse <hexPointer> <x> <y> <z>` — the latter two
currently inert pending the signature update (see §4).

## 6. Infrastructure quick reference (unchanged from the plan doc, repeated
here for a fully self-contained handoff)

- Repo: `C:\Users\sergi\Documents\ChatGPT\Privat\cs2-soccermod`. Plugin:
  `src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.cs` (assembly
  `SoccerModNativeHull.dll`, namespace still `SoccerModMvp` — don't
  refactor yet).
- Build (portable SDK, no system dotnet):
  ```
  DOTNET_CLI_HOME=C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-home
  NUGET_PACKAGES=C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\nuget-packages
  C:\Users\sergi\Documents\ChatGPT\Privat\.codex-tmp\dotnet-sdk\dotnet.exe build
    src\server-plugin\SoccerModMvp\SoccerModMvp.csproj -c Release --no-restore
  ```
- Deploy (one command, key-auth via `~/.ssh/config` host `cs2-soccermod`,
  which is in the user's REAL Windows profile at `C:\Users\sergi\.ssh\`, not
  sandboxed — the user can also just run `ssh cs2-soccermod` themselves):
  `SOCCERMOD_HOST=cs2-soccermod bash deploy/testserver/push-ball-build.sh`
  — backs up, installs model+DLL, restarts the service, prints plugin load
  lines. Never pipe a tar stream alongside a `bash -s` heredoc in that
  script — stdin collides; it already avoids this by base64-embedding
  payloads.
- RCON from the server itself: `ssh cs2-soccermod '/root/rcon "<command>"'`
  (`/root/rcon` sources the password from `/etc/cs2-soccermod-test.env`;
  the secret never appears on a command line).
- Telemetry: `ssh cs2-soccermod "journalctl -u cs2-soccermod-test.service
  --since '10 min ago' --no-pager | grep SM2CSSREF"` (physics trials) or
  `grep SM2DIAG` (general) or `grep SM2NATIVE` (native plugin).
- Native plugin build environment (all on the server, not local):
  `/root/native-build/` contains `hl2sdk-cs2/` (alliedmodders hl2sdk, `cs2`
  branch), `metamod-source/` (with `hl2sdk-manifests` submodule already
  populated), `venv/` (Python venv with AMBuild installed from source —
  PyPI has no `ambuild` package, must `pip install` from
  `github.com/alliedmodders/ambuild`), `CS2Fixes/` (reference/gamedata
  source, GPLv3), `soccermod_native/` (our plugin, copied from
  `metamod-source/samples/s2_sample_mm` and stripped down). Toolchain:
  g++ 12.4, cmake, ninja, lld (`apt install lld` — required, `ld.lld` link
  flag is used). Build:
  ```
  cd /root/native-build/soccermod_native/build
  /root/native-build/venv/bin/python ../configure.py \
    --hl2sdk-root=/root/native-build \
    --hl2sdk-manifests=/root/native-build/metamod-source/hl2sdk-manifests \
    --mms_path=/root/native-build/metamod-source --sdks=cs2 --enable-optimize
  /root/native-build/venv/bin/ambuild
  ```
- Model compile (local Windows, then push the `.vmdl_c`): content at
  `E:\SteamLibrary\...\content\csgo_addons\soccermod_phase1\models\soccermod\`,
  compile with `resourcecompiler.exe -f -nop4 -game <game/csgo> <vmdl>`,
  verify with
  `.local/tools/valve-resource-format-20.0/cli/Source2Viewer-CLI.exe`.
  Hull generator: `tools/make-ball-hull.cs` (`dotnet run`).

## 7. Non-negotiables, still in force

Left-click knife kick only, body push (now genuinely solid, see §2a), wall
hochbuggen (working, via wall-assist not spin), no lob/menu mechanics, no
return to the v2 analytic position controller, ball must not damage/kill
players (fixed today, §2b). Match/CAP/goal/score work stays paused until
the ball itself is accepted.
