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
