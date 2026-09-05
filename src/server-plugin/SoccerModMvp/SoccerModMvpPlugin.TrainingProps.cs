using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using V3 = System.Numerics.Vector3;

namespace SoccerModMvp;
public sealed partial class SoccerModMvpPlugin
{
    // Exact resources verified in the German host's base-game VPK. These are
    // CS2 training props; the original Source 1 art still needs conversion.
    private static readonly Dictionary<string, string> TrainingPropModels = new()
    {
        ["cone"] = "models/de_overpass/construction/traffic_cone_1.vmdl",
        ["can"] = "models/de_overpass/decorations/food/drink_container_can_1.vmdl",
        ["plate"] = "models/cs_italy/italy_shops/italy_plate_01.vmdl"
    };
    private sealed class TrainingPlacement
    {
        public string Kind { get; set; } = "cone";
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float Yaw { get; set; }
        public TrainingPlacement Copy() => (TrainingPlacement)MemberwiseClone();
        public V3 Position => new(X, Y, Z);
    }
    private sealed class TrainingDevice
    {
        public int Id;
        public ulong Owner;
        public TrainingPlacement Placement = new();
        public CPhysicsPropMultiplayer? Prop;
        public readonly List<CBeam> Beams = new();
        public int Hits;
        public readonly Dictionary<uint, V3> Previous = new();
        public readonly Dictionary<uint, double> NextHit = new();
    }
    private sealed class TrainingLayoutStore
    {
        public Dictionary<string, Dictionary<string, List<TrainingPlacement>>> Maps { get; set; } = new();
    }
    private const string TrainingLayoutsFile = "soccermod_training_layouts.json";
    private TrainingLayoutStore _trainingLayouts = new();
    private readonly List<TrainingDevice> _trainingDevices = new();
    private int _nextTrainingDevice;
    private bool _advancedTraining;
    private bool _advancedPreviousGoals;
    private readonly Dictionary<ulong, CsTeam> _advancedOriginalTeams = new();
    private bool PropsAccess(CCSPlayerController p) => TrainingHasAccess(p) && !MatchRunning && !IsWebsiteCapActive();
    private void OpenTrainingPropsMenu(CCSPlayerController player)
    {
        if (!PropsAccess(player)) return;
        var menu = new NumberMenu { Title = "Training - Props / Position Manager", OnBack = OpenTrainingMenu };
        foreach (var kind in new[] { "cone", "can", "plate", "hoop" })
            menu.Add($"Spawn {(kind == "hoop" ? "hoop target (outline)" : kind)}", p =>
            {
                if (!PropsAccess(p) || !IsEligiblePlayer(p) || !TryGetAimHitPoint(p, out var point)) return;
                SpawnTrainingDevice(p.AuthorizedSteamID?.SteamId64 ?? 0, new TrainingPlacement
                { Kind = kind, X = point.X, Y = point.Y, Z = point.Z + (kind == "hoop" ? 64 : 8), Yaw = p.PlayerPawn.Value!.EyeAngles.Y });
                OpenTrainingPropsMenu(p);
            });
        menu.Add("Position / Remove props", OpenTrainingDeviceList);
        menu.Add("Save / Load layout", OpenTrainingLayouts);
        menu.Add("Advanced training", OpenAdvancedTrainingMenu);
        menu.Add("Remove my props", p => { if (!PropsAccess(p)) return; ClearTrainingDevices(p.AuthorizedSteamID?.SteamId64 ?? 0); OpenTrainingPropsMenu(p); });
        OpenNumberMenu(player, menu);
    }
    private bool ValidPlacement(TrainingPlacement p) => (p.Kind == "hoop" || TrainingPropModels.ContainsKey(p.Kind))
        && float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && float.IsFinite(p.Yaw)
        && Math.Abs(p.X) < 32768 && Math.Abs(p.Y) < 32768 && Math.Abs(p.Z) < 32768;
    private bool SpawnTrainingDevice(ulong owner, TrainingPlacement placement)
    {
        if (owner == 0 || !ValidPlacement(placement) || _trainingDevices.Count >= 32
            || _trainingDevices.Count(d => d.Owner == owner) >= 12
            || placement.Kind == "hoop" && _trainingDevices.Count(d => d.Placement.Kind == "hoop") >= 8) return false;
        var device = new TrainingDevice { Id = ++_nextTrainingDevice, Owner = owner, Placement = placement.Copy() };
        try
        {
            if (placement.Kind == "hoop") DrawTrainingHoop(device);
            else
            {
                var prop = Utilities.CreateEntityByName<CPhysicsPropMultiplayer>(BallDesignerName);
                if (prop is not { IsValid: true }) return false;
                device.Prop = prop;
                using var kv = new CEntityKeyValues();
                kv.SetString("targetname", $"sm2_training_prop_{device.Id}");
                kv.SetString("model", TrainingPropModels[placement.Kind]);
                kv.SetInt("physicsmode", 1);
                kv.SetVector("origin", C(placement.Position));
                kv.SetAngle("angles", new QAngle(0, placement.Yaw, 0));
                prop.DispatchSpawn(kv);
                if (!prop.IsValid) return false;
                if (placement.Kind == "cone") prop.AcceptInput("DisableMotion");
            }
            _trainingDevices.Add(device); return true;
        }
        catch (Exception ex) { RemoveTrainingDevice(device); Logger.LogWarning(ex, "[SM2DIAG] training_prop_failed kind={Kind}", placement.Kind); return false; }
    }
    private void DrawTrainingHoop(TrainingDevice device)
    {
        foreach (var beam in device.Beams) if (beam.IsValid) beam.Remove();
        device.Beams.Clear();
        var yaw = device.Placement.Yaw * MathF.PI / 180;
        var axis = new V3(MathF.Cos(yaw), MathF.Sin(yaw), 0);
        V3 Point(int i) => device.Placement.Position + 48 * (axis * MathF.Cos(i * MathF.Tau / 16) + V3.UnitZ * MathF.Sin(i * MathF.Tau / 16));
        for (var i = 0; i < 16; i++)
        {
            var beam = Utilities.CreateEntityByName<CBeam>("beam"); if (beam is not { IsValid: true }) continue;
            beam.Render = System.Drawing.Color.Orange; beam.Width = beam.EndWidth = 2;
            beam.Teleport(position: C(Point(i))); var end = Point(i + 1);
            beam.EndPos.X = end.X; beam.EndPos.Y = end.Y; beam.EndPos.Z = end.Z;
            beam.DispatchSpawn(); device.Beams.Add(beam);
        }
    }
    private void TrainingDevicesOnTick()
    {
        if (MatchRunning || _trainingDevices.Count == 0 || Server.TickCount % 2 != 0) return;
        var balls = PlayableBalls().ToArray();
        foreach (var device in _trainingDevices.Where(d => d.Placement.Kind == "hoop"))
        {
            var valid = balls.Select(b => b.Ball.EntityHandle.Raw).ToHashSet();
            foreach (var key in device.Previous.Keys.Where(k => !valid.Contains(k)).ToArray()) { device.Previous.Remove(key); device.NextHit.Remove(key); }
            foreach (var ball in balls)
            {
                var key = ball.Ball.EntityHandle.Raw; var point = N(ball.Origin);
                if (device.Previous.TryGetValue(key, out var previous) && Server.TickedTime >= device.NextHit.GetValueOrDefault(key)
                    && TrainingTargetMath.ThroughHoop(previous, point, device.Placement.Position, device.Placement.Yaw * MathF.PI / 180, 48, BallCollisionRadius))
                {
                    device.Hits++; device.NextHit[key] = Server.TickedTime + 1;
                    Utilities.GetPlayerFromSteamId64(device.Owner)?.PrintToChat(FormatSoccerModMessage($"Hoop #{device.Id}: {device.Hits} hits."));
                }
                device.Previous[key] = point;
            }
        }
    }
    private void OpenTrainingDeviceList(CCSPlayerController player)
    {
        if (!PropsAccess(player)) return;
        var menu = new NumberMenu { Title = "Training - My Props", OnBack = OpenTrainingPropsMenu };
        foreach (var device in _trainingDevices.Where(d => d.Owner == (player.AuthorizedSteamID?.SteamId64 ?? 0)))
            menu.Add($"#{device.Id} {device.Placement.Kind} ({device.Hits} hits)", p => OpenTrainingDeviceEditor(p, device));
        OpenNumberMenu(player, menu);
    }
    private bool CanEditDevice(CCSPlayerController player, TrainingDevice device) => PropsAccess(player)
        && _trainingDevices.Contains(device) && device.Owner == (player.AuthorizedSteamID?.SteamId64 ?? 0);
    private void OpenTrainingDeviceEditor(CCSPlayerController player, TrainingDevice device)
    {
        if (!CanEditDevice(player, device)) return;
        var menu = new NumberMenu { Title = $"Position {device.Placement.Kind} #{device.Id}", OnBack = OpenTrainingDeviceList };
        void Move(CCSPlayerController p, Action<TrainingPlacement> edit)
        {
            if (!CanEditDevice(p, device)) return;
            edit(device.Placement); device.Previous.Clear();
            if (device.Prop is { IsValid: true } prop) prop.Teleport(C(device.Placement.Position), new QAngle(0, device.Placement.Yaw, 0), new Vector());
            else if (device.Placement.Kind == "hoop") DrawTrainingHoop(device);
            OpenTrainingDeviceEditor(p, device);
        }
        menu.Add("Move to crosshair", p => { if (TryGetAimHitPoint(p, out var point)) Move(p, v => { v.X = point.X; v.Y = point.Y; v.Z = point.Z + (v.Kind == "hoop" ? 64 : 8); }); });
        foreach (var delta in new[] { -16f, 16f })
        {
            menu.Add($"X {delta:+0;-0}", p => Move(p, v => v.X += delta));
            menu.Add($"Y {delta:+0;-0}", p => Move(p, v => v.Y += delta));
            menu.Add($"Z {delta:+0;-0}", p => Move(p, v => v.Z += delta));
        }
        menu.Add("Rotate 15 degrees", p => Move(p, v => v.Yaw = (v.Yaw + 15) % 360));
        menu.Add("Reset to saved position", p => Move(p, _ => { }));
        menu.Add("Remove", p => { if (!CanEditDevice(p, device)) return; RemoveTrainingDevice(device); OpenTrainingDeviceList(p); });
        OpenNumberMenu(player, menu);
    }
    private void RemoveTrainingDevice(TrainingDevice device)
    {
        if (device.Prop is { IsValid: true }) device.Prop.Remove();
        foreach (var beam in device.Beams) if (beam.IsValid) beam.Remove();
        device.Beams.Clear(); _trainingDevices.Remove(device);
    }
    private void ClearTrainingDevices(ulong? owner = null)
    {
        foreach (var device in _trainingDevices.Where(d => owner is null || d.Owner == owner).ToArray()) RemoveTrainingDevice(device);
    }
    private void OpenTrainingLayouts(CCSPlayerController player)
    {
        if (!PropsAccess(player)) return;
        var menu = new NumberMenu { Title = "Training - Layouts on this map", OnBack = OpenTrainingPropsMenu };
        menu.Add("Save my current layout", p => BeginChatTextInput(p, "New layout name (1-32 characters).", (actor, name) =>
        {
            if (!PropsAccess(actor) || string.IsNullOrWhiteSpace(name) || name.Length > 32 || name.Any(char.IsControl)) return;
            if (!_trainingLayouts.Maps.TryGetValue(_currentMapName, out var layouts)) _trainingLayouts.Maps[_currentMapName] = layouts = new();
            if (layouts.ContainsKey(name)) { actor.PrintToChat(FormatSoccerModMessage("Name already exists; choose a new name.")); return; }
            if (layouts.Count >= 50) { actor.PrintToChat(FormatSoccerModMessage("Maximum 50 layouts per map.")); return; }
            layouts[name] = _trainingDevices.Where(d => d.Owner == (actor.AuthorizedSteamID?.SteamId64 ?? 0)).Select(d => d.Placement.Copy()).ToList();
            if (!SaveJsonAtomic(TrainingLayoutsFile, _trainingLayouts)) layouts.Remove(name);
            OpenTrainingLayouts(actor);
        }, OpenTrainingLayouts));
        if (_trainingLayouts.Maps.TryGetValue(_currentMapName, out var saved))
            foreach (var (name, positions) in saved)
                menu.Add($"Load: {name}", p =>
                {
                    if (!PropsAccess(p)) return;
                    var id = p.AuthorizedSteamID?.SteamId64 ?? 0; if (id == 0) return;
                    ClearTrainingDevices(id);
                    var count = positions.Take(12).Count(pos => SpawnTrainingDevice(id, pos));
                    p.PrintToChat(FormatSoccerModMessage($"Loaded {count}/{positions.Count} devices (server limits apply).")); OpenTrainingLayouts(p);
                });
        OpenNumberMenu(player, menu);
    }
    private void SetAdvancedTraining(bool enabled)
    {
        if (_advancedTraining == enabled) return;
        _advancedTraining = enabled;
        if (enabled)
        {
            _advancedPreviousGoals = _trainingGoalsDisabled; _trainingGoalsDisabled = true; _advancedOriginalTeams.Clear();
            foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && p.Team == CsTeam.CounterTerrorist))
                if (p.AuthorizedSteamID is { } steam) { _advancedOriginalTeams[steam.SteamId64] = p.Team; p.ChangeTeam(CsTeam.Terrorist); }
        }
        else
        {
            _trainingGoalsDisabled = _advancedPreviousGoals;
            foreach (var (id, team) in _advancedOriginalTeams)
                if (Utilities.GetPlayerFromSteamId64(id) is { IsValid: true, Team: CsTeam.Terrorist } p) p.ChangeTeam(team);
            _advancedOriginalTeams.Clear();
        }
    }
    private void OpenAdvancedTrainingMenu(CCSPlayerController player)
    {
        if (!PropsAccess(player)) return;
        var menu = new NumberMenu { Title = "Advanced Training", OnBack = OpenTrainingMenu };
        menu.Add($"Training mode: {OnOff(_advancedTraining)}", p => { if (!PropsAccess(p)) return; SetAdvancedTraining(!_advancedTraining); OpenAdvancedTrainingMenu(p); });
        menu.AddInfo("Training mode puts CTs on T and disables goals.");
        menu.Add("Cone / Prop Manager", OpenTrainingPropsMenu);
        foreach (var sign in new[] { -1, 1 })
            menu.Add($"Goal targets: {(sign < 0 ? "negative" : "positive")} end", p =>
            {
                if (!PropsAccess(p)) return;
                var id = p.AuthorizedSteamID?.SteamId64 ?? 0;
                foreach (var x in new[] { -1, 1 }) SpawnTrainingDevice(id, new TrainingPlacement
                { Kind = "hoop", X = GoalCenterX + x * Math.Max(0, _goalHalfWidthX - 55), Y = sign * _goalLineY,
                    Z = (_goalApertureMinZ + _goalApertureMaxZ) / 2, Yaw = 0 });
                OpenAdvancedTrainingMenu(p);
            });
        OpenNumberMenu(player, menu);
    }
}
