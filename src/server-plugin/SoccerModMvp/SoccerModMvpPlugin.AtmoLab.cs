using System.Drawing;
using System.Globalization;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-29 "Arena Vision" Phase 0, engine lab (test server only, needs
// soccermod_atmo.enabled): which Source 2 features can the plugin drive at
// runtime? Each test only uses files CS2 already ships, so no Workshop upload
// is needed to find out. The owner looks in game and says what he sees.
//   lab 1 [fog] [ev] [spread]  volumetric fog + 4 coloured barn lights from the roof corners
//   lab 2 [bloom|red|blue|filmic|mirage|off]  master post_processing_volume with a CS2 .vpost
//   lab 3 [1-4] [ev] [size]    barn light with a slideshow-projector cookie onto the centre circle
//   lab 4                      Panorama in the world (point_clientui_world_panel, scorebug layout)
//   lab 5                      point_camera + func_monitor (live picture)
//   lab 7 [1|2]                CS2 firework particles over both ends
//   lab 8                      crowd sounds from four stand positions (spatial mix)
//   lab all | list | clear
public sealed partial class SoccerModMvpPlugin
{
    private const string AtmoLabTargetName = "sm2_atmo_lab";
    private static readonly (float X, float Y)[] AtmoRoofCorners = { (1434f, 1818f), (-1434f, 1818f), (-1434f, -1818f), (1434f, -1818f) };
    private const float AtmoRoofZ = 905f;
    private static readonly Dictionary<string, string> AtmoLabVposts = new()
    {
        ["bloom"] = "lighting/postprocessing/correction/bloomtest.vpost",
        ["red"] = "lighting/postprocessing/correction/cc_freeze_t.vpost",
        ["blue"] = "lighting/postprocessing/correction/cc_freeze_ct.vpost",
        ["filmic"] = "lighting/postprocessing/filmic_default.vpost",
        ["mirage"] = "lighting/postprocessing/de_mirage_postprocess.vpost",
    };
    private static readonly string[] AtmoLabCookies =
    {
        "materials/effects/lightcookies/slideshow_projector_01.vtex",
        "materials/effects/lightcookies/slideshow_projector_02.vtex",
        "materials/effects/lightcookies/slideshow_projector_03.vtex",
        "materials/effects/lightcookies/slideshow_projector_04.vtex",
    };
    private static readonly string[] AtmoLabFireworks =
    {
        "particles/inferno_fx/firework_crate_explosion_01.vpcf",
        "particles/inferno_fx/firework_crate_explosion_02.vpcf",
        "particles/inferno_fx/fireworks_explosion_glow_03.vpcf",
        "particles/inferno_fx/fireworks_explosion_trail_04.vpcf",
        "particles/environment/flared_light01.vpcf",
        "particles/entity/env_explosion/explosion_hegrenade_a.vpcf",
    };

    private readonly List<CBaseEntity> _atmoLabEntities = new();
    private readonly Dictionary<CBaseEntity, double> _atmoLabExpiry = new();
    private readonly List<(double At, Action Run)> _atmoLabQueue = new();

    private void AtmoLabOnLoad(bool hotReload)
    {
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            if (!File.Exists(ConfigPath(AtmoFlagFile))) return;
            foreach (var vpost in AtmoLabVposts.Values) manifest.AddResource(vpost);
            foreach (var cookie in AtmoLabCookies) manifest.AddResource(cookie);
            foreach (var fx in AtmoLabFireworks) manifest.AddResource(fx);
            AtmoFxPrecache(manifest);
            AtmoBallFxPrecache(manifest);
            Logger.LogInformation("[SM2DIAG] atmo_lab_precached vposts={V} cookies={C} particles={P}", AtmoLabVposts.Count, AtmoLabCookies.Length, AtmoLabFireworks.Length);
        });
        RegisterListener<Listeners.OnMapEnd>(() =>
        {
            _atmoLabEntities.Clear();
            _atmoLabExpiry.Clear();
            _atmoLabQueue.Clear();
        });
    }

    private void AtmoLabOnTick()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var ran = 0;
        var now = (double)Server.TickedTime;
        foreach (var (entity, at) in _atmoLabExpiry.Where(kv => kv.Value <= now).ToList())
        {
            if (entity.IsValid) entity.Remove();
            _atmoLabExpiry.Remove(entity);
            _atmoLabEntities.Remove(entity);
        }
        // Due items run in the order they were queued (oldest first), so a later step always
        // wins over an earlier one that falls into the same 0.25 s tick (review 2026-09-29).
        var due = _atmoLabQueue.Where(q => q.At <= now).OrderBy(q => q.At).ToList();
        if (due.Count > 0) _atmoLabQueue.RemoveAll(q => q.At <= now);
        foreach (var (_, run) in due)
        {
            run();
            ran++;
        }
        // Performance watch (owner: effects must not eat the server frame).
        if (watch.Elapsed.TotalMilliseconds > 2.0)
            Logger.LogInformation("[SM2DIAG] atmo_tick_slow ms={Ms:F2} ran={Ran} entities={Count}", watch.Elapsed.TotalMilliseconds, ran, _atmoLabEntities.Count);
    }

    private void OnAtmoLabCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!AtmoOn)
        {
            command.ReplyToCommand("[SM] Engine lab needs the flag file soccermod_atmo.enabled and the v8 stadium.");
            return;
        }
        var test = command.ArgCount >= 3 ? command.GetArg(2).ToLowerInvariant() : "list";
        string Arg(int i, string fallback) => command.ArgCount > i ? command.GetArg(i) : fallback;
        float Num(int i, float fallback) => float.TryParse(Arg(i, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        string result;
        switch (test)
        {
            case "1": result = AtmoLabFog(Num(3, 1.0f), Num(4, 6.0f), Num(5, 0.08f)); break;
            case "2": result = AtmoLabPostFx(Arg(3, "bloom").ToLowerInvariant()); break;
            case "3": result = AtmoLabCookie((int)Num(3, 1), Num(4, 7.0f), Num(5, 700.0f)); break;
            case "4": result = AtmoLabWorldPanel(); break;
            case "5": result = AtmoLabMonitor(); break;
            case "7": result = AtmoLabFireworksShow((int)Num(3, 1)); break;
            case "8": result = AtmoLabSpatialSound(); break;
            case "light":
                {
                    // lab light <x> <y> <z> <pitch> <yaw> [ev] [cookie 0-4] [size] [spread]: one white barn light with explicit angles.
                    var cookieIndex = (int)Num(9, 0);
                    var origin = new Vector(Num(3, 0), Num(4, 0), Num(5, 780));
                    var angles = new QAngle(Num(6, 90), Num(7, 0), 0);
                    var light = AtmoLabSpawn<CBarnLight>("light_barn", "light", kv =>
                    {
                        kv.SetVector("origin", origin);
                        kv.SetAngle("angles", angles);
                        kv.SetBool("enabled", true);
                        kv.SetInt("brightness_units", 0);
                        kv.SetFloat("brightness", Num(8, 10.0f));
                        kv.SetInt("colormode", 0);
                        kv.SetColor("color", Color.White);
                        kv.SetInt("directlight", 2);
                        kv.SetInt("castshadows", 0);
                        kv.SetFloat("range", 3000.0f);
                        kv.SetString("size_params", string.Create(CultureInfo.InvariantCulture, $"{Num(10, 64)} {Num(10, 64)} {Num(11, 0.08f)}"));
                        if (cookieIndex >= 1) kv.SetString("lightcookie", AtmoLabCookies[Math.Clamp(cookieIndex, 1, AtmoLabCookies.Length) - 1]);
                    });
                    result = $"light={(light is not null)} at={origin.X:F0},{origin.Y:F0},{origin.Z:F0} angles={angles.X:F0},{angles.Y:F0} cookie={cookieIndex}";
                    break;
                }
            case "fx":
                {
                    // lab fx <1-5> <x> <y> <z> [seconds]: one CS2 particle system at a spot (index into the precached list).
                    var effect = int.TryParse(Arg(3, "1"), out var fxIndex) ? AtmoLabFireworks[Math.Clamp(fxIndex, 1, AtmoLabFireworks.Length) - 1] : Arg(3, "firework_red");
                    if (!effect.StartsWith("particles/", StringComparison.Ordinal)) effect = AtmoParticleDir + effect + ".vpcf";
                    var at = new Vector(Num(4, 0), Num(5, 0), Num(6, 300));
                    // Pattern of other CS2 plugins: set the effect on the entity, spawn, teleport, start.
                    var fx = Utilities.CreateEntityByName<CParticleSystem>("info_particle_system");
                    if (fx is not null && fx.IsValid)
                    {
                        fx.EffectName = effect;
                        fx.StartActive = true;
                        fx.DispatchSpawn();
                        fx.Teleport(at, new QAngle(0, 0, 0), new Vector(0, 0, 0));
                        fx.AcceptInput("Start");
                        fx.Entity!.Name = AtmoLabTargetName + "_fx";
                        _atmoLabEntities.Add(fx);
                        _atmoLabExpiry[fx] = Server.TickedTime + Num(7, 10);
                    }
                    result = $"fx={(fx is not null)} {effect} at {at.X:F0},{at.Y:F0},{at.Z:F0}";
                    break;
                }
            case "crowd":
                AtmoCrowdEnsure("lab");
                AtmoCrowdPlay(Arg(3, "cheer"));
                result = $"crowd={_atmoCrowds.Count(c => c is { IsValid: true })} sections anim={Arg(3, "cheer")}";
                break;
            case "show":
                AtmoGoalShow(Arg(3, "red").StartsWith("b", StringComparison.OrdinalIgnoreCase) ? CsTeamBlue : CsTeamRed, (int)Num(4, -1), (int)Num(5, -1));
                result = "goal show started";
                break;
            case "fogset":
                {
                    var n = 0;
                    foreach (var c in _atmoLabEntities.OfType<CEnvVolumetricFogController>().Where(c => c.IsValid))
                    {
                        c.AcceptInput("SetFogStrength", value: Num(3, 1.0f).ToString(CultureInfo.InvariantCulture));
                        c.AcceptInput("SetDrawDistance", value: Num(4, 4000.0f).ToString(CultureInfo.InvariantCulture));
                        c.AcceptInput("ForceRefresh");
                        n++;
                    }
                    result = $"fog controllers updated={n}";
                    break;
                }
            case "all":
                result = string.Join(" | ", AtmoLabFog(1.0f, 6.0f, 0.08f), AtmoLabCookie(1, 7.0f, 700.0f), AtmoLabFireworksShow(1), AtmoLabSpatialSound());
                break;
            case "clear":
                AtmoLabClear();
                result = "cleared";
                break;
            default:
                result = "tests: 1 fog+beams [fog] [ev] [spread] | 2 postfx [bloom|red|blue|filmic|mirage|off] | 3 cookie [1-4] [ev] [size] | 4 world panel | 5 camera monitor | 7 fireworks [1|2] | 8 spatial sound | all | clear";
                break;
        }
        Logger.LogInformation("[SM2DIAG] atmo_lab test={Test} result={Result} by={By}", test, result, player?.PlayerName ?? "console");
        command.ReplyToCommand($"[SM] lab {test}: {result}");
    }

    private void AtmoLabClear(string? group = null)
    {
        foreach (var entity in _atmoLabEntities.ToList())
        {
            if (group is not null && entity.Entity?.Name != AtmoLabTargetName + "_" + group) continue;
            if (entity.IsValid) entity.Remove();
            _atmoLabEntities.Remove(entity);
            _atmoLabExpiry.Remove(entity);
        }
    }

    private T? AtmoLabSpawn<T>(string className, string group, Action<CEntityKeyValues> setup, double lifeSeconds = 0) where T : CBaseEntity
    {
        var entity = Utilities.CreateEntityByName<T>(className);
        if (entity is null || !entity.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] atmo_lab_create_failed class={Class}", className);
            return null;
        }
        using var keyValues = new CEntityKeyValues();
        keyValues.SetString("targetname", AtmoLabTargetName + "_" + group);
        setup(keyValues);
        entity.DispatchSpawn(keyValues);
        if (!entity.IsValid)
        {
            Logger.LogWarning("[SM2DIAG] atmo_lab_spawn_failed class={Class}", className);
            return null;
        }
        entity.Entity!.Name = AtmoLabTargetName + "_" + group;
        _atmoLabEntities.Add(entity);
        if (lifeSeconds > 0) _atmoLabExpiry[entity] = Server.TickedTime + lifeSeconds;
        return entity;
    }

    private static QAngle AtmoAim(Vector from, Vector to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var dz = to.Z - from.Z;
        var yaw = MathF.Atan2(dy, dx) * 180.0f / MathF.PI;
        var pitch = -MathF.Atan2(dz, MathF.Sqrt(dx * dx + dy * dy)) * 180.0f / MathF.PI;
        return new QAngle(pitch, yaw, 0.0f);
    }

    private CBarnLight? AtmoLabBarnLight(string group, Vector origin, Vector target, Color color, float ev, float spread, float range, string? cookie = null, float size = 64.0f)
    {
        var angles = AtmoAim(origin, target);
        var light = AtmoLabSpawn<CBarnLight>("light_barn", group, kv =>
        {
            kv.SetVector("origin", origin);
            kv.SetAngle("angles", angles);
            kv.SetBool("enabled", true);
            kv.SetInt("brightness_units", 0);
            kv.SetFloat("brightness", ev);
            kv.SetInt("colormode", 0);
            kv.SetColor("color", color);
            kv.SetInt("directlight", 2);
            kv.SetInt("castshadows", 0);
            kv.SetFloat("range", range);
            kv.SetFloat("skirt", 0.1f);
            kv.SetFloat("skirt_near", 0.02f);
            kv.SetInt("luminaire_shape", 0);
            kv.SetFloat("shape", 0.0f);
            kv.SetFloat("soft_x", 0.35f);
            kv.SetFloat("soft_y", 0.35f);
            kv.SetString("size_params", string.Create(CultureInfo.InvariantCulture, $"{size} {size} {spread}"));
            if (cookie is not null) kv.SetString("lightcookie", cookie);
        });
        return light;
    }

    // ---- 1: volumetric fog + beams -------------------------------------------------------------

    private string AtmoLabFog(float fogStrength, float ev, float spread)
    {
        AtmoLabClear("fog");
        var controller = AtmoLabSpawn<CEnvVolumetricFogController>("env_volumetric_fog_controller", "fog", kv =>
        {
            kv.SetVector("origin", new Vector(0, 0, 400));
            kv.SetBool("IsMaster", true);
            kv.SetFloat("FogStrength", fogStrength);
            kv.SetFloat("DrawDistance", 4000.0f);
            kv.SetFloat("FadeInStart", 20.0f);
            kv.SetFloat("FadeInEnd", 100.0f);
            kv.SetFloat("FadeSpeed", 1.0f);
            kv.SetFloat("NoiseStrength", 0.3f);
            kv.SetFloat("NoiseSpeed", 0.15f);
            kv.SetString("NoiseScale", "0.01 0.01 0.01");
            kv.SetColor("TintColor", Color.White);
        });
        var volume = AtmoLabSpawn<CEnvVolumetricFogVolume>("env_volumetric_fog_volume", "fog", kv =>
        {
            kv.SetVector("origin", new Vector(0, 0, 0));
            kv.SetString("box_mins", "-2400 -2800 -64");
            kv.SetString("box_maxs", "2400 2800 1100");
            kv.SetFloat("FogStrength", 1.0f);
            kv.SetInt("Shape", 0);
            kv.SetFloat("FalloffExponent", 1.0f);
        });
        var lights = 0;
        var colors = new[] { Color.FromArgb(255, 60, 40), Color.FromArgb(60, 120, 255), Color.FromArgb(255, 60, 40), Color.FromArgb(60, 120, 255) };
        for (var i = 0; i < AtmoRoofCorners.Length; i++)
        {
            var (x, y) = AtmoRoofCorners[i];
            var light = AtmoLabBarnLight("fog", new Vector(x, y, AtmoRoofZ), new Vector(x * 0.25f, y * 0.35f, StadiumPitchPlaneZ), colors[i], ev, spread, 4000.0f);
            if (light is null) continue;
            // CS2's editor hides the fog keys of light_barn; the schema still has them.
            light.Fog = 1;
            light.FogStrength = 1.0f;
            light.FogScale = 1.0f;
            light.FogShadows = 0;
            Utilities.SetStateChanged(light, "CBarnLight", "m_nFog");
            Utilities.SetStateChanged(light, "CBarnLight", "m_flFogStrength");
            Utilities.SetStateChanged(light, "CBarnLight", "m_flFogScale");
            lights++;
        }
        if (controller is not null)
        {
            controller.AcceptInput("SetFogStrength", value: fogStrength.ToString(CultureInfo.InvariantCulture));
            controller.AcceptInput("ForceRefresh");
        }
        return $"fog controller={(controller is not null)} volume={(volume is not null)} beams={lights}/4 (fog={fogStrength:F1} ev={ev:F1} spread={spread:F2})";
    }

    // ---- 2: post-processing ----------------------------------------------------------------------

    private string AtmoLabPostFx(string look)
    {
        AtmoLabClear("postfx");
        if (look == "off") return "off";
        if (!AtmoLabVposts.TryGetValue(look, out var vpost)) return $"unknown look '{look}' (bloom|red|blue|filmic|mirage|off)";
        var volume = AtmoLabSpawn<CPostProcessingVolume>("post_processing_volume", "postfx", kv =>
        {
            kv.SetVector("origin", new Vector(0, 0, 0));
            kv.SetString("postprocessing", vpost);
            kv.SetBool("master", true);
            kv.SetFloat("fadetime", 1.0f);
            kv.SetBool("enableexposure", false);
        });
        var counts = string.Join(",", Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot && p.PlayerPawn.Value is { IsValid: true })
            .Select(p => $"{p.PlayerName}:{p.PlayerPawn.Value!.CameraServices?.PostProcessingVolumes.Count ?? -1}"));
        return $"volume={(volume is not null)} vpost={vpost} playerVolumes=[{counts}]";
    }

    // ---- 3: cookie projection ----------------------------------------------------------------------

    private string AtmoLabCookie(int index, float ev, float size)
    {
        AtmoLabClear("cookie");
        var cookie = AtmoLabCookies[Math.Clamp(index, 1, AtmoLabCookies.Length) - 1];
        var light = AtmoLabBarnLight("cookie", new Vector(0, 0, 780), new Vector(0, 0.1f, StadiumPitchPlaneZ), Color.White, ev, 0.02f, 1200.0f, cookie, size);
        return $"projector={(light is not null)} cookie={cookie} ev={ev:F1} size={size:F0}";
    }

    // ---- 4: Panorama in the world --------------------------------------------------------------------

    private string AtmoLabWorldPanel()
    {
        AtmoLabClear("panel");
        var made = 0;
        // Two ways of naming the layout, since world panels are undocumented for CS2.
        foreach (var (layout, y) in new[] { (ScoreHudLayout, 1250f), ("file://{resources}/layout/custom_game/soccermod_scorebug.xml", -1250f) })
        {
            var panel = AtmoLabSpawn<CPointClientUIWorldPanel>("point_clientui_world_panel", "panel", kv =>
            {
                kv.SetVector("origin", new Vector(0, y, 220));
                kv.SetAngle("angles", new QAngle(0, y > 0 ? -90 : 90, 0));
                kv.SetString("dialog_layout_name", layout);
                kv.SetFloat("width", 420.0f);
                kv.SetFloat("height", 120.0f);
                kv.SetFloat("panel_dpi", 2.0f);
                kv.SetBool("ignore_input", true);
                kv.SetBool("lit", false);
                kv.SetInt("horizontal_align", 1);
                kv.SetInt("vertical_align", 1);
                kv.SetInt("orientation", 0);
            });
            if (panel is null) continue;
            panel.AcceptInput("Enable");
            made++;
        }
        return $"panels={made}/2 above both penalty areas (+y: {ScoreHudLayout}, -y: file://{{resources}} form)";
    }

    // ---- 5: live camera picture -----------------------------------------------------------------------

    private string AtmoLabMonitor()
    {
        AtmoLabClear("monitor");
        var from = new Vector(0, -900, 520);
        var camera = AtmoLabSpawn<CPointCamera>("point_camera", "monitor", kv =>
        {
            kv.SetVector("origin", from);
            kv.SetAngle("angles", AtmoAim(from, new Vector(0, 0, StadiumPitchPlaneZ)));
            kv.SetFloat("FOV", 70.0f);
            kv.SetFloat("aspectRatio", 1.78f);
        });
        camera?.AcceptInput("SetOn");
        var monitor = AtmoLabSpawn<CFuncMonitor>("func_monitor", "monitor", kv =>
        {
            kv.SetVector("origin", new Vector(0, 1950, 888));
            kv.SetString("target", AtmoLabTargetName + "_monitor");
            kv.SetString("targetcamera", AtmoLabTargetName + "_monitor");
            kv.SetBool("start_enabled", true);
        });
        if (monitor is not null && camera is not null)
        {
            monitor.HTargetCamera.Raw = camera.EntityHandle.Raw;
            Utilities.SetStateChanged(monitor, "CFuncMonitor", "m_hTargetCamera");
            monitor.AcceptInput("Enable");
        }
        return $"camera={(camera is not null)} monitor={(monitor is not null)} (func_monitor has no brush model at runtime; a picture here would be a surprise)";
    }

    // ---- 7: fireworks ------------------------------------------------------------------------------------

    private string AtmoLabFireworksShow(int variant)
    {
        var effect = AtmoLabFireworks[Math.Clamp(variant, 1, AtmoLabFireworks.Length) - 1];
        var spots = new List<Vector>();
        foreach (var (x, y) in AtmoRoofCorners) spots.Add(new Vector(x, y, 1250));
        spots.Add(new Vector(0, 2300, 1400));
        spots.Add(new Vector(0, -2300, 1400));
        var now = (double)Server.TickedTime;
        for (var i = 0; i < spots.Count; i++)
        {
            var spot = spots[i];
            _atmoLabQueue.Add((now + i * 0.45, () =>
            {
                var fx = AtmoLabSpawn<CParticleSystem>("info_particle_system", "fireworks", kv =>
                {
                    kv.SetVector("origin", spot);
                    kv.SetString("effect_name", effect);
                    kv.SetBool("start_active", true);
                }, lifeSeconds: 8.0);
                fx?.AcceptInput("Start");
            }));
        }
        return $"{spots.Count} bursts of {effect} over 2.7 s";
    }

    // ---- 8: spatial crowd -------------------------------------------------------------------------------

    private string AtmoLabSpatialSound()
    {
        AtmoLabClear("sound");
        var stands = new (string Name, Vector At)[]
        {
            ("east (+x)", new Vector(2000, 0, 450)),
            ("north (+y)", new Vector(0, 2400, 450)),
            ("west (-x)", new Vector(-2000, 0, 450)),
            ("south (-y)", new Vector(0, -2400, 450)),
        };
        var now = (double)Server.TickedTime;
        var made = 0;
        for (var i = 0; i < stands.Length; i++)
        {
            var (name, at) = stands[i];
            var emitter = AtmoLabSpawn<CBaseEntity>("info_target", "sound", kv => kv.SetVector("origin", at), lifeSeconds: 14.0);
            if (emitter is null) continue;
            made++;
            var pitch = 0.9f + 0.07f * i;
            _atmoLabQueue.Add((now + i * 2.5, () =>
            {
                if (!emitter.IsValid) return;
                emitter.EmitSound(StadiumCrowdGoal, SoundRecipients(SoccerSound.Stadium), 1.0f, pitch);
                foreach (var p in Utilities.GetPlayers().Where(p => p.IsValid && !p.IsBot)) p.PrintToCenter($"Crowd from {name}");
            }));
        }
        return $"{made}/4 stands, one after another every 2.5 s: east, north, west, south";
    }
}
