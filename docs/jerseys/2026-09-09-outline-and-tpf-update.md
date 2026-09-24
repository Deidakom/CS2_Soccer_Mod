# Jersey outline and front-inspection update — September 9, 2026

This revision keeps the approved static jersey setup and changes only the
requested Away text treatment plus the server-side inspection command.

## Texture correction

- Blue Away field jersey: fixed `6` and `Away` use black fill with white
  borders.
- White Away goalkeeper jersey: fixed `1` and `Away` use white fill with
  black borders.
- Home textures and the restored belt geometry are unchanged.
- Dynamic jersey rendering remains disabled; the fixed textures are the
  source of the displayed numbers and labels.

The package is intentionally minimal: compared with the preceding live VPK,
only these two compiled body textures differ:

```text
materials/soccermod/kits/tm_leet_v2_body_variantb_color_png_6c16c1b4.vtex_c
materials/soccermod/kits/tm_leet_v2_body_variantb_gkaway_color_png_53a6157d.vtex_c
```

## `!tpf`

`!tpf` (console command `css_tpf`) toggles a front-facing third-person
inspection camera. The camera sits in front of the player, aims back at the
player's eyes, follows movement smoothly, and uses the same spawn,
disconnect, and cleanup lifecycle as `!tp`. It does not remove or replace
weapons. `!tp` remains the normal rear third-person toggle.

## Verified package

```text
artifacts/kits-jersey-outline-tpf-upload/3797479770_dir.vpk
size: 33,900,554 bytes
sha256: DC1A0AD8F09AAAC06A06D82E27129DE752571253F88D700D100E2B6ED3162675
```

Source2Viewer VPK verification passed. The corresponding Workshop update
manifest is `workshop-jersey-outline-tpf-update.vdf`.
