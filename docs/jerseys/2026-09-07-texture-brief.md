# Football kit texture brief (for Codex)

Paint 8 PNGs: the `TextureColor` (diffuse/albedo) map for each of 4 kits x
2 body regions. Everything else (normal maps, AO, roughness, metalness,
cloth, rim mask) stays the untouched stock CS2 texture — do not generate or
touch those; the addon already ships copies of them unchanged.

## Files to produce

All at `docs/jerseys/refs/` (UV reference PNGs are provided there, extracted
directly from the game files — see below). Output at the **same pixel
dimensions as the reference**, sRGB PNG, no alpha needed (opaque).

| Kit | Body texture (jersey, 2048x2048) | Legs texture (shorts/socks/boots, 1024x1024) |
|---|---|---|
| Home | `home_body_color.png` | `home_legs_color.png` |
| Away | `away_body_color.png` | `away_legs_color.png` |
| GK Home | `gkhome_body_color.png` | `gkhome_legs_color.png` |
| GK Away | `gkaway_body_color.png` | `gkaway_legs_color.png` |

UV reference (paint directly over these, they show the seams/UV layout of
the exact mesh in use - `tm_leet` variants a-d share one UV layout per
region):
- Body reference: `docs/jerseys/refs/uvref_body_2048.png` (stock
  `tm_leet_v2_body_varianta_color.png`)
- Legs reference: `docs/jerseys/refs/uvref_legs_1024.png` (stock
  `tm_leet_v2_lower_body_varianta_color.png`)

## Colors (fixed, do not change)

| Kit | Jersey | Shorts / socks | Boots |
|---|---|---|---|
| Home | red / black | red/black to match jersey | pink |
| Away | blue / white | blue/white to match jersey | yellow |
| GK Home | orange / black | orange/black to match jersey | black gloves are on the body texture (arms/hands), boots black |
| GK Away | white with a little blue | white/blue to match jersey | black gloves on body texture, boots black |

All four: same generic **three-stripe motif** on the sleeves/shoulders as a
trademark-safe Adidas-look substitute (NOT the real three-stripe logo -
vary spacing/angle enough that it reads as "sporty stripes," not a brand
mark). One consistent hairstyle/head look across all four - the head
texture is not part of this brief (left stock), so nothing to paint there.

## What NOT to change

- Do not touch UV layout, silhouette, or add new geometry - this is a
  texture-only override of an existing rigged mesh (`tm_leet` variants a-d).
  Whatever isn't cloth (skin, hands, face, hair, the one holster strap
  fused into the mesh) should be copied pixel-for-pixel from the reference,
  not repainted, so shading/wrinkle detail (baked into separate normal/AO
  maps you are not touching) still lines up correctly.
- Do not add a logo, real team crest, or any third-party trademark.
- Keep the same UV seams visible in the reference (i.e. paint in the same
  UV space, don't shift/rotate the layout).

## Delivery

Save the 8 PNGs back into `docs/jerseys/refs/` (or hand them back directly)
using the exact filenames in the table above. Claude will drop them into
`content/csgo_addons/soccermod_jerseys/characters/models/tm_leet/materials/`
as the `TextureColor` for each kit's `.vmat` and hand back to you (Sergi)
for a compile + look-check in CS2 Workshop Tools before the other three
kits are considered final (Home is the gate kit).
