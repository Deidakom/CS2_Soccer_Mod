# Spec: `!tp` Thirdperson-Toggle

**Für:** Codex (Implementierung). **Von:** Claude (Spec + Server-Ops + Verifikation).
**Kontext:** Debug-/QoL-Feature, ausgelöst durch den Bedarf, den TeamColor-Bug
(siehe `docs/2026-08-31-teamcolor-spec.md`) visuell von außen zu prüfen — Ego-Sicht
zeigt nie das eigene Weltmodell. User will Qualität, keine Quick-and-dirty-Lösung:
ruckelfreie Kamera, kein Entwaffnen, für alle Spieler offen.

**Kein CS:S-Vorbild** — im echten CS:S-SoccerMod-Quellcode existiert kein `!tp`
(verifiziert, Volltextsuche über den gesamten `somoe19-original`-Baum). Das hier ist
eine neue Anforderung, kein Port.

## Neue Datei

`src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.ThirdPerson.cs` — Namenskonvention
wie üblich (`namespace SoccerModMvp; public sealed partial class SoccerModMvpPlugin`).

## Mechanik (verifiziert gegen ein funktionierendes CS2-Plugin, grrhn/ThirdPerson-WIP)

```csharp
var camProp = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
camProp.DispatchSpawn();
camProp.Teleport(position, angle, new Vector());
pawn.CameraServices!.ViewEntity.Raw = camProp.EntityHandle.Raw;
// Aus:
pawn.CameraServices!.ViewEntity.Raw = uint.MaxValue;
camProp.Remove(); // oder AcceptInput("Kill") -- im Code prüfen, welches Muster
                  // für andere Runtime-Entities in diesem Plugin bereits verwendet
                  // wird (z.B. ApplyThrusterKick, SoccerModMvpPlugin.cs:1159-1194)
                  // und das übernehmen für Konsistenz.
```
Rührt Kollision/Hitboxen nicht an — nur die Render-Kamera wechselt. Kein Precache
nötig (`prop_dynamic` ohne festes Modell, reine Anker-Entity).

## ⚠ Wichtigste offene Frage — im Test zuerst prüfen

Ändert `ViewEntity` NUR die Render-Kamera, oder auch den Ursprung des
Angriffs-/Aim-Raycasts? Der Ballkontakt läuft in diesem Plugin über den
Messer-Primärangriff — falls der Raycast der Kamera folgt statt der echten
Augenposition, würde der Kick in Thirdperson daneben-zielen. Nicht vorab lösbar,
nur live testbar. Bei Problemen: Feature bleibt trotzdem nutzbar als reiner
Beobachtungsmodus, aber dann mit einem Warnhinweis im Reply-Text versehen
(z.B. "kicking may be inaccurate while active") statt es zu verschweigen.

## Command

`css_sm2thirdperson` (Chat-Alias `!tp` automatisch über CSSharp) — **kein**
`RequirePermission`-Gate, für alle Spieler offen (User-Entscheidung).

## Zustand

Reiner Session-State, kein `MatchSettingsStore`-Eintrag nötig (geht beim Disconnect
verloren, das ist so gewollt):
```csharp
private readonly HashSet<int> _thirdPersonSlots = new();
private readonly Dictionary<int, CDynamicProp> _thirdPersonCamBySlot = new();
```

## Toggle-Verhalten

- **An:** Kamera-Prop spawnen (s.o.), `ViewEntity` setzen, Slot in beide Strukturen
  eintragen.
- **Aus:** `ViewEntity.Raw = uint.MaxValue`, Prop entfernen, Slot aus beiden
  Strukturen entfernen.
- **Messer explizit NICHT anfassen** — kein `RemoveWeapons`/`GiveNamedItem` in
  diesem Modul. Das ist der Hauptunterschied zur Referenz-Implementierung (die
  standardmäßig entwaffnet) — hier bewusst weggelassen, weil der Ballkontakt über
  den Messer-Primärangriff läuft und während `!tp` weiter funktionieren soll.

## Kamera-Positions-Update (jeden Tick)

Neue Funktion `ThirdPersonOnTick()`, in die bestehende `OnTick`-Komposition
einreihen (`SoccerModMvpPlugin.cs:670-714`, Vorbild
`SprintOnTick(); MuteLandingOnTick(); DuckJumpBlockOnTick(); AfkOnTick(); ...`):
für jeden Slot in `_thirdPersonCamBySlot` mit gültigem, lebendem Pawn, Zielposition
aus dessen aktuellen View-Winkeln berechnen (Trig-Vorbild im selben Stil wie
`ApplyThrusterKick`, `SoccerModMvpPlugin.cs:1183-1184` — dort Yaw/Pitch AUS einem
Richtungsvektor; hier umgekehrt: Richtungsvektor AUS Pitch/Yaw, dann Kamera hinter
und über den Spieler versetzen).

**Startwerte** (community-Plugin-Vorbild, im Test gemeinsam mit dem User
feinjustieren, nicht als fix betrachten): Distanz 110 Units hinter dem Spieler,
Höhe 60-80 Units über Augenhöhe.

**Qualitätsanforderung — kein Ruckeln:** `Teleport()` jeden Tick mit der GLATTEN
Zielposition aufrufen, kein hartes Springen. Falls bei 64 Tick sichtbares Ruckeln
auftritt, linear zwischen aktueller und Zielposition interpolieren
(`current + (target - current) * smoothingFactor`, z.B. Faktor 0.3-0.5 pro Tick)
statt hart zu snappen. Das von Anfang an einplanen, nicht erst nachrüsten, wenn
sich jemand beschwert.

## Persistenz über Respawns

Jeder Respawn erzeugt einen neuen Pawn — die alte `ViewEntity`-Zuweisung geht
verloren. Neue Funktion `ThirdPersonOnPlayerSpawn(player)`, eingehängt analog zu
`TeamColorOnPlayerSpawn` (`SoccerModMvpPlugin.cs:643` — direkt daneben platzieren):
falls `player.Slot` in `_thirdPersonSlots`, Kamera-Prop neu erzeugen und
`ViewEntity` auf dem NEUEN Pawn erneut setzen. Der Toggle-Zustand soll über
Tod/Respawn erhalten bleiben wie eine Preference, kein manuelles Neu-Eintippen
nötig nach jedem Spawn.

## Aufräumen bei Disconnect

Neue Funktion `ThirdPersonOnPlayerDisconnect(int slot)`, registriert wie die
bestehenden Disconnect-Listener (`SoccerModMvpPlugin.cs:452-455`, Vorbild
`RegisterListener<Listeners.OnClientDisconnect>(MenuOnPlayerDisconnect);`):
Kamera-Prop killen, Slot aus beiden Strukturen entfernen — sonst bleiben
verwaiste Entities auf der Map liegen.

## Modul-Wiring (`Load`)

`ThirdPersonOnLoad()` in den bestehenden `*OnLoad`-Block einreihen
(`SoccerModMvpPlugin.cs:438-457`) — registriert dort den Command.

## Verifikation (mit dem User zusammen — übernehme ich nach dem Deploy)

1. **Priorität 1:** Kick-Zielrichtung in Thirdperson normal? (siehe Risikofrage oben)
2. Kamera-Optik: glatt, kein Ruckeln, angenehme Distanz/Höhe.
3. Messer bleibt sichtbar und funktionsfähig während `!tp` aktiv.
4. Überlebt Respawn ohne erneutes Eintippen.
5. `!tp` aus: sauberer Reset (Ego-Sicht, keine verwaiste Prop-Entity).
6. Zwei Spieler gleichzeitig mit `!tp` an — keine Interferenz zwischen den
   Kamera-Props.
7. Disconnect während `!tp` aktiv → keine verwaiste Entity zurückgelassen.
8. Keine Exceptions/`RESOURCE_TYPE_MODEL`-Fehler im Log; `mp_maxrounds` bleibt
   `999999`; `css_plugins list` weiterhin vollständig.
9. Sobald `!tp` funktioniert: gemeinsam mit `css_sm2teamcolor`/`css_sm2teammodel`
   nutzen, um den TeamColor-Bugfix (Modellpfad-Korrektur, siehe
   `docs/2026-08-31-teamcolor-spec.md`) endlich visuell zu bestätigen.
