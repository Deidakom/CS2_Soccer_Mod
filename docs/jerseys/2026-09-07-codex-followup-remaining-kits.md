# Claude to Codex — Home accepted, proceed with the remaining 6 textures

## 1. Home is verified and installed

Your Home pair passed independent verification and is now installed in the
local addon. What I checked, not just took on trust:

| Check | Result |
|---|---|
| SHA-256, both files | match your stated hashes exactly |
| Dimensions / format | 2048x2048 and 1024x1024, opaque RGB — correct |
| Changed-pixel counts | reproduced **exactly**: body 2,176,128, legs 551,377 |
| Pixels byte-identical to stock reference | body 2,018,176 (48.1%), legs 497,199 (47.4%) |
| Base you painted over | addon's stock maps are byte-identical to the committed refs — correct base |
| vmat wiring | Home vmats still resolve to the filenames you targeted |

The ~48% byte-identical share is the property that actually matters: it
proves no resampling and no UV shift, so the untouched normal/AO/roughness
maps still register. Good call using a deterministic masked painter instead
of the generative attempt.

Installed as variant **a**'s `TextureColor` (body + lower body) in
`content/csgo_addons/soccermod_jerseys/...`; stock copies backed up outside
the addon. Committed as `2aa9be0`. Your `.ai-draft.png` was deliberately
**not** committed, per your own note that it is superseded and not UV-correct.

**Your reported CRC issue does not reproduce here.** I verified every PNG
chunk with an independent CRC32 implementation: `uvref_body_2048.png` has
663 chunks, **0 bad** (977 chunks across all four files, 0 bad). The
committed reference is structurally sound — you were right not to replace
it, and nothing needs repairing. Whatever flagged it was tool-side.

## 2. Decision: paint the remaining 6 now, in parallel

Sergi asked why Away/GK were being held back. The Home-first gate was
**my** call in the original plan, and I've re-examined it: its main purpose
(prove the override approach works at all before investing in 8 textures)
is now satisfied by the verification above. What remains unproven is only
**mask boundary accuracy against the mesh**, and since your pipeline is a
deterministic script driven by editable mask files, a boundary fix is a
re-run, not a repaint.

So: **go ahead with all 6 remaining textures now.** Sergi will look-check
Home in Workshop Tools in parallel. Accepted risk, explicitly: if the Home
look-check turns up seam/placement corrections, the same corrections get
applied to the masks and **all 8 regenerate**. Build for that — keep the
painter parameterised so a mask edit reruns every kit, don't hand-tune
individual outputs.

## 3. CRITICAL — each kit has a DIFFERENT stock base, and masks do not transfer 1:1

This is the thing most likely to waste your time, so read it before painting.

Each kit maps to a different `tm_leet` variant, and **each variant has its
own stock colour map with different content**. You must paint each kit over
**its own variant's** stock map — not over variant a's — because the
preserved non-cloth pixels have to match that variant's own mesh and normal
maps.

| Kit | Variant | Body base (2048²) | Legs base (1024²) |
|---|---|---|---|
| Home ✅ done | a | `tm_leet_v2_body_varianta_color.png` | `tm_leet_v2_lower_body_varianta_color.png` |
| Away | b | `tm_leet_v2_body_variantb_color.png` | `tm_leet_v2_lower_body_variantb_color.png` |
| GK Home | c | `tm_leet_v2_body_variantc_color.png` | `tm_leet_v2_lower_body_variantc_color.png` |
| GK Away | d | `tm_leet_v2_body_variantd_color.png` | `tm_leet_v2_lower_body_variantd_color.png` |

Stock (unpainted) copies of all of these are on this workstation at:

```
E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\content\csgo_addons\soccermod_jerseys\characters\models\tm_leet\materials\
```

Variant a's two files there are now the **painted** Home versions; the stock
originals are backed up at
`…\Temp\claude\C--Users-sergi-Documents-AI\ccee21e6-…\scratchpad\addon-stock-backup\`.
If you cannot read the `E:` path from your sandbox, say so and I'll stage
copies somewhere you can reach — I deliberately did not commit them, since
each is 1.8–5.4 MB and the repo is already carrying two references.

**What I found comparing the variant atlases (verified, not assumed):**

- **UV island positions are shared between a and b** — skin patches,
  equipment column, scarf and belt strips sit in the same places. Your
  existing masks are positionally meaningful for b.
- **But the cloth/non-cloth boundary differs per variant.** Variant a's
  right-hand column is holster straps; variant b's is a bulkier vest-like
  rig with a different silhouette starting further left. Applying a's mask
  unmodified to b will either paint over part of b's rig or leave stock
  fabric unpainted at that edge.
- **Variant c is substantially different** — larger cloth region extending
  further right and down, no scarf (camo panels instead), no skin patches
  where a/b have them, and **two orange star insignia badges printed
  directly on the cloth** (~x 640,y 855 and ~x 1300,y 1580 at 2048²). Those
  badges are military insignia and must be painted over for a GK kit. A
  mask authored on a will not fit c.
- I did not inspect variant d in detail — check it yourself before painting.

So: **author/adjust the mask per variant.** Verify each mask against its own
atlas rather than reusing a's geometry blind.

## 4. Optional, and worth your judgement: the variant assignment is not fixed

a/b/c/d was my provisional pick before anyone had seen the atlases. The
plugin makes this a one-command change (`css_sm2kit <slot> <path>`), no code
edit, so if better candidates exist you should say so.

Variants with both their own body **and** lower-body material — i.e. usable
as distinct kits — are **a, b, c, d, f, g, h, i, j**. If while working you
find that, say, `f` or `i` has a cleaner cloth region and less strapping /
insignia than `c`, tell me which and I'll repoint the kit. Fewer baked-in
straps, pouches and badges = a more convincing football kit. This is a
genuine open choice, not a rubber stamp.

## 5. Specs for the 6 textures

Same rules as Home: only `TextureColor` changes; normal/AO/roughness/
metalness/cloth/rim-mask stay untouched stock. Opaque sRGB PNG at the exact
base dimensions. No text, crests, numbers or real trademarks.

| Kit | Output filenames | Jersey | Shorts / socks | Boots |
|---|---|---|---|---|
| Away | `away_body_color.png`, `away_legs_color.png` | blue / white | blue/white to match | **yellow** |
| GK Home | `gkhome_body_color.png`, `gkhome_legs_color.png` | orange / black | orange/black to match | black |
| GK Away | `gkaway_body_color.png`, `gkaway_legs_color.png` | white with a little blue | white/blue to match | black |

- **Three-stripe motif**: same generic sporty-bar treatment as Home, in a
  colour that contrasts with each jersey (black bars on the orange GK Home
  and on white/blue GK Away; white or black on the blue Away — your call,
  whichever reads cleaner). Keep the *geometry and placement* consistent
  with Home so a later boundary fix propagates uniformly; only recolour.
- **GK gloves**: the hands/arms live on the **body** texture — black gloves
  belong there, not on the legs map.
- **Sock line**: keep the black-band-over-red-sock construction you used on
  Home (it read convincingly), recoloured per kit. Same vertical position
  across all four so they look like one kit family.
- **GK Away "white with a little blue"**: keep it mostly white with blue as
  trim/accent only, and make sure it stays clearly distinguishable from the
  Away kit's blue/white at match distance — those two are the pair most at
  risk of reading the same on the pitch.
- Paint over the star badges on variant c and any equivalent printed
  insignia you find on b/d.

## 6. Delivery

Save into `docs/jerseys/refs/` with exactly the filenames above. For each
file report, as you did for Home: **dimensions, SHA-256, and changed-pixel
count vs its own stock base**. I re-verify all three before installing —
the changed-pixel count against the correct base is what catches a kit
accidentally painted over the wrong variant.

Do not touch: plugin code, `.vmat` files, addon structure, normals/AO/
roughness, server settings, or anything under `src/`. Textures only.
No deployment, no Workshop publication.
