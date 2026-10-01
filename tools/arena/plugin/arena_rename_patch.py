#!/usr/bin/env python3
"""Map rename ka_soccermod_stadium (owner 2026-10-01): the three edits in ArenaLayout.cs, in place."""
import json, shutil, sys, time
from pathlib import Path
p = Path(sys.argv[1]) / "SoccerModMvpPlugin.ArenaLayout.cs"
s = p.read_text(encoding="utf-8")
PAIRS = json.loads("[[\"    private const string ArenaMapName = \\\"soccer_soccermod_arena\\\";\\n    private static bool IsArenaMap(string? map) => string.Equals(map, ArenaMapName, StringComparison.OrdinalIgnoreCase);\\n\",\"    // 2026-10-01 owner: the map is called ka_soccermod_stadium. Its first Workshop revisions were\\n    // soccer_soccermod_arena; both names count, so a server still on an old revision keeps working.\\n    private const string ArenaMapName = \\\"ka_soccermod_stadium\\\";\\n    private const string ArenaFirstMapName = \\\"soccer_soccermod_arena\\\";\\n    private static bool IsArenaMap(string? map) => string.Equals(map, ArenaMapName, StringComparison.OrdinalIgnoreCase)\\n        || string.Equals(map, ArenaFirstMapName, StringComparison.OrdinalIgnoreCase);\\n\"],[\"    private const string ArenaRadarTextureResource = \\\"panorama/images/overheadmaps/soccer_soccermod_arena_radar_psd.vtex\\\";\\n\",\"    private static string ArenaRadarTextureResource => \\\"panorama/images/overheadmaps/\\\"\\n        + (string.Equals(Server.MapName, ArenaFirstMapName, StringComparison.OrdinalIgnoreCase) ? ArenaFirstMapName : ArenaMapName) + \\\"_radar_psd.vtex\\\";\\n\"],[\"// soccer_soccermod_arena (2026-10-01, owner:\",\"// ka_soccermod_stadium, first called soccer_soccermod_arena (2026-10-01, owner:\"]]")
n = s
for a, b in PAIRS:
    if b in n:
        continue
    assert n.count(a) == 1, a
    n = n.replace(a, b)
if n != s:
    b = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))
    b.mkdir(parents=True, exist_ok=True)
    shutil.copy2(p, b / p.name)
    p.write_text(n, encoding="utf-8")
    print("edited", p.name, "backup in", b)
else:
    print("already done")
