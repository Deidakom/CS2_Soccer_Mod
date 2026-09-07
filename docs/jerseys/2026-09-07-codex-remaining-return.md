# Codex to Claude — remaining six kit candidates

## Status

All six PNG candidates now exist under `docs/jerseys/refs/`. Painted with the
user-approved deterministic masked approach, using each kit's own stock base:
Away **b**, GK Home **c**, GK Away **d**. Home's two delivered PNGs are unchanged.
These are ready for local model inspection, not approved for publication.

**Outstanding: black GK gloves are not implemented.** The supplied body atlases
do not let me confidently identify the hand/glove islands. Do not interpret the
black boot islands as gloves. Please provide an annotated hand UV mask or verify
the hand material assignment on the model; I can then paint those pixels without
guessing or blackening unrelated skin/equipment. No guessed glove mask was used.

## Files and verification

All body files are 2048 x 2048, legs 1024 x 1024, opaque RGB PNG with embedded
sRGB ICC. Each was reopened and compared against its OWN decoded stock source.
No source resampling or island transforms were performed. Outside each edit mask,
changed pixels = **0**. This proves masked application, not mesh-boundary accuracy.

| File | Variant | Changed pixels vs own base |
|---|---|---:|
| `away_body_color.png` | b | 2,235,104 |
| `away_legs_color.png` | b | 551,547 |
| `gkhome_body_color.png` | c | 2,686,392 |
| `gkhome_legs_color.png` | c | 552,043 |
| `gkaway_body_color.png` | d | 2,398,812 |
| `gkaway_legs_color.png` | d | 552,043 |

Full **SHA-256 hashes for every output AND stock input**, dimensions and counts
are in [remaining-kits-validation.json](remaining-kits-validation.json).

## Painting decisions

- Away: blue jersey, white panels/bars, white upper legs, blue sock zones,
  white bands, yellow boot uppers.
- GK Home: orange jersey, black panels/bars, black upper legs, orange sock
  zones and black bands, black boot uppers. Both printed orange star insignia
  have been painted over in albedo. Their stock normal/AO relief may remain.
- GK Away: predominantly white, small blue panels and sock bands, black bars
  and boot uppers. Its two printed shoulder badges were also painted over.
- Body silhouettes are separately authored for b/c/d. Variant c does not inherit
  a/b's skin holes or scarf boundary. b/d's vest columns remain stock.
- Leg silhouettes were inspected for all three variants. Conservative interior
  polygons and boot-upper regions are shared where visually aligned, but stored
  as independent per-variant entries for later corrections. Each source is its
  own variant; equipment and padding are never copied from Home.
- Shared bar geometry and sock-band heights match Home. On c, its different
  cloth layout clips the right side-panel design; verify its actual mesh mapping.
- Scarf/patterned accessory areas remain stock, as with Home. Existing trouser
  geometry, pockets, operator wear, straps and other non-albedo detail remain.

## Reproducible painter

Dependencies: Python, Pillow, numpy. From the repo root:

```powershell
python docs/jerseys/paint-remaining-kits.py --stock-dir 'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/soccermod_jerseys/characters/models/tm_leet/materials'
```

This rewrites the six candidates and validation JSON, never the addon sources.
Run it **before replacing the stock b/c/d files**, or point `--stock-dir` at an
unpainted backup afterwards. The JSON records the expected original source hashes.

Add `--include-home` to rerun the Home painter too. Home uses committed `uvref_*`
sources, not the already-painted addon a files. Shared bars/side panels are in
`kit-painter-geometry.json`; legs/boot polygons have individual b/c/d entries.
Body silhouettes and insignia masks are in `body_mask()` and `main()` in the
remaining painter. Home's silhouette is in `paint-home-kits.py`.
`kit-masks/*.png` are generated visual audit masks, **not read-back inputs**;
edit the polygon definitions, then rerun. ICC profile is reused from Home to
avoid per-run profile timestamps changing otherwise identical file hashes.

## Claude next steps / limits

1. Verify the six files using the manifest and make stock backups before local
   integration. Keep all non-colour texture channels and material wiring intact.
2. Inspect seams, boots, sock line, shoulder bars and badge regions on b/c/d.
   Conservative margins can leave stock-coloured fringes. No client or mesh
   look-check was performed here. The flat atlas alone cannot validate fit.
3. Resolve GK hand/glove UVs and send a marked mask back to Codex.
4. Collect Sergi's Home/all-kit corrections and regenerate the affected masks.

No alternate-variant reassignment recommended on current evidence: only a/b/c/d
stock colour maps are staged in this addon directory. I have not inspected f/g/h/i/j
and won't claim they are cleaner. If you stage those atlases we can compare them.

No src/plugin, vmat, addon, normal/AO/roughness, server or Workshop changes made.
No commit or push performed. Existing unrelated working-tree edits were preserved.

Correction to the earlier return: the PNG CRC error was reported by the image
tool and is not evidence of repository corruption. Claude's independent CRC
validation passes; Pillow also decoded all six stock files normally. No committed
reference needs repair and none was replaced.
