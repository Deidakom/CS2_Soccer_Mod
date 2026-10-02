#!/usr/bin/env python3
"""Owner 2026-10-02:
- "In general rename every map to Home vs Away and not Red vs Blue, colours could change in future"
  (his answer: everywhere, also the plugin's scoreboards): the top scoreboard and the TAB board show
  HOME (the T side) and AWAY (the CT side) where no team name is set.
- "For the indoor hall map, the 2v2 map and the street arena the ball shadow is not fixed ... seems to be
  a problem with new maps": the hall-type maps are lit by baked lamps or have the sun low, so the ball
  has no shadow under it there. The contact shadow (ContactShadow.cs: a soft dark disc straight under
  the ball, on the traced floor) is now always on on those maps, without the server flag file; it lies
  1.0 above the floor there (the painted lines lie up to 0.8 above it).
- Street arena: the low sun shines into the goal containers; the moving net and the goal frame cast no
  shadow there (he saw the shadow in the goal flicker).

Run tools/hall/plugin/apply_hall_atmo.py first (OnStreet). Exact-match edits, idempotent, with backups.

  python3 apply_home_away_shadow.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-homeaway-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.ScoreHud.cs": [
        ('MatchRuleMath.ScoreHudTeamName(_teamNameT, "RED")', 'MatchRuleMath.ScoreHudTeamName(_teamNameT, "HOME")'),
        ('MatchRuleMath.ScoreHudTeamName(_teamNameCt, "BLUE")', 'MatchRuleMath.ScoreHudTeamName(_teamNameCt, "AWAY")'),
    ],
    "SoccerModMvpPlugin.TabBoard.cs": [
        ('MatchRuleMath.ScoreHudTeamName(_teamNameT, "RED")', 'MatchRuleMath.ScoreHudTeamName(_teamNameT, "HOME")'),
        ('MatchRuleMath.ScoreHudTeamName(_teamNameCt, "BLUE")', 'MatchRuleMath.ScoreHudTeamName(_teamNameCt, "AWAY")'),
    ],
    "SoccerModMvpPlugin.ContactShadow.cs": [
        (
            "    private bool BallShadowActive => _ballShadowPrecached && FlagFileOn(BallShadowFlagFile);\n",
            "    // 2026-10-02 owner: on the hall-type maps (indoor hall, 2v2, street, 1v1) the ball had no shadow under\n"
            "    // it - there the contact shadow is always on, without the flag file.\n"
            "    private bool BallShadowActive => _ballShadowPrecached && (FlagFileOn(BallShadowFlagFile) || OnHall);\n",
        ),
        (
            "            if (!File.Exists(ConfigPath(BallShadowFlagFile))) return;\n",
            "            if (!File.Exists(ConfigPath(BallShadowFlagFile)) && !IsHallMap(Server.MapName)) return;\n",
        ),
        (
            "            var lift = GrassSpawned ? 2.6f : 0.6f;\n",
            "            var lift = GrassSpawned ? 2.6f : OnHall ? 1.0f : 0.6f;   // the hall maps' painted lines lie up to 0.8 above the floor\n",
        ),
        (
            "            prop.Entity!.Name = BallShadowTargetName;\n            prop.AcceptInput(\"DisableCollision\");\n            return prop;\n",
            "            prop.Entity!.Name = BallShadowTargetName;\n            prop.AcceptInput(\"DisableCollision\");\n"
            "            Logger.LogInformation(\"[SM2DIAG] ball_shadow_spawned model={Model} z={Z:F1}\", model, at.Z);\n"
            "            return prop;\n",
        ),
    ],
    "SoccerModMvpPlugin.DynamicNet.cs": [
        (
            "            keyValues.SetString(\"model\", DynamicNetModel);\n            keyValues.SetInt(\"solid\", 0);\n",
            "            keyValues.SetString(\"model\", DynamicNetModel);\n            keyValues.SetInt(\"solid\", 0);\n"
            "            if (OnStreet) keyValues.SetInt(\"disableshadows\", 1);   // low sun into the goal containers: the net's shadow flickered\n",
        ),
    ],
    "SoccerModMvpPlugin.GoalFrame.cs": [
        (
            "            keyValues.SetString(\"model\", GoalFrameModel);\n            keyValues.SetInt(\"solid\", 0);\n",
            "            keyValues.SetString(\"model\", GoalFrameModel);\n            keyValues.SetInt(\"solid\", 0);\n"
            "            if (OnStreet) keyValues.SetInt(\"disableshadows\", 1);   // as the net (DynamicNet.cs)\n",
        ),
    ],
}

plan = {}
for name, edits in EDITS.items():
    path = src / name
    raw = path.read_bytes().decode("utf-8")
    eol = "\r\n" if "\r\n" in raw else "\n"
    new = raw
    for old, repl in edits:
        old, repl = old.replace("\n", eol), repl.replace("\n", eol)
        if repl in new:
            continue
        if new.count(old) != 1:
            sys.exit(f"{name}: expected exactly one match for:\n{old}")
        new = new.replace(old, repl)
    if new != raw:
        plan[path] = new
for path, new in plan.items():
    backup.mkdir(parents=True, exist_ok=True)
    shutil.copy2(path, backup / path.name)
    path.write_bytes(new.encode("utf-8"))
    print("edited", path.name)
print(f"{len(plan)} file(s) changed" + (f", backups in {backup}" if plan else " (already applied)"))
