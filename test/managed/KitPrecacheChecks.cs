using System.Reflection;
using System.Runtime.CompilerServices;
using CounterStrikeSharp.API.Modules.Utils;
using SoccerModMvp;

internal static class KitPrecacheChecks
{
    internal static void Run()
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = typeof(SoccerModMvpPlugin);
        var plugin = RuntimeHelpers.GetUninitializedObject(type);
        void Set(string name, object value) => type.GetField(name, instance)!.SetValue(plugin, value);
        object Call(string name, params object[] args) => type.GetMethod(name, instance)!.Invoke(plugin, args)!;
        (string? Path, bool Kit) Resolve(CsTeam team, bool gk = false)
        {
            var args = new object[] { team, gk, false };
            var path = (string?)Call("ResolveTeamModel", args);
            return (path, (bool)args[2]);
        }
        var names = new[] { "Home", "Away", "GkHome", "GkAway" };
        foreach (var name in names) Set("_kitModel" + name, $"models/kits/{name}.vmdl");
        Set("_teamModelMode", TeamModelMode.Kits);
        if (Resolve(CsTeam.Terrorist) != ("agents/models/tm_phoenix/tm_phoenix.vmdl", false))
            throw new Exception("Hot reload must use the stock model and tint until kit precache runs.");

        var resources = new List<string>();
        Call("CaptureKitResources", (Action<string>)resources.Add);
        if (!resources.SequenceEqual(names.Select(name => $"models/kits/{name}.vmdl")))
            throw new Exception("All four configured kit resources must be registered.");
        Set("_kitModelHome", "models/kits/replacement.vmdl");
        if (Resolve(CsTeam.Terrorist) != ("models/kits/Home.vmdl", true))
            throw new Exception("A mid-map kit edit must not assign an unregistered replacement.");
        if (Resolve(CsTeam.CounterTerrorist, true) != ("models/kits/GkAway.vmdl", true))
            throw new Exception("The away goalkeeper must keep its registered model.");
        Set("_teamsSwapped", true);
        if (Resolve(CsTeam.CounterTerrorist) != ("models/kits/Home.vmdl", true))
            throw new Exception("Registered kits must follow squads through halftime.");
        Call("CaptureKitResources", (Action<string>)resources.Add);
        if (Resolve(CsTeam.CounterTerrorist) != ("models/kits/replacement.vmdl", true))
            throw new Exception("The next precache pass must activate pending kit changes.");
        Set("_teamModelMode", TeamModelMode.Off);
        if (Resolve(CsTeam.CounterTerrorist) != (null, false))
            throw new Exception("Off must preserve the player's model without the kit tint.");
        Set("_teamModelMode", TeamModelMode.Kits);
        try { Call("CaptureKitResources", (Action<string>)(_ => throw new InvalidOperationException("precache failed"))); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException) { }
        if (Resolve(CsTeam.CounterTerrorist) != ("agents/models/ctm_sas/ctm_sas.vmdl", false))
            throw new Exception("A failed precache pass cannot leave a stale kit snapshot active.");

        var normalize = type.GetMethod("TryNormalizeKitPath", BindingFlags.Static | BindingFlags.NonPublic)!;
        foreach (var value in new[] { "../kit.vmdl", "/models/kit.vmdl", "models/../kit.vmdl", "models//kit.vmdl",
                     "C:\\models\\kit.vmdl", "https://example/kit.vmdl", "models/kit.vmdl_c", "models/kit x.vmdl", "models/.vmdl", "models/kit\n.vmdl" })
        {
            var args = new object[] { value, "" };
            if ((bool)normalize.Invoke(null, args)!) throw new Exception($"Invalid kit path accepted: {value}");
        }
        var valid = new object[] { " models\\soccermod\\kits\\kit_home.vmdl ", "" };
        if (!(bool)normalize.Invoke(null, valid)! || (string)valid[1] != "models/soccermod/kits/kit_home.vmdl")
            throw new Exception("Windows separators must normalize to a relative resource path.");
        Console.WriteLine("Kit precache checks passed: hot reload, mid-map edits, map activation, squad/GK mapping and invalid paths.");
    }
}
