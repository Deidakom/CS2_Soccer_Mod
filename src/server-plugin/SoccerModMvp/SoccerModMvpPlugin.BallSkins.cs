using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace SoccerModMvp;

// 2026-10-18 owner: a ball look per player (!menu - Settings - Stadium - Ball): the
// map ball (default, personal effects are opt-in) or a skin of
// models/soccermod/ball/wc_ball.vmdl (tools/ball/generate-ball-skins.mjs): Trionda 2026
// so far. The match
// ball itself is never touched: a skin is a non-solid copy of the ball 0.4 % larger,
// parented to it (follows position and spin smoothly), one per ball and chosen skin;
// each player is sent only his own skin's shells (CheckTransmit Remove only).
// Phase A (owner test, 2026-10-18): the model may be a loose file on the server only;
// then the menu entry is offered to the owner alone (other clients lack the model).
public sealed partial class SoccerModMvpPlugin
{
    private const string BallSkinModel = "models/soccermod/ball/wc_ball.vmdl";
    private const string BallSkinPrefsFile = "soccermod_ball_skin_prefs.json";
    private const string BallSkinTargetName = "sm2_ball_skin";
    private static readonly string[] BallSkinNames = { "Stadium ball", "Trionda 2026" };

    private bool _ballSkinPrecached, _ballSkinLooseOnly;
    private int _ballSkinForced = -1;   // admin test: shells for this skin even if nobody picked it
    private Dictionary<ulong, int> _ballSkinPrefs = new();
    private readonly Dictionary<(uint Ball, int Skin), CDynamicProp> _ballSkinShells = new();

    private bool BallSkinAvailableFor(CCSPlayerController player) => _ballSkinPrecached && (!_ballSkinLooseOnly || IsOwner(player));

    // 0 = the map ball, 1..5 = skin 0..4 of the model
    private int BallSkinOf(CCSPlayerController player) =>
        _ballSkinPrefs.TryGetValue(SteamIdOf(player), out var s) && s > 0 && s < BallSkinNames.Length && BallSkinAvailableFor(player) ? s : 0;

    private void SetBallSkin(CCSPlayerController player, int choice)
    {
        var id = SteamIdOf(player);
        if (id == 0) return;
        if (choice == 0) _ballSkinPrefs.Remove(id); else _ballSkinPrefs[id] = choice;
        SaveJsonAtomic(BallSkinPrefsFile, _ballSkinPrefs);
        BallSkinEnsure("choice");
        player.PrintToChat($" \x04[SM]\x01 Ball: \x04{BallSkinNames[choice]}\x01 (only you see it).");
    }

    private void OpenBallSkinMenu(CCSPlayerController player)
    {
        var menu = new NumberMenu { Title = "Settings - Ball", OnBack = OpenStadiumSettingsMenu };
        var current = BallSkinOf(player);
        for (var i = 0; i < BallSkinNames.Length; i++)
        {
            var choice = i;
            menu.Add($"{(choice == current ? "* " : "")}{BallSkinNames[choice]}", p => { SetBallSkin(p, choice); OpenBallSkinMenu(p); });
        }
        OpenNumberMenu(player, menu);
    }

    private void BallSkinsOnLoad()
    {
        _ballSkinPrefs = LoadJsonOrNull<Dictionary<ulong, int>>(BallSkinPrefsFile) ?? new();
        AddCommand("css_sm2ballskin", "Admin: ball skin status.", (player, command) =>
        {
            if (!RequirePermission(player, command, "admin")) return;
            if (command.ArgCount >= 3 && command.GetArg(1) == "force") { _ballSkinForced = int.TryParse(command.GetArg(2), out var fs) ? fs : -1; BallSkinEnsure("force"); }
            command.ReplyToCommand($"[SM] Ball skins: precached={_ballSkinPrecached} looseOnly={_ballSkinLooseOnly} prefs={_ballSkinPrefs.Count} shells={_ballSkinShells.Count}");
            foreach (var ((ballKey, skin), shell) in _ballSkinShells)
            {
                if (!shell.IsValid) continue;
                var ball = VisibleBalls().FirstOrDefault(b => b.Ball.EntityHandle.Raw == ballKey).Ball;
                command.ReplyToCommand($"[SM]  skin={skin} shell={shell.AbsOrigin} ball={ball?.AbsOrigin} absScale={shell.CBodyComponent?.SceneNode?.AbsScale:F3} ballScale={ball?.CBodyComponent?.SceneNode?.AbsScale:F3} scale={shell.CBodyComponent?.SceneNode?.Scale:F3}/{ball?.CBodyComponent?.SceneNode?.Scale:F3} mins={shell.Collision.Mins} maxs={shell.Collision.Maxs} ballMaxs={ball?.Collision.Maxs} parent={shell.CBodyComponent?.SceneNode?.PParent?.Owner?.Index}");
            }
        });
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _ballSkinPrecached = false;
            var mounted = MountedAddonFiles().Contains(BallSkinModel + "_c");
            var loose = File.Exists(Path.Combine(Server.GameDirectory, "csgo", BallSkinModel + "_c"));
            if (!mounted && !loose)
            {
                Logger.LogInformation("[SM2DIAG] ball_skin_unavailable reason=model_not_mounted model={Model}", BallSkinModel);
                return;
            }
            manifest.AddResource(BallSkinModel);
            _ballSkinPrecached = true;
            _ballSkinLooseOnly = !mounted;
            Logger.LogInformation("[SM2DIAG] ball_skin_precached looseOnly={Loose}", _ballSkinLooseOnly);
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _ballSkinShells.Clear();
            AddTimer(0.5f, () => BallSkinEnsure("maintenance"), TimerFlags.REPEAT | TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => BallSkinEnsure("round_start"));
            return HookResult.Continue;
        });
        RegisterListener<Listeners.CheckTransmit>(BallSkinCheckTransmit);
    }

    // Shells for every visible ball x every skin someone in the game picked; the rest go.
    private void BallSkinEnsure(string reason)
    {
        var wanted = new HashSet<int>();
        if (_ballSkinForced >= 0) wanted.Add(_ballSkinForced);
        if (_ballSkinPrecached)
            foreach (var player in Utilities.GetPlayers())
                if (player is { IsValid: true, IsBot: false } && BallSkinOf(player) is > 0 and var s) wanted.Add(s - 1);
        var balls = wanted.Count == 0 ? new List<PlayableBall>() : VisibleBalls().Where(b => b.Ball.IsValid).ToList();
        var keep = new HashSet<(uint, int)>();
        var spawned = 0;
        foreach (var target in balls)
        foreach (var skin in wanted)
        {
            var key = (target.Ball.EntityHandle.Raw, skin);
            keep.Add(key);
            if (!_ballSkinShells.TryGetValue(key, out var shell) || !shell.IsValid)
            {
                shell = SpawnBallSkinShell(target.Ball, skin);
                if (shell is null) { keep.Remove(key); continue; }
                _ballSkinShells[key] = shell;
                spawned++;
            }
            FitBallSkinScale(shell, target.Ball);
        }
        foreach (var key in _ballSkinShells.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            if (_ballSkinShells[key].IsValid) _ballSkinShells[key].Remove();
            _ballSkinShells.Remove(key);
        }
        if (spawned > 0) Logger.LogInformation("[SM2DIAG] ball_skin_spawned reason={Reason} count={Count} shells={Shells}", reason, spawned, _ballSkinShells.Count);
    }

    private CDynamicProp? SpawnBallSkinShell(CPhysicsPropMultiplayer ball, int skin)
    {
        if (ball.AbsOrigin is not { } at) return null;
        var prop = Utilities.CreateEntityByName<CDynamicProp>("prop_dynamic");
        if (prop is null || !prop.IsValid) return null;
        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", BallSkinTargetName);
        keyValues.SetString("model", BallSkinModel);
        keyValues.SetInt("solid", 0);
        keyValues.SetVector("origin", new Vector(at.X, at.Y, at.Z));
        keyValues.SetAngle("angles", ball.AbsRotation is { } r ? new QAngle(r.X, r.Y, r.Z) : new QAngle(0, 0, 0));
        prop.DispatchSpawn(keyValues);
        if (!prop.IsValid) return null;
        prop.Entity!.Name = BallSkinTargetName;
        prop.AcceptInput("DisableCollision");
        if (skin > 0) prop.AcceptInput("Skin", value: skin.ToString(CultureInfo.InvariantCulture));
        prop.AcceptInput("SetParent", ball, prop, "!activator");
        return prop;
    }

    // The shell is the map ball's own mesh: give it the ball's own scale plus 0.4 % (no
    // z-fighting), whether or not the engine passes the ball's SetScale on to children.
    private void FitBallSkinScale(CDynamicProp shell, CPhysicsPropMultiplayer ball)
    {
        if (shell.CBodyComponent?.SceneNode is not { } node || ball.CBodyComponent?.SceneNode is not { } ballNode) return;
        var want = (ballNode.AbsScale > 0.001f ? ballNode.AbsScale : ballNode.Scale) * 1.004f;
        var abs = node.AbsScale > 0.001f ? node.AbsScale : node.Scale;
        if (MathF.Abs(abs - want) < 0.002f) return;
        var local = node.Scale * want / abs;
        shell.AcceptInput("SetScale", value: local.ToString("0.####", CultureInfo.InvariantCulture));
    }

    private void BallSkinCheckTransmit(CCheckTransmitInfoList infoList)
    {
        if (_ballSkinShells.Count == 0) return;
        foreach ((CCheckTransmitInfo info, CCSPlayerController? receiver) in infoList)
        {
            var chosen = receiver is { IsValid: true } ? BallSkinOf(receiver) - 1 : -1;
            foreach (var ((_, skin), shell) in _ballSkinShells)
                if (skin != chosen && shell.IsValid) info.TransmitEntities.Remove(shell);
        }
    }
}
