# Spec: `!gk` Torwart-Tönung (Weiß für CT, Orange-Neon für T)

**Für:** Codex (Implementierung). **Von:** Claude (Spec + Server-Ops + Verifikation).
**Kontext:** User will pro Team einen visuell markierten Torwart — CT-Torwart
"White Neon Tint", T-Torwart "Orange Neon Skin". Baut direkt auf dem bereits
live bestätigten TeamColor-Mechanismus auf (`SoccerModMvpPlugin.TeamColor.cs`,
`pawn.Render` + `SetStateChanged` — Server-Logs bestätigen mehrfach korrekte
Ausführung bei jedem Spawn/Rundenstart).

## CS:S-Referenz existiert (anders als beim `!tp`-Feature)

`somoe19-original\...\modules\skins.sp` hatte ein echtes `!gk`-Äquivalent
(`sm_gk` → `ClientCommandSetGoalkeeperSkin`). Kernmechanik, die hier 1:1 als
Verhalten übernommen werden soll (nicht der Code selbst, der ist SourcePawn):
- Genau **1 GK pro Team** — zweiter Claim-Versuch wird abgelehnt
  ("Only 1 goalkeeper skin per team allowed." — Referenz-Wortlaut, Ton für
  unsere Reply-Message übernehmen, kein Blocker).
- **Claim/Release**: Toggle-Befehl, kein Admin-Flag — jeder Spieler kann sich
  selbst zum GK seines Teams machen, wenn der Slot frei ist.
- **Freigabe bei Teamwechsel und Disconnect** — der GK-Status ist an
  Team-Zugehörigkeit gebunden, nicht an eine Spieler-Identität über
  Sessions hinweg.
- **Self-Healing-Sweep**: die Referenz hat zusätzlich eine Funktion, die den
  Zustand aus der Spielerliste neu ableitet (Robustheit gegen Drift) — hier
  nicht zwingend nötig, weil unser State einfacher ist (kein Cross-Referenzieren
  von Client-Modellnamen wie im Original), aber die Disconnect/Team-Change-
  Hooks müssen zuverlässig sein.

## Aktueller Code-Stand — keine Torwart-Identität vorhanden

`SoccerModMvpPlugin.GkAreas.cs` hat NUR geometrische Save-Erkennung
(`_gkArmedSaverSlot`/`_gkArmedSaverTeam` — transient, nur während ein
Save-Versuch "läuft"). Keine persistente "das ist unser Torwart"-Markierung.
Dieses Feature ist komplett neu, keine bestehende Struktur zu erweitern außer
dem Render-Aufruf in TeamColor.

## Neue Datei

`src/server-plugin/SoccerModMvp/SoccerModMvpPlugin.GkSkin.cs`

```csharp
private readonly Dictionary<CsTeam, int> _gkSlotByTeam = new(); // CT/T -> Slot, kein Eintrag = kein GK
```

### Command

`css_sm2gk` (Chat-Alias `!gk` automatisch, analog zum `!tp`-Muster: zusätzlich
`css_gk` als eigener Command registrieren, damit CSSharps Alias-Stripping exakt
`!gk` ergibt — Vorbild `ThirdPerson.cs:24-27`, dort `css_tp` neben
`css_sm2thirdperson`). **Kein** `RequirePermission`-Gate — jeder Spieler
selbst-claimt, passend zum CS:S-Original.

### Toggle-Logik

- Spieler ohne gültiges Team (nicht T/CT) → Fehlermeldung, kein Claim.
- Spieler IST bereits der GK seines Teams → Toggle AUS: Eintrag aus
  `_gkSlotByTeam` entfernen, sofort `ApplyTeamAppearance` für diesen Spieler
  neu anwenden (fällt zurück auf normale Team-Tönung).
- Spieler ist NICHT der GK, aber der Team-Slot ist BEREITS belegt (von jemand
  anderem) → Ablehnen, Reply im Ton der Referenz: z.B.
  `"[SM] only one goalkeeper skin allowed per team — <Name> already has it"`.
- Spieler ist NICHT der GK, Slot ist frei → Claim: Eintrag setzen,
  `ApplyTeamAppearance` sofort neu anwenden (GK-Farbe greift).

### Freigabe bei Teamwechsel und Disconnect

- **Disconnect**: neue Funktion `GkSkinOnPlayerDisconnect(int slot)`,
  registriert wie die bestehenden Disconnect-Listener
  (`SoccerModMvpPlugin.cs:452-457`) — falls der disconnectende Slot irgendwo
  in `_gkSlotByTeam` als Wert steht, den Eintrag entfernen.
- **Teamwechsel**: prüfen, ob es bereits einen Hook für Team-Change gibt
  (`EventPlayerTeam` o.ä. — im Code suchen, ob `RegisterEventHandler<EventPlayerTeam>`
  schon existiert; falls nicht, neu registrieren). Beim Wechsel: falls der
  wechselnde Spieler GK seines ALTEN Teams war, Eintrag dort entfernen (er
  nimmt den GK-Status nicht automatisch ins neue Team mit — müsste dort neu
  `!gk` tippen, passend zur CS:S-Logik "1 pro Team", nicht "1 pro Spieler").

## Integration in die Render-Tönung

**Minimal-Change in `ApplyTeamAppearance`** (`SoccerModMvpPlugin.TeamColor.cs`,
aktueller Stand um Zeile 79-82 — exakte Zeilen beim Implementieren neu lesen,
der Diagnostik-Logging-Zusatz von heute hat die Datei bereits leicht verschoben):

```csharp
pawn.Render = !_teamColorEnabled
    ? Color.White
    : IsGkSlot(player.Slot, player.Team)
        ? GkRenderColor(player.Team)
        : TeamRenderColor(player.Team);
Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");
```

Neue kleine Helper (in der neuen `GkSkin.cs`-Datei, da partial class):
```csharp
private bool IsGkSlot(int slot, CsTeam team) =>
    _gkSlotByTeam.TryGetValue(team, out var gkSlot) && gkSlot == slot;

private static Color GkRenderColor(CsTeam team) => team == CsTeam.CounterTerrorist
    ? Color.FromArgb(255, 255, 255)   // "White Neon" -- Hinweis unten lesen
    : Color.FromArgb(255, 140, 0);    // "Orange Neon"
```

**Modell-Verhalten**: GK behält bei aktiviertem `css_sm2teammodel` weiterhin
das normale Team-Stock-Modell (Phoenix/SAS) — nur die Tönung ändert sich. Der
User bat explizit um einen "Tint"/"Skin" (Farbe), nicht um eine andere
Körperform — falls das falsch verstanden ist, im Test klären, nicht vorab
annehmen.

## ⚠ Hinweis zu "Neon" — dieselbe Einschränkung wie bei den Team-Farben

`Render` ist ein Multiply-Tint auf die vorhandene Textur, kein echtes Glühen.
`Color.White` (255,255,255) ist rechnerisch ein No-Op-Multiply — der CT-Torwart
erscheint schlicht UNGETÖNT (Original-Textur-Helligkeit), was ihn trotzdem klar
von den cyan-blau getönten Mitspielern abhebt, aber nicht "leuchtend weiß"
im Sinne eines echten Glow-Effekts ist. Falls das dem User zu schwach vorkommt,
im Test ansprechen statt stillschweigend hinzunehmen.

## Modul-Wiring

`GkSkinOnLoad()` in den bestehenden `*OnLoad`-Block einreihen
(`SoccerModMvpPlugin.cs:438-457`, NACH `TeamColorOnLoad` da von dessen Zustand
abhängig) — registriert dort den Command + ggf. den Team-Change-Handler.

## Verifikation

1. `!gk` als T-Spieler → sofort Orange-Neon-Tönung, Teamkollegen bleiben
   normal rot/pink getönt.
2. `!gk` als CT-Spieler → sofort weiß/ungetönt, Teamkollegen bleiben cyan-blau.
3. Zweiter Spieler versucht `!gk` im selben Team → Ablehnung mit klarer
   Meldung, wer bereits GK ist.
4. `!gk` nochmal vom aktuellen GK → Toggle aus, zurück zur normalen
   Team-Tönung.
5. GK disconnectet → Slot wird frei, nächster Spieler kann `!gk` claimen.
6. GK wechselt Team → verliert GK-Status im alten Team, muss im neuen Team
   neu claimen.
7. Überlebt Respawn und Rundenstart (folgt automatisch aus der bestehenden
   `ApplyTeamAppearance`-Reassert-Kette, sofern `IsGkSlot` korrekt geprüft wird).
8. Keine Exceptions im Log; `css_plugins list` weiterhin vollständig.
