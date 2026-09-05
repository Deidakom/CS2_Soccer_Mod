# Spectator menu input

> Publication update: the owner authorized deployment and both GitHub pushes.
> This batch is deployed as 1.4.2-dev; see the [deployment report](deployments/2026-09-05-queue-update.md).
> Earlier local/hold statements below record the development history.

The owner confirmed that running `spec_usenumberkeys_nobinds 0` in their client
console restored numbered SoccerMod menu input while spectating, including the
CAP flow after moving everyone to spectators.

The dispatch and CAP actor permission paths already accept spectator controllers;
there is no requirement to have a living player pawn. The interference was CS2's
raw-number spectator selection, which bypasses custom number-key bindings.

The local queue now includes:

- The spectator setting in menu setup instructions, on its own copyable line.
- `!menukeys` to repeat setup at any time.
- A once-per-connection hint when a spectator opens any numbered menu, including
  the CAP menu reopened after "Put all players to spectator".
- An optional client setup file at `deploy/client/soccermod_menu.cfg`.
- Menu key diagnostics that include the player's team.
- Tests protecting spectator-capable dispatch and CAP administration.

`!1` through `!9` in chat remain an existing fallback for selecting the displayed
number; `!0` closes the menu. They do not depend on spectator number-key bindings.

The setting is listed as **clientdll archive**, not a replicated server convar,
in the [CS2 convar dump](https://cs2.poggu.me/dumped-data/convar-list/).
Putting it in `server.cfg` does not establish it as a default for connected
clients. CounterStrikeSharp exposes per-client ConVar replication, but an API
that sends a request is not evidence this client-only setting accepts it.
Automatic per-client application is not implemented or claimed as verified.
The confirmed remedy is the client's saved setting. The server cannot receive
and repair a number-key event already consumed by the spectator UI.

Local build: success, no warnings/errors. Node tests: 105 passed. No deployment,
commit or push. The owner's successful client-side fix required no server change.
