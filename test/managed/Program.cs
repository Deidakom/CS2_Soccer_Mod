using SoccerModMvp;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

var saved = new PlayerActivitySample(1, 2, 3, 4, 5, 0);
var moving = saved with { X = 40, Yaw = 90, Buttons = 1 };
if (saved.UnchangedComponents(moving) != 0 || saved.X != 1 || saved.Yaw != 5)
    throw new Exception("Movement must not mutate the saved activity sample.");
if (saved.UnchangedComponents(saved) != 3)
    throw new Exception("An idle player must have three unchanged components.");
if (saved.UnchangedComponents(saved with { Buttons = 1 }) != 2)
    throw new Exception("Button-only activity must preserve SoMoE's two-component rule.");
if (saved.UnchangedComponents(saved with { X = 1.5f, Pitch = 4.5f }) != 3)
    throw new Exception("Small position and angle noise must not count as movement.");
if (saved.UnchangedComponents(saved with { X = 2, Pitch = 5 }) != 1)
    throw new Exception("Movement at the threshold must count as activity.");
Console.WriteLine("Activity regression checks passed (5 scenarios).");

// Exercise storage and cleanup methods from the real plugin assembly without
// initializing BasePlugin, which requires a running CounterStrikeSharp host.
var pluginType = typeof(SoccerModMvpPlugin);
var plugin = RuntimeHelpers.GetUninitializedObject(pluginType);
const BindingFlags privateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
FieldInfo Field(string name) => pluginType.GetField(name, privateInstance)!;
object Call(string name, params object[] args) => pluginType.GetMethod(name, privateInstance)!.Invoke(plugin, args)!;
void InitializeField(string name) => Field(name).SetValue(plugin, Activator.CreateInstance(Field(name).FieldType));
InitializeField("_teamMatchStats");
InitializeField("_teamRoundStats");
InitializeField("_statsStore");
InitializeField("_statsBySteamId");
var first = Call("GetOrCreateStatsEntry", 1UL, "first name");
var repeated = Call("GetOrCreateStatsEntry", 1UL, "renamed");
var second = Call("GetOrCreateStatsEntry", 2UL, "second player");
if (!ReferenceEquals(first, repeated) || ReferenceEquals(first, second)
    || (string)first.GetType().GetProperty("Name")!.GetValue(first)! != "renamed")
    throw new Exception("Stats lookup must reuse the same player's record and update its name.");
var store = Field("_statsStore").GetValue(plugin)!;
var entries = (IList)store.GetType().GetProperty("Entries")!.GetValue(store)!;
if (entries.Count != 2)
    throw new Exception("Repeated touches must not create duplicate persisted player records.");
var publicStats = first.GetType().GetProperty("Public")!.GetValue(first)!;
var matchStats = first.GetType().GetProperty("Match")!.GetValue(first)!;
publicStats.GetType().GetProperty("Points")!.SetValue(publicStats, 30);
matchStats.GetType().GetProperty("Points")!.SetValue(matchStats, 20);
Call("ResetMatchStats");
var resetMatch = first.GetType().GetProperty("Match")!.GetValue(first)!;
if ((int)resetMatch.GetType().GetProperty("Points")!.GetValue(resetMatch)! != 0
    || (int)publicStats.GetType().GetProperty("Points")!.GetValue(publicStats)! != 30)
    throw new Exception("Starting a match must clear its counters and preserve public totals.");

foreach (var name in new[] { "_gkSavesBySlot", "_goalsBySlot", "_forfeitVotes", "_playerPositions",
    "_lastAcceptedKickTimeBySlot", "_playersNearBall", "_playersPushingBall" })
    InitializeField(name);
foreach (var name in new[] { "_lastKickerSlot", "_secondLastKickerSlot", "_gkArmedSaverSlot" })
    Field(name).SetValue(plugin, 7);
Call("BallTouchOnPlayerDisconnect", 3);
if ((int)Field("_lastKickerSlot").GetValue(plugin)! != 7)
    throw new Exception("Another player's disconnect must not erase the current scorer.");
Call("BallTouchOnPlayerDisconnect", 7);
foreach (var name in new[] { "_lastKickerSlot", "_secondLastKickerSlot", "_gkArmedSaverSlot" })
    if ((int)Field(name).GetValue(plugin)! != -1)
        throw new Exception("Disconnected slots must not remain eligible for ball-touch credit.");
Field("_lastKickerSlot").SetValue(plugin, 8);
Call("ResetBallTouchHistory");
if ((int)Field("_lastKickerSlot").GetValue(plugin)! != -1)
    throw new Exception("A ball reset must clear the previous round's touch history.");
Console.WriteLine("Plugin storage and lifecycle regression checks passed (6 scenarios).");

InitializeField("_pawnImpacts");
InitializeField("_recentBallVelocities");
Field("_lastKickerSlot").SetValue(plugin, 8);
Field("_secondLastKickerSlot").SetValue(plugin, 9);
Call("ResetDerivedMotion", false);
if ((int)Field("_lastKickerSlot").GetValue(plugin)! != 8
    || (int)Field("_secondLastKickerSlot").GetValue(plugin)! != 9)
    throw new Exception("A pause must preserve scorer and assist attribution while resetting motion samples.");
Call("ResetDerivedMotion", true);
if ((int)Field("_lastKickerSlot").GetValue(plugin)! != -1
    || (int)Field("_secondLastKickerSlot").GetValue(plugin)! != -1)
    throw new Exception("A new ball must still clear old scorer and assist attribution.");
Console.WriteLine("Pause attribution checks passed (2 scenarios).");

var cardType = pluginType.GetNestedType("RefereeCardEntry", BindingFlags.NonPublic)!;
var cardState = Activator.CreateInstance(cardType)!;
var applyCard = pluginType.GetMethod("ApplyCard", BindingFlags.Static | BindingFlags.NonPublic)!;
bool Apply(bool red) => (bool)applyCard.Invoke(null, new[] { cardState, (object)red })!;
bool IsCard(string name) => (bool)cardType.GetProperty(name)!.GetValue(cardState)!;
if (!Apply(false) || !IsCard("Yellow") || IsCard("Red")) throw new Exception("First yellow must warn without sending off.");
if (!Apply(false) || IsCard("Yellow") || !IsCard("Red")) throw new Exception("Second yellow must become a red card.");
if (Apply(false) || !IsCard("Red")) throw new Exception("Another yellow cannot undo a sending-off.");
cardState = Activator.CreateInstance(cardType)!;
if (!Apply(true) || !IsCard("Red") || IsCard("Yellow")) throw new Exception("A straight red must send off immediately.");
if (Apply(true) || !IsCard("Red")) throw new Exception("Repeated reds must be idempotent.");
Console.WriteLine("Referee card checks passed (5 scenarios).");

var extendChat = pluginType.GetMethod("ExtendChatTo", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (var sample in new[]
{
    (false, 0, 2, true), (false, 0, 3, true), (false, 1, 2, true), (false, 1, 3, true),
    (false, 2, 2, true), (false, 2, 3, true), (true, 0, 2, false), (true, 0, 3, false),
    (true, 1, 2, true), (true, 1, 3, false), (true, 2, 2, true), (true, 2, 3, true)
})
    if ((bool)extendChat.Invoke(null, new object[] { sample.Item1, sample.Item2, 2, sample.Item3 })! != sample.Item4)
        throw new Exception($"Dead-chat recipient routing mismatch: {sample}");
Console.WriteLine("Dead-chat visibility checks passed (12 scenarios).");

// These calls exercise the exact math compiled into SoccerModNativeHull.dll,
// rather than the old JavaScript prototype's unrelated contact rules.
BallPhysicsRegression.Run();

var kickoffCentre = new System.Numerics.Vector3(10, 20, 0);
var approachVelocity = new System.Numerics.Vector3(100, 200, -30);
var denied = KickoffBoundary.Constrain(kickoffCentre, approachVelocity, kickoffCentre, 1, false);
if (!denied.Changed || denied.Position.Y >= 20 - 252.5f || denied.Velocity.X != 100 || denied.Velocity.Z != -30 || denied.Velocity.Y != 0)
    throw new Exception("Opponents must stay outside the centre arc while retaining tangent/gravity.");
var allowed = KickoffBoundary.Constrain(kickoffCentre, approachVelocity, kickoffCentre, 1, true);
if (allowed.Changed) throw new Exception("The kicking team must reach the centre ball.");
var outside = KickoffBoundary.Constrain(new(510,-20,0),new(0,-100,0),kickoffCentre,1,true);
if (!outside.Changed || outside.Position.Y != 36) throw new Exception("Kickers cannot cross halfway outside the centre arc.");
var mirrored = KickoffBoundary.Constrain(kickoffCentre,new(100,-200,-30),kickoffCentre,-1,false);
if (Math.Abs(mirrored.Position.Y - 20 + denied.Position.Y - 20) > .001f)
    throw new Exception("Kickoff restriction must mirror when teams change ends.");
var unaffected = KickoffBoundary.Constrain(new(510, -100, 0),new(100,-20,0),kickoffCentre,1,false);
if (unaffected.Changed) throw new Exception("Legal player movement must stay untouched.");
Console.WriteLine("Kickoff boundary checks passed (5 scenarios).");

var stamina = new SprintStamina();
stamina.Update(0);
if (!stamina.TryStart(0)) throw new Exception("Full stamina must start immediately.");
for (var tick = 1; tick <= 192; tick++) stamina.Update(tick / 64.0);
if (stamina.Active || !stamina.Exhausted || stamina.Stamina != 0) throw new Exception("Three seconds must exhaust stamina.");
if (stamina.TryStart(3.1)) throw new Exception("Exhaustion must block immediate restart.");
for (var tick = 193; tick <= 735; tick++) stamina.Update(tick / 64.0);
if (stamina.Exhausted || stamina.Stamina != 100) throw new Exception("Recovery delay plus full recharge must unlock stamina.");
var partial = new SprintStamina(); partial.Update(0); partial.TryStart(0);
for (var tick = 1; tick <= 64; tick++) partial.Update(tick / 64.0);
partial.Stop(1);
if (Math.Abs(partial.Stamina - 66.6667f) > .01f || partial.TryStart(1.9) || !partial.TryStart(2))
    throw new Exception("Early release preserves stamina and allows reuse after the one-second delay.");
var hold = new SprintStamina(); hold.Update(0); hold.Input(0,true,true); hold.Update(.1); hold.Input(.1,false,true);
if (hold.Active || hold.Stamina >= 100) throw new Exception("Hold must consume stamina and stop on release.");
var toggle = new SprintStamina(); toggle.Update(0); toggle.Input(0,true,false); toggle.Update(.1); toggle.Input(.1,false,false);
if (!toggle.Active || Math.Abs(toggle.Stamina - hold.Stamina) > .001f) throw new Exception("Toggle must cost the same but continue after release.");
toggle.Input(.2,true,false);
if (toggle.Active) throw new Exception("A second toggle press must stop sprint.");
var held = new SprintStamina(); held.Update(0); held.Input(0,true,true);
for (var tick=1; tick<=768; tick++) { held.Update(tick/64.0); held.Input(tick/64.0,true,true); }
if (held.Active) throw new Exception("Holding through exhaustion must not automatically restart.");
held.Input(12.1,false,true); held.Input(12.2,true,true);
if (!held.Active) throw new Exception("Release and press after full recharge must start again.");
Console.WriteLine("Sprint 2.0 checks passed (9 scenarios).");

var monday = new DateTime(2026, 9, 7, 23, 0, 0);
if (!MatchRuleMath.InLogWindow(monday, 1 << 1, 22*60, 2*60)
    || !MatchRuleMath.InLogWindow(monday.AddHours(2), 1 << 1, 22*60, 2*60)
    || MatchRuleMath.InLogWindow(monday.AddHours(4), 1 << 1, 22*60, 2*60))
    throw new Exception("Overnight log schedules must follow their start weekday and exclude the stop minute.");
if (MatchRuleMath.InLogWindow(monday, 0, 0, 0) || !MatchRuleMath.InLogWindow(monday, 127, 0, 0)
    || MatchRuleMath.InLogWindow(monday, 127, 8*60, 20*60))
    throw new Exception("Empty days, full days and daytime log schedules must be distinct.");
if (!MatchRuleMath.CrossedHalfway(-100,100,19) || !MatchRuleMath.CrossedHalfway(100,-100,19)
    || !MatchRuleMath.CrossedHalfway(100,19,19) || MatchRuleMath.CrossedHalfway(100,20,19)
    || MatchRuleMath.CrossedHalfway(float.NaN,0,19))
    throw new Exception("Stoppage must detect fast crossings in both directions, respect the ball radius and reject invalid samples.");
Console.WriteLine("Match rule checks passed (3 scenarios).");

if (!TrainingTargetMath.ThroughHoop(new(0,-100,0),new(0,100,0),default,0,48,19)
    || !TrainingTargetMath.ThroughHoop(new(0,100,0),new(0,-100,0),default,0,48,19)
    || TrainingTargetMath.ThroughHoop(new(30,-100,0),new(30,100,0),default,0,48,19)
    || TrainingTargetMath.ThroughHoop(new(0,0,0),new(10,0,0),default,0,48,19))
    throw new Exception("Hoops must count full-ball crossings, reject rim clips and reject motion along the hoop plane.");
if (!TrainingTargetMath.ThroughHoop(new(-100,0,0),new(100,0,0),default,MathF.PI/2,48,19)
    || TrainingTargetMath.ThroughHoop(new(0,-100,0),new(0,100,0),default,0,18,19))
    throw new Exception("Rotated hoops must use their actual plane and cannot accept balls larger than their aperture.");
Console.WriteLine("Training target checks passed (2 scenarios).");

// Actual production history finalizer, independent of the engine's Finished state.
object Line(object entry, string pool) => entry.GetType().GetProperty(pool)!.GetValue(entry)!;
void SetStat(object line, string stat, int value) => line.GetType().GetProperty(stat)!.SetValue(line,value);
int Stat(object line,string stat) => (int)line.GetType().GetProperty(stat)!.GetValue(line)!;
var firstCurrent=Line(first,"Current"); var firstEligible=Line(first,"Match");
SetStat(firstCurrent,"Hits",2); SetStat(firstCurrent,"Points",40);
SetStat(firstEligible,"Hits",2); SetStat(firstEligible,"Points",40); SetStat(firstEligible,"RoundsWon",1);
var secondCurrent=Line(second,"Current"); SetStat(secondCurrent,"Hits",1); SetStat(secondCurrent,"Points",8);
Call("FinalizeStatsHistory");
var history=Line(first,"Competitive");
if (Stat(history,"Matches")!=1 || Stat(history,"Points")!=65 || Stat(history,"Motm")!=1
    || Stat(Line(first,"Public"),"Matches")!=1 || Stat(Line(second,"Competitive"),"Points")!=0
    || Stat(Line(second,"Public"),"Matches")!=1 || Stat(Line(first,"Match"),"Hits")!=0)
    throw new Exception("Full time must persist eligible history, count participants and exclude ineligible competitive play.");
Call("FinalizeStatsHistory");
if (Stat(history,"Points")!=65 || Stat(Line(first,"Public"),"Matches")!=1)
    throw new Exception("Finalizing twice must not duplicate match counts or MOTM rewards.");
Call("ResetMatchStats");
if (Stat(history,"Points")!=65 || Stat(Line(first,"Current"),"Points")!=0)
    throw new Exception("The next match must preserve competitive history and clear its live dashboard.");
var emptyLine=Line(first,"Match");
var score=emptyLine.GetType().GetMethod("Score")!;
if ((double)score.Invoke(emptyLine,new object[]{1})! != 0 || (double)score.Invoke(emptyLine,new object[]{2})! != 0)
    throw new Exception("Zero-round and zero-match averages must be finite zero values.");
var oldJson="{\"SteamId64\":42,\"Name\":\"old\",\"Public\":{\"Points\":123},\"Match\":{\"Points\":4}}";
var migrated=System.Text.Json.JsonSerializer.Deserialize(oldJson,first.GetType())!;
if (Stat(Line(migrated,"Public"),"Points")!=123 || Stat(Line(migrated,"Competitive"),"Points")!=0)
    throw new Exception("Legacy public totals must survive migration without fabricated competitive history.");
Console.WriteLine("Ranking history checks passed (5 scenarios).");
var requiredRoster = new Dictionary<ulong,int> { [1]=2, [2]=3 };
var currentRoster = new Dictionary<ulong,int>(requiredRoster);
var readyRoster = new HashSet<ulong> { 1, 2 };
if (!MatchRuleMath.EveryoneReady(requiredRoster,currentRoster,readyRoster)) throw new Exception("The ready paused roster must resume.");
currentRoster.Remove(2);
if (MatchRuleMath.EveryoneReady(requiredRoster,currentRoster,readyRoster)) throw new Exception("Disconnected paused players must prevent automatic resume.");
currentRoster[2]=2;
if (MatchRuleMath.EveryoneReady(requiredRoster,currentRoster,readyRoster)) throw new Exception("Changing team must invalidate readiness.");
currentRoster[2]=3; currentRoster[3]=2;
if (MatchRuleMath.EveryoneReady(requiredRoster,currentRoster,readyRoster)) throw new Exception("New unready players must prevent automatic resume.");
if (MatchRuleMath.EveryoneReady(new Dictionary<ulong,int>(),new Dictionary<ulong,int>(),new HashSet<ulong>())) throw new Exception("An empty roster must not resume.");
Console.WriteLine("Ready roster checks passed (5 scenarios).");

// Exercise the actual workbench validator and settings round trip without a game host.
var dials = ((IEnumerable)Call("BallDials")).Cast<object>().ToArray();
foreach (var dial in dials)
{
    var type = dial.GetType();
    var minimum = (float)type.GetProperty("Min")!.GetValue(dial)!;
    ((Action<float>)type.GetProperty("Write")!.GetValue(dial)!)(minimum);
}
Field("_kickSoundName").SetValue(plugin, "Weapon_Knife.HitWall");
var tuning = Call("CaptureBallTuning");
var tuningType = tuning.GetType();
var tuningValues = (Dictionary<string, float>)tuningType.GetProperty("Values")!.GetValue(tuning)!;
bool TuningValid() => (bool)Call("ValidateBallTuning", tuning);
if (!TuningValid() || dials.Length != 46) throw new Exception("Every workbench dial must accept its documented minimum.");
foreach (var bad in new[] { float.NaN, float.PositiveInfinity, -1f, 99999f })
{
    tuningValues["ballPushMaxSpeed"] = bad;
    if (TuningValid()) throw new Exception("Invalid tuning must fail before changing any runtime value.");
}
tuningValues["ballPushMaxSpeed"] = 0;
tuningValues["settleTicks"] = 1.5f;
if (TuningValid()) throw new Exception("Settle ticks must be whole numbers.");
tuningValues["settleTicks"] = 1;
tuningValues["softPassStartRatio"] = tuningValues["softPassFullRatio"];
if (TuningValid()) throw new Exception("Soft pass start must precede full strength reduction.");
tuningValues["softPassStartRatio"] = 0;
tuningValues["softPitchStartDegrees"] = tuningValues["softPitchFullDegrees"];
if (TuningValid()) throw new Exception("Look-down start must precede full reduction.");
tuningValues["softPitchStartDegrees"] = 0;
tuningValues["unknown"] = 1;
if (TuningValid()) throw new Exception("Unknown preset settings must be rejected.");
tuningValues.Remove("unknown");
tuningType.GetProperty("Sound")!.SetValue(tuning, "name;quit");
if (TuningValid()) throw new Exception("Sound input must not admit command syntax.");
tuningType.GetProperty("Sound")!.SetValue(tuning, "");
if (!TuningValid()) throw new Exception("Zero push, zero bounce and sound off are valid.");
Call("AssignBallTuning", tuning);
var snapshot = Call("CaptureBallTuning");
tuningValues["ballPushMaxSpeed"] = 500;
var capturedValues = (Dictionary<string, float>)tuningType.GetProperty("Values")!.GetValue(snapshot)!;
if (capturedValues["ballPushMaxSpeed"] != 0) throw new Exception("Undo snapshots must not share mutable dictionaries.");
var workbenchTemp = Path.Combine(Path.GetTempPath(), "soccer-workbench-" + Guid.NewGuid());
Directory.CreateDirectory(workbenchTemp);
try
{
    pluginType.GetProperty("ModulePath")!.SetValue(plugin, Path.Combine(workbenchTemp, "SoccerModNativeHull.dll"));
    pluginType.GetProperty("Logger")!.SetValue(plugin, Microsoft.Extensions.Logging.Abstractions.NullLogger<SoccerModMvpPlugin>.Instance);
    if (!(bool)Call("SaveBallSettings", "regression")) throw new Exception("Settings save must report success.");
    foreach (var dial in dials)
    {
        var type = dial.GetType();
        ((Action<float>)type.GetProperty("Write")!.GetValue(dial)!)(
            (float)type.GetProperty("Max")!.GetValue(dial)!);
    }
    Call("BallSettingsOnLoad");
    if ((float)Field("_ballPushMaxSpeed").GetValue(plugin)! != 0
        || (float)Field("_wallAssistMaxAddedVertical").GetValue(plugin)! != 0
        || (float)Field("_kickCooldownSeconds").GetValue(plugin)! != .05f)
        throw new Exception("Zero values and new aim/cooldown controls must survive restart.");
    foreach (var dial in dials)
    {
        var type = dial.GetType();
        var actual = ((Func<float>)type.GetProperty("Read")!.GetValue(dial)!)();
        if (actual != (float)type.GetProperty("Min")!.GetValue(dial)!)
            throw new Exception($"Dial {type.GetProperty("Key")!.GetValue(dial)} did not survive persistence.");
    }
    // A deliberately unwritable parent must restore runtime tuning on save failure.
    var blockedPath = Path.Combine(workbenchTemp, "file"); File.WriteAllText(blockedPath, "block directory creation");
    pluginType.GetProperty("ModulePath")!.SetValue(plugin, Path.Combine(blockedPath, "plugin.dll"));
    tuningValues["ballPushMaxSpeed"] = 500;
    if ((bool)Call("ApplyBallTuning", tuning, true) || (float)Field("_ballPushMaxSpeed").GetValue(plugin)! != 0)
        throw new Exception("Failed persistence must leave live tuning unchanged.");
}
finally { Directory.Delete(workbenchTemp, true); }
Console.WriteLine("Ball workbench checks passed (14 scenarios, 46 controls).");

// Kickoff lifetime is event-driven: arming must not require a game clock/timer.
// Hide rendering in this headless host; exercise the real restriction methods.
InitializeField("_kickoffBeams");
InitializeField("_menuParity");
var kickoffMenuSettings = Field("_menuParity").GetValue(plugin)!;
kickoffMenuSettings.GetType().GetProperty("KickoffOutline")!.SetValue(kickoffMenuSettings, false);
Field("_kickoffWallEnabled").SetValue(plugin, true);
var kickoffTeamType = Field("_kickoffTeam").FieldType;
var kickoffCt = Enum.ToObject(kickoffTeamType, 3);
var kickoffT = Enum.ToObject(kickoffTeamType, 2);
var kickoffRandom = new Random(42);
var kickoffDraws = Enumerable.Range(0, 100).Select(_ => Call("DrawKickoffTeam", kickoffRandom)).ToArray();
if (!kickoffDraws.Contains(kickoffCt) || !kickoffDraws.Contains(kickoffT)
    || kickoffDraws.Any(team => !Equals(team, kickoffCt) && !Equals(team, kickoffT)))
    throw new Exception("Opening kickoff must support both playing teams only.");
if ((System.Drawing.Color)Call("KickoffOutlineColor", kickoffT) != System.Drawing.Color.Red
    || (System.Drawing.Color)Call("KickoffOutlineColor", kickoffCt) != System.Drawing.Color.DodgerBlue)
    throw new Exception("Home/T kickoff must be red and Away/CT kickoff blue.");
Console.WriteLine("Kickoff draw and team colour checks passed (2 scenarios).");
Call("StartKickoffRestriction", kickoffCt);
for (var tick = 0; tick < 6400; tick++) Call("MaintainKickoffOutline");
if (!(bool)Field("_kickoffRestrictionActive").GetValue(plugin)!)
    throw new Exception("Visual maintenance or hidden outlines must not expire an untouched kickoff.");
Call("ClearKickoffRestrictionOnTouch", Enum.ToObject(kickoffTeamType, 1));
if (!(bool)Field("_kickoffRestrictionActive").GetValue(plugin)!)
    throw new Exception("A spectator is not a ball contact that releases kickoff.");
Call("ClearKickoffRestrictionOnTouch", kickoffT);
if ((bool)Field("_kickoffRestrictionActive").GetValue(plugin)!)
    throw new Exception("An accepted playing-team ball contact must release kickoff.");
Call("StartKickoffRestriction", kickoffCt);
Call("CompleteKickoffRestriction", "ball_activity");
Call("MaintainKickoffOutline");
if ((bool)Field("_kickoffRestrictionActive").GetValue(plugin)!)
    throw new Exception("Maintenance must never resurrect a completed kickoff.");
Console.WriteLine("Kickoff lifetime checks passed (4 scenarios).");

if (SprintBarView.Text(100) != "[|||||||||| 100% ||||||||||]" || SprintBarView.Text(0) != "[.......... 0% ..........]"
    || SprintBarView.Text(55) != "[|||||||||| 55% |.........]") throw new Exception("Sprint bar must reflect actual stamina in twenty segments.");
if (SprintBarView.Text(float.NaN) != "[.......... 0% ..........]" || SprintBarView.Text(110) != SprintBarView.Text(100))
    throw new Exception("Sprint bar must clamp invalid and out-of-range display values.");
if (SprintBarView.Visible(1, false, 100, true, false, false)
    || !SprintBarView.Visible(1, false, 50, true, false, false)) throw new Exception("Context bar must show recharge and hide when full.");
if (SprintBarView.Visible(0, true, 50, true, true, false) || SprintBarView.Visible(0, true, 50, false, false, false)
    || SprintBarView.Visible(0, true, 50, true, false, true) || SprintBarView.Visible(2, true, 50, true, false, false))
    throw new Exception("Menus, death/spectating, CAP suppression and disabled preference must hide the sprint bar.");
var sprintHtml = SprintBarView.Html(55, true, "<Home> 1 - 0 Away\n12:34");
if (!sprintHtml.Contains("#66EEFF") || !sprintHtml.Contains("#FFFFFF")
    || !sprintHtml.Contains("55%") || !sprintHtml.Contains("&lt;Home&gt;")
    || sprintHtml.Contains("<Home>")) throw new Exception("Screen HUD must brighten the bar and escape team names.");
if (SprintBarView.Html(55, true, "").Contains("<br>")
    || !SprintBarView.Html(55, false, "").Contains("#FF6464")
    || SprintBarView.Html(55, true, "").Contains("#FF6464")
    || SprintBarView.Html(100, false, "").Contains("#FF6464"))
    throw new Exception("Compact HUD must not insert empty rows and must colour only refilling red.");
if (sprintHtml.IndexOf("&lt;Home&gt;") > sprintHtml.IndexOf("55%")
    || SprintBarView.Html(55, true, "").Contains("&nbsp;"))
    throw new Exception("Real score must sit above the meter; empty score must not add padding.");
Console.WriteLine("Sprint bar checks passed (6 scenarios).");

// Exercise actual menu pagination for empty, boundary and long menus, including
// disabled information rows. Every original option must remain reachable once.
var numberMenuType = pluginType.GetNestedType("NumberMenu", BindingFlags.NonPublic)!;
var menuOptionType = pluginType.GetNestedType("NumberMenuOption", BindingFlags.NonPublic)!;
var menuRenderField = Field("_menuRenderMode");
menuRenderField.SetValue(plugin, Enum.ToObject(menuRenderField.FieldType, 1));
var htmlRenderer = pluginType.GetMethod("BuildMenuHtml", BindingFlags.Static | BindingFlags.NonPublic)!;
foreach (var count in new[] { 0, 1, 5, 6, 10, 11, 46 })
{
    var menu = Activator.CreateInstance(numberMenuType)!;
    numberMenuType.GetProperty("Title")!.SetValue(menu, "Soccer Mod - Ball <settings>");
    var options = (IList)numberMenuType.GetProperty("Options")!.GetValue(menu)!;
    for (var i = 0; i < count; i++)
    {
        var option = Activator.CreateInstance(menuOptionType)!;
        menuOptionType.GetProperty("Text")!.SetValue(option, $"Choice <{i}> & value");
        menuOptionType.GetProperty("Enabled")!.SetValue(option, i % 3 != 0);
        options.Add(option);
    }
    var pages = (IList)Call("BuildMenuPages", menu);
    var collected = new List<object>();
    for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
    {
        var page = pages[pageIndex]!;
        var pageType = page.GetType();
        var items = (IList)pageType.GetField("Items")!.GetValue(page)!;
        collected.AddRange(items.Cast<object>());
        if (items.Count > 3 || !(bool)pageType.GetField("ShowTitle")!.GetValue(page)!
            || (int)pageType.GetProperty("BackKey")!.GetValue(page)! != items.Count + 1
            || (int)pageType.GetProperty("NextKey")!.GetValue(page)! != items.Count + ((bool)pageType.GetField("HasBack")!.GetValue(page)! ? 2 : 1))
            throw new Exception("Menu must reserve three choices, a heading and consecutive navigation.");
        var html = (string)htmlRenderer.Invoke(null, new object[] { "Soccer Mod - Ball <settings>", page })!;
        if (!html.Contains("Ball &lt;settings&gt;") || html.Contains("Soccer Mod -")
            || !html.Contains($"{pageIndex + 1}/{pages.Count}") || !html.Contains("0 Close")
            || html.Contains("Choice <") || html.Split("<br>").Length > 5
            || (items.Count > 0 && html.IndexOf("0 Close") > html.IndexOf("Choice &lt;"))) throw new Exception("Menu headings, page counts and escaped content must survive every page.");
    }
    if (!collected.SequenceEqual(options.Cast<object>()))
        throw new Exception("Pagination must not lose, duplicate or reorder menu options.");
}
Console.WriteLine("Menu redesign checks passed (7 menu sizes).");
