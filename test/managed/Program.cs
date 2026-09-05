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
