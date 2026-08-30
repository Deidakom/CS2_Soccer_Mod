# Toolchain readiness

Observed locally on 2026-08-27.

## Available

| Component | Observation | Status |
|---|---|---|
| CS2 client/runtime | Installed under `E:\SteamLibrary\steamapps\common\Counter-Strike Global Offensive` | Ready for client/runtime checks |
| Steam app | App 730; installed and target build IDs are both `24957633` | Current as of the audit; record before every engine run |
| CS2 Workshop Tools/content tree | DLC `2279721` is enabled; Hammer, the resource compiler, and the content tree are present | Ready for Source 2 addon work |
| Valve `point_script.d.ts` | Current demo-addon declaration SHA-256 is `2DA5D7D10FFCEA1AAC52E668CF153974A3D973AEB8E7DC9A15FB8A2227B50BF9` | Ready; raw declaration remains build-pinned |
| CSF Football Stadium | Workshop item `3361075564` is installed, hash-frozen, and live-loads as `soccer_cssl_stadium_v8` | Ready as the Phase 1 reference |
| Writable Phase 1 addon | Workshop Tools created `soccermod_phase1`; its generated template-lab `.vmap` and bundled script compile and live-load | Ready for the controlled ball lab; not Stadium source |
| Git | Installed | Ready |
| PowerShell | Installed | Ready |
| .NET runtime | x64 .NET 8.0.23 runtime only | Insufficient for managed plugin development |
| Legacy reference Python | A bundled Python runtime can run the existing read-only BSP verification scripts | Ready for Phase 0 reference checks |

## Missing

| Component | Evidence | Needed for |
|---|---|---|
| SteamCMD/dedicated-server installation | `steamcmd` is not installed or on PATH | Reproducible clean dedicated-server target |
| .NET SDK | `dotnet --info` reports no SDKs | CounterStrikeSharp/Swiftly adapter spike |
| Metamod and CounterStrikeSharp | No CS2 addon/plugin directories are installed | Optional server-layer spike only |
| Native C++ toolchain | No CMake, Clang, MSVC `cl`, or MSBuild detected | Last-resort native helper only |

## API drift audit

Between audited builds `24934554` and `24957633`, the installed demo addon's
raw `point_script.d.ts` hash changed from
`DBB8AE95F12C6F513909A527609A8DF498AE5BB54A2024445A27537B33D61752`
to
`2DA5D7D10FFCEA1AAC52E668CF153974A3D973AEB8E7DC9A15FB8A2227B50BF9`.
The exact diff contains comments/documentation changes only. Comment removal
and declaration-text normalization produce the same 14,301 bytes for both
files, with SHA-256
`7EA7AA89027FB3BDF9144E1B3CD37CC76716B047BDEE3F7CE6E96EEDA8544BC1`.
This supports the current API surface while retaining a strict raw-file pin for
future drift detection.

## Phase 0 setup sequence

1. In Steam, install **Counter-Strike 2 Workshop Tools** for app 730.
2. Verify Hammer launches and locate the installed
   `point_script.d.ts`. Check the current demo-addon location
   `content/csgo_addons/cs_script_demo/maps/scripts/point_script.d.ts` first,
   then the older `content/csgo/maps/editor/zoo/scripts/point_script.d.ts`
   location. The verifier checks both because Valve's demo layout has changed.
3. Record the app build ID, API declaration SHA-256, timestamp, and Workshop
   Tools installation path.
4. Create a new Source 2 addon for the ball lab. Do not start with the final
   stadium map.
5. Prepare a separate clean dedicated-server installation through SteamCMD on
   the intended production OS. Do not treat the mutable desktop client install
   as the production server.
6. Use two remote clients for replication/latency tests; add twelve-player load
   before passing the ball gate.
7. Install the managed SDK, Metamod, and CounterStrikeSharp only for the later
   optional adapter spike. Pin exact downloaded versions and checksums.
8. Install a C++ toolchain only if both official physics approaches fail and a
   native micro-helper is explicitly approved.

## Framework status

- **SourceMod:** not a CS2 implementation route. Existing `.sp` and `.smx`
  artifacts are Source 1 references only.
- **Metamod:Source:** CS2 support is in the 2.x development line and is suitable
  as a native loader when needed.
- **CounterStrikeSharp:** preferred optional managed server adapter.
- **SwiftlyS2:** credible comparison candidate if CounterStrikeSharp lacks a
  required primitive or proves unstable.
- **Direct C++/Plugify:** fallback paths with higher operational and maintenance
  cost.

Current framework installation instructions must be re-read immediately before
installation; CS2 updates and framework compatibility move too quickly for a
hard-coded old package to be trustworthy.
