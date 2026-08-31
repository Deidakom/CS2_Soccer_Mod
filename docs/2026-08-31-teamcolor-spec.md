# Spec: Team-Farben-Modul (TeamColor) — Phase A + B

**Für:** Codex (Implementierung). **Von:** Claude (Spec + Server-Ops + Verifikation).
**Kontext:** User will sichtbare Team-Trennung — Rot (T) vs. Blau (CT) — wie in CS:S.
Das hier ist Phase A (Render-Tint) + Phase B (einheitliches Stock-Modell pro Team).
Phase C (echte Trikot-Addon-Modelle) ist separat und wartet auf diesen Test.

## Warum ein neues, isoliertes Modul

Das Plugin hat aktuell **keinerlei** Model-/Render-Code (verifiziert per Volltextsuche
über `SetModel`, `m_clrRender`, `RenderColor`, `RenderMode`, `Glow`, `m_nSkin`,
`SetBodygroup` in `src/server-plugin/SoccerModMvp/*.cs`) — reines Neuland, keine
Merge-Konflikte mit bestehendem Code zu erwarten. Bitte trotzdem in einem eigenen
Zeitfenster deployen (nicht parallel zu einem laufenden Ball-Plugin-Deploy), da beide
an derselben Datei-Familie (`SoccerModMvpPlugin.*.cs`) arbeiten und der Server nur
einmal neu geladen werden soll.

## Neue Datei

`src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.TeamColor.cs` — Namenskonvention:
`namespace SoccerMod Mvp; public sealed partial class SoccerModMvpPlugin`, Datei-Suffix
`.TeamColor.cs` (Vorbild: `SoccerModMvpPlugin.BodyImpact.cs`, `SoccerModMvpPlugin.Health.cs`).

## Phase A — Render-Tint

**Mechanik (verifiziert gegen ein funktionierendes CS2-CounterStrikeSharp-Plugin,
ABKAM2023/CS2-TeamColorChangeModel):**
```csharp
pawn.Render = System.Drawing.Color.FromArgb(r, g, b);
Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
```
Reihenfolge wichtig: `Render`-Property setzen, DANN `SetStateChanged` — sonst wird die
Änderung nicht ans Netzwerk propagiert (Haus-Muster im Repo, siehe
`SoccerModMvpPlugin.Health.cs:66,85,92` und `SoccerModMvpPlugin.Match.cs:242,247`
für weitere `SetStateChanged`-Beispiele nach Schema-Writes).

**Farben (Default):** T = `(255, 40, 40)` (Rot), CT = `(40, 80, 255)` (Blau).
Reines `(255,0,0)`/`(0,0,255)` bewusst vermieden — dunkelt die Standard-Texturen zu
stark ab, laut Community-Erfahrung mit diesem Ansatz. Als Startwerte nehmen, im
In-Game-Test ggf. anpassen (siehe Config-Keys unten).

## Phase B — Einheitliches Stock-Modell pro Team

**Modellpfade (verifiziert über ein zweites, unabhängiges CS2-Plugin,
Challengermode/cm-cs2-defaultskins — exakt dieselbe Anwendung im selben Pattern):**
```csharp
private const string ModelPathT  = "characters/models/tm_phoenix/tm_phoenix.vmdl";
private const string ModelPathCt = "characters/models/ctm_sas/ctm_sas.vmdl";
```
Anwendung:
```csharp
Server.NextFrame(() =>
{
    if (pawn is { IsValid: true })
    {
        pawn.SetModel(team == CsTeam.Terrorist ? ModelPathT : ModelPathCt);
    }
});
```
Das sind Stock-CS2-Base-Game-Assets (in jedem CS2-Client bereits vorhanden) — **NICHT**
ins Precache-Manifest eintragen (siehe Warnung unten). Reihenfolge im Spawn-Ablauf:
Modell (Phase B) zuerst setzen, danach Tint (Phase A) — ein `SetModel`-Call kann
zuvor gesetzte Render-Properties zurücksetzen.

## ⚠ Precache-Falle — nicht wiederholen

`SoccerModMvpPlugin.cs:459-472` registriert `Listeners.OnServerPrecacheResources`
mit einem Kommentar über einen bereits erlebten Fehler: `manifest.AddResource(...)`
für ein Nicht-Addon-Modell verursacht einen "missing-model cascade". Die Stock-Pfade
oben brauchen **keinen** `AddResource`-Eintrag (sie sind Teil des Basisspiels) —
NICHTS an dieser Stelle in `SoccerModMvpPlugin.cs` anfassen.

## Einhängepunkt: `OnPlayerSpawn`

`SoccerModMvpPlugin.cs:628-650`, aktueller Stand (nicht verändern, nur ergänzen):
```csharp
private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
{
    var player = @event.Userid;
    if (player is null || !player.IsValid) return HookResult.Continue;

    ResetSprint(player);
    HealthOnPlayerSpawn(player);
    WebsiteCapOnPlayerSpawn(player);
    RefereeEnforceOnSpawn(player);
    MenuMaybeSendBindReminder(player);
    SnapshotPlayer(player, "spawn_event");
    Server.NextFrame(() => SnapshotPlayerIfValid(player, "spawn_next_frame"));
    AddTimer(0.25f, () =>
    {
        SnapshotPlayerIfValid(player, "spawn_plus_0_25s_pre_grant");
        EnsurePlayerKnife(player, "spawn_plus_0_25s");
    }, TimerFlags.STOP_ON_MAPCHANGE);
    AddTimer(1.0f, () => SnapshotPlayerIfValid(player, "spawn_plus_1_00s"), TimerFlags.STOP_ON_MAPCHANGE);
    return HookResult.Continue;
}
```
**Ergänzen:** ruf `TeamColorOnPlayerSpawn(player)` direkt nach `RefereeEnforceOnSpawn(player)`
auf. Innerhalb von `TeamColorOnPlayerSpawn` selbst das Haus-Muster wiederholen: sofort
versuchen (`Server.NextFrame`) UND im bestehenden 0.25s-Timer re-assertieren (dieselbe
`AddTimer(0.25f, ...)`-Closure erweitern, NICHT einen zweiten Timer aufmachen) — der
Code geht davon aus, dass ein reiner Spawn-Frame-Write nicht zuverlässig hält
(siehe Kommentar-Kontext um `EnsurePlayerKnife` an derselben Stelle).

Team-Bestimmung: `player.Team` (Typ `CsTeam`), Vorbild
`SoccerModMvpPlugin.cs:2328` — `player.Team == CsTeam.Terrorist ? ... : ...`.
Pawn-Zugriff: `player.PlayerPawn.Value` (immer auf `IsValid` prüfen).

Zusätzlich: Round-Start-Sweep registrieren, analog zu `EnsureAllPlayerKnives` bei
`SoccerModMvpPlugin.cs:619,622` (next-frame + 0.25s nach `OnRoundStart`) — iteriert
`Utilities.GetPlayers()`, wendet Tint+Modell auf alle lebenden Spieler beider Teams an.

## Commands + Persistenz

Zwei unabhängige Toggles (Tint kann ohne Modell-Swap laufen, falls letzterer Probleme
macht):

```
css_sm2teamcolor <on|off>
css_sm2teammodel <on|off>
```

Muster 1:1 aus `SoccerModMvpPlugin.BodyImpact.cs:346-360` übernehmen
(`OnBallImpactToggleCommand`):
```csharp
private void OnTeamColorToggleCommand(CCSPlayerController? player, CommandInfo command)
{
    if (!RequirePermission(player, command, "match")) return;
    if (command.ArgCount >= 2)
    {
        _teamColorEnabled = command.GetArg(1).Equals("on", StringComparison.OrdinalIgnoreCase);
        SaveMatchSettings("team_color_toggle_command");
    }
    command.ReplyToCommand($"[SM] team color tint: {(_teamColorEnabled ? "on" : "off")} (usage: css_sm2teamcolor <on|off>)");
}
```
(Analog für `css_sm2teammodel` / `_teamModelEnabled`.) Permission-Flag `"match"`
gewählt (Gameplay-Präsentation, kein Ball-spezifisches Setting) — bei Bedarf anpassen.

**Persistenz über `MatchSettingsStore`** (`SoccerModMvpPlugin.Config.cs:438-555`,
NICHT `BallSettingsStore` — das ist der falsche Store für dieses Feature):
- Neue Properties in der Store-Klasse, **nullable** (Migrationsmuster für
  nachträglich hinzugefügte Felder, Vorbild `Config.cs:130-131`):
  `bool? TeamColorEnabled`, `bool? TeamModelEnabled`,
  `int? TeamColorTr/Tg/Tb`, `int? TeamColorCtr/Ctg/Ctb`.
- Load-Guard in `MatchSettingsOnLoad` (Vorbild `Config.cs:205-209` — bools
  unconditional, Zahlen nur wenn im gültigen Bereich, hier `0..255`):
  ```csharp
  if (stored.TeamColorEnabled is { } colorEnabled) _teamColorEnabled = colorEnabled;
  if (stored.TeamColorTr is { } tr && tr is >= 0 and <= 255) _teamColorTr = tr;
  // ... analog für alle Kanäle + TeamModelEnabled
  ```
- Save-Snapshot in `SaveMatchSettings` ergänzen (Vorbild `Config.cs:264-265`).

## Modul-Wiring (`Load`)

`TeamColorOnLoad()` in den bestehenden `*OnLoad`-Block einreihen
(`SoccerModMvpPlugin.cs:438-457`, NACH `MatchSettingsOnLoad` da das Modul dessen
geladene Werte braucht) — registriert dort beide Commands. Kein `OnTick`-Bedarf
(Spawn-Event + Round-Start-Sweep reichen; die 0.25s-Timer sind bereits event-getrieben).

## Bekannte Nebenwirkungen zu prüfen

- WeaponPaints-Handschuhe (`pawn.EconGloves`) nach `SetModel` — visuell separat vom
  Körpermodell, sollte unberührt bleiben, aber im Test explizit gegenchecken.
- `EnsurePlayerKnife` (Messer-Grant) unabhängig vom Körpermodell — sollte nicht
  kollidieren, da unterschiedliche Entities (Waffe vs. Pawn-Modell).
- Bots: `IsEligiblePlayer` (`SoccerModMvpPlugin.cs:2949-2965`) schließt Bots aus —
  falls Bots ebenfalls eingefärbt werden sollen, NICHT diesen Helper für den
  Team-Color-Gate verwenden, sondern nur auf `player.Team` prüfen. Für Phase A/B
  reicht ein einfacher Team-Check ohne Bot-Ausschluss (die Sichtbarkeit soll für
  alle sichtbaren Spieler gelten).

## Verifikation (nach Deploy, mit dem User zusammen — von mir/Claude übernommen)

1. Beide Teams spawnen sichtbar rot/blau (Distanz-Check auf Stadiongröße).
2. Farbe/Modell überleben Respawn UND `mp_restartgame`/Round-Restart.
3. `css_sm2teamcolor off` → Normalzustand (Standard-CS2-Skin, keine Tönung);
   `css_sm2teammodel off` → zurück zu Original-Client-Auswahl.
4. Persistenz: Service-Restart → Einstellungen bleiben (aus `soccermod_match_settings.json`).
5. Keine Exceptions/`status=139` im CSSharp-Log (`log-all<datum>.txt`), `mp_maxrounds`
   bleibt `999999`, `css_plugins list` zeigt das Plugin weiterhin `LOADED`.
6. WeaponPaints-Handschuhe + Messer weiterhin korrekt.

## Nicht in diesem Spec (bewusst draußen)

Phase C (Workshop-Addon-Trikots, `!gk`-Skin-Toggle) — MAM ist bereits inert gestaged,
Scharfschaltung ist User-Aktion, Content-Pipeline übernehme ich (Claude) separat nach
bestandenem A/B-Test. Kein `AddResource`, kein `mm_extra_addons` in diesem Schritt.
