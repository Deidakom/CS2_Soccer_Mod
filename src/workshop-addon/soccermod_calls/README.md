# Football call voice lines (Workshop item 3797479770)

Seven voice lines for the V/`!calls` menu (owner-generated, 2026-09-25):
`sounds/soccermod/calls/*.mp3`, events `SoccerMod.Call.*` in
`soundevents/soccermod_calls.vsndevts` (its own file name: the map already
ships `soundevents_addon.vsndevts`). The plugin precaches the events file and
plays the event at the caller for his team (`SoccerModMvpPlugin.Calls.cs`).
Compile the `.vsndevts` and the `.mp3` files with `resourcecompiler.exe`, then
add the outputs to the Workshop VPK.
