# Goalkeeper gearless model candidate

## Scope

This is a local, unpublished candidate for the requested goalkeeper changes:

- both goalkeeper slots use the exact same validated gearless variant-b model
  template (`kit_home.vmdl`); the old variant-c/d goalkeeper models are not
  used by this candidate;
- both models therefore share the same head, arms, skeleton, animations,
  attachments and third-person geometry, with no harness, scarf, holsters or
  pouches;
- the body atlas has a fixed outlined `1` on both front and back shirt UV
  islands;
- Home/orange GK uses black gloves and pink boots;
- Away/white GK uses blue gloves and green boots.

The server/plugin model paths are unchanged (`kit_gkhome.vmdl` and
`kit_gkaway.vmdl`), so this asset revision does not require a code or config
change.  The approved Workshop addon and the live server were not changed.

## Publication checkpoint

The candidate was submitted as a revision of Workshop item `3797479770`.
SteamCMD returned `Success`, but an authenticated
`workshop_download_item 730 3797479770` still returned the previous
32,124,882-byte package (`727dcb2f...`). The live server likewise retained
that old hash after `mm_download_addon`. The revision is therefore awaiting
Steam moderation/propagation; the server was deliberately not reloaded onto
an unapproved package, which would give clients missing-model errors.

## Candidate package

The isolated Workshop Tools addon is:

`E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/game/csgo_addons/soccermod_jerseys_gearless`

Local package:

`artifacts/kits-gk-gearless-upload/3797479770_dir.vpk`

- 44 compiled/addon entries
- 35,000,606 bytes
- SHA-256 `bbeeed16f860befe1eac88c9429e2204046c491e325b870f01423592447f8e91`
- dependency audit: 43 resources, no unresolved references
- Source2Viewer VPK CRC verification passed

The three default AO/metal/normal resources referenced by the glove shader
are supplied by CS2's base `pak01_dir.vpk`; the glove colour PNGs themselves
are packaged in this VPK.  This avoids the earlier failed dependency on
Valve's VPK-only fingerless glove colour/normal files.

## Reproduction

From the repository root, first generate the deterministic references:

```powershell
& 'C:/Users/sergi/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' `
  docs/jerseys/paint-gk-gearless.py `
  --stock-dir 'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive/content/csgo_addons/soccermod_jerseys_gearless/materials/soccermod/kits'
```

Then prepare and compile the isolated addon with the bundled PowerShell 7
runtime:

```powershell
& 'C:/Users/sergi/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' `
  -NoProfile -ExecutionPolicy Bypass `
  -File docs/jerseys/prepare-gk-gearless.ps1 `
  -CsRoot 'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive' `
  -AddonName soccermod_jerseys_gearless

& 'C:/Users/sergi/.cache/codex-runtimes/codex-primary-runtime/dependencies/native/powershell/pwsh.exe' `
  -NoProfile -ExecutionPolicy Bypass `
  -File docs/jerseys/compile-kits.ps1 `
  -CsRoot 'E:/SteamLibrary/steamapps/common/Counter-Strike Global Offensive' `
  -AddonName soccermod_jerseys_gearless -Force
```

The preparer creates a timestamped backup under
`artifacts/gk-gearless-source-backup/` before changing the isolated addon.

## Verification completed

- 11 repository unit tests passed (`test_gearless_mesh.py` and
  `test_kit_tools.py`).
- Resource compilation emitted both goalkeeper models, all six custom GK
  materials and both custom glove textures.
- VPK packaging and the dependency audit passed.
- No Workshop upload, server copy, map reload or live enablement was done in
  this step.  Publish only after a visual CS2 check of both GK slots: third
  person, first person, crouch, running, shadows, gloves, boots, fixed number
  and absence of gear.
