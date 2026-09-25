# Stadium logo removal (Workshop item 3797479770)

The map (Workshop 3361075564, author long gone) shows its community logos and
URLs through four materials. Our Workshop item ships replacements at the same
paths, so the map itself is never modified or republished:

| Material | Was | Replacement |
|---|---|---|
| `materials/tm/cs2football` | counterstrikefootball.co.uk | invisible (translucent, opacity 0) |
| `materials/tm/cssleague` | cssleague.eu | invisible |
| `materials/tm/cssl_logo_red_200` | CSF badge | invisible |
| `materials/tm/csf_discord` | Discord QR code | plain dark panel |

Each keeps the original material's shader; only the texture changes. Compile
with `resourcecompiler.exe -nop4 -game <cs2>\game\csgo -i <file>.vmat` from a
local content addon, then add the `.vmat_c`/`.vtex_c` outputs to the Workshop
VPK (`docs/jerseys/replace-vpk-files.ps1 -Addition`, or the C# VpkEntryReplacer).
Published 2026-09-25 (manifest 5741545683823038169). Whether an addon may
override another addon's (the map's) files was untested at publish time.
