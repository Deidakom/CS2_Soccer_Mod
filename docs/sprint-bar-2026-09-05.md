# Compact sprint bar (local queue)

> Publication update: the owner authorized deployment and both GitHub pushes.
> This batch is deployed as 1.4.2-dev; see the [deployment report](deployments/2026-09-05-queue-update.md).
> Earlier local/hold statements below record the development history.

Implemented locally; **not deployed, committed or pushed**.

The CSS reference uses ten segments driven by actual stamina. The new CS2 bar
uses the same simple idea: `[|||||.....] 55%`. It drains while sprinting and
refills during recharge. There is no title, extra status paragraph, background
box, flashing animation or sound. Sprint timing and movement are unchanged.

The default for new preferences is **During activity**: hide at full charge,
show during sprint/recovery. Existing saved HUD choices, including Disabled,
are preserved. Enable it with `!sprintbar on`, choose `!sprintbar always` or
hide it with `!sprintbar off`. The same choices are in Sprint Settings.

## Rendering and lifecycle

The bar is a small camera-relative `point_worldtext` entity below the crosshair,
using the installed engine font and requiring no new Workshop assets. It follows
the first-person eye or the existing third-person camera. Only its owning
controller receives it. The transmission filter only removes this plugin's bar
entities from other recipients; it never forces entities into transmission.

This uses the installed API's
[CPointWorldText properties](https://docs.cssharp.dev/api/CounterStrikeSharp.API.Core.CPointWorldText.html)
and follows the framework's
[CheckTransmit removal example](https://github.com/roflmuffin/CounterStrikeSharp/blob/main/examples/WithCheckTransmit/WithCheckTransmitPlugin.cs).
It does not depend on the removed viewmodel entity, the experimental game_text
HUD, native interaction progress fields, or the menu's center-text channel.

The bar disappears while a menu is open, on death/spectating, when sprint is
suppressed for CAP, on disconnect, at round/map cleanup and on plugin unload.
Entity ownership includes the controller handle to prevent slot reuse from
showing another player's bar. Text changes are sent at most every eight ticks;
camera positioning follows the tick. The legacy sprint profile displays its
actual remaining burst/cooldown instead of an unrelated stamina value.

## Validation

The local build and 103 Node tests / 99 managed scenarios passed. Bar-specific
cases cover empty/half/full/clamped values, recharge visibility, menu/death/CAP/
disabled suppression, and camera-relative position under different pitch/yaw.
Wiring checks cover private transmission, disconnect cleanup, and removal of
the competing center-panel sprint writer.

This is **not yet client-verified**. After the queue is ready for deployment,
check text orientation, perceived size and stability while running/turning,
first-/third-person views, multiple players, menu opening/closing, death and a
round restart. Server-side geometry tests cannot prove smooth client rendering
or the final screen footprint at every resolution/FOV.
