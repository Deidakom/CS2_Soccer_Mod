# Handover to Codex — football kit textures

## Where this fits

Repo: `Deidakom/CS2_Soccer_Mod`, branch `main`. Football kits project:
full plan at
[docs/jerseys/2026-09-07-football-kits-plan.md](2026-09-07-football-kits-plan.md).
Work split (user-approved 2026-09-01, still standing): **Codex paints
textures, Claude does plugin code + server-ops.**

**Already done (Claude, commit `5af06a8` on `main`, live on the test
server):** the plugin side. `TeamModelMode.Kits` assigns
`agents/models/tm_leet/tm_leet_variant{a,b,c,d}.vmdl` to
Home/Away/GK-Home/GK-Away, forces white tint, and follows the squad through
the halftime side swap. `css_sm2teammodel <off|stock|kits>`,
`css_sm2kit <home|away|gkhome|gkaway> [model.vmdl]`. This already works
today, unpainted (visibly a plaid `tm_leet` operator) — nothing here is
blocked on you for the code to function; only the *look* is pending.

**Your task: paint 8 texture PNGs.** Full spec below (same content as
[docs/jerseys/2026-09-07-texture-brief.md](2026-09-07-texture-brief.md)).
There is nothing else in scope for you right now — no plugin code, no
vmat/addon files, no compiling. Just the 8 PNGs.

## What to paint

The `TextureColor` (diffuse/albedo) map for each of 4 kits x 2 body
regions. Everything else (normal maps, AO, roughness, metalness, cloth, rim
mask) stays the untouched stock CS2 texture — do not generate or touch
those; they're already copied into the addon unchanged.

| Kit | Body texture (jersey, 2048x2048) | Legs texture (shorts/socks/boots, 1024x1024) |
|---|---|---|
| Home | `home_body_color.png` | `home_legs_color.png` |
| Away | `away_body_color.png` | `away_legs_color.png` |
| GK Home | `gkhome_body_color.png` | `gkhome_legs_color.png` |
| GK Away | `gkaway_body_color.png` | `gkaway_legs_color.png` |

sRGB PNG, no alpha needed (opaque), exact pixel dimensions above.

**Paint directly over the UV reference PNGs** already in the repo (these
are the real stock `tm_leet` textures extracted from the game files, so the
UV seams/layout match the actual mesh exactly):
- Body: [docs/jerseys/refs/uvref_body_2048.png](refs/uvref_body_2048.png)
- Legs: [docs/jerseys/refs/uvref_legs_1024.png](refs/uvref_legs_1024.png)

## Colors (fixed — do not deviate)

| Kit | Jersey | Shorts / socks | Boots |
|---|---|---|---|
| Home | red / black | red/black to match jersey | pink |
| Away | blue / white | blue/white to match jersey | yellow |
| GK Home | orange / black | orange/black to match jersey | black gloves (on the body texture, arms/hands), black boots |
| GK Away | white with a little blue | white/blue to match jersey | black gloves (on the body texture), black boots |

All four: the same generic **three-stripe motif** on the sleeves/shoulders
— a trademark-safe Adidas-*look* substitute, NOT the real three-stripe
logo (vary spacing/angle enough that it reads as "sporty stripes," not a
brand mark). One consistent hairstyle across all four — the head texture
is out of scope here (left stock), nothing to paint there.

## What NOT to change

- No new geometry, no UV changes — this is a texture-only override of an
  existing rigged mesh. Anything that isn't cloth (skin, hands, face, hair,
  the one holster strap fused into the mesh) should be copied
  pixel-for-pixel from the reference, not repainted, so it still lines up
  with the normal/AO maps you're not touching.
- No real logo, team crest, or third-party trademark.
- Don't shift or rotate the UV layout — paint in the same UV space as the
  reference.

## Delivery

Save the 8 PNGs into `docs/jerseys/refs/` using the exact filenames above
(or hand them back directly if you can't write to the repo). Claude will
drop them into the local `soccermod_jerseys` Workshop addon as each kit's
`TextureColor` and hand back to Sergi for a compile + look-check in CS2
Workshop Tools. **Home is the gate kit** — the other three don't get
finished until Sergi signs off on how Home looks in-game (there's a known
ceiling: `tm_leet`'s one shoulder strap is fused into the mesh and can't be
textured away, so expect "a cleanly recolored operator in a kit," not a
geometrically clean jersey).
