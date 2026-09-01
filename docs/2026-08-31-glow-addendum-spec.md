# Addendum-Spec: Echtes Leuchten (CS2 Glow) zusätzlich zum Textur-Tint

**Für:** Codex. **Von:** Claude. **Kontext:** User will "richtig leuchtende" Farben —
der bisherige `pawn.Render`-Multiply-Tint (TeamColor + GK) bleibt bestehen, wird
aber um einen echten CS2-Glow-Silhouetten-Effekt in derselben Farbe ERGÄNZT.

**User-Entscheidungen:** Glow nur bei Sichtkontakt (nicht durch Wände) ·
zusätzlich zum bestehenden Tint, nicht als Ersatz.

## Mechanik (verifiziert)

CSSharp exponiert das Source-2-Glow-System direkt auf jeder
`CBaseModelEntity` (bestätigt über die offizielle API-Doku,
`docs.cssharp.dev`): `Glow`-Property vom Typ `CGlowProperty`. Unser eigener
Code behandelt den Player-Pawn bereits erfolgreich als `CBaseModelEntity`
für den bestehenden Tint (`Utilities.SetStateChanged(pawn, "CBaseModelEntity",
"m_clrRender")` in `TeamColor.cs` — live bestätigt funktionsfähig), also sollte
`pawn.Glow` direkt ansprechbar sein, ohne Umweg über eine zusätzliche Prop-Entity.

**Felder von `CGlowProperty`** (verifiziert über den Source-2-Schema-Dump,
`s2v.app/SchemaExplorer/cs2/server/CGlowProperty` — Namen exakt, aber OHNE
dokumentierte Enum-Werte):
```
m_bGlowing              bool     -- Master-Schalter
m_glowColorOverride     Color    -- die Glow-Farbe (RGB + vermutlich Alpha)
m_iGlowType             int32    -- KEINE dokumentierten Enum-Werte gefunden
m_iGlowTeam             int32    -- wer den Glow sehen darf, KEINE Enum-Werte gefunden
m_nGlowRange            int32
m_nGlowRangeMin         int32
m_flGlowTime / m_flGlowStartTime
m_bEligibleForScreenHighlight
m_bFlashing
```
CSSharp exponiert diese vermutlich als benannte C#-Properties auf `CGlowProperty`
(Vorbild aus einem echten Community-Plugin, exkludera-cssharp/glowing-entities):
```csharp
entity.Glow.GlowColorOverride = someColor;
entity.Glow.GlowRange = 5000;
entity.Glow.GlowRangeMin = 0;
entity.Glow.GlowTeam = someTeamValue;
entity.Glow.GlowType = someTypeValue; // die Referenz nutzte 2 (GlowOnAim) und 3 (immer)
```

## ⚠ Nicht verifiziert — bitte in der CSSharp-API-Assembly selbst nachsehen

`m_iGlowType` und `m_iGlowTeam` sind reine `int32`, keine öffentlich
dokumentierten Enum-Werte gefunden (weder in der Schema-Doku noch in
Community-Plugin-Docs). Bevor geraten wird: In der lokal referenzierten
`CounterStrikeSharp.API`-Assembly (NuGet-Paket, liegt im Projekt-Cache) nach
`CGlowProperty`/`GlowType`/`GlowTeam` suchen — falls CSSharp dafür ein echtes
C#-Enum bereitstellt (wahrscheinlich, gegeben wie sauber `Glow` sonst gewrappt
ist), steht die Bedeutung der Werte dort mit Namen, nicht nur Zahlen. Falls
nur `int` ohne Enum: **Sichtkontakt-only ist NICHT garantiert einfach als ein
Zahlenwert erreichbar** — das war die vom User bevorzugte, aber technisch
unsicherere Option (siehe Frage, die ich dem User gestellt habe). Bitte im
Test ehrlich zurückmelden, welcher Wert am nächsten an "nur bei Sichtkontakt"
herankommt, statt eine Vermutung als Tatsache zu behandeln — falls sich
herausstellt, dass CS2 zuverlässig NUR "immer sichtbar" (durch Wände) anbietet,
das dem User so zurückmelden, nicht stillschweigend durch-Wände-Glow ausliefern.

`GlowTeam`: muss so gesetzt werden, dass **beide Teams** den Glow sehen (nicht
nur die eigene Mannschaft) — das ist der ganze Zweck der visuellen Trennung.
Falls es einen "kein Team-Filter"-Wert gibt (vermutlich -1 oder 0, empirisch
prüfen), den verwenden.

## Integration in `ApplyTeamAppearance`

Aktueller Stand der Datei (`SoccerModMvpPlugin.TeamColor.cs`, GK-Integration ist
bereits von dir eingebaut — `isGk`/`GkRenderColor` existieren schon):
```csharp
var isGk = IsGkSlot(player.Slot, player.Team);
var color = !_teamColorEnabled
    ? Color.White
    : isGk ? GkRenderColor(player.Team) : TeamRenderColor(player.Team);
pawn.Render = color;
Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_clrRender");

// NEU: derselbe `color`-Wert auch für den Glow verwenden, damit Tint und
// Glow immer exakt dieselbe Farbe zeigen -- eine Quelle der Wahrheit, keine
// zweite Farbdefinition parallel pflegen.
pawn.Glow.GlowColorOverride = color;
pawn.Glow.GlowRange = <empirisch, Startwert 4000>;
pawn.Glow.GlowRangeMin = 0;
pawn.Glow.GlowTeam = <empirisch ermittelter "alle sehen es"-Wert>;
pawn.Glow.GlowType = <empirisch ermittelter "nur bei Sichtkontakt"-Wert>;
pawn.Glow.Glowing = _teamColorEnabled; // Master-Schalter synchron zum Tint-Toggle
Utilities.SetStateChanged(pawn, "CBaseModelEntity", "m_Glow");
```
(Property-Namen in C# ggf. leicht anders als die rohen `m_`-Feldnamen — beim
Implementieren gegen die tatsächliche `CGlowProperty`-Klasse in der
`CounterStrikeSharp.API`-Assembly prüfen, nicht blind übernehmen.)

**Wenn `_teamColorEnabled` aus ist**: Glow ebenfalls komplett deaktivieren
(`Glowing = false`), nicht nur die Tint-Farbe auf Weiß setzen — sonst bliebe
ein weißer Glow-Rand sichtbar, obwohl der User die Funktion ausgeschaltet hat.

## Kein neuer Command nötig

Glow hängt direkt am bestehenden `css_sm2teamcolor on|off` — kein separater
Toggle, es ist eine Erweiterung derselben Funktion, kein eigenständiges Feature.

## Verifikation

1. **Wichtigste Frage zuerst**: Ist der Glow tatsächlich nur bei Sichtkontakt
   sichtbar, oder scheint er durch Wände/das Stadion-Dach? Ehrlich zurückmelden.
2. Farbe von Glow und Tint stimmen überein (T=Neon-Rot, CT=Neon-Cyan-Blau,
   GK-CT=Weiß, GK-T=Orange).
3. Beide Teams sehen den Glow aller Spieler (nicht nur die eigene Seite).
4. `css_sm2teamcolor off` → auch der Glow verschwindet vollständig, kein
   Rest-Leuchten.
5. Überlebt Respawn/Rundenstart wie der bestehende Tint (folgt automatisch,
   da in derselben Funktion).
6. Keine Exceptions im Log; Performance-Check falls spürbar (Glow ist eine
   Server-seitig recht günstige Networked-Property, sollte unauffällig sein,
   aber bei vielen Spielern gleichzeitig im Blick behalten).
