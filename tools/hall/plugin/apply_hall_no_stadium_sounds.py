#!/usr/bin/env python3
"""Owner 2026-10-02: "auto disable the stadium sound effects for the indoor hall. they should not
be available for this map".

On ka_soccermod_indoor nobody is sent a stadium sound (referee whistles, air horn, crowd goal and
boo, the crowd background / reactions / chants, the announcer) and the "Stadium & Effects" entry is
not in the sound menu there. Players' own choices are kept for the other maps. The visual goal
show and the cheering fans stay. Exact-match edits, idempotent, with backups.

  python3 apply_hall_no_stadium_sounds.py /root/football-build/src/server-plugin/SoccerModMvp
"""
import shutil, sys, time
from pathlib import Path

src = Path(sys.argv[1])
backup = Path("/root/arena-plugin") / ("backup-" + time.strftime("%Y%m%d-%H%M%S"))

EDITS = {
    "SoccerModMvpPlugin.SoundPrefs.cs": [
        (
            "    private RecipientFilter StadiumRecipients(SoccerSound category) =>\n"
            "        SoundRecipients(category, p => SoundOn(p, SoccerSound.Stadium));\n",
            "    // 2026-10-02 owner: no stadium sounds in the indoor hall - nobody is sent one there (HallLayout.cs).\n"
            "    private RecipientFilter StadiumRecipients(SoccerSound category) =>\n"
            "        SoundRecipients(category, p => !OnHall && SoundOn(p, SoccerSound.Stadium));\n",
        ),
        (
            '        menu.Add($"Stadium & Effects: {(SoundOn(player, SoccerSound.Stadium) ? "On" : "Off")} ›", OpenStadiumSoundsMenu);',
            '        if (!OnHall) menu.Add($"Stadium & Effects: {(SoundOn(player, SoccerSound.Stadium) ? "On" : "Off")} ›", OpenStadiumSoundsMenu);   // not offered in the indoor hall',
        ),
    ],
    "SoccerModMvpPlugin.AtmoCrowdSound.cs": [
        (
            "    private bool AtmoCrowdSoundOn => AtmoOn && AtmoSet.CrowdSound;",
            "    private bool AtmoCrowdSoundOn => AtmoOn && !OnHall && AtmoSet.CrowdSound;   // no stadium sounds in the indoor hall",
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
