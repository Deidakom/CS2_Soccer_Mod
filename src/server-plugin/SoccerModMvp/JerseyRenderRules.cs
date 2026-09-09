using System.Collections.ObjectModel;

namespace SoccerModMvp;

/// <summary>
/// The calibrated layout contract shared by the runtime renderer and the
/// headless tests.  These profiles intentionally key on the complete model
/// resource path: stock models and user-configured, uncalibrated models must
/// never receive a guessed overlay.
/// </summary>
public sealed record JerseyLayoutProfile(
    string ModelPath,
    string TorsoBone,
    string NameSocket,
    string NumberSocket,
    float NameFontSize,
    float NameWorldUnitsPerPixel,
    float NameMaximumWidth,
    float NumberFontSize,
    float NumberWorldUnitsPerPixel,
    float NumberMaximumWidth,
    float SurfaceClearance)
{
    public static JerseyLayoutProfile Create(string modelPath) => new(
        ModelPath: modelPath,
        TorsoBone: "spine_2",
        NameSocket: "soccermod_jersey_name",
        NumberSocket: "soccermod_jersey_number",
        // The socket carries position and rotation.  The font values only
        // calibrate glyph size; they do not affect attachment placement.
        NameFontSize: 32.0f,
        NameWorldUnitsPerPixel: 0.10f,
        NameMaximumWidth: 15.0f,
        NumberFontSize: 96.0f,
        NumberWorldUnitsPerPixel: 0.09f,
        NumberMaximumWidth: 12.0f,
        SurfaceClearance: 0.08f);
}

public sealed record JerseyTextPlan(
    string Text,
    string SocketName,
    float FontSize,
    float WorldUnitsPerPixel,
    float EstimatedHeight,
    float MaximumWidth);

public sealed record JerseyRenderPlan(
    string ModelPath,
    bool IsGoalkeeper,
    string SanitizedName,
    int? Number,
    JerseyTextPlan Name,
    JerseyTextPlan? NumberText)
{
    public int ChildCount => NumberText is null ? 1 : 2;
}

public enum JerseyLifecycleAction
{
    None,
    Create,
    Recreate,
    UpdateContent,
    Remove,
}

/// <summary>
/// A small production-facing snapshot.  Entity validity and attachment
/// verification stay in the plugin adapter; this record contains only the
/// state that determines whether an existing pair can be reused.
/// </summary>
public sealed record JerseyEntitySnapshot(
    uint ControllerRaw,
    uint PawnRaw,
    ulong SteamId,
    string ModelPath,
    bool HomeSquad,
    bool IsGoalkeeper,
    string Name,
    string? Number);

public sealed record JerseyLifecycleDecision(
    JerseyLifecycleAction Action,
    string Reason);

public static class JerseyRenderRules
{
    public const int MaximumNameLength = 10;

    private static readonly ReadOnlyCollection<JerseyLayoutProfile> ProfileList =
        new(new[]
        {
            JerseyLayoutProfile.Create("models/soccermod/kits/kit_home.vmdl"),
            JerseyLayoutProfile.Create("models/soccermod/kits/kit_away.vmdl"),
            JerseyLayoutProfile.Create("models/soccermod/kits/kit_gkhome.vmdl"),
            JerseyLayoutProfile.Create("models/soccermod/kits/kit_gkaway.vmdl"),
        });

    public static IReadOnlyList<JerseyLayoutProfile> Profiles => ProfileList;

    public static bool TryGetProfile(string? modelPath, out JerseyLayoutProfile profile)
    {
        var normalized = NormalizeModelPath(modelPath);
        var match = ProfileList.FirstOrDefault(
            candidate => string.Equals(candidate.ModelPath, normalized, StringComparison.Ordinal));
        if (match is null)
        {
            profile = null!;
            return false;
        }

        profile = match;
        return true;
    }

    public static string NormalizeName(string? value)
    {
        var input = value ?? string.Empty;
        var builder = new System.Text.StringBuilder(MaximumNameLength);
        var lastDash = false;

        foreach (var raw in input)
        {
            if (builder.Length >= MaximumNameLength)
            {
                break;
            }

            var character = raw;
            if (character is >= 'a' and <= 'z')
            {
                character = (char)(character - ('a' - 'A'));
            }

            if (character is >= 'A' and <= 'Z')
            {
                builder.Append(character);
                lastDash = false;
            }
            else if (character is ' ' or '-' or '_'
                     && builder.Length > 0
                     && !lastDash)
            {
                builder.Append('-');
                lastDash = true;
            }
        }

        while (builder.Length > 0 && builder[^1] == '-')
        {
            builder.Length--;
        }

        return builder.Length == 0 ? "PLAYER" : builder.ToString();
    }

    public static bool TryBuildPlan(
        string? modelPath,
        bool isGoalkeeper,
        string? playerName,
        int number,
        out JerseyRenderPlan plan,
        out string reason,
        IReadOnlySet<string>? availableSockets = null)
    {
        plan = null!;
        reason = string.Empty;
        if (!TryGetProfile(modelPath, out var profile))
        {
            reason = "unknown_model_or_unapproved_socket_profile";
            return false;
        }

        if (availableSockets is not null
            && (!availableSockets.Contains(profile.NameSocket)
                || (!isGoalkeeper && !availableSockets.Contains(profile.NumberSocket))))
        {
            reason = "model_socket_missing";
            return false;
        }

        if (!isGoalkeeper && number is < 2 or > 99)
        {
            reason = "invalid_outfield_number";
            return false;
        }

        var sanitizedName = NormalizeName(playerName);
        var nameScale = FitWorldUnitsPerPixel(
            sanitizedName,
            profile.NameFontSize,
            profile.NameWorldUnitsPerPixel,
            profile.NameMaximumWidth);
        var name = new JerseyTextPlan(
            sanitizedName,
            profile.NameSocket,
            profile.NameFontSize,
            nameScale,
            profile.NameFontSize * nameScale,
            profile.NameMaximumWidth);

        JerseyTextPlan? numberText = null;
        if (!isGoalkeeper)
        {
            var numberValue = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var numberScale = FitWorldUnitsPerPixel(
                numberValue,
                profile.NumberFontSize,
                profile.NumberWorldUnitsPerPixel,
                profile.NumberMaximumWidth);
            numberText = new JerseyTextPlan(
                numberValue,
                profile.NumberSocket,
                profile.NumberFontSize,
                numberScale,
                profile.NumberFontSize * numberScale,
                profile.NumberMaximumWidth);
        }

        plan = new JerseyRenderPlan(
            profile.ModelPath,
            isGoalkeeper,
            sanitizedName,
            numberText is null ? null : number,
            name,
            numberText);
        return true;
    }

    public static JerseyLifecycleDecision DecideLifecycle(
        JerseyEntitySnapshot? current,
        JerseyRenderPlan? desired,
        uint controllerRaw,
        uint pawnRaw,
        ulong steamId,
        string modelPath,
        bool homeSquad,
        bool isGoalkeeper)
    {
        if (desired is null)
        {
            return current is null
                ? new(JerseyLifecycleAction.None, "no_render_plan")
                : new(JerseyLifecycleAction.Remove, "render_plan_unavailable");
        }

        if (current is null)
        {
            return new(JerseyLifecycleAction.Create, "no_existing_children");
        }

        var identityChanged = current.ControllerRaw != controllerRaw
            || current.PawnRaw != pawnRaw
            || current.SteamId != steamId
            || !string.Equals(current.ModelPath, modelPath, StringComparison.Ordinal)
            || current.HomeSquad != homeSquad
            || current.IsGoalkeeper != isGoalkeeper;
        if (identityChanged)
        {
            return new(JerseyLifecycleAction.Recreate, "controller_pawn_model_squad_or_gk_changed");
        }

        var desiredNumber = desired.Number?.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!string.Equals(current.Name, desired.SanitizedName, StringComparison.Ordinal)
            || !string.Equals(current.Number, desiredNumber, StringComparison.Ordinal))
        {
            return new(JerseyLifecycleAction.UpdateContent, "text_content_changed");
        }

        return new(JerseyLifecycleAction.None, "stable_state");
    }

    private static float FitWorldUnitsPerPixel(
        string text,
        float fontSize,
        float defaultWorldUnitsPerPixel,
        float maximumWidth)
    {
        // Arial's widest uppercase glyphs are close to 0.60 em.  This is only
        // a conservative layout bound; actual glyph bounds remain a visual
        // acceptance item because the engine font rasterizer is authoritative.
        var estimatedPixels = Math.Max(1.0f, text.Length * fontSize * 0.60f);
        return MathF.Min(defaultWorldUnitsPerPixel, maximumWidth / estimatedPixels);
    }

    private static string NormalizeModelPath(string? modelPath) =>
        (modelPath ?? string.Empty).Trim().Replace('\\', '/');
}
