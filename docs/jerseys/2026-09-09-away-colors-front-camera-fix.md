# Away color and front-camera fix — September 9, 2026

This update supersedes the previous outline/front-inspection package while
leaving the static jersey setup and disabled dynamic renderer unchanged.

## Texture correction

- Blue Away field jersey: fixed `6` and `Away` are white with black borders.
- White Away goalkeeper jersey: fixed `1` and `Away` are blue with white
  borders.
- Home textures, front numbers, Adidas marks, purple/black Home GK colors,
  white Home GK gloves, and the approved belt geometry are unchanged.

Only the two compiled Away body textures are replaced in the minimal Workshop
package:

```text
materials/soccermod/kits/tm_leet_v2_body_variantb_color_png_6c16c1b4.vtex_c
materials/soccermod/kits/tm_leet_v2_body_variantb_gkaway_color_png_53a6157d.vtex_c
```

## `!tpf` front inspection

`!tpf` (console command `css_tpf`) toggles the front-facing third-person
inspection camera. The camera is placed on the character's chest-facing side
using the pawn's body yaw, then aimed at a torso target so the player's chest
and front jersey are visible. It follows movement smoothly and shares the
existing `!tp` spawn, disconnect, and cleanup lifecycle. Use `!tpf` again to
disable it; `!tp` remains the normal rear camera.

The camera prop stays model-less and non-rendering. It does not remove,
replace, or hide the player's weapons or other equipment.

## Installation

Follow [INSTALLATION.md](INSTALLATION.md). After Workshop approval, restart
CS2 or reconnect so Steam downloads the new VPK, then use `!tpf` in-game to
inspect the front of the model.

The final local VPK is 33,898,029 bytes with SHA-256
`6170c2eb6ed5cfddd7fd570956aeed7053c8800e7e75f7d3dfce2bd2744a26e6`.
It passed Source2Viewer verification and an independent raw-entry comparison
confirmed that exactly the two listed texture entries differ from the last
approved VPK. The Workshop publish/download result is recorded with the
deployment notes.
