using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Timers;
using CounterStrikeSharp.API.Modules.Utils;
using Microsoft.Extensions.Logging;

namespace SoccerModMvp;

// 2026-09-27 owner: day sky "Kloofendal 38d Partly Cloudy (Pure Sky)" (Poly
// Haven, CC0) instead of the map's sky - test server only for now.
//
// Material: materials/soccermod/sky/sky_kloofendal_38d.vmat (sky.vfx, BC6H
// HDR cube compiled from a 4096x2048 lat-long EXR) in the SoccerMod Workshop
// item. It lives outside materials/skybox/ on purpose, so it never overrides
// the map's own sky for other servers.
//
// Enabled per server by the flag file soccermod_sky_kloofendal.enabled in this
// plugin's directory (checked when the map precaches). CS2's env_sky has no
// input to change its material (m_hSkyMaterial is a resource handle set from
// the "skyname" keyvalue at spawn), so we spawn our own env_sky with the new
// material and disable every other env_sky. The map recreates its entities on
// a new round, so this is re-applied at every round start (idempotent).
// Without the flag nothing is precached, spawned or disabled.
public sealed partial class SoccerModMvpPlugin
{
    private const string SkyKloofMaterial = "materials/soccermod/sky/sky_kloofendal_38d.vmat";
    private const string SkyKloofFlagFile = "soccermod_sky_kloofendal.enabled";
    private const string SkyKloofTargetName = "sm2_sky_kloofendal";

    private bool _skyKloofWanted;
    private bool _skyKloofPrecached;
    private CEnvSky? _skyKloof;

    private void SkyOnLoad(bool hotReload)
    {
        AddCommand("css_sm2sky", "Admin: SoccerMod day sky status|apply|off (test server flag file).", OnSkyAdminCommand);
        _skyKloofWanted = File.Exists(ConfigPath(SkyKloofFlagFile));
        RegisterListener<Listeners.OnServerPrecacheResources>(manifest =>
        {
            _skyKloofWanted = File.Exists(ConfigPath(SkyKloofFlagFile));
            _skyKloofPrecached = false;
            if (!_skyKloofWanted) return;
            if (!MountedAddonFiles().Contains(SkyKloofMaterial + "_c"))
            {
                Logger.LogInformation("[SM2DIAG] sky_unavailable reason=material_not_in_mounted_workshop_items material={Material}", SkyKloofMaterial);
                return;
            }
            manifest.AddResource(SkyKloofMaterial);
            _skyKloofPrecached = true;
            Logger.LogInformation("[SM2DIAG] sky_precached material={Material}", SkyKloofMaterial);
        });
        RegisterListener<Listeners.OnMapStart>(_ =>
        {
            _skyKloof = null;
            AddTimer(0.5f, () => SkyEnsure("map_start_plus_0_50s"), TimerFlags.STOP_ON_MAPCHANGE);
        });
        RegisterEventHandler<EventRoundStart>((_, _) =>
        {
            Server.NextFrame(() => SkyEnsure("round_start"));
            return HookResult.Continue;
        });
        if (hotReload)
        {
            // A reload keeps our old env_sky alive; its material is still
            // resident, so it may be re-applied without a map change.
            var old = SkyFindOwn();
            _skyKloofPrecached = old is not null;
            AddTimer(1.0f, () => SkyEnsure("hot_reload"));
        }
    }

    private CEnvSky? SkyFindOwn() =>
        Utilities.FindAllEntitiesByDesignerName<CEnvSky>("env_sky")
            .FirstOrDefault(e => e.IsValid && e.Entity?.Name == SkyKloofTargetName);

    private void SkyEnsure(string reason)
    {
        if (!_skyKloofWanted) return;
        if (IsStreetMap(Server.MapName)) return;   // the street arena keeps its own sunset sky (HallLayout.cs)
        if (!_skyKloofPrecached)
        {
            Logger.LogInformation("[SM2DIAG] sky_skipped reason={Reason} cause=not_precached_this_map (flag file set after map start, or material missing)", reason);
            return;
        }

        var skies = Utilities.FindAllEntitiesByDesignerName<CEnvSky>("env_sky").Where(e => e.IsValid).ToList();
        _skyKloof = skies.FirstOrDefault(e => e.Entity?.Name == SkyKloofTargetName);
        var mapSky = skies.FirstOrDefault(e => e.Entity?.Name != SkyKloofTargetName);
        var spawned = false;
        if (_skyKloof is null)
        {
            var sky = Utilities.CreateEntityByName<CEnvSky>("env_sky");
            if (sky is null || !sky.IsValid)
            {
                Logger.LogError("[SM2DIAG] sky_create_failed reason={Reason}", reason);
                return;
            }
            using var keyValues = new CEntityKeyValues();
            keyValues.SetString("targetname", SkyKloofTargetName);
            keyValues.SetString("skyname", SkyKloofMaterial);
            keyValues.SetString("tint_color", "255 255 255");
            keyValues.SetFloat("brightnessscale", 1.0f);
            keyValues.SetString("StartDisabled", "0");
            // Same place as the map's env_sky (position is irrelevant for the sky itself).
            keyValues.SetVector("origin", mapSky?.AbsOrigin is { } o ? new Vector(o.X, o.Y, o.Z) : new Vector(0, 0, 0));
            sky.DispatchSpawn(keyValues);
            if (!sky.IsValid)
            {
                Logger.LogError("[SM2DIAG] sky_spawn_invalid reason={Reason}", reason);
                return;
            }
            sky.Entity!.Name = SkyKloofTargetName;
            _skyKloof = sky;
            spawned = true;
        }

        var disabled = 0;
        foreach (var other in skies.Where(e => e.Entity?.Name != SkyKloofTargetName))
        {
            if (!other.Enabled) continue;
            other.AcceptInput("Disable");
            if (other.Enabled)
            {
                // Fallback if the input is not handled: flip the networked flag.
                other.Enabled = false;
                Utilities.SetStateChanged(other, "CEnvSky", "m_bEnabled");
            }
            disabled++;
        }
        if (!_skyKloof.Enabled)
        {
            _skyKloof.AcceptInput("Enable");
            if (!_skyKloof.Enabled)
            {
                _skyKloof.Enabled = true;
                Utilities.SetStateChanged(_skyKloof, "CEnvSky", "m_bEnabled");
            }
        }

        if (spawned || disabled > 0)
            Logger.LogInformation(
                "[SM2DIAG] sky_applied reason={Reason} material={Material} spawned={Spawned} map_skies_disabled={Disabled} env_sky_total={Total} own_enabled={Enabled}",
                reason, SkyKloofMaterial, spawned, disabled, skies.Count + (spawned ? 1 : 0), _skyKloof.Enabled);
    }

    // Turns our sky off for this map and gives the map its own sky back.
    private void SkyRestoreMap(string reason)
    {
        var own = SkyFindOwn();
        own?.Remove();
        _skyKloof = null;
        var enabled = 0;
        foreach (var other in Utilities.FindAllEntitiesByDesignerName<CEnvSky>("env_sky").Where(e => e.IsValid && e.Entity?.Name != SkyKloofTargetName))
        {
            if (other.Enabled) continue;
            other.AcceptInput("Enable");
            if (!other.Enabled)
            {
                other.Enabled = true;
                Utilities.SetStateChanged(other, "CEnvSky", "m_bEnabled");
            }
            enabled++;
        }
        Logger.LogInformation("[SM2DIAG] sky_restored_map reason={Reason} removed_own={Removed} map_skies_enabled={Enabled}", reason, own is not null, enabled);
    }

    private void OnSkyAdminCommand(CCSPlayerController? player, CommandInfo command)
    {
        if (!RequirePermission(player, command, "admin")) return;
        var arg = command.ArgCount >= 2 ? command.GetArg(1).ToLowerInvariant() : "status";
        switch (arg)
        {
            case "apply":
                _skyKloofWanted = File.Exists(ConfigPath(SkyKloofFlagFile));
                if (!_skyKloofWanted)
                    command.ReplyToCommand($"[SM] Day sky: flag file {SkyKloofFlagFile} is missing in the plugin directory.");
                SkyEnsure("admin_apply");
                break;
            case "off":
                _skyKloofWanted = false; // until the next map (the flag file decides again then)
                SkyRestoreMap("admin_off");
                break;
        }
        var skies = Utilities.FindAllEntitiesByDesignerName<CEnvSky>("env_sky").Where(e => e.IsValid).ToList();
        command.ReplyToCommand(
            $"[SM] Day sky: flag={File.Exists(ConfigPath(SkyKloofFlagFile))}, wanted={_skyKloofWanted}, precached={_skyKloofPrecached}, " +
            $"env_sky={string.Join(", ", skies.Select(s => $"{s.Entity?.Name ?? "?"}:{(s.Enabled ? "on" : "off")}"))} " +
            "(usage: css_sm2sky status|apply|off)");
    }
}
