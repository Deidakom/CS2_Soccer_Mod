using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using SoccerModMvp;

internal static class CannonGoalChecks
{
    internal static void Run()
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SoccerModMvpPlugin);
        var plugin = RuntimeHelpers.GetUninitializedObject(type);
        var cannonsField = type.GetField("_personalCannons", flags)!;
        var cannons = (IDictionary)Activator.CreateInstance(cannonsField.FieldType)!;
        cannonsField.SetValue(plugin, cannons);
        var shared = type.GetField("_cannonTimer", flags)!;
        var manual = type.GetField("_trainingGoalsDisabled", flags)!;
        var timer = RuntimeHelpers.GetUninitializedObject(shared.FieldType);
        bool Suppressed() => (bool)type.GetProperty("CannonGoalsSuppressed", flags)!.GetValue(plugin)!;
        void Check(bool expected) { if (Suppressed() != expected) throw new Exception("Cannon goal suppression lifecycle mismatch."); }
        Check(false);
        shared.SetValue(plugin, timer); Check(true);
        var phase = type.GetField("_matchPhase", flags)!;
        var crossing = type.GetMethod("MatchCheckGoalCrossing", flags)!;
        foreach (var name in new[] { "Warmup", "Live" })
        {
            phase.SetValue(plugin, Enum.Parse(phase.FieldType, name));
            // Null endpoints deliberately prove the real goal entry point
            // returns before geometry or scoring can be reached.
            if ((bool)crossing.Invoke(plugin, new object[] { null, null })!)
                throw new Exception("An active cannon must block actual goal detection.");
        }
        var stateType = cannonsField.FieldType.GenericTypeArguments[1];
        object Personal(int slot)
        {
            var state = Activator.CreateInstance(stateType, true)!;
            stateType.GetField("Timer")!.SetValue(state, timer);
            cannons.Add(slot, state); return state;
        }
        var first = Personal(1); var second = Personal(2);
        shared.SetValue(plugin, null); Check(true);
        stateType.GetField("Timer")!.SetValue(first, null); Check(true);
        // Manual disable remains independent across the last automatic stop.
        manual.SetValue(plugin, true);
        stateType.GetField("Timer")!.SetValue(second, null); Check(false);
        if (!(bool)manual.GetValue(plugin)!) throw new Exception("Cannon cleanup must not clear manual goal disable.");
        stateType.GetField("Timer")!.SetValue(first, timer); Check(true);
        cannons.Remove(1); Check(false); // disconnect
        stateType.GetField("Timer")!.SetValue(second, timer); Check(true);
        cannons.Clear(); Check(false); // map cleanup
        Console.WriteLine("Cannon goal suppression checks passed: shared, multiple personal, last-stop, disconnect, map cleanup and manual setting independence.");
    }
}
