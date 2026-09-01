const ROLE_CAPACITY = { GK: 2, DEF: 4, MID: 2, WING: 4 };
const ROLE_SHORT = { GK: "GK", DEF: "DF", MID: "MF", WING: "WG" };
const ROLE_CLASS = { GK: "gk", DEF: "def", MID: "mid", WING: "wing" };
// Example values. Replace these with your own server address/password
// (or wire them up from your backend) before deploying.
const GAME_OPTIONS = {
  css: {
    label: "Counter-Strike: Source",
    short: "CS:S",
    maps: ["Your CS:S Map"],
    server: { address: "YOUR_SERVER_IP:PORT", password: "CHANGE_ME" }
  },
  cs2: {
    label: "Counter-Strike 2",
    short: "CS2",
    maps: ["soccer_cssl_stadium_v8"],
    server: { address: "YOUR_SERVER_IP:PORT", password: "CHANGE_ME" }
  }
};

const COPY = {
  en: {
    "nav.play":"Cap","nav.caps":"Caps","nav.ranking":"Statistics","nav.create":"Create cap","server.online":"Online","steam.signin":"Sign in with Steam","theme.light":"Use light mode","theme.dark":"Use dark mode",
    "hero.open":"Open cap","hero.tagline":"Community-organized 6v6 football for Counter-Strike 2 and Counter-Strike: Source.","hero.game":"Game","hero.mode":"Mode","hero.sixvSix":"6 versus 6","hero.created":"Created by","hero.players":"Players in cap",
    "rail.queue":"Gather players","rail.ready":"Ready check","rail.seconds":"60 seconds","rail.draw":"Draw","rail.teams":"Teams & positions","rail.server":"Server","rail.connect":"Connect directly","team.home":"Home Team","team.away":"Away Team","team.red":"Red","team.blue":"Blue",
    "queue.roster":"Current roster","queue.title":"Cap","queue.fill":"Demo: fill queue","queue.rules":"Cap rules","queue.coverageNote":"Main and optional positions are preferences. If a formation slot remains empty, the draw assigns an emergency fill.","queue.leave":"Leave cap",
    "join.signup":"Your signup","join.verified":"Steam verified","join.title":"Where do you want to play?","join.help":"Choose at least two field positions. Goalkeeper is an exclusive selection.","join.mainPosition":"Main position","join.mainPlaceholder":"Select positions first","join.mainHelp":"The draw favors this position. Your other selections remain optional.","join.fair":"Preference-first draw","join.drawNote":"Main positions are favored first, then optional positions. An emergency fill is used only when needed.",
    "role.exclusive":"Exclusive position","role.twoSlots":"2 slots per team","role.oneSlot":"1 slot per team","role.gkCapacity":"2 total · 1 per team","role.defCapacity":"4 total · 2 per team","role.midCapacity":"2 total · 1 per team","role.wingCapacity":"4 total · 2 per team",
    "match.found":"Match found","match.lineup":"YOUR LINEUP IS READY","match.locked":"The draw is saved and cannot be repeated.","match.yourRole":"Your position","match.emergencyFill":"Emergency fill","match.sixPlayers":"6 players","match.playerCount":"{count} players","match.onePlayer":"1 player","match.serverReadyCss":"Counter-Strike: Source server is ready","match.serverDetailCss":"Frankfurt · Password included automatically","match.serverPreparing":"Preparing the CS:S server","match.serverPreparingDetail":"Current players are being removed for the new cap.","match.serverUnavailable":"CS2 server not configured yet","match.serverUnavailableDetail":"Auto-join will be enabled when the CS2 server details are added.","match.serverPrepareFailed":"The CS:S server could not be prepared","match.serverPrepareFailedDetail":"Auto-join is temporarily unavailable. An admin can try again.","match.copy":"Copy command","match.autojoinCss":"Auto-join CS:S","match.unavailable":"Not available yet","match.audit":"Verify draw","match.algorithm":"Algorithm","match.auditNote":"The draw maximizes main choices, then optional choices. Unselected positions are used only to complete the formation.",
    "footer.note":"Community preview · Steam sign-in is verified through Steam OpenID.","footer.top":"Back to top ↑",
    "admin.endTest":"End Test Mode",
    "login.kicker":"Secure Steam account","login.title":"Sign in through Steam","login.description":"Continue to Steam Community. Steam verifies your account and returns only your public SteamID64.","login.warning":"Your Steam password is entered only on steamcommunity.com.","login.verified":"Verified Steam account","login.displayName":"Steam name","login.steamId":"SteamID64","login.profile":"View Steam profile ↗","login.signout":"Sign out","login.failed":"Steam could not verify the sign-in. Please try again.","login.cancelled":"Steam sign-in was cancelled.",
    "admin.manage":"Manage users","admin.emptyQueue":"Empty queue","admin.emptyConfirm":"Remove every player from this queue?","admin.remove":"Remove","admin.removeConfirm":"Remove {name} from the queue?","admin.removed":"{name} was removed from the queue.","admin.removedYou":"An admin removed you from the queue.","admin.testOn":"Test mode: On","admin.testOff":"Test mode: Off","admin.testActive":"Admin test mode active","admin.testNotice":"Starts with every player currently queued. No 12-player minimum or temporary players.","admin.testStarted":"Test Mode started with {count} queued players.","admin.testEnded":"Test Mode ended · {count} players removed from the queue.","admin.kicker":"Administration","admin.title":"Users and access","admin.description":"Assign admin access only to people you trust. Every other verified Steam account remains a regular user.","admin.user":"User","admin.admin":"Admin","admin.owner":"Owner","admin.saved":"Access updated.","admin.emptied":"Queue emptied · {count} players removed.","admin.loadFailed":"Admin data could not be loaded.","queue.empty":"No players are queued yet. Sign in with Steam and choose your positions.","queue.testWaiting":"Test Mode · queue is empty","queue.testReady":"Test Mode · {count} queued players ready",
    "community.kicker":"Community intelligence","community.title":"SOCCERMOD STATISTICS","community.description":"Live figures from the Soccer Mod server and caps organized through KICKOFF.","community.loading":"Loading server data","community.live":"Live Soccer Mod data","community.unavailable":"Soccer Mod data unavailable","community.capsPlayed":"Caps recorded","community.capsNote":"Completed matchmaking draws, excluding admin tests","community.registered":"Registered players","community.active":"{count} active in the last 30 days","community.appearances":"Match appearances","community.appearancesNote":"Player appearances stored by Soccer Mod","community.goals":"Goals scored","community.assists":"{count} assists recorded","community.positionsKicker":"Signup preferences","community.positionsTitle":"Main position demand","community.positionsDescription":"A player's latest main-position choice is counted once. Optional positions do not inflate the result.","community.players":"{count} players","community.player":"1 player","community.activityKicker":"On the ball","community.activityTitle":"Gameplay activity","community.allTime":"All time","community.insightTitle":"Reading the game","community.noGameplay":"The server is connected, but no competitive Soccer Mod match statistics have been stored yet.","community.recovery":"Teams recovered the ball in {rate}% of recorded turnover events.","community.leadersKicker":"Performance","community.leadersTitle":"Top contributors","community.points":"Ranking points","community.noLeaders":"The leaderboard will appear after the first recorded competitive match.","community.matches":"{count} matches","community.recentKicker":"Match history","community.recentTitle":"Recent caps","community.noCaps":"No full cap has been recorded yet. Admin Test Mode runs are excluded.","community.methodTitle":"What Soccer Mod measures","community.methodText":"Goals, assists, own goals, ball contacts, passes, interceptions, ball losses, goalkeeper saves, period results, MVP and player of the match come directly from the mod's statistics system.","community.passes":"Passes","community.interceptions":"Interceptions","community.saves":"Saves","community.contacts":"Ball contacts","community.losses":"Ball losses","community.mvp":"MVP awards","community.motm":"Player of match","community.ownGoals":"Own goals",
    "create.kicker":"New cap","create.title":"Create cap","create.description":"Choose the game first. Each game uses its own supported map.","create.name":"Cap name","create.game":"Game","create.map":"Map","create.ready":"60 sec ready","create.publish":"Publish cap",
    "ready.found":"Match found","ready.title":"ARE YOU READY?","ready.description":"All twelve players must accept. Teams and positions are drawn afterward.","ready.testDescription":"Test Mode includes every real player currently queued and ignores the 12-player minimum.","ready.count":"/ {count} ready","ready.accept":"I am ready","ready.timeout":"If you do not accept, your slot will be released.",
    "rules.kicker":"How it works","rules.title":"CAP RULES","rules.choose":"Choose positions","rules.chooseText":"GK only, or at least two field positions.","rules.confirm":"Confirm readiness","rules.confirmText":"At 12/12, every player has 60 seconds.","rules.fair":"Preference-first draw","rules.fairText":"Main and optional positions are prioritized. Emergency fills only complete missing slots.","rules.direct":"Connect directly","rules.directText":"The server unlocks after the draw.",
    "role.GK":"Goalkeeper","role.DEF":"Defence","role.MID":"Midfield","role.WING":"Wing","creator":"CAP LEADER","you":"YOU","now":"now","since":"since","available":"optional","required":"required","mainApplicants":"main","joinedCount":"{count} of 12 joined","playersAria":"{count} of 12 players","full":"Roster complete · Ready check","missing":"{count} players needed","missingOne":"1 player needed","valid":"Valid selection: {roles}.","validPriority":"{main} is main · {optional} optional.","chooseMain":"Choose your main position.","selectMore":"Select {count} more field positions.","savePositions":"Save positions","joinCap":"Join cap","profileConnected":"Steam account verified. Choose your positions.","queueFull":"The cap is full. You are on the waitlist.","noCompatible":"No compatible roster slot is available for this selection.","positionsSaved":"Positions saved.","joined":"You joined · slot {count}.","left":"You left the cap.","demoFilled":"Demo: the roster was filled with compatible players.","readyExpired":"Ready check expired. The cap is open again.","readyConfirmed":"Ready confirmed ✓","drawing":"Everyone is ready. Drawing teams and positions …","matchReady":"Match ready. Your position is highlighted.","capPublished":"{name} cap was published.","copied":"Connection command copied.","copyFailed":"Copy unavailable – please select the command.","signedAs":"Signed in as {name}.","liveJoined":"{name} joined the cap.","liveWaiting":"Waiting for the first player to join.","liveLeft":"{name} left the cap.","liveSaved":"Your positions were updated.","spectator":"Spectator","activity.policy":"Queue presence is checked every 10 minutes.","activity.kicker":"Queue presence check","activity.title":"Are you still here?","activity.description":"Confirm now to keep your place in the cap.","activity.timeLeft":"Time remaining","activity.confirm":"I'm still here","activity.warning":"If you do not confirm, you will be removed from the queue.","activity.confirmed":"Presence confirmed. Your place is safe for another 10 minutes.","activity.expired":"You missed the activity check and were removed from the queue.","activity.removed":"You are no longer in the queue."
    ,"join.help":"Choose at least two positions, including Goalkeeper. Set one as Main and the others as Optional.","rules.chooseText":"Choose at least two positions. Goalkeeper can be Main or Optional.","selectMore":"Positions still required: {count}."
    ,"admin.searchPlaceholder":"Search by name or SteamID","admin.noResults":"No users match your search.","profile.manage":"Manage profile","profile.kicker":"Player profile","profile.title":"Customize your profile","profile.description":"Your Steam name remains your KICKOFF identity. Add details that help other players understand when and how you play.","profile.steamManaged":"Name managed by Steam","profile.caps":"Caps played","profile.mostRole":"Most played","profile.mainPreference":"Current main","profile.notSet":"Not set","profile.country":"Country / region","profile.countryPlaceholder":"e.g. Germany","profile.favoriteGame":"Favorite game","profile.availability":"Usually available","profile.flexible":"Flexible","profile.weekdayEvenings":"Weekday evenings","profile.weekends":"Weekends","profile.lateNight":"Late night","profile.bio":"About you","profile.bioPlaceholder":"Soccer Mod experience, play style or team preferences","profile.save":"Save profile","profile.saved":"Profile saved.","profile.loadFailed":"Profile could not be loaded."
  },
  de: {
    "nav.play":"Cap","nav.caps":"Caps","nav.ranking":"Statistiken","nav.create":"Cap erstellen","server.online":"Online","steam.signin":"Mit Steam anmelden","theme.light":"Helles Design verwenden","theme.dark":"Dunkles Design verwenden",
    "hero.open":"Offener Cap","hero.tagline":"Community-organisierter 6v6-Fußball für Counter-Strike 2 und Counter-Strike: Source.","hero.game":"Spiel","hero.mode":"Modus","hero.sixvSix":"6 gegen 6","hero.created":"Erstellt von","hero.players":"Spieler im Cap",
    "rail.queue":"Spieler sammeln","rail.ready":"Bereitschaft","rail.seconds":"60 Sekunden","rail.draw":"Auslosung","rail.teams":"Teams & Positionen","rail.server":"Server","rail.connect":"Direkt verbinden","team.home":"Heimteam","team.away":"Auswärtsteam","team.red":"Rot","team.blue":"Blau",
    "queue.roster":"Aktueller Kader","queue.title":"Cap","queue.fill":"Demo: Queue auffüllen","queue.rules":"Cap-Regeln","queue.coverageNote":"Haupt- und optionale Positionen sind Wünsche. Bleibt ein Platz frei, vergibt die Auslosung eine Notfallposition.","queue.leave":"Cap verlassen",
    "join.signup":"Deine Anmeldung","join.verified":"Steam-verifiziert","join.title":"Wo willst du spielen?","join.help":"Wähle mindestens zwei Feldpositionen. Torwart ist eine exklusive Auswahl.","join.mainPosition":"Hauptposition","join.mainPlaceholder":"Zuerst Positionen wählen","join.mainHelp":"Diese Position wird bei der Auslosung bevorzugt. Die übrigen bleiben optional.","join.fair":"Auslosung nach Priorität","join.drawNote":"Zuerst zählen Hauptpositionen, dann optionale. Eine Notfallposition wird nur vergeben, wenn sie benötigt wird.",
    "role.exclusive":"Exklusive Position","role.twoSlots":"2 Plätze pro Team","role.oneSlot":"1 Platz pro Team","role.gkCapacity":"2 gesamt · 1 pro Team","role.defCapacity":"4 gesamt · 2 pro Team","role.midCapacity":"2 gesamt · 1 pro Team","role.wingCapacity":"4 gesamt · 2 pro Team",
    "match.found":"Match gefunden","match.lineup":"DEINE AUFSTELLUNG STEHT","match.locked":"Die Auslosung wurde gespeichert und kann nicht wiederholt werden.","match.yourRole":"Deine Position","match.emergencyFill":"Notfallposition","match.sixPlayers":"6 Spieler","match.playerCount":"{count} Spieler","match.onePlayer":"1 Spieler","match.serverReadyCss":"Counter-Strike: Source-Server ist bereit","match.serverDetailCss":"Frankfurt · Passwort wird automatisch übergeben","match.serverPreparing":"CS:S-Server wird vorbereitet","match.serverPreparingDetail":"Aktuelle Spieler werden für den neuen Cap entfernt.","match.serverUnavailable":"CS2-Server noch nicht eingerichtet","match.serverUnavailableDetail":"Auto-Join wird aktiviert, sobald die CS2-Serverdaten ergänzt sind.","match.serverPrepareFailed":"Der CS:S-Server konnte nicht vorbereitet werden","match.serverPrepareFailedDetail":"Auto-Join ist vorübergehend nicht verfügbar. Ein Admin kann es erneut versuchen.","match.copy":"Befehl kopieren","match.autojoinCss":"CS:S automatisch beitreten","match.unavailable":"Noch nicht verfügbar","match.audit":"Auslosung prüfen","match.algorithm":"Algorithmus","match.auditNote":"Die Auslosung maximiert Hauptwünsche, dann optionale Wünsche. Nicht gewählte Positionen werden nur zum Vervollständigen der Formation vergeben.",
    "footer.note":"Community-Vorschau · Die Steam-Anmeldung wird über Steam OpenID verifiziert.","footer.top":"Nach oben ↑",
    "admin.endTest":"Testmodus beenden",
    "login.kicker":"Sicheres Steam-Konto","login.title":"Über Steam anmelden","login.description":"Weiter zur Steam Community. Steam bestätigt dein Konto und übermittelt nur deine öffentliche SteamID64.","login.warning":"Dein Steam-Passwort gibst du ausschließlich auf steamcommunity.com ein.","login.verified":"Verifiziertes Steam-Konto","login.displayName":"Steam-Name","login.steamId":"SteamID64","login.profile":"Steam-Profil öffnen ↗","login.signout":"Abmelden","login.failed":"Steam konnte die Anmeldung nicht bestätigen. Bitte erneut versuchen.","login.cancelled":"Die Steam-Anmeldung wurde abgebrochen.",
    "admin.manage":"Nutzer verwalten","admin.emptyQueue":"Queue leeren","admin.emptyConfirm":"Wirklich alle Spieler aus dieser Queue entfernen?","admin.remove":"Entfernen","admin.removeConfirm":"{name} aus der Queue entfernen?","admin.removed":"{name} wurde aus der Queue entfernt.","admin.removedYou":"Ein Admin hat dich aus der Queue entfernt.","admin.testOn":"Testmodus: An","admin.testOff":"Testmodus: Aus","admin.testActive":"Admin-Testmodus aktiv","admin.testNotice":"Startet mit allen aktuell eingereihten Spielern. Kein 12-Spieler-Minimum und keine temporären Testspieler.","admin.testStarted":"Testmodus mit {count} Spielern aus der Queue gestartet.","admin.testEnded":"Testmodus beendet · {count} Spieler aus der Queue entfernt.","admin.kicker":"Administration","admin.title":"Nutzer und Rechte","admin.description":"Vergib Adminrechte nur an vertrauenswürdige Personen. Alle anderen verifizierten Steam-Konten bleiben normale Nutzer.","admin.user":"Nutzer","admin.admin":"Admin","admin.owner":"Eigentümer","admin.saved":"Berechtigung aktualisiert.","admin.emptied":"Queue geleert · {count} Spieler entfernt.","admin.loadFailed":"Admin-Daten konnten nicht geladen werden.","queue.empty":"Noch ist niemand in der Queue. Melde dich mit Steam an und wähle deine Positionen.","queue.testWaiting":"Testmodus · Queue ist leer","queue.testReady":"Testmodus · {count} Spieler bereit",
    "community.kicker":"Community-Übersicht","community.title":"SOCCERMOD-STATISTIKEN","community.description":"Live-Zahlen vom Soccer-Mod-Server und über KICKOFF organisierte Caps.","community.loading":"Serverdaten werden geladen","community.live":"Live-Daten aus Soccer Mod","community.unavailable":"Soccer-Mod-Daten nicht verfügbar","community.capsPlayed":"Erfasste Caps","community.capsNote":"Abgeschlossene Auslosungen, ohne Admin-Tests","community.registered":"Registrierte Spieler","community.active":"{count} aktiv in den letzten 30 Tagen","community.appearances":"Match-Teilnahmen","community.appearancesNote":"Von Soccer Mod gespeicherte Spieler-Teilnahmen","community.goals":"Erzielte Tore","community.assists":"{count} Assists erfasst","community.positionsKicker":"Anmeldewünsche","community.positionsTitle":"Nachfrage nach Hauptpositionen","community.positionsDescription":"Die letzte Hauptposition jedes Spielers zählt einmal. Optionale Positionen erhöhen das Ergebnis nicht.","community.players":"{count} Spieler","community.player":"1 Spieler","community.activityKicker":"Am Ball","community.activityTitle":"Spielaktivität","community.allTime":"Gesamt","community.insightTitle":"Spielverständnis","community.noGameplay":"Der Server ist verbunden, aber es wurden noch keine kompetitiven Soccer-Mod-Matchstatistiken gespeichert.","community.recovery":"Bei {rate}% der erfassten Ballverlust-Situationen wurde der Ball zurückgewonnen.","community.leadersKicker":"Leistung","community.leadersTitle":"Top-Spieler","community.points":"Ranglistenpunkte","community.noLeaders":"Die Rangliste erscheint nach dem ersten erfassten kompetitiven Match.","community.matches":"{count} Matches","community.recentKicker":"Matchverlauf","community.recentTitle":"Letzte Caps","community.noCaps":"Noch wurde kein vollständiger Cap erfasst. Admin-Testläufe werden nicht gezählt.","community.methodTitle":"Was Soccer Mod misst","community.methodText":"Tore, Assists, Eigentore, Ballkontakte, Pässe, Interceptions, Ballverluste, Torwart-Paraden, Periodenergebnisse, MVP und Spieler des Matches kommen direkt aus dem Statistiksystem des Mods.","community.passes":"Pässe","community.interceptions":"Interceptions","community.saves":"Paraden","community.contacts":"Ballkontakte","community.losses":"Ballverluste","community.mvp":"MVP-Auszeichnungen","community.motm":"Spieler des Matches","community.ownGoals":"Eigentore",
    "create.kicker":"Neuer Cap","create.title":"Cap erstellen","create.description":"Wähle zuerst das Spiel. Jedes Spiel nutzt nur die dafür unterstützte Map.","create.name":"Cap-Name","create.game":"Spiel","create.map":"Map","create.ready":"60 Sek. Ready","create.publish":"Cap veröffentlichen",
    "ready.found":"Match gefunden","ready.title":"BIST DU BEREIT?","ready.description":"Alle zwölf Spieler müssen bestätigen. Danach werden Teams und Positionen ausgelost.","ready.testDescription":"Der Testmodus nimmt alle echten Spieler aus der aktuellen Queue und ignoriert das 12-Spieler-Minimum.","ready.count":"/ {count} bereit","ready.accept":"Ich bin bereit","ready.timeout":"Wenn du nicht bestätigst, wird dein Platz freigegeben.",
    "rules.kicker":"So funktioniert es","rules.title":"CAP-REGELN","rules.choose":"Positionen wählen","rules.chooseText":"GK exklusiv oder mindestens zwei Feldpositionen.","rules.confirm":"Bereitschaft bestätigen","rules.confirmText":"Bei 12/12 haben alle Spieler 60 Sekunden.","rules.fair":"Auslosung nach Priorität","rules.fairText":"Haupt- und optionale Positionen haben Vorrang. Notfallpositionen füllen nur fehlende Plätze.","rules.direct":"Direkt verbinden","rules.directText":"Nach der Auslosung wird der Server freigeschaltet.",
    "role.GK":"Torwart","role.DEF":"Abwehr","role.MID":"Mittelfeld","role.WING":"Flügel","creator":"CAP-LEITER","you":"DU","now":"jetzt","since":"seit","available":"optional","required":"benötigt","mainApplicants":"Hauptposition","joinedCount":"{count} von 12 dabei","playersAria":"{count} von 12 Spielern","full":"Kader vollständig · Ready-Check","missing":"Noch {count} Spieler gesucht","missingOne":"Noch 1 Spieler gesucht","valid":"Auswahl gültig: {roles}.","validPriority":"{main} ist Hauptposition · {optional} optional.","chooseMain":"Wähle deine Hauptposition.","selectMore":"Noch {count} Feldpositionen auswählen.","savePositions":"Positionen speichern","joinCap":"Dem Cap beitreten","profileConnected":"Steam-Konto verifiziert. Jetzt Positionen wählen.","queueFull":"Der Cap ist voll. Du stehst auf der Warteliste.","noCompatible":"Mit dieser Auswahl ist aktuell kein kompatibler Kaderplatz frei.","positionsSaved":"Positionen gespeichert.","joined":"Du bist dabei · Platz {count}.","left":"Du hast den Cap verlassen.","demoFilled":"Demo: Der Kader wurde mit kompatiblen Spielern aufgefüllt.","readyExpired":"Ready-Check abgelaufen. Der Cap wurde wieder geöffnet.","readyConfirmed":"Bereitschaft bestätigt ✓","drawing":"Alle bereit. Teams und Positionen werden ausgelost …","matchReady":"Match bereit. Deine Position wurde markiert.","capPublished":"{name} Cap wurde veröffentlicht.","copied":"Verbindungsbefehl kopiert.","copyFailed":"Kopieren nicht verfügbar – Befehl bitte markieren.","signedAs":"Angemeldet als {name}.","liveJoined":"{name} ist dem Cap beigetreten.","liveWaiting":"Warte auf den ersten Spieler.","liveLeft":"{name} hat den Cap verlassen.","liveSaved":"Deine Positionen wurden gespeichert.","spectator":"Zuschauer","activity.policy":"Die Anwesenheit in der Queue wird alle 10 Minuten geprüft.","activity.kicker":"Anwesenheitsprüfung","activity.title":"Bist du noch da?","activity.description":"Bestätige jetzt, um deinen Platz im Cap zu behalten.","activity.timeLeft":"Verbleibende Zeit","activity.confirm":"Ich bin noch da","activity.warning":"Ohne Bestätigung wirst du aus der Queue entfernt.","activity.confirmed":"Anwesenheit bestätigt. Dein Platz ist für weitere 10 Minuten sicher.","activity.expired":"Du hast die Anwesenheitsprüfung verpasst und wurdest aus der Queue entfernt.","activity.removed":"Du bist nicht mehr in der Queue."
    ,"join.help":"Wähle mindestens zwei Positionen, auch mit Torwart. Lege eine als Hauptposition und die übrigen als optional fest.","rules.chooseText":"Wähle mindestens zwei Positionen. Torwart kann Haupt- oder optionale Position sein.","selectMore":"Noch benötigte Positionen: {count}."
    ,"admin.searchPlaceholder":"Nach Name oder SteamID suchen","admin.noResults":"Keine Nutzer entsprechen deiner Suche.","profile.manage":"Profil verwalten","profile.kicker":"Spielerprofil","profile.title":"Profil anpassen","profile.description":"Dein Steam-Name bleibt deine KICKOFF-Identität. Ergänze Informationen, die anderen zeigen, wann und wie du spielst.","profile.steamManaged":"Name wird von Steam verwaltet","profile.caps":"Gespielte Caps","profile.mostRole":"Meistgespielt","profile.mainPreference":"Aktuelle Hauptposition","profile.notSet":"Nicht festgelegt","profile.country":"Land / Region","profile.countryPlaceholder":"z. B. Deutschland","profile.favoriteGame":"Bevorzugtes Spiel","profile.availability":"Meistens verfügbar","profile.flexible":"Flexibel","profile.weekdayEvenings":"Unter der Woche abends","profile.weekends":"Wochenenden","profile.lateNight":"Spät nachts","profile.bio":"Über dich","profile.bioPlaceholder":"Soccer-Mod-Erfahrung, Spielstil oder Teamwünsche","profile.save":"Profil speichern","profile.saved":"Profil gespeichert.","profile.loadFailed":"Profil konnte nicht geladen werden."
  },
  ru: {
    "nav.play":"Кап","nav.caps":"Капы","nav.ranking":"Статистика","nav.create":"Создать кап","server.online":"Онлайн","steam.signin":"Войти через Steam","theme.light":"Включить светлую тему","theme.dark":"Включить тёмную тему",
    "hero.open":"Открытый кап","hero.tagline":"6v6-футбол сообщества для Counter-Strike 2 и Counter-Strike: Source.","hero.game":"Игра","hero.mode":"Режим","hero.sixvSix":"6 на 6","hero.created":"Создал","hero.players":"Игроков в капе",
    "rail.queue":"Сбор игроков","rail.ready":"Готовность","rail.seconds":"60 секунд","rail.draw":"Жеребьёвка","rail.teams":"Команды и позиции","rail.server":"Сервер","rail.connect":"Подключиться","team.home":"Команда хозяев","team.away":"Команда гостей","team.red":"Красные","team.blue":"Синие",
    "queue.roster":"Текущий состав","queue.title":"Кап","queue.fill":"Демо: заполнить очередь","queue.rules":"Правила капа","queue.coverageNote":"Основная и дополнительные позиции — предпочтения. Если место остаётся свободным, жеребьёвка назначает резервную позицию.","queue.leave":"Покинуть кап",
    "join.signup":"Ваша заявка","join.verified":"Подтверждено Steam","join.title":"Где вы хотите играть?","join.help":"Выберите минимум две полевые позиции. Вратарь выбирается отдельно.","join.mainPosition":"Основная позиция","join.mainPlaceholder":"Сначала выберите позиции","join.mainHelp":"Эта позиция получает приоритет при жеребьёвке. Остальные считаются дополнительными.","join.fair":"Жеребьёвка по приоритетам","join.drawNote":"Сначала учитывается основная позиция, затем дополнительные. Резервная позиция назначается только при необходимости.",
    "role.exclusive":"Отдельная позиция","role.twoSlots":"2 места в команде","role.oneSlot":"1 место в команде","role.gkCapacity":"2 всего · 1 в команде","role.defCapacity":"4 всего · 2 в команде","role.midCapacity":"2 всего · 1 в команде","role.wingCapacity":"4 всего · 2 в команде",
    "match.found":"Матч найден","match.lineup":"СОСТАВ ГОТОВ","match.locked":"Результат сохранён и не может быть разыгран повторно.","match.yourRole":"Ваша позиция","match.emergencyFill":"Резервная позиция","match.sixPlayers":"6 игроков","match.playerCount":"Игроков: {count}","match.onePlayer":"1 игрок","match.serverReadyCss":"Сервер Counter-Strike: Source готов","match.serverDetailCss":"Франкфурт · Пароль передаётся автоматически","match.serverPreparing":"Подготовка сервера CS:S","match.serverPreparingDetail":"Текущие игроки удаляются перед новым капом.","match.serverUnavailable":"Сервер CS2 ещё не настроен","match.serverUnavailableDetail":"Автовход станет доступен после добавления данных сервера CS2.","match.serverPrepareFailed":"Не удалось подготовить сервер CS:S","match.serverPrepareFailedDetail":"Автовход временно недоступен. Администратор может повторить попытку.","match.copy":"Копировать команду","match.autojoinCss":"Автовход в CS:S","match.unavailable":"Пока недоступно","match.audit":"Проверить жеребьёвку","match.algorithm":"Алгоритм","match.auditNote":"Жеребьёвка максимально учитывает основные, затем дополнительные позиции. Невыбранные позиции назначаются только для завершения схемы.",
    "footer.note":"Предпросмотр сообщества · Вход подтверждается через Steam OpenID.","footer.top":"Наверх ↑",
    "admin.endTest":"Завершить тестовый режим",
    "login.kicker":"Безопасный аккаунт Steam","login.title":"Войти через Steam","login.description":"Вы перейдёте в Steam Community. Steam подтвердит аккаунт и вернёт только публичный SteamID64.","login.warning":"Пароль Steam вводится только на steamcommunity.com.","login.verified":"Подтверждённый аккаунт Steam","login.displayName":"Имя в Steam","login.steamId":"SteamID64","login.profile":"Открыть профиль Steam ↗","login.signout":"Выйти","login.failed":"Steam не подтвердил вход. Попробуйте ещё раз.","login.cancelled":"Вход через Steam отменён.",
    "admin.manage":"Управление пользователями","admin.emptyQueue":"Очистить очередь","admin.emptyConfirm":"Удалить всех игроков из очереди?","admin.remove":"Удалить","admin.removeConfirm":"Удалить {name} из очереди?","admin.removed":"{name} удалён из очереди.","admin.removedYou":"Администратор удалил вас из очереди.","admin.testOn":"Тестовый режим: вкл.","admin.testOff":"Тестовый режим: выкл.","admin.testActive":"Тестовый режим администратора активен","admin.testNotice":"Запускается со всеми игроками в текущей очереди. Минимум 12 игроков и временные игроки не нужны.","admin.testStarted":"Тестовый режим запущен для игроков в очереди: {count}.","admin.testEnded":"Тестовый режим завершён · из очереди удалено: {count}.","admin.kicker":"Администрирование","admin.title":"Пользователи и доступ","admin.description":"Выдавайте права администратора только доверенным людям. Остальные подтверждённые аккаунты Steam остаются пользователями.","admin.user":"Пользователь","admin.admin":"Администратор","admin.owner":"Владелец","admin.saved":"Права обновлены.","admin.emptied":"Очередь очищена · удалено игроков: {count}.","admin.loadFailed":"Не удалось загрузить данные администратора.","queue.empty":"В очереди пока нет игроков. Войдите через Steam и выберите позиции.","queue.testWaiting":"Тестовый режим · очередь пуста","queue.testReady":"Тестовый режим · готово игроков: {count}",
    "community.kicker":"Обзор сообщества","community.title":"СТАТИСТИКА SOCCERMOD","community.description":"Данные сервера Soccer Mod и капов, организованных через KICKOFF.","community.loading":"Загрузка данных сервера","community.live":"Данные Soccer Mod в реальном времени","community.unavailable":"Данные Soccer Mod недоступны","community.capsPlayed":"Учтённые капы","community.capsNote":"Завершённые жеребьёвки без админ-тестов","community.registered":"Зарегистрированные игроки","community.active":"Активны за 30 дней: {count}","community.appearances":"Участия в матчах","community.appearancesNote":"Участия игроков, сохранённые Soccer Mod","community.goals":"Забитые голы","community.assists":"Зафиксировано передач: {count}","community.positionsKicker":"Предпочтения игроков","community.positionsTitle":"Спрос на основные позиции","community.positionsDescription":"Последняя основная позиция игрока учитывается один раз. Дополнительные позиции не увеличивают результат.","community.players":"Игроков: {count}","community.player":"1 игрок","community.activityKicker":"Игра с мячом","community.activityTitle":"Игровая активность","community.allTime":"За всё время","community.insightTitle":"Чтение игры","community.noGameplay":"Сервер подключён, но статистика соревновательных матчей Soccer Mod пока не сохранена.","community.recovery":"Мяч был возвращён в {rate}% зафиксированных эпизодов потери.","community.leadersKicker":"Результативность","community.leadersTitle":"Лучшие игроки","community.points":"Очки рейтинга","community.noLeaders":"Таблица появится после первого учтённого соревновательного матча.","community.matches":"Матчей: {count}","community.recentKicker":"История матчей","community.recentTitle":"Последние капы","community.noCaps":"Полные капы пока не учтены. Админ-тесты не засчитываются.","community.methodTitle":"Что измеряет Soccer Mod","community.methodText":"Голы, передачи, автоголы, касания мяча, пасы, перехваты, потери, сейвы вратаря, результаты периодов, MVP и игрок матча поступают напрямую из статистики мода.","community.passes":"Пасы","community.interceptions":"Перехваты","community.saves":"Сейвы","community.contacts":"Касания мяча","community.losses":"Потери мяча","community.mvp":"Награды MVP","community.motm":"Игрок матча","community.ownGoals":"Автоголы",
    "create.kicker":"Новый кап","create.title":"Создать кап","create.description":"Сначала выберите игру. Для каждой игры доступна только поддерживаемая карта.","create.name":"Название капа","create.game":"Игра","create.map":"Карта","create.ready":"60 сек. на готовность","create.publish":"Опубликовать кап",
    "ready.found":"Матч найден","ready.title":"ВЫ ГОТОВЫ?","ready.description":"Все двенадцать игроков должны подтвердить готовность. Затем определятся команды и позиции.","ready.testDescription":"Тестовый режим берёт всех реальных игроков из текущей очереди и игнорирует минимум 12 игроков.","ready.count":"/ {count} готовы","ready.accept":"Я готов","ready.timeout":"Без подтверждения ваше место будет освобождено.",
    "rules.kicker":"Как это работает","rules.title":"ПРАВИЛА КАПА","rules.choose":"Выберите позиции","rules.chooseText":"Только GK или минимум две полевые позиции.","rules.confirm":"Подтвердите готовность","rules.confirmText":"При 12/12 у всех есть 60 секунд.","rules.fair":"Жеребьёвка по приоритетам","rules.fairText":"Основная и дополнительные позиции имеют приоритет. Резервные назначения лишь заполняют свободные места.","rules.direct":"Подключитесь","rules.directText":"После жеребьёвки сервер станет доступен.",
    "role.GK":"Вратарь","role.DEF":"Защита","role.MID":"Полузащита","role.WING":"Фланг","creator":"СОЗДАТЕЛЬ","you":"ВЫ","now":"сейчас","since":"в очереди","available":"доп.","required":"нужно","mainApplicants":"основных","joinedCount":"{count} из 12","playersAria":"{count} из 12 игроков","full":"Состав готов · Проверка готовности","missing":"Нужно ещё игроков: {count}","missingOne":"Нужен ещё 1 игрок","valid":"Выбор принят: {roles}.","validPriority":"Основная: {main} · дополнительные: {optional}.","chooseMain":"Выберите основную позицию.","selectMore":"Выберите ещё полевых позиций: {count}.","savePositions":"Сохранить позиции","joinCap":"Войти в кап","profileConnected":"Аккаунт Steam подтверждён. Выберите позиции.","queueFull":"Кап заполнен. Вы добавлены в лист ожидания.","noCompatible":"Для этого выбора сейчас нет подходящего места.","positionsSaved":"Позиции сохранены.","joined":"Вы в капе · место {count}.","left":"Вы покинули кап.","demoFilled":"Демо: состав заполнен совместимыми игроками.","readyExpired":"Время подтверждения истекло. Кап снова открыт.","readyConfirmed":"Готовность подтверждена ✓","drawing":"Все готовы. Определяем команды и позиции …","matchReady":"Матч готов. Ваша позиция выделена.","capPublished":"Кап {name} опубликован.","copied":"Команда подключения скопирована.","copyFailed":"Не удалось скопировать — выделите команду вручную.","signedAs":"Выполнен вход: {name}.","liveJoined":"{name} вошёл в кап.","liveWaiting":"Ожидаем первого игрока.","liveLeft":"{name} покинул кап.","liveSaved":"Ваши позиции обновлены.","spectator":"Зритель","activity.policy":"Присутствие в очереди проверяется каждые 10 минут.","activity.kicker":"Проверка присутствия","activity.title":"Вы ещё здесь?","activity.description":"Подтвердите присутствие, чтобы сохранить место в капе.","activity.timeLeft":"Осталось времени","activity.confirm":"Я ещё здесь","activity.warning":"Без подтверждения вы будете удалены из очереди.","activity.confirmed":"Присутствие подтверждено. Ваше место сохранено ещё на 10 минут.","activity.expired":"Вы пропустили проверку и были удалены из очереди.","activity.removed":"Вы больше не находитесь в очереди."
    ,"join.help":"Выберите минимум две позиции, включая вратаря. Одну укажите как основную, остальные — как дополнительные.","rules.chooseText":"Выберите минимум две позиции. Вратарь может быть основной или дополнительной позицией.","selectMore":"Осталось выбрать позиций: {count}."
    ,"admin.searchPlaceholder":"Поиск по имени или SteamID","admin.noResults":"Пользователи не найдены.","profile.manage":"Управление профилем","profile.kicker":"Профиль игрока","profile.title":"Настройте профиль","profile.description":"Имя Steam остаётся вашей учётной записью KICKOFF. Добавьте сведения о том, когда и как вы играете.","profile.steamManaged":"Имя управляется через Steam","profile.caps":"Сыграно капов","profile.mostRole":"Частая позиция","profile.mainPreference":"Текущая основная","profile.notSet":"Не задано","profile.country":"Страна / регион","profile.countryPlaceholder":"например, Германия","profile.favoriteGame":"Любимая игра","profile.availability":"Обычно доступен","profile.flexible":"Гибко","profile.weekdayEvenings":"Вечера по будням","profile.weekends":"Выходные","profile.lateNight":"Поздно ночью","profile.bio":"О себе","profile.bioPlaceholder":"Опыт в Soccer Mod, стиль игры или пожелания к команде","profile.save":"Сохранить профиль","profile.saved":"Профиль сохранён.","profile.loadFailed":"Не удалось загрузить профиль."
  }
};

Object.assign(COPY.en, {
  "site.title": "KICKOFF — Soccer Mod Caps",
  "site.home": "KICKOFF home",
  "nav.primary": "Primary navigation",
  "nav.language": "Language",
  "rail.progress": "Cap progress",
  "dialog.close": "Close dialog",
  "noCap.title": "No cap is currently open",
  "noCap.description": "Create a cap to open registrations for the next Soccer Mod 6v6 match.",
  "activity.live": "Live:",
  "activity.demo": "Demo:",
  "join.positionsAria": "Choose positions",
  "profile.statsAria": "Player statistics",
  "match.seed": "Seed",
  "chat.kicker": "Cap chat",
  "chat.title": "Queue room",
  "chat.live": "Live",
  "chat.label": "Message",
  "chat.placeholder": "Write a message to everyone in this cap",
  "chat.send": "Send",
  "chat.note": "Only players currently queued for this cap can read and write here. The chat is deleted when the cap closes.",
  "chat.empty": "No messages yet. Say hello to the cap.",
  "chat.rateLimited": "Please wait a moment before sending another message.",
  "chat.failed": "Your message could not be sent.",
  "chat.justNow": "now",
  "admin.moderationNote": "Suspending or banning a player removes them from the queue and blocks website access until you restore the account.",
  "admin.status.active": "Active",
  "admin.status.suspended": "Suspended",
  "admin.status.banned": "Banned",
  "admin.statusConfirm": "Set {name} to {status}? They will lose website and queue access until restored.",
  "admin.statusSaved": "Account status updated.",
  "admin.statusFailed": "Account status could not be updated.",
  "login.restricted": "This Steam account is suspended or banned from KICKOFF."
});
Object.assign(COPY.de, {
  "site.title": "KICKOFF — Soccer-Mod-Caps",
  "site.home": "KICKOFF-Startseite",
  "nav.primary": "Hauptnavigation",
  "nav.language": "Sprache",
  "rail.progress": "Cap-Fortschritt",
  "dialog.close": "Dialog schließen",
  "noCap.title": "Derzeit ist kein Cap geöffnet",
  "noCap.description": "Erstelle einen Cap, um die Anmeldung für das nächste Soccer-Mod-6v6-Match zu öffnen.",
  "activity.live": "Live:",
  "activity.demo": "Demo:",
  "join.positionsAria": "Positionen auswählen",
  "profile.statsAria": "Spielerstatistiken",
  "match.seed": "Startwert",
  "chat.kicker": "Cap-Chat",
  "chat.title": "Queue-Raum",
  "chat.live": "Live",
  "chat.label": "Nachricht",
  "chat.placeholder": "Schreibe eine Nachricht an alle in diesem Cap",
  "chat.send": "Senden",
  "chat.note": "Nur Spieler, die derzeit für diesen Cap eingereiht sind, können hier lesen und schreiben. Der Chat wird gelöscht, wenn der Cap geschlossen wird.",
  "chat.empty": "Noch keine Nachrichten. Sag dem Cap Hallo.",
  "chat.rateLimited": "Bitte warte kurz, bevor du die nächste Nachricht sendest.",
  "chat.failed": "Deine Nachricht konnte nicht gesendet werden.",
  "chat.justNow": "jetzt",
  "admin.moderationNote": "Wenn du einen Spieler sperrst oder bannst, wird er aus der Queue entfernt und der Website-Zugang bleibt gesperrt, bis du das Konto wieder freigibst.",
  "admin.status.active": "Aktiv",
  "admin.status.suspended": "Gesperrt",
  "admin.status.banned": "Gebannt",
  "admin.statusConfirm": "{name} auf „{status}“ setzen? Der Website- und Queue-Zugang bleibt bis zur Freigabe gesperrt.",
  "admin.statusSaved": "Kontostatus aktualisiert.",
  "admin.statusFailed": "Kontostatus konnte nicht aktualisiert werden.",
  "login.restricted": "Dieses Steam-Konto ist bei KICKOFF gesperrt oder gebannt."
});
Object.assign(COPY.ru, {
  "site.title": "KICKOFF — Капы Soccer Mod",
  "site.home": "Главная KICKOFF",
  "nav.primary": "Основная навигация",
  "nav.language": "Язык",
  "rail.progress": "Ход капа",
  "dialog.close": "Закрыть окно",
  "noCap.title": "Сейчас нет открытого капа",
  "noCap.description": "Создайте кап, чтобы открыть регистрацию на следующий матч Soccer Mod 6v6.",
  "activity.live": "Сейчас:",
  "activity.demo": "Демо:",
  "join.positionsAria": "Выберите позиции",
  "profile.statsAria": "Статистика игрока",
  "match.seed": "Сид",
  "chat.kicker": "Чат капа",
  "chat.title": "Комната очереди",
  "chat.live": "Сейчас",
  "chat.label": "Сообщение",
  "chat.placeholder": "Напишите сообщение всем игрокам этого капа",
  "chat.send": "Отправить",
  "chat.note": "Читать и писать здесь могут только игроки, которые сейчас стоят в очереди этого капа. Чат удаляется после закрытия капа.",
  "chat.empty": "Сообщений пока нет. Поздоровайтесь с игроками капа.",
  "chat.rateLimited": "Подождите немного перед отправкой следующего сообщения.",
  "chat.failed": "Не удалось отправить сообщение.",
  "chat.justNow": "сейчас",
  "admin.moderationNote": "При блокировке или бане игрок удаляется из очереди и теряет доступ к сайту, пока вы не восстановите аккаунт.",
  "admin.status.active": "Активен",
  "admin.status.suspended": "Приостановлен",
  "admin.status.banned": "Заблокирован",
  "admin.statusConfirm": "Установить для {name} статус «{status}»? Доступ к сайту и очереди будет закрыт до восстановления.",
  "admin.statusSaved": "Статус аккаунта обновлён.",
  "admin.statusFailed": "Не удалось обновить статус аккаунта.",
  "login.restricted": "Этот аккаунт Steam приостановлен или заблокирован в KICKOFF."
});

Object.assign(COPY.en, {
  "cap.dismiss":"Dismiss cap", "cap.cancel":"Cancel cap", "cap.dismissConfirm":"Dismiss this cap and remove every queued player?", "cap.cancelConfirm":"Cancel this cap, stop the match, and clear the assignments?", "cap.dismissed":"Cap dismissed.", "cap.matchStopped":"Match stopped — cap closed ({score})", "cap.matchFinished":"Full time — cap closed ({score})",
  "profile.positions":"Default cap positions", "profile.positionsHelp":"Set your main and optional positions once. They will be ready whenever you join a cap.", "profile.defaultMain":"Default main position", "profile.preferencesInvalid":"Choose at least two positions and one main position, or clear every position.",
  "create.type":"Cap type", "create.standard":"Standard cap · KICKOFF server", "create.custom":"Custom cap · organizer's server", "create.standardHelp":"Teams auto-join the KICKOFF Germany server after the draw.", "create.customHelp":"No auto-join. After the draw, post your server IP and password in the cap chat.",
  "match.customServerTitle":"Custom server details required", "match.customServerDetail":"The cap organizer must post the server IP and password in the cap chat.", "match.customServerAction":"Waiting for server details",
  "match.serverReadyCs2":"Counter-Strike 2 server is ready", "match.serverDetailCs2":"Frankfurt · Password included automatically", "match.serverPreparingCs2":"Preparing the CS2 server", "match.serverPrepareFailedCs2":"The CS2 server could not be prepared", "match.autojoinCs2":"Auto-join CS2",
  "match.durationVoting":"Voting on cap length", "match.durationVotingDetail":"Auto-join starts after the 10-second vote.",
  "vote.kicker":"Cap formed", "vote.title":"CHOOSE THE CAP LENGTH", "vote.description":"Vote now. Non-voters are ignored and the most votes win.", "vote.aria":"Choose the duration per half", "vote.fast":"Fast mode", "vote.default":"Default", "vote.long":"Extended", "vote.perHalf":"per half", "vote.waiting":"Waiting for votes…", "vote.voted":"Your vote: {length} per half", "vote.result":"Selected: {length} per half", "vote.rule":"No votes or a tie uses the 10-minute default."
});
Object.assign(COPY.de, {
  "cap.dismiss":"Cap schließen", "cap.cancel":"Cap abbrechen", "cap.dismissConfirm":"Diesen Cap schließen und alle eingereihten Spieler entfernen?", "cap.cancelConfirm":"Diesen Cap abbrechen, das Match stoppen und die Zuordnungen löschen?", "cap.dismissed":"Cap geschlossen.", "cap.matchStopped":"Match gestoppt – Cap geschlossen ({score})", "cap.matchFinished":"Abpfiff – Cap geschlossen ({score})",
  "profile.positions":"Standardpositionen für Caps", "profile.positionsHelp":"Lege Haupt- und optionale Positionen einmal fest. Sie stehen bereit, sobald du einem Cap beitrittst.", "profile.defaultMain":"Standard-Hauptposition", "profile.preferencesInvalid":"Wähle mindestens zwei Positionen und eine Hauptposition oder entferne alle Positionen.",
  "create.type":"Cap-Typ", "create.standard":"Standard-Cap · KICKOFF-Server", "create.custom":"Eigener Cap · Server des Organisators", "create.standardHelp":"Die Teams treten nach der Auslosung automatisch dem deutschen KICKOFF-Server bei.", "create.customHelp":"Kein Auto-Join. Teile nach der Auslosung Server-IP und Passwort im Cap-Chat.",
  "match.customServerTitle":"Serverdaten für eigenen Cap erforderlich", "match.customServerDetail":"Der Cap-Organisator muss Server-IP und Passwort im Cap-Chat mitteilen.", "match.customServerAction":"Warte auf Serverdaten",
  "match.serverReadyCs2":"Counter-Strike-2-Server ist bereit", "match.serverDetailCs2":"Frankfurt · Passwort wird automatisch übergeben", "match.serverPreparingCs2":"CS2-Server wird vorbereitet", "match.serverPrepareFailedCs2":"Der CS2-Server konnte nicht vorbereitet werden", "match.autojoinCs2":"CS2 automatisch beitreten",
  "match.durationVoting":"Abstimmung über die Cap-Länge", "match.durationVotingDetail":"Auto-Join startet nach der 10-Sekunden-Abstimmung.",
  "vote.kicker":"Cap vollständig", "vote.title":"WÄHLE DIE CAP-LÄNGE", "vote.description":"Stimme jetzt ab. Nichtwähler werden ignoriert; die meisten Stimmen gewinnen.", "vote.aria":"Dauer pro Halbzeit wählen", "vote.fast":"Schnellmodus", "vote.default":"Standard", "vote.long":"Verlängert", "vote.perHalf":"pro Halbzeit", "vote.waiting":"Warte auf Stimmen…", "vote.voted":"Deine Stimme: {length} pro Halbzeit", "vote.result":"Gewählt: {length} pro Halbzeit", "vote.rule":"Ohne Stimmen oder bei Gleichstand gilt der Standard von 10 Minuten."
});
Object.assign(COPY.ru, {
  "cap.dismiss":"Закрыть кап", "cap.cancel":"Отменить кап", "cap.dismissConfirm":"Закрыть этот кап и удалить всех игроков из очереди?", "cap.cancelConfirm":"Отменить кап, остановить матч и очистить назначения?", "cap.dismissed":"Кап закрыт.", "cap.matchStopped":"Матч остановлен — кап закрыт ({score})", "cap.matchFinished":"Матч завершён — кап закрыт ({score})",
  "profile.positions":"Стандартные позиции для капа", "profile.positionsHelp":"Один раз задайте основную и дополнительные позиции. Они будут готовы при вступлении в кап.", "profile.defaultMain":"Стандартная основная позиция", "profile.preferencesInvalid":"Выберите минимум две позиции и основную позицию либо очистите все позиции.",
  "create.type":"Тип капа", "create.standard":"Стандартный кап · сервер KICKOFF", "create.custom":"Свой кап · сервер организатора", "create.standardHelp":"После жеребьёвки команды автоматически подключатся к немецкому серверу KICKOFF.", "create.customHelp":"Автоподключения нет. После жеребьёвки отправьте IP сервера и пароль в чат капа.",
  "match.customServerTitle":"Требуются данные своего сервера", "match.customServerDetail":"Организатор капа должен указать IP сервера и пароль в чате капа.", "match.customServerAction":"Ожидание данных сервера",
  "match.serverReadyCs2":"Сервер Counter-Strike 2 готов", "match.serverDetailCs2":"Франкфурт · Пароль передаётся автоматически", "match.serverPreparingCs2":"Подготовка сервера CS2", "match.serverPrepareFailedCs2":"Не удалось подготовить сервер CS2", "match.autojoinCs2":"Автовход в CS2",
  "match.durationVoting":"Голосование за длительность капа", "match.durationVotingDetail":"Автовход начнётся после 10-секундного голосования.",
  "vote.kicker":"Кап собран", "vote.title":"ВЫБЕРИТЕ ДЛИТЕЛЬНОСТЬ", "vote.description":"Голосуйте сейчас. Не голосовавшие не учитываются; побеждает большинство.", "vote.aria":"Выберите длительность тайма", "vote.fast":"Быстрый режим", "vote.default":"Стандарт", "vote.long":"Длинный", "vote.perHalf":"за тайм", "vote.waiting":"Ожидание голосов…", "vote.voted":"Ваш голос: {length} за тайм", "vote.result":"Выбрано: {length} за тайм", "vote.rule":"Без голосов или при ничьей используется стандарт 10 минут."
});

let locale = localStorage.getItem("kickoff-language") || "en";
if (!COPY[locale]) locale = "en";
const theme = "blue";
let colorMode = localStorage.getItem("kickoff-color-mode") || (window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark");
if (!["dark", "light"].includes(colorMode)) colorMode = "dark";
const t = (key, values = {}) => (COPY[locale][key] || COPY.en[key] || key).replace(/\{(\w+)\}/g, (_, name) => values[name] ?? `{${name}}`);
const roleLabel = (role) => t(`role.${role}`);
const assignmentLabel = (role, source) => `${roleLabel(role)}${source === "fallback" ? ` · ${t("match.emergencyFill")}` : ""}`;

// Placeholder players for the "Demo: fill queue" button - fake names and
// obviously-invalid SteamID64 values (real ones start 7656119[7-9]...).
const demoPool = [
  { name: "Player One", id: "00000000000000001", roles: ["GK", "DEF"], mainRole: "GK" },
  { name: "Player Two", id: "00000000000000002", roles: ["DEF", "MID"] },
  { name: "Player Three", id: "00000000000000003", roles: ["DEF", "WING"] },
  { name: "Player Four", id: "00000000000000004", roles: ["MID", "WING"] },
  { name: "Player Five", id: "00000000000000005", roles: ["DEF", "MID", "WING"] }
];

const state = {
  players: [],
  selectedRoles: new Set(),
  mainRole: null,
  signedIn: false,
  currentUser: null,
  queueLocked: false,
  nonce: "",
  readyAccepted: false,
  readyOthers: 1,
  readySeconds: 60,
  readyTick: null,
  readySimulation: null,
  assignmentTimer: null,
  teams: null,
  gameKey: "css",
  capName: "Soccer Mod",
  mapName: "Titan Club 2026",
  serverStatus: "ready",
  autoJoinAttempted: false,
  capActive: false,
  capCreator: null,
  capMode: "standard",
  testMode: false,
  matchAssignments: [],
  currentAssignedRole: null,
  currentAssignmentSource: null,
  activityTimer: null,
  activityGraceSeconds: 60,
  serverClockOffset: 0,
  adminUsers: [],
  profile: null,
  profileSelectedRoles: new Set(),
  profileMainRole: null,
  activity: { key: "liveWaiting", values: {}, prefixKey: "activity.live" },
  chatMessages: [],
  durationVote: null,
  matchSignature: null,
  matchStatusTimer: null
};

const $ = (selector) => document.querySelector(selector);
const $$ = (selector) => [...document.querySelectorAll(selector)];
const escapeHTML = (value) => String(value).replace(/[&<>'"]/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "'": "&#39;", '"': "&quot;" })[char]);

function applyCapMetadata(details) {
  const gameKey = GAME_OPTIONS[details?.game] ? details.game : "css";
  const game = GAME_OPTIONS[gameKey];
  const map = game.maps.includes(details?.map) ? details.map : game.maps[0];
  const name = String(details?.name || "Soccer Mod").trim().slice(0, 48) || "Soccer Mod";
  if (state.gameKey !== gameKey || state.mapName !== map) state.autoJoinAttempted = false;
  state.gameKey = gameKey;
  state.mapName = map;
  state.capName = name;
  $("#capName").textContent = name.toUpperCase();
  $("#gameName").textContent = game.label;
  $("#mapName").textContent = map;
  $("#matchMapName").textContent = map;
}

const initials = (name) => name.replace(/[^a-z0-9]/gi, "").slice(0, 2).toUpperCase() || "SM";
const COUNTRY_CODES = "AF,AL,DZ,AD,AO,AG,AR,AM,AU,AT,AZ,BS,BH,BD,BB,BY,BE,BZ,BJ,BT,BO,BA,BW,BR,BN,BG,BF,BI,CV,KH,CM,CA,CF,TD,CL,CN,CO,KM,CG,CD,CR,CI,HR,CU,CY,CZ,DK,DJ,DM,DO,EC,EG,SV,GQ,ER,EE,SZ,ET,FJ,FI,FR,GA,GM,GE,DE,GH,GR,GD,GT,GN,GW,GY,HT,HN,HU,IS,IN,ID,IR,IQ,IE,IL,IT,JM,JP,JO,KZ,KE,KI,KP,KR,KW,KG,LA,LV,LB,LS,LR,LY,LI,LT,LU,MG,MW,MY,MV,ML,MT,MH,MR,MU,MX,FM,MD,MC,MN,ME,MA,MZ,MM,NA,NR,NP,NL,NZ,NI,NE,NG,MK,NO,OM,PK,PW,PA,PG,PY,PE,PH,PL,PT,QA,RO,RU,RW,KN,LC,VC,WS,SM,ST,SA,SN,RS,SC,SL,SG,SK,SI,SB,SO,ZA,SS,ES,LK,SD,SR,SE,CH,SY,TJ,TZ,TH,TL,TG,TO,TT,TN,TR,TM,TV,UG,UA,AE,GB,US,UY,UZ,VU,VA,VE,VN,YE,ZM,ZW".split(",");
const COUNTRY_ALIASES = { "russia": "RU", "south korea": "KR", "north korea": "KP", "united kingdom": "GB", "uk": "GB", "usa": "US", "united states": "US", "czech republic": "CZ", "moldova": "MD", "bolivia": "BO", "venezuela": "VE", "tanzania": "TZ", "laos": "LA", "brunei": "BN", "iran": "IR", "syria": "SY" };
const COUNTRY_CODE_BY_NAME = (() => {
  const names = new Map(Object.entries(COUNTRY_ALIASES));
  if (typeof Intl.DisplayNames === "function") {
    const displayNames = new Intl.DisplayNames(["en"], { type: "region" });
    COUNTRY_CODES.forEach(code => names.set(displayNames.of(code).toLocaleLowerCase(), code));
  }
  return names;
})();
const flagCodeForCountry = (country) => COUNTRY_CODE_BY_NAME.get(String(country || "").trim().toLocaleLowerCase()) || "";
const inQueue = () => Boolean(state.currentUser && state.players.some(player => player.id === state.currentUser.id));

function createNonce() {
  const bytes = new Uint32Array(2);
  crypto.getRandomValues(bytes);
  return [...bytes].map(value => value.toString(16).padStart(8, "0")).join("");
}

function applyLanguage(language) {
  locale = COPY[language] ? language : "en";
  localStorage.setItem("kickoff-language", locale);
  document.documentElement.lang = locale;
  document.title = t("site.title");
  $$('[data-i18n]').forEach(element => { element.textContent = t(element.dataset.i18n); });
  $$('[data-i18n-placeholder]').forEach(element => { element.placeholder = t(element.dataset.i18nPlaceholder); });
  $$('[data-i18n-aria]').forEach(element => { element.setAttribute("aria-label", t(element.dataset.i18nAria)); });
  $$('[data-i18n-alt]').forEach(element => { element.alt = t(element.dataset.i18nAlt); });
  $$('[data-role-label]').forEach(element => { element.textContent = roleLabel(element.dataset.roleLabel); });
  $$('[data-language]').forEach(button => button.classList.toggle("active", button.dataset.language === locale));
  renderNoCapCopy();
  renderSteamIdentity();
  updateModeToggle();
  renderActivity();
  updateRoleUI();
  renderRoster();
  renderServerConnection();
  renderTestMode();
  renderReadyCopy();
  if (state.profile) renderProfileSummary();
  if (state.teams) {
    renderTeam("#homeTeam", state.teams.home);
    renderTeam("#awayTeam", state.teams.away);
    renderTeamCounts();
    $("#yourRole").textContent = state.currentAssignedRole ? assignmentLabel(state.currentAssignedRole, state.currentAssignmentSource) : t("spectator");
  }
  renderChat();
}

function applyTheme() {
  localStorage.setItem("kickoff-accent", "blue");
  document.documentElement.dataset.theme = theme;
}

function updateModeToggle() {
  const button = $("#colorModeToggle");
  if (!button) return;
  const nextLabel = colorMode === "dark" ? t("theme.light") : t("theme.dark");
  button.setAttribute("aria-label", nextLabel);
  button.title = nextLabel;
  button.querySelector("span").textContent = colorMode === "dark" ? "☼" : "☾";
}

function applyColorMode(nextMode) {
  colorMode = nextMode === "light" ? "light" : "dark";
  localStorage.setItem("kickoff-color-mode", colorMode);
  document.documentElement.dataset.mode = colorMode;
  const themeMeta = document.querySelector('meta[name="theme-color"]');
  if (themeMeta) themeMeta.content = colorMode === "light" ? "#f3f5f7" : "#0d1726";
  updateModeToggle();
}

function setActivity(key, values = {}, prefixKey = "activity.live") {
  state.activity = { key, values, prefixKey };
  renderActivity();
}

function renderActivity() {
  $("#activityText").innerHTML = `<b>${escapeHTML(t(state.activity.prefixKey))}</b> ${escapeHTML(t(state.activity.key, state.activity.values))}`;
}

function showLatestJoiner() {
  const latest = state.players.reduce((current, player) => {
    if (!current) return player;
    return Number(player.joinedAt || 0) > Number(current.joinedAt || 0) ? player : current;
  }, null);
  setActivity(latest ? "liveJoined" : "liveWaiting", latest ? { name: latest.name } : {});
}

function toast(message) {
  const element = $("#toast");
  element.textContent = message;
  element.classList.add("show");
  clearTimeout(toast.timer);
  toast.timer = setTimeout(() => element.classList.remove("show"), 2600);
}

function renderRoster() {
  if (state.players.length === 0) {
    $("#roster").innerHTML = `<div class="queue-empty-state"><span>12</span><p>${escapeHTML(t("queue.empty"))}</p></div>`;
    updateCounts();
    return;
  }
  $("#roster").innerHTML = state.players.map((player, index) => {
    const isCurrent = state.currentUser?.id === player.id;
    const canRemove = state.currentUser?.role === "admin" && !state.queueLocked;
    const countryCode = flagCodeForCountry(player.country);
    return `
      <article class="player-row${isCurrent ? " current-player" : ""}">
        <div class="avatar" aria-hidden="true">${escapeHTML(initials(player.name))}</div>
        <div class="player-name"><b>${escapeHTML(player.name)}${countryCode ? ` <img class="player-country-flag" src="flags/${countryCode.toLowerCase()}.png" width="20" height="15" alt="${escapeHTML(player.country)}" title="${escapeHTML(player.country)}" loading="lazy">` : ""}${player.creator ? ` · ${t("creator")}` : ""}${isCurrent ? ` · ${t("you")}` : ""}</b><small>#${String(index + 1).padStart(2, "0")}</small></div>
        <div class="chips">${player.roles.map(role => `<span class="chip${role === player.mainRole ? " main" : ""}">${role}</span>`).join("")}</div>
        <div class="queue-row-actions"><span class="queue-time">${t("since")} ${escapeHTML(player.time || t("now"))}</span>${canRemove ? `<button class="remove-player-button" type="button" data-remove-player="${escapeHTML(player.id)}" data-remove-name="${escapeHTML(player.name)}">${escapeHTML(t("admin.remove"))}</button>` : ""}</div>
      </article>`;
  }).join("");
  $$('[data-remove-player]').forEach(button => button.addEventListener("click", () => removePlayerAsAdmin(button.dataset.removePlayer, button.dataset.removeName)));
  updateCounts();
}

function updateCounts() {
  const count = state.players.length;
  $("#playerCount").textContent = String(count).padStart(2, "0");
  $("#heroScore").setAttribute("aria-label", t("playersAria", { count }));
  $("#queueProgress").style.width = `${Math.min(100, count / 12 * 100)}%`;
  $("#railCount").textContent = t("joinedCount", { count });
  $("#estimate").textContent = isAdminTestMode()
    ? t(count >= 1 ? "queue.testReady" : "queue.testWaiting", { count })
    : count >= 12 ? t("full") : t(12 - count === 1 ? "missingOne" : "missing", { count: 12 - count });
  for (const role of Object.keys(ROLE_CAPACITY)) {
    const mainCount = state.players.filter(player => player.mainRole === role).length;
    const optionalCount = state.players.filter(player => player.mainRole !== role && player.roles.includes(role)).length;
    const card = document.querySelector(`[data-coverage="${role}"]`);
    card.querySelector("small").textContent = `${mainCount} ${t("mainApplicants")} · ${optionalCount} ${t("available")} · ${ROLE_CAPACITY[role]} ${t("required")}`;
    card.querySelector("strong").textContent = `${mainCount}/${ROLE_CAPACITY[role]}`;
    card.classList.toggle("coverage-short", mainCount + optionalCount < ROLE_CAPACITY[role]);
  }
  $("#leaveQueue").hidden = !inQueue() || state.queueLocked;
}

function showQueuePage() {
  const capOpen = state.capActive || Boolean(state.teams);
  $(".cap-hero").hidden = !capOpen;
  $(".status-rail").hidden = !capOpen;
  $("#queue").hidden = !capOpen || Boolean(state.teams);
  $("#noOpenCap").hidden = capOpen;
  $("#matchRoom").hidden = !state.teams;
  renderChat();
}

function stopMatchStatusPolling() {
  if (state.matchStatusTimer) window.clearInterval(state.matchStatusTimer);
  state.matchStatusTimer = null;
}

function clearMatchRoomState() {
  stopMatchStatusPolling();
  state.teams = null;
  state.matchAssignments = [];
  state.currentAssignedRole = null;
  state.currentAssignmentSource = null;
  state.matchSignature = null;
  state.queueLocked = false;
  state.durationVote = null;
  if ($("#durationVoteDialog")?.open) $("#durationVoteDialog").close();
}

async function pollMatchStatus() {
  if (state.gameKey !== "cs2" || !state.matchSignature || !state.teams) return;
  try {
    const response = await fetch(`/api/match/status?signature=${encodeURIComponent(state.matchSignature)}`, {
      headers: { Accept: "application/json" },
      credentials: "same-origin"
    });
    if (response.status === 409) {
      stopMatchStatusPolling();
      await loadQueue();
      return;
    }
    if (!response.ok) return;
    const status = await response.json();
    if (!status.ended) return;
    const score = `${Number(status.scoreCt) || 0}-${Number(status.scoreT) || 0}`;
    clearMatchRoomState();
    state.players = [];
    state.capActive = false;
    state.capCreator = null;
    state.testMode = false;
    state.serverStatus = "ready";
    showQueuePage();
    renderRoster();
    renderSteamIdentity();
    updateRoleUI();
    renderServerConnection();
    toast(t(status.reason === "full_time" ? "cap.matchFinished" : "cap.matchStopped", { score }));
    await loadQueue();
  } catch {
    // A transient website/helper failure must not interrupt the match.
  }
}

function startMatchStatusPolling() {
  stopMatchStatusPolling();
  if (state.gameKey !== "cs2" || !state.matchSignature || !state.teams) return;
  void pollMatchStatus();
  state.matchStatusTimer = window.setInterval(() => void pollMatchStatus(), 2000);
}

function renderNoCapCopy() {
  const title = $("#noCapTitle");
  const description = $("#noCapDescription");
  if (title) title.textContent = t("noCap.title");
  if (description) description.textContent = t("noCap.description");
}

function chatIsAvailable() {
  return Boolean(state.capActive && state.currentUser && (inQueue() || state.capCreator?.id === state.currentUser.id));
}

function formatChatTime(timestamp) {
  const value = Number(timestamp || 0);
  if (!value || Math.abs(Date.now() - value * 1000) < 45_000) return t("chat.justNow");
  return new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit" }).format(new Date(value * 1000));
}

function renderChat() {
  const chat = $("#capChat");
  const messages = $("#chatMessages");
  const input = $("#chatInput");
  if (!chat || !messages || !input) return;
  const available = chatIsAvailable();
  chat.hidden = !available;
  if (!available) {
    state.chatMessages = [];
    return;
  }
  messages.innerHTML = state.chatMessages.length ? state.chatMessages.map(message => {
    const isCurrent = message.steamid === state.currentUser?.id;
    return `<article class="chat-message${isCurrent ? " current" : ""}"><b>${escapeHTML(message.name)}</b><time datetime="${new Date(Number(message.createdAt) * 1000).toISOString()}">${escapeHTML(formatChatTime(message.createdAt))}</time><p>${escapeHTML(message.message)}</p></article>`;
  }).join("") : `<p class="chat-empty">${escapeHTML(t("chat.empty"))}</p>`;
  input.disabled = false;
}

async function loadCapChat() {
  if (!chatIsAvailable()) {
    renderChat();
    return;
  }
  try {
    const response = await fetch("/api/cap/chat", { headers: { Accept: "application/json" }, credentials: "same-origin" });
    if (!response.ok) {
      if (response.status === 401 || response.status === 403) renderChat();
      return;
    }
    const data = await response.json();
    state.chatMessages = Array.isArray(data.messages) ? data.messages : [];
    renderChat();
  } catch {
    // Keep the last received messages during a transient connection failure.
  }
}

async function sendChatMessage(event) {
  event.preventDefault();
  if (!chatIsAvailable()) return;
  const input = $("#chatInput");
  const button = $("#chatForm button");
  const message = input.value.trim();
  if (!message) return;
  input.disabled = true;
  button.disabled = true;
  try {
    const { response, data } = await apiPost("/api/cap/chat", { message });
    if (!response.ok) {
      toast(t(data.error === "chat_rate_limited" ? "chat.rateLimited" : "chat.failed"));
      return;
    }
    input.value = "";
    if (data.message) {
      state.chatMessages = [...state.chatMessages.filter(item => item.id !== data.message.id), data.message].slice(-80);
    }
    renderChat();
    $("#chatMessages").scrollTop = $("#chatMessages").scrollHeight;
  } catch {
    toast(t("chat.failed"));
  } finally {
    input.disabled = false;
    button.disabled = false;
  }
}

function renderCapCreator() {
  const element = $("#capCreator");
  if (element) element.textContent = state.capCreator?.name || "—";
}

function updateRoleUI() {
  const selectionValid = state.selectedRoles.size >= 2;
  if (state.mainRole && !state.selectedRoles.has(state.mainRole)) state.mainRole = null;
  const valid = selectionValid && Boolean(state.mainRole);
  $$('[data-role]').forEach(button => button.setAttribute("aria-pressed", String(state.selectedRoles.has(button.dataset.role))));
  const mainSelect = $("#mainRoleInput");
  const chosenRoles = [...state.selectedRoles];
  mainSelect.replaceChildren(new Option(t("join.mainPlaceholder"), ""), ...chosenRoles.map(role => new Option(roleLabel(role), role)));
  mainSelect.disabled = chosenRoles.length === 0;
  mainSelect.value = state.mainRole || "";
  const optionalRoles = chosenRoles.filter(role => role !== state.mainRole);
  if (valid) {
    $("#roleHint").innerHTML = `<span>✓</span> ${t("validPriority", { main: roleLabel(state.mainRole), optional: optionalRoles.length ? optionalRoles.map(roleLabel).join(" + ") : "—" })}`;
  } else if (selectionValid) {
    $("#roleHint").innerHTML = `<span>i</span> ${t("chooseMain")}`;
  } else {
    $("#roleHint").innerHTML = `<span>i</span> ${t("selectMore", { count: Math.max(0, 2 - state.selectedRoles.size) })}`;
  }
  $("#joinQueue").disabled = state.queueLocked || (state.signedIn && !valid);
  $("#joinQueue").textContent = inQueue() ? t("savePositions") : (state.signedIn ? t("joinCap") : t("steam.signin"));
}

function setStep(step) {
  const order = ["queue", "ready", "draw", "server"];
  const currentIndex = order.indexOf(step);
  $$('[data-step]').forEach(item => {
    const index = order.indexOf(item.dataset.step);
    item.classList.toggle("current", index === currentIndex);
    item.classList.toggle("done", index < currentIndex);
  });
}

function canPlaceAll(players) {
  const ordered = [...players].sort((a, b) => a.roles.length - b.roles.length || a.id.localeCompare(b.id));
  const memo = new Map();
  function search(index, capacity) {
    if (index === ordered.length) return true;
    const key = `${index}|${capacity.GK},${capacity.DEF},${capacity.MID},${capacity.WING}`;
    if (memo.has(key)) return memo.get(key);
    for (const role of ordered[index].roles) {
      if (capacity[role] <= 0) continue;
      const next = { ...capacity, [role]: capacity[role] - 1 };
      if (search(index + 1, next)) { memo.set(key, true); return true; }
    }
    memo.set(key, false);
    return false;
  }
  return search(0, { ...ROLE_CAPACITY });
}

function handleRoleButton(button) {
  if (state.queueLocked) return;
  const role = button.dataset.role;
  state.selectedRoles.has(role) ? state.selectedRoles.delete(role) : state.selectedRoles.add(role);
  updateRoleUI();
}

function startSteamSignIn() {
  window.location.assign("/auth/steam?return_to=/");
}

function openSteamAccount() {
  renderSteamIdentity();
  $("#loginDialog").showModal();
}

function renderSteamIdentity() {
  const signedOut = $("#steamSignedOut");
  const signedIn = $("#steamSignedIn");
  if (!signedOut || !signedIn) return;
  signedOut.hidden = state.signedIn;
  signedIn.hidden = !state.signedIn;
  if (!state.signedIn || !state.currentUser) {
    $("#steamLogin").textContent = t("steam.signin");
    $("#manageUsers").hidden = true;
    $("#manageUsersAccount").hidden = true;
    $("#emptyQueue").hidden = true;
    $("#testModeToggle").hidden = true;
    $("#cancelCapMatch").hidden = true;
    renderTestMode();
    return;
  }
  const id = state.currentUser.id;
  $("#steamLogin").textContent = state.currentUser.name;
  $("#verifiedSteamName").textContent = state.currentUser.name;
  $("#verifiedSteamId").textContent = id;
  $("#steamProfileLink").href = `https://steamcommunity.com/profiles/${encodeURIComponent(id)}`;
  const isAdmin = state.currentUser.role === "admin";
  $("#manageUsers").hidden = !isAdmin;
  $("#manageUsersAccount").hidden = !isAdmin;
  $("#emptyQueue").hidden = !isAdmin;
  $("#testModeToggle").hidden = !isAdmin;
  $("#dismissCap").hidden = !(state.capActive && state.capCreator?.id === id);
  $("#cancelCapMatch").hidden = !(state.capActive && state.teams && state.capCreator?.id === id);
  renderTestMode();
}

async function loadSteamSession() {
  try {
    const response = await fetch("/api/me", { headers: { Accept: "application/json" }, credentials: "same-origin" });
    if (response.ok) {
      const profile = await response.json();
      if (/^\d{17}$/.test(profile.steamid)) {
        state.signedIn = true;
        state.currentUser = { id: profile.steamid, name: profile.name || `Steam ${profile.steamid.slice(-6)}`, role: profile.role || "user", roles: [] };
      }
    }
  } catch {
    state.signedIn = false;
    state.currentUser = null;
  }
  renderSteamIdentity();
  updateRoleUI();
  await loadQueue();
  await loadProfilePreferences();
  maybeStartReadyCheck();

  const params = new URLSearchParams(window.location.search);
  const authResult = params.get("auth");
  if (authResult === "failed") toast(t("login.failed"));
  if (authResult === "cancelled") toast(t("login.cancelled"));
  if (authResult === "restricted") toast(t("login.restricted"));
  if (authResult && window.history.replaceState) window.history.replaceState({}, "", window.location.pathname + window.location.hash);
}

async function loadProfilePreferences() {
  if (!state.signedIn || inQueue()) return;
  try {
    const response = await fetch("/api/profile", { headers: { Accept: "application/json" }, credentials: "same-origin" });
    if (!response.ok) return;
    const data = await response.json();
    state.profile = data.profile;
    const preferences = data.profile?.preferences || {};
    const roles = Array.isArray(preferences.roles) ? preferences.roles : [];
    if (roles.length >= 2 && roles.includes(preferences.mainRole)) {
      state.selectedRoles = new Set(roles);
      state.mainRole = preferences.mainRole;
      updateRoleUI();
    }
  } catch {
    // Position defaults are optional; the cap remains usable when loading them fails.
  }
}

async function signOutSteam() {
  try {
    await fetch("/auth/logout", { method: "POST", credentials: "same-origin", headers: { "X-Requested-With": "KICKOFF" } });
  } finally {
    window.location.assign("/");
  }
}

async function apiPost(path, payload = {}) {
  const response = await fetch(path, {
    method: "POST",
    credentials: "same-origin",
    headers: { "Content-Type": "application/json", "X-Requested-With": "KICKOFF", Accept: "application/json" },
    body: JSON.stringify(payload)
  });
  const data = await response.json().catch(() => ({}));
  return { response, data };
}

function serverNow() {
  return Math.floor(Date.now() / 1000 + state.serverClockOffset);
}

function closeActivityDialog() {
  const dialog = $("#activityDialog");
  if (dialog?.open) dialog.close();
}

function scheduleActivityCheck() {
  clearInterval(state.activityTimer);
  state.activityTimer = null;
  const ownMembership = state.currentUser && state.players.find(player => player.id === state.currentUser.id);
  if (!ownMembership?.activityDueAt) {
    closeActivityDialog();
    return;
  }

  const tick = () => {
    const secondsUntilDue = Number(ownMembership.activityDueAt) - serverNow();
    if (secondsUntilDue > 0) {
      closeActivityDialog();
      return;
    }
    const secondsLeft = Math.max(0, Number(ownMembership.activityDueAt) + state.activityGraceSeconds - serverNow());
    const dialog = $("#activityDialog");
    $("#activityCountdown").textContent = `00:${String(secondsLeft).padStart(2, "0")}`;
    if (!dialog.open) dialog.showModal();
    if (secondsLeft === 0) {
      clearInterval(state.activityTimer);
      state.activityTimer = null;
      closeActivityDialog();
      loadQueue();
    }
  };
  tick();
  if (Number(ownMembership.activityDueAt) > serverNow() || $("#activityDialog").open) {
    state.activityTimer = setInterval(tick, 1000);
  }
}

async function confirmQueueActivity() {
  const button = $("#confirmActivity");
  button.disabled = true;
  const { response, data } = await apiPost("/api/queue/activity");
  button.disabled = false;
  if (!response.ok) {
    closeActivityDialog();
    if (Array.isArray(data.members)) state.players = data.members;
    renderRoster();
    updateRoleUI();
    toast(t(data.error === "activity_expired" ? "activity.expired" : "activity.removed"));
    scheduleActivityCheck();
    return;
  }
  if (Number.isFinite(Number(data.serverTime))) state.serverClockOffset = Number(data.serverTime) - Date.now() / 1000;
  if (Number.isFinite(Number(data.activityGraceSeconds))) state.activityGraceSeconds = Number(data.activityGraceSeconds);
  state.players = Array.isArray(data.members) ? data.members : state.players;
  closeActivityDialog();
  renderRoster();
  updateRoleUI();
  scheduleActivityCheck();
  toast(t("activity.confirmed"));
}

async function loadQueue() {
  try {
    const wasInQueue = inQueue();
    const response = await fetch("/api/queue", { headers: { Accept: "application/json" }, credentials: "same-origin" });
    if (!response.ok) return;
    const data = await response.json();
    if (Number.isFinite(Number(data.serverTime))) state.serverClockOffset = Number(data.serverTime) - Date.now() / 1000;
    if (Number.isFinite(Number(data.activityGraceSeconds))) state.activityGraceSeconds = Number(data.activityGraceSeconds);
    state.players = Array.isArray(data.members) ? data.members : [];
    state.capActive = Boolean(data.capActive);
    if (!state.capActive && state.teams) clearMatchRoomState();
    state.capCreator = data.creator || null;
    state.capMode = data.capMode === "custom" ? "custom" : "standard";
    if (/^[0-9a-f]{16}$/.test(String(data.capNonce || ""))) state.nonce = data.capNonce;
    applyCapMetadata({ name: data.capName, game: data.game, map: data.map });
    state.serverStatus = state.capMode === "custom" ? "custom" : "ready";
    state.testMode = Boolean(data.testMode);
    showLatestJoiner();
    const ownMembership = state.currentUser && state.players.find(player => player.id === state.currentUser.id);
    if (ownMembership) {
      state.currentUser = { ...state.currentUser, name: ownMembership.name, roles: [...ownMembership.roles] };
      state.selectedRoles = new Set(ownMembership.roles);
      state.mainRole = ownMembership.mainRole || ownMembership.roles[0] || null;
    }
    if (data.activityExpired) toast(t("activity.expired"));
    else if (wasInQueue && !ownMembership) toast(t("admin.removedYou"));
    renderRoster();
    showQueuePage();
    renderCapCreator();
    renderSteamIdentity();
    updateRoleUI();
    renderTestMode();
    scheduleActivityCheck();
    void loadCapChat();
  } catch {
    // Keep the last known roster during a transient connection error.
  }
}

async function joinOrUpdateQueue() {
  if (!state.signedIn) { startSteamSignIn(); return; }
  const roles = [...state.selectedRoles];
  const mainRole = state.mainRole;
  const candidate = { ...state.currentUser, roles, mainRole, time: t("now") };
  const existingIndex = state.players.findIndex(player => player.id === candidate.id);
  const nextPlayers = existingIndex >= 0
    ? state.players.map((player, index) => index === existingIndex ? { ...player, roles, mainRole } : player)
    : [...state.players, candidate];
  if (nextPlayers.length > 12) { toast(t("queueFull")); return; }
  const { response, data } = await apiPost("/api/queue/join", { roles, mainRole });
  if (!response.ok) {
    toast(t(data.error === "queue_full" ? "queueFull" : "noCompatible"));
    return;
  }
  state.players = data.members || nextPlayers;
  state.capActive = true;
  state.capCreator = data.creator || state.capCreator;
  state.currentUser = { ...candidate, name: state.currentUser.name };
  setActivity(existingIndex >= 0 ? "liveSaved" : "liveJoined", existingIndex >= 0 ? {} : { name: candidate.name });
  renderRoster();
  updateRoleUI();
  scheduleActivityCheck();
  toast(existingIndex >= 0 ? t("positionsSaved") : t("joined", { count: state.players.length }));
  maybeStartReadyCheck();
}

async function leaveQueue() {
  if (!inQueue() || state.queueLocked) return;
  const { response, data } = await apiPost("/api/queue/leave");
  if (!response.ok) return;
  state.players = data.members || state.players.filter(player => player.id !== state.currentUser.id);
  state.capActive = !data.capClosed;
  if (data.capClosed) state.capCreator = null;
  setActivity("liveLeft", { name: state.currentUser.name });
  renderRoster();
  showQueuePage();
  updateRoleUI();
  scheduleActivityCheck();
  toast(t("left"));
}

async function emptyQueueAsAdmin() {
  if (state.currentUser?.role !== "admin" || !window.confirm(t("admin.emptyConfirm"))) return;
  const { response, data } = await apiPost("/api/admin/queue/empty");
  if (!response.ok) return;
  state.players = [];
  state.queueLocked = false;
  state.durationVote = null;
  if ($("#durationVoteDialog").open) $("#durationVoteDialog").close();
  showLatestJoiner();
  renderRoster();
  updateRoleUI();
  scheduleActivityCheck();
  toast(t("admin.emptied", { count: data.removed || 0 }));
}

async function removePlayerAsAdmin(steamid, name) {
  if (state.currentUser?.role !== "admin" || state.queueLocked || !window.confirm(t("admin.removeConfirm", { name }))) return;
  const { response, data } = await apiPost("/api/admin/queue/remove", { steamid });
  if (!response.ok) {
    await loadQueue();
    return;
  }
  state.players = Array.isArray(data.members) ? data.members : state.players.filter(player => player.id !== steamid);
  setActivity("admin.removed", { name });
  renderRoster();
  updateRoleUI();
  scheduleActivityCheck();
  toast(t("admin.removed", { name }));
}

function isAdminTestMode() {
  return Boolean(state.testMode && state.currentUser?.role === "admin");
}

function readyTarget() {
  return isAdminTestMode() ? Math.max(1, state.players.length) : 12;
}

function renderTestMode() {
  const button = $("#testModeToggle");
  const notice = $("#testModeNotice");
  if (!button || !notice) return;
  const isAdmin = state.currentUser?.role === "admin";
  button.hidden = !isAdmin;
  button.classList.toggle("active", state.testMode);
  button.textContent = t(state.testMode ? "admin.testOn" : "admin.testOff");
  notice.hidden = !isAdmin || !state.testMode;
  $("#cancelTestModeReady").hidden = !isAdmin || !state.testMode;
  $("#endTestMode").hidden = !isAdmin || !state.testMode || state.matchAssignments.length === 0;
}

async function saveTestMode(enabled, startWhenReady = true) {
  if (state.currentUser?.role !== "admin") return false;
  const { response, data } = await apiPost("/api/admin/test-mode", { enabled });
  if (!response.ok) return false;
  state.testMode = Boolean(data.testMode);
  if (Array.isArray(data.members)) {
    state.players = data.members;
    showLatestJoiner();
  }
  renderTestMode();
  renderRoster();
  updateRoleUI();
  updateCounts();
  toast(t(enabled ? "admin.testStarted" : "admin.testEnded", { count: enabled ? state.players.length : Number(data.removed || 0) }));
  if (enabled && startWhenReady) maybeStartReadyCheck();
  return data;
}

async function toggleTestMode() {
  if (state.currentUser?.role !== "admin") return;
  if (state.testMode) {
    await cancelTestMode();
    return;
  }
  if (!state.queueLocked) await saveTestMode(true);
}

async function cancelTestMode() {
  if (!isAdminTestMode() || !await saveTestMode(false, false)) return;
  clearReadyTimers();
  clearTimeout(state.assignmentTimer);
  state.assignmentTimer = null;
  const readyDialog = $("#readyDialog");
  if (readyDialog.open) readyDialog.close();
  state.queueLocked = false;
  clearMatchRoomState();
  state.readyAccepted = false;
  state.readyOthers = 1;
  state.serverStatus = GAME_OPTIONS[state.gameKey]?.server ? "ready" : "unavailable";
  setStep("queue");
  showQueuePage();
  renderRoster();
  updateRoleUI();
  renderServerConnection();
  renderTestMode();
}

function maybeStartReadyCheck() {
  if (state.queueLocked) return;
  if (isAdminTestMode() && state.players.length >= 1) {
    startReadyCheck();
    return;
  }
  if (inQueue() && state.players.length === 12) startReadyCheck();
}

async function openAdminPanel() {
  if (state.currentUser?.role !== "admin") return;
  $("#adminDialog").showModal();
  $("#adminUserSearch").value = "";
  $("#adminUserList").innerHTML = `<div class="admin-loading">…</div>`;
  try {
    const response = await fetch("/api/admin/users", { headers: { Accept: "application/json" }, credentials: "same-origin" });
    if (!response.ok) throw new Error("admin list unavailable");
    const data = await response.json();
    state.adminUsers = data.users || [];
    renderAdminUsers(state.adminUsers);
  } catch {
    $("#adminUserList").innerHTML = `<p class="admin-error">${escapeHTML(t("admin.loadFailed"))}</p>`;
  }
}

function renderAdminUsers(users) {
  if (!users.length) {
    $("#adminUserList").innerHTML = `<p class="admin-error">${escapeHTML(t("admin.noResults"))}</p>`;
    return;
  }
  $("#adminUserList").innerHTML = users.map(user => `
    <article class="admin-user-row">
      <div class="avatar" aria-hidden="true">${escapeHTML(initials(user.name))}</div>
      <div><b>${escapeHTML(user.name)}</b><small>${escapeHTML(user.steamid)}${user.owner ? ` · ${escapeHTML(t("admin.owner"))}` : ""}</small></div>
      <div class="admin-user-controls">
        <select data-admin-steamid="${escapeHTML(user.steamid)}" aria-label="${escapeHTML(user.name)} access" ${user.owner ? "disabled" : ""}>
          <option value="user" ${user.role === "user" ? "selected" : ""}>${escapeHTML(t("admin.user"))}</option>
          <option value="admin" ${user.role === "admin" ? "selected" : ""}>${escapeHTML(t("admin.admin"))}</option>
        </select>
        <select data-account-steamid="${escapeHTML(user.steamid)}" data-account-status="${escapeHTML(user.status || "active")}" aria-label="${escapeHTML(user.name)} status" ${user.owner ? "disabled" : ""}>
          <option value="active" ${(user.status || "active") === "active" ? "selected" : ""}>${escapeHTML(t("admin.status.active"))}</option>
          <option value="suspended" ${user.status === "suspended" ? "selected" : ""}>${escapeHTML(t("admin.status.suspended"))}</option>
          <option value="banned" ${user.status === "banned" ? "selected" : ""}>${escapeHTML(t("admin.status.banned"))}</option>
        </select>
      </div>
    </article>`).join("");
  $$('[data-admin-steamid]').forEach(select => select.addEventListener("change", async () => {
    select.disabled = true;
    const { response } = await apiPost("/api/admin/users/role", { steamid: select.dataset.adminSteamid, role: select.value });
    select.disabled = false;
    if (response.ok) {
      const user = state.adminUsers.find(item => item.steamid === select.dataset.adminSteamid);
      if (user) user.role = select.value;
      toast(t("admin.saved"));
    }
  }));
  $$('[data-account-steamid]').forEach(select => select.addEventListener("change", async () => {
    const user = state.adminUsers.find(item => item.steamid === select.dataset.accountSteamid);
    const previousStatus = user?.status || "active";
    const nextStatus = select.value;
    if (!user || !window.confirm(t("admin.statusConfirm", { name: user.name, status: t(`admin.status.${nextStatus}`) }))) {
      select.value = previousStatus;
      return;
    }
    select.disabled = true;
    const { response } = await apiPost("/api/admin/users/status", { steamid: select.dataset.accountSteamid, status: nextStatus });
    select.disabled = false;
    if (!response.ok) {
      select.value = previousStatus;
      toast(t("admin.statusFailed"));
      return;
    }
    user.status = nextStatus;
    renderAdminUsers(state.adminUsers);
    toast(t("admin.statusSaved"));
    void loadQueue();
  }));
}

function filterAdminUsers() {
  const query = $("#adminUserSearch").value.trim().toLocaleLowerCase();
  const matches = query
    ? state.adminUsers.filter(user => user.name.toLocaleLowerCase().includes(query) || user.steamid.includes(query))
    : state.adminUsers;
  renderAdminUsers(matches);
}

function renderProfileSummary() {
  const profile = state.profile;
  if (!profile) return;
  $("#profileSteamName").textContent = profile.name;
  $("#profileAvatar").textContent = initials(profile.name);
  $("#profileCaps").textContent = String(profile.stats?.capsPlayed || 0);
  $("#profileMostRole").textContent = profile.stats?.mostPlayedRole ? roleLabel(profile.stats.mostPlayedRole) : t("profile.notSet");
  $("#profileMainRoleSummary").textContent = profile.preferences?.mainRole ? roleLabel(profile.preferences.mainRole) : t("profile.notSet");
}

function populateProfileForm() {
  const profile = state.profile;
  if (!profile) return;
  $("#profileCountry").value = profile.country || "";
  $("#profileFavoriteGame").value = profile.favoriteGame || "css";
  $("#profileAvailability").value = profile.availability || "flexible";
  $("#profileBio").value = profile.bio || "";
  const preferences = profile.preferences || {};
  state.profileSelectedRoles = new Set(Array.isArray(preferences.roles) ? preferences.roles : []);
  state.profileMainRole = preferences.mainRole || null;
  renderProfilePreferences();
  renderProfileSummary();
}

function renderProfilePreferences() {
  const selected = state.profileSelectedRoles;
  $$('[data-profile-role]').forEach(button => button.setAttribute("aria-pressed", String(selected.has(button.dataset.profileRole))));
  if (state.profileMainRole && !selected.has(state.profileMainRole)) state.profileMainRole = null;
  const select = $("#profileDefaultMainRole");
  select.replaceChildren(new Option(t("join.mainPlaceholder"), ""), ...[...selected].map(role => new Option(roleLabel(role), role)));
  select.disabled = selected.size === 0;
  select.value = state.profileMainRole || "";
}

function toggleProfileRole(button) {
  const role = button.dataset.profileRole;
  state.profileSelectedRoles.has(role) ? state.profileSelectedRoles.delete(role) : state.profileSelectedRoles.add(role);
  renderProfilePreferences();
}

async function openProfileDialog() {
  if (!state.signedIn) return;
  $("#loginDialog").close();
  $("#profileDialog").showModal();
  $("#saveProfile").disabled = true;
  try {
    const response = await fetch("/api/profile", { headers: { Accept: "application/json" }, credentials: "same-origin" });
    if (!response.ok) throw new Error("profile unavailable");
    const data = await response.json();
    state.profile = data.profile;
    populateProfileForm();
  } catch {
    $("#profileDialog").close();
    toast(t("profile.loadFailed"));
  } finally {
    $("#saveProfile").disabled = false;
  }
}

async function saveProfile(event) {
  event.preventDefault();
  const roles = [...state.profileSelectedRoles];
  const mainRole = state.profileMainRole;
  if (roles.length && (roles.length < 2 || !mainRole || !roles.includes(mainRole))) {
    toast(t("profile.preferencesInvalid"));
    return;
  }
  const button = $("#saveProfile");
  button.disabled = true;
  const { response, data } = await apiPost("/api/profile", {
    country: $("#profileCountry").value,
    favoriteGame: $("#profileFavoriteGame").value,
    availability: $("#profileAvailability").value,
    bio: $("#profileBio").value,
    preferences: { roles, mainRole }
  });
  button.disabled = false;
  if (!response.ok || !data.profile) {
    toast(t("profile.loadFailed"));
    return;
  }
  state.profile = data.profile;
  populateProfileForm();
  toast(t("profile.saved"));
}

async function dismissCap() {
  const confirmationKey = state.teams ? "cap.cancelConfirm" : "cap.dismissConfirm";
  if (!state.capActive || state.capCreator?.id !== state.currentUser?.id || !window.confirm(t(confirmationKey))) return;
  const { response, data } = await apiPost("/api/cap/dismiss");
  if (!response.ok) return;
  state.players = [];
  state.capActive = false;
  state.capCreator = null;
  state.testMode = false;
  clearMatchRoomState();
  state.chatMessages = [];
  showQueuePage();
  renderSteamIdentity();
  renderRoster();
  updateRoleUI();
  scheduleActivityCheck();
  toast(t("cap.dismissed", { count: data.removed || 0 }));
}

function fillDemoQueue() {
  if (state.queueLocked || state.players.length >= 12) return;
  if (state.signedIn && !inQueue()) {
    const candidate = { ...state.currentUser, roles: [...state.selectedRoles], mainRole: state.mainRole, time: t("now") };
    if (candidate.roles.length === 0) candidate.roles = ["DEF", "MID"];
    if (!candidate.mainRole) candidate.mainRole = candidate.roles[0];
    state.players.push(candidate);
  }
  for (const player of demoPool) {
    if (state.players.length >= 12) break;
    if (!state.players.some(existing => existing.id === player.id)) {
      state.players.push({ ...player, roles: [...player.roles], mainRole: player.mainRole || player.roles[0], time: t("now") });
    }
  }
  updateRoleUI();
  renderRoster();
  setActivity("demoFilled", {}, "activity.demo");
  maybeStartReadyCheck();
}

function startReadyCheck() {
  if (state.queueLocked) return;
  const target = readyTarget();
  state.queueLocked = true;
  state.readyAccepted = false;
  state.readyOthers = target === 1 ? 0 : 1;
  state.readySeconds = 60;
  setStep("ready");
  updateRoleUI();
  updateCounts();
  $("#readyDialog").classList.remove("ready-accepted");
  $("#acceptReady").disabled = false;
  $("#acceptReady").textContent = t("ready.accept");
  renderReadyCopy();
  updateReadyDisplay();
  $("#readyDialog").showModal();
  state.readyTick = setInterval(() => {
    state.readySeconds -= 1;
    updateReadyDisplay();
    if (state.readySeconds <= 0) {
      clearReadyTimers();
      $("#readyDialog").close();
      state.queueLocked = false;
      setStep("queue");
      toast(t("readyExpired"));
      updateRoleUI();
    }
  }, 1000);
  if (target > 1) {
    state.readySimulation = setInterval(() => {
      if (state.readyOthers < target - 1) state.readyOthers += 1;
      updateReadyDisplay();
      maybeFinishReady();
    }, 480);
  }
}

function renderReadyCopy() {
  const target = readyTarget();
  const pulse = $("#readyTargetPulse");
  const description = $("#readyDescription");
  const label = $("#readyCountLabel");
  if (pulse) pulse.textContent = target;
  if (description) description.textContent = t(isAdminTestMode() ? "ready.testDescription" : "ready.description");
  if (label) label.textContent = t("ready.count", { count: target });
}

function updateReadyDisplay() {
  const target = readyTarget();
  const ready = state.readyOthers + (state.readyAccepted ? 1 : 0);
  $("#readyCount").textContent = ready;
  $("#readyTimer").textContent = `00:${String(state.readySeconds).padStart(2, "0")}`;
  $("#readyProgress").style.width = `${ready / target * 100}%`;
}

function acceptReady() {
  if (state.readyAccepted) return;
  state.readyAccepted = true;
  $("#readyDialog").classList.add("ready-accepted");
  $("#acceptReady").disabled = true;
  $("#acceptReady").textContent = t("readyConfirmed");
  updateReadyDisplay();
  maybeFinishReady();
}

function maybeFinishReady() {
  if (state.readyAccepted && state.readyOthers >= readyTarget() - 1) beginAssignment();
}

function clearReadyTimers() {
  clearInterval(state.readyTick);
  clearInterval(state.readySimulation);
  state.readyTick = null;
  state.readySimulation = null;
}

function beginAssignment() {
  clearReadyTimers();
  $("#readyDialog").close();
  setStep("draw");
  toast(t("drawing"));
  state.assignmentTimer = setTimeout(() => {
    state.assignmentTimer = null;
    if (state.queueLocked) showMatchRoom();
  }, 950);
}

function hashString(text) {
  let hash = 2166136261;
  for (let index = 0; index < text.length; index += 1) {
    hash ^= text.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return hash >>> 0;
}

function seededRandom(seed) {
  let value = seed >>> 0;
  return () => {
    value += 0x6D2B79F5;
    let result = value;
    result = Math.imul(result ^ result >>> 15, result | 1);
    result ^= result + Math.imul(result ^ result >>> 7, result | 61);
    return ((result ^ result >>> 14) >>> 0) / 4294967296;
  };
}

function assignRolesUniform(players, random, requireFullRoster = true) {
  const ordered = [...players].sort((a, b) => a.id.localeCompare(b.id));
  const memo = new Map();
  function count(index, capacity) {
    if (index === ordered.length) {
      return (!requireFullRoster || Object.values(capacity).every(value => value === 0))
        ? { best: 0, ways: 1 }
        : { best: Number.NEGATIVE_INFINITY, ways: 0 };
    }
    const key = `${index}|${capacity.GK},${capacity.DEF},${capacity.MID},${capacity.WING}`;
    if (memo.has(key)) return memo.get(key);
    let best = Number.NEGATIVE_INFINITY;
    let ways = 0;
    for (const role of Object.keys(ROLE_CAPACITY)) {
      if (capacity[role] <= 0) continue;
      const suffix = count(index + 1, { ...capacity, [role]: capacity[role] - 1 });
      if (suffix.ways === 0) continue;
      const preference = role === ordered[index].mainRole ? 100 : ordered[index].roles.includes(role) ? 1 : 0;
      const score = suffix.best + preference;
      if (score > best) {
        best = score;
        ways = suffix.ways;
      } else if (score === best) {
        ways += suffix.ways;
      }
    }
    const result = { best, ways };
    memo.set(key, result);
    return result;
  }
  const result = [];
  let capacity = { ...ROLE_CAPACITY };
  for (let index = 0; index < ordered.length; index += 1) {
    const optimum = count(index, capacity).best;
    const options = Object.keys(ROLE_CAPACITY).filter(role => capacity[role] > 0).map(role => {
      const next = { ...capacity, [role]: capacity[role] - 1 };
      const suffix = count(index + 1, next);
      const preference = role === ordered[index].mainRole ? 100 : ordered[index].roles.includes(role) ? 1 : 0;
      const score = suffix.best + preference;
      return { role, next, score, weight: suffix.ways };
    }).filter(option => option.weight > 0 && option.score === optimum);
    const totalWeight = options.reduce((sum, option) => sum + option.weight, 0);
    let draw = random() * totalWeight;
    let chosen = options[options.length - 1];
    for (const option of options) {
      draw -= option.weight;
      if (draw < 0) { chosen = option; break; }
    }
    const assignmentSource = chosen.role === ordered[index].mainRole
      ? "main"
      : ordered[index].roles.includes(chosen.role) ? "optional" : "fallback";
    result.push({ ...ordered[index], assignedRole: chosen.role, assignmentSource });
    capacity = chosen.next;
  }
  return result;
}

function shuffle(items, random) {
  const result = [...items];
  for (let index = result.length - 1; index > 0; index -= 1) {
    const swap = Math.floor(random() * (index + 1));
    [result[index], result[swap]] = [result[swap], result[index]];
  }
  return result;
}

function splitTeams(assignments, random) {
  const home = [];
  const away = [];
  for (const role of Object.keys(ROLE_CAPACITY)) {
    const group = shuffle(assignments.filter(player => player.assignedRole === role), random);
    let first;
    let second;
    if (home.length === away.length) {
      [first, second] = random() < 0.5 ? [home, away] : [away, home];
    } else {
      [first, second] = home.length < away.length ? [home, away] : [away, home];
    }
    group.forEach((player, index) => (index % 2 === 0 ? first : second).push(player));
  }
  const order = { GK: 0, DEF: 1, MID: 2, WING: 3 };
  home.sort((a, b) => order[a.assignedRole] - order[b.assignedRole]);
  away.sort((a, b) => order[a.assignedRole] - order[b.assignedRole]);
  return { home, away };
}

function renderTeam(selector, players) {
  $(selector).innerHTML = players.map(player => {
    const role = player.assignedRole;
    const isCurrent = state.currentUser?.id === player.id;
    const isFallback = player.assignmentSource === "fallback";
    return `<div class="formation-player${isCurrent ? " you" : ""}${isFallback ? " fallback" : ""}" data-you-label="${escapeHTML(t("you"))}"><span class="role-icon ${ROLE_CLASS[role]}">${ROLE_SHORT[role]}</span><div class="avatar">${escapeHTML(initials(player.name))}</div><div><b>${escapeHTML(player.name)}</b><small>${escapeHTML(assignmentLabel(role, player.assignmentSource))} · ${escapeHTML(player.id.slice(-6))}</small></div></div>`;
  }).join("");
}

function renderTeamCounts() {
  if (!state.teams) return;
  const setCount = (selector, count) => {
    const element = $(selector);
    if (element) element.textContent = t(count === 1 ? "match.onePlayer" : "match.playerCount", { count });
  };
  setCount("#homePlayerCount", state.teams.home.length);
  setCount("#awayPlayerCount", state.teams.away.length);
}

function renderServerConnection() {
  const game = GAME_OPTIONS[state.gameKey] || GAME_OPTIONS.css;
  const isCs2 = state.gameKey === "cs2";
  const room = $("#serverRoom");
  const title = $("#serverStatusTitle");
  const detail = $("#serverStatusDetail");
  const command = $("#connectCommand");
  const copyButton = $("#copyConnect");
  const connectButton = $("#connectServer");
  if (!room || !title || !detail || !command || !copyButton || !connectButton) return;

  const disabledState = (status, titleKey, detailKey, buttonKey) => {
    room.dataset.serverState = status;
    title.textContent = t(titleKey);
    detail.textContent = t(detailKey);
    command.textContent = "—";
    copyButton.disabled = true;
    connectButton.removeAttribute("href");
    connectButton.setAttribute("aria-disabled", "true");
    connectButton.classList.add("disabled");
    connectButton.textContent = t(buttonKey);
  };

  if (state.capMode === "custom") {
    disabledState("custom", "match.customServerTitle", "match.customServerDetail", "match.customServerAction");
    return;
  }
  if (!game.server) {
    disabledState("unavailable", "match.serverUnavailable", "match.serverUnavailableDetail", "match.unavailable");
    return;
  }
  if (state.serverStatus === "preparing") {
    const preparingKey = isCs2 ? "match.serverPreparingCs2" : "match.serverPreparing";
    disabledState("preparing", preparingKey, "match.serverPreparingDetail", preparingKey);
    return;
  }
  if (state.serverStatus === "voting") {
    disabledState("voting", "match.durationVoting", "match.durationVotingDetail", "match.durationVoting");
    return;
  }
  if (state.serverStatus === "error") {
    disabledState("error", isCs2 ? "match.serverPrepareFailedCs2" : "match.serverPrepareFailed", "match.serverPrepareFailedDetail", "match.unavailable");
    return;
  }

  room.dataset.serverState = "ready";
  title.textContent = t(isCs2 ? "match.serverReadyCs2" : "match.serverReadyCss");
  detail.textContent = t(isCs2 ? "match.serverDetailCs2" : "match.serverDetailCss");
  command.textContent = `password ${game.server.password}; connect ${game.server.address}`;
  copyButton.disabled = false;
  connectButton.href = `steam://connect/${game.server.address}/${encodeURIComponent(game.server.password)}`;
  connectButton.removeAttribute("aria-disabled");
  connectButton.classList.remove("disabled");
  connectButton.textContent = t(isCs2 ? "match.autojoinCs2" : "match.autojoinCss");
}

function attemptAutomaticJoin() {
  const game = GAME_OPTIONS[state.gameKey];
  if (state.capMode !== "standard" || state.serverStatus !== "ready" || !game?.server || state.autoJoinAttempted || isAdminTestMode()) return;
  state.autoJoinAttempted = true;
  window.setTimeout(() => {
    if (state.serverStatus === "ready") window.location.assign(`steam://connect/${game.server.address}/${encodeURIComponent(game.server.password)}`);
  }, 700);
}

function halfLengthLabel(seconds) {
  return `${seconds === 450 ? "7.5" : seconds / 60} min`;
}

function renderDurationVote(vote) {
  if (!vote) return;
  state.durationVote = vote;
  $("#durationVoteTimer").textContent = String(Math.max(0, Number(vote.secondsRemaining) || 0));
  $$('[data-duration-vote]').forEach(button => {
    const seconds = Number(button.dataset.durationVote);
    button.classList.toggle("selected", seconds === vote.ownVote);
    button.disabled = Boolean(vote.resolved);
  });
  $$('[data-vote-count]').forEach(counter => {
    counter.textContent = String(vote.counts?.[counter.dataset.voteCount] || 0);
  });
  const status = $("#durationVoteStatus");
  if (vote.resolved) status.textContent = t("vote.result", { length: halfLengthLabel(vote.halfSeconds) });
  else if (vote.ownVote) status.textContent = t("vote.voted", { length: halfLengthLabel(vote.ownVote) });
  else status.textContent = t("vote.waiting");
}

async function castDurationVote(halfSeconds) {
  const current = state.durationVote;
  if (!current || current.resolved) return;
  const { response, data } = await apiPost("/api/match/duration-vote", {
    signature: current.signature,
    halfSeconds
  });
  if (response.ok && data.vote) renderDurationVote(data.vote);
}

async function waitForDurationVote(assignments) {
  const { response, data } = await apiPost("/api/match/duration-vote/start", {
    game: state.gameKey,
    map: state.mapName,
    assignments
  });
  if (!response.ok || !data.vote) throw new Error("duration_vote_start_failed");
  const dialog = $("#durationVoteDialog");
  renderDurationVote(data.vote);
  if (!dialog.open) dialog.showModal();
  while (!state.durationVote?.resolved) {
    await new Promise(resolve => window.setTimeout(resolve, 400));
    const signature = state.durationVote?.signature;
    if (!signature) throw new Error("duration_vote_cancelled");
    const voteResponse = await fetch(`/api/match/duration-vote?signature=${encodeURIComponent(signature)}`, {
      headers: { Accept: "application/json" },
      credentials: "same-origin"
    });
    if (!voteResponse.ok) throw new Error("duration_vote_poll_failed");
    const voteData = await voteResponse.json();
    if (!voteData.vote) throw new Error("duration_vote_missing");
    renderDurationVote(voteData.vote);
  }
  const result = state.durationVote;
  await new Promise(resolve => window.setTimeout(resolve, 500));
  if (dialog.open) dialog.close();
  return result;
}

async function prepareServerForCap() {
  const game = GAME_OPTIONS[state.gameKey] || GAME_OPTIONS.css;
  if (state.capMode === "custom") {
    state.serverStatus = "custom";
    renderServerConnection();
    return;
  }
  if (!game.server) {
    state.serverStatus = "unavailable";
    renderServerConnection();
    return;
  }
  state.serverStatus = "voting";
  renderServerConnection();
  try {
    const assignments = state.matchAssignments
      .filter(player => !player.test)
      .map(player => ({ id: player.id, role: player.assignedRole, team: player.assignedTeam }));
    const vote = await waitForDurationVote(assignments);
    state.matchSignature = vote.signature;
    state.serverStatus = "preparing";
    renderServerConnection();
    let prepared = false;
    for (let attempt = 0; attempt < 30 && !prepared; attempt += 1) {
      const { response, data } = await apiPost("/api/match/prepare", {
        game: state.gameKey,
        map: state.mapName,
        assignments,
        voteSignature: vote.signature,
        halfSeconds: vote.halfSeconds
      });
      if (!response.ok) throw new Error("server_prepare_failed");
      prepared = data.prepared === true;
      if (!prepared) await new Promise(resolve => window.setTimeout(resolve, 300));
    }
    if (!prepared) throw new Error("server_prepare_timeout");
    state.serverStatus = "ready";
  } catch {
    if ($("#durationVoteDialog").open) $("#durationVoteDialog").close();
    state.serverStatus = "error";
  }
  renderServerConnection();
  attemptAutomaticJoin();
  if (state.serverStatus === "ready") startMatchStatusPolling();
}

function playersForAssignment() {
  return state.players.slice(0, 12).map(player => ({ ...player, roles: [...player.roles] }));
}

function showMatchRoom() {
  const matchPlayers = playersForAssignment();
  const canonical = matchPlayers.map(player => `${player.id}:${player.mainRole || player.roles[0]}:${player.roles.slice().sort().join(",")}`).sort().join("|");
  const seed = hashString(`${state.nonce || "missing-server-nonce"}|${state.gameKey}|SOCCER-MOD-6V6|${canonical}|CAP-DP-v3`);
  const random = seededRandom(seed);
  const assignments = assignRolesUniform(matchPlayers, random, !isAdminTestMode() || matchPlayers.length === 12);
  const teams = splitTeams(assignments, random);
  teams.home.forEach(player => { player.assignedTeam = "home"; });
  teams.away.forEach(player => { player.assignedTeam = "away"; });
  state.matchAssignments = [...teams.home, ...teams.away];
  state.teams = teams;
  renderTeam("#homeTeam", teams.home);
  renderTeam("#awayTeam", teams.away);
  renderTeamCounts();
  const currentAssignment = assignments.find(player => player.id === state.currentUser?.id);
  state.currentAssignedRole = currentAssignment?.assignedRole || null;
  state.currentAssignmentSource = currentAssignment?.assignmentSource || null;
  $("#yourRole").textContent = currentAssignment
    ? assignmentLabel(currentAssignment.assignedRole, currentAssignment.assignmentSource)
    : t("spectator");
  $("#auditSeed").textContent = seed.toString(16).toUpperCase().padStart(8, "0");
  $("#queue").hidden = true;
  $("#matchRoom").hidden = false;
  renderSteamIdentity();
  renderChat();
  void loadCapChat();
  renderTestMode();
  setStep("server");
  $("#matchRoom").scrollIntoView({ behavior: "smooth", block: "start" });
  toast(t("matchReady"));
  void prepareServerForCap();
}

function updateCreateMapOptions() {
  const gameKey = $("#gameInput").value;
  const game = GAME_OPTIONS[gameKey] || GAME_OPTIONS.css;
  const mapSelect = $("#mapInput");
  mapSelect.replaceChildren(...game.maps.map(map => new Option(map, map)));
  $("#createGameSummary").textContent = game.short;
}

function updateCreateCapType() {
  const isCustom = $("#capTypeInput").value === "custom";
  $("#capTypeHelp").textContent = t(isCustom ? "create.customHelp" : "create.standardHelp");
}

function openCreateCapDialog() {
  if (!state.signedIn) { startSteamSignIn(); return; }
  updateCreateMapOptions();
  updateCreateCapType();
  $("#createDialog").showModal();
}

async function createCap(event) {
  event.preventDefault();
  const name = $("#capNameInput").value.trim() || "Soccer Mod";
  const gameKey = $("#gameInput").value;
  const game = GAME_OPTIONS[gameKey] || GAME_OPTIONS.css;
  const map = $("#mapInput").value;
  const capMode = $("#capTypeInput").value === "custom" ? "custom" : "standard";
  if (!game.maps.includes(map)) {
    updateCreateMapOptions();
    return;
  }
  const { response, data } = await apiPost("/api/cap/create", { mode: capMode, name, game: gameKey, map });
  if (!response.ok) return;
  state.capActive = true;
  state.capCreator = state.currentUser ? { id: state.currentUser.id, name: state.currentUser.name } : null;
  state.capMode = capMode;
  applyCapMetadata({ name: data.capName || name, game: data.game || gameKey, map: data.map || map });
  state.serverStatus = capMode === "custom" ? "custom" : "ready";
  state.autoJoinAttempted = false;
  renderServerConnection();
  $("#createDialog").close();
  showQueuePage();
  renderCapCreator();
  renderSteamIdentity();
  state.nonce = /^[0-9a-f]{16}$/.test(String(data.capNonce || "")) ? data.capNonce : "";
  toast(t("capPublished", { name }));
}

async function copyConnect() {
  if (state.serverStatus !== "ready" || !GAME_OPTIONS[state.gameKey]?.server) return;
  try {
    await navigator.clipboard.writeText($("#connectCommand").textContent);
    toast(t("copied"));
  } catch {
    toast(t("copyFailed"));
  }
}

$$('[data-role]').forEach(button => button.addEventListener("click", () => handleRoleButton(button)));
$("#steamLogin").addEventListener("click", () => state.signedIn ? openSteamAccount() : startSteamSignIn());
$("#steamAuthStart").addEventListener("click", event => { event.preventDefault(); startSteamSignIn(); });
$("#closeSteamDialog").addEventListener("click", () => $("#loginDialog").close());
$("#steamLogout").addEventListener("click", signOutSteam);
$("#joinQueue").addEventListener("click", joinOrUpdateQueue);
$("#leaveQueue").addEventListener("click", leaveQueue);
$("#emptyQueue").addEventListener("click", emptyQueueAsAdmin);
$("#dismissCap").addEventListener("click", dismissCap);
$("#cancelCapMatch").addEventListener("click", dismissCap);
$("#confirmActivity").addEventListener("click", confirmQueueActivity);
$("#chatForm").addEventListener("submit", sendChatMessage);
$("#activityDialog").addEventListener("cancel", event => event.preventDefault());
$("#durationVoteDialog").addEventListener("cancel", event => event.preventDefault());
$$('[data-duration-vote]').forEach(button => button.addEventListener("click", () => castDurationVote(Number(button.dataset.durationVote))));
$("#testModeToggle").addEventListener("click", toggleTestMode);
$("#cancelTestModeNotice").addEventListener("click", cancelTestMode);
$("#cancelTestModeReady").addEventListener("click", cancelTestMode);
$("#endTestMode").addEventListener("click", cancelTestMode);
$("#manageUsers").addEventListener("click", openAdminPanel);
$("#manageUsersAccount").addEventListener("click", () => { $("#loginDialog").close(); openAdminPanel(); });
$("#adminUserSearch").addEventListener("input", filterAdminUsers);
$("#closeAdminDialog").addEventListener("click", () => $("#adminDialog").close());
$("#manageProfile").addEventListener("click", openProfileDialog);
$("#closeProfileDialog").addEventListener("click", () => $("#profileDialog").close());
$$('[data-profile-role]').forEach(button => button.addEventListener("click", () => toggleProfileRole(button)));
$("#profileDefaultMainRole").addEventListener("change", event => { state.profileMainRole = event.target.value || null; renderProfilePreferences(); });
$("#profileForm").addEventListener("submit", saveProfile);
$("#acceptReady").addEventListener("click", acceptReady);
$("#createCap").addEventListener("click", openCreateCapDialog);
$("#createCapEmpty").addEventListener("click", openCreateCapDialog);
$("#gameInput").addEventListener("change", updateCreateMapOptions);
$("#capTypeInput").addEventListener("change", updateCreateCapType);
$("#closeCreateDialog").addEventListener("click", () => $("#createDialog").close());
$("#mainRoleInput").addEventListener("change", event => { state.mainRole = event.target.value || null; updateRoleUI(); });
$("#createForm").addEventListener("submit", createCap);
$("#showRules").addEventListener("click", () => $("#rulesDialog").showModal());
$("#closeRules").addEventListener("click", () => $("#rulesDialog").close());
$("#copyConnect").addEventListener("click", copyConnect);
$$('[data-language]').forEach(button => button.addEventListener("click", () => applyLanguage(button.dataset.language)));
$("#colorModeToggle").addEventListener("click", () => applyColorMode(colorMode === "dark" ? "light" : "dark"));
applyTheme();
applyColorMode(colorMode);
applyLanguage(locale);
if (window.location.hash === "#community") window.location.replace("/community.html");
showQueuePage();
renderNoCapCopy();
loadSteamSession().then(() => {
  if (new URLSearchParams(window.location.search).get("create") === "1") openCreateCapDialog();
});
setInterval(() => { if (!state.queueLocked) loadQueue(); }, 15000);
setInterval(() => { if (!state.queueLocked) void loadCapChat(); }, 5000);
