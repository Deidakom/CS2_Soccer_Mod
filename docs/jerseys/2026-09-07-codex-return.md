# Codex to Claude — Home kit candidate ready for model look-check

## Status

The user approved deterministic, pixel-preserving masked painting after the
AI attempt failed. The Home pair is now ready for **local integration and a
model look-check**, not approved for publication. Home remains the approval
gate; the other six textures are deliberately deferred until Sergi signs off.
No plugin, material, addon, geometry, UV, normal/AO/roughness files or server
settings were changed by this task.

## Deliverables

| File (relative to this document) | Dimensions | SHA-256 |
|---|---|---|
| `refs/home_body_color.png` | 2048 x 2048 | `9aa139c1d97395d077ad5d82604f4cdef6a800b699d55fa5e88bd8719cd587c6` |
| `refs/home_legs_color.png` | 1024 x 1024 | `92f966830966b7ada181684100735466dc530da2998bda039eb1e26c6e4d2d8d` |

Both are opaque RGB PNGs with an embedded sRGB ICC profile. The originals
were not resampled: selected pixels were recoloured at their original UV
coordinates. The body uses red fabric, black panels and two groups of three
unequal sporty bars; legs use black upper fabric, red lower fabric with black
bands, and pink boot uppers. No text, crests or logo assets were added.

## Validation and limits

- Reopened both PNGs successfully and checked exact dimensions and RGB mode.
- All pixels outside the author-selected cloth/boot masks are identical to
  the decoded references: zero differences. Changed pixels: body 2,176,128;
  legs 551,377. This validates mask application, **not** anatomical correctness
  of every mask boundary; those boundaries were selected from the atlas.
- Inspected atlas previews. Preserved exposed-skin patches, equipment,
  straps, buckles and dark soles; scarf remains stock. Conservative margins
  may show stock-coloured fringes at seams and need adjustment after model QA.
- Plaid contrast was strongly compressed, not completely reconstructed away.
  Stock normals and AO can still reveal pockets, wrinkles and operator details.
- These are recoloured trousers with sock-coloured lower sections, not true
  shorts geometry. The fused shoulder strap remains. No head changes.
- Sleeve stripe location and front/back band alignment are provisional until
  checked on the mesh. No in-game verification has been performed by Codex.

## Claude next steps

Use only the two exact deliverable filenames above as Home `TextureColor`
inputs in the existing local addon workflow. Keep all other texture channels
unchanged. Inspect shoulders, cuffs, crotch, trouser seams and boots from all
sides, then let Sergi look-check Home in CS2. Return any marked seam/placement
corrections to Codex. Do not finish Away/GK variants before Home approval.
No server deployment or Workshop publication was performed by Codex.

For repeatability on this workstation, the painter and editable masks are in
the ignored `.codex-tmp/paint-home-kits.py`, `home_body_editable_mask.png` and
`home_legs_editable_mask.png`. They are local working aids, not addon inputs.

## Available draft

`refs/home_body_color.ai-draft.png` is a built-in image-generation colour
study only, **not** `home_body_color.png`. It shows the intended red/black
direction, but is 1254x1254 rather than the required 2048x2048 and changes
protected non-clothing details. Do not simply upscale it and treat it as a
UV-correct texture. The valid-size masked candidates above supersede it.

## Input issue

The committed `refs/uvref_body_2048.png` has an IDAT CRC mismatch reported by
the image tool (expected 0x0f4a076d, calculated 0x130af9b6). Windows System.Drawing
can decode it. A temporary re-encoded input was checked against the Windows
decoded original at all 2048x2048 pixels: zero differences. The committed
reference was NOT replaced. The legs reference opened normally at 1024x1024.

## Rejected AI attempt provenance (not used for final candidate pixels)

Built-in image generation, not the CLI/API fallback. Edit input was the
temporarily repaired body reference. Prompt used:

> Use case: precise-object-edit. Edit this exact 2048x2048 CS2 tm_leet BODY diffuse UV texture atlas in place. Output exactly 2048x2048 opaque sRGB PNG. It is an EDIT TARGET, not visual inspiration. Repaint ONLY the brown plaid shirt cloth as strong clean red football jersey fabric with restrained black trim and three short black diagonal sporty bars of unequal length/irregular spacing on sleeve/shoulder cloth islands. No branded stripe logo, no text, no crest, no numbers. Preserve all existing UV contours, island shapes, seam and fold positions exactly; no shift, resize, rotation or repacking. Main brown plaid cloth is upper/left ~82% of image through ~72% height. Preserve existing subtle fabric shading/wrinkles while removing plaid. Lower patterned scarf cloth may become coordinated black/red fabric, with same contours. CRITICAL keep ALL noncloth original pixels pixel-for-pixel: dark brown holster and strap islands down the right side, long belt strips at bottom, metal buckle islands lower left, pink skin and dark red interior/torn-hole patches upper left/top centre/lower left. No clothing mockup, character, scene, garment silhouette, diagram or wireframe. Return only the edited square UV atlas in identical framing. Do not stylize, reconstruct or redraw unchanged parts; this must register with the original untouched normal and AO maps.

This prompt did NOT deliver the required pixel-preservation or resolution.
