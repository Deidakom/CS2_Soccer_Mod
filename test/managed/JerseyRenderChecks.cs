using SoccerModMvp;

internal static class JerseyRenderChecks
{
    internal static void Run()
    {
        var profiles = JerseyRenderRules.Profiles;
        if (profiles.Count != 4)
            throw new Exception("The renderer must have exactly four calibrated kit profiles.");
        if (profiles.Select(p => p.ModelPath).Distinct(StringComparer.Ordinal).Count() != 4)
            throw new Exception("Each kit model must have its own explicit profile.");
        if (profiles.Select(p => p.NameSocket).Distinct(StringComparer.Ordinal).Count() != 1
            || profiles.Select(p => p.NumberSocket).Distinct(StringComparer.Ordinal).Count() != 1
            || profiles.Any(p => p.TorsoBone != "spine_2"))
        {
            throw new Exception("All calibrated kits must use the authored torso socket contract.");
        }

        var home = profiles.Single(p => p.ModelPath.EndsWith("kit_home.vmdl", StringComparison.Ordinal));
        if (!JerseyRenderRules.TryBuildPlan(home.ModelPath, false, "Max Mustermann", 88, out var outfield, out _))
            throw new Exception("A calibrated outfield kit must produce a render plan.");
        if (outfield.ChildCount != 2 || outfield.Number is not 88 || outfield.NumberText?.Text != "88")
            throw new Exception("Outfield kits must create one name child and one number child.");

        if (!JerseyRenderRules.TryBuildPlan(home.ModelPath, true, "Keeper", 1, out var goalkeeper, out _))
            throw new Exception("A calibrated goalkeeper kit must produce a render plan.");
        if (goalkeeper.ChildCount != 1 || goalkeeper.Number is not null || goalkeeper.NumberText is not null)
            throw new Exception("Goalkeepers must keep the painted 1 and receive no dynamic number child.");

        if (JerseyRenderRules.TryBuildPlan(
                home.ModelPath,
                false,
                "Player",
                88,
                out _,
                out _,
                new HashSet<string> { home.NameSocket }))
        {
            throw new Exception("A missing number socket must make the model unavailable.");
        }

        if (JerseyRenderRules.TryBuildPlan("models/player/custom.vmdl", false, "Player", 88, out _, out _))
            throw new Exception("Uncalibrated models must never receive a guessed overlay.");

        if (!JerseyRenderRules.TryBuildPlan(home.ModelPath, false, "wwwwwwwwww", 88, out var wide, out _)
            || wide.Name.WorldUnitsPerPixel > home.NameWorldUnitsPerPixel
            || wide.Name.EstimatedHeight > home.NameMaximumWidth + 0.001f)
        {
            throw new Exception("Long names must be bounded by the configured jersey width.");
        }
        if (JerseyRenderRules.NormalizeName("  éva__smith!! ") != "VA-SMITH")
            throw new Exception("The name sanitizer must keep the established ASCII jersey policy.");

        var stable = new JerseyEntitySnapshot(
            10,
            20,
            30,
            outfield.ModelPath,
            true,
            false,
            outfield.SanitizedName,
            outfield.Number?.ToString());
        var same = JerseyRenderRules.DecideLifecycle(stable, outfield, 10, 20, 30, outfield.ModelPath, true, false);
        if (same.Action != JerseyLifecycleAction.None)
            throw new Exception("An unchanged attached jersey must be reused.");

        var changedName = stable with { Name = "OLD" };
        if (JerseyRenderRules.DecideLifecycle(changedName, outfield, 10, 20, 30, outfield.ModelPath, true, false).Action
            != JerseyLifecycleAction.UpdateContent)
        {
            throw new Exception("A name change must update text without reparenting.");
        }

        var changedNumber = stable with { Number = "87" };
        if (JerseyRenderRules.DecideLifecycle(changedNumber, outfield, 10, 20, 30, outfield.ModelPath, true, false).Action
            != JerseyLifecycleAction.UpdateContent)
        {
            throw new Exception("A number change must update text without reparenting.");
        }

        if (JerseyRenderRules.DecideLifecycle(stable, outfield, 10, 21, 30, outfield.ModelPath, true, false).Action
            != JerseyLifecycleAction.Recreate
            || JerseyRenderRules.DecideLifecycle(stable, outfield, 10, 20, 30, outfield.ModelPath, false, false).Action
            != JerseyLifecycleAction.Recreate
            || JerseyRenderRules.DecideLifecycle(stable, goalkeeper, 10, 20, 30, home.ModelPath, true, true).Action
            != JerseyLifecycleAction.Recreate)
        {
            throw new Exception("Pawn, squad and goalkeeper transitions must recreate the attached state.");
        }

        if (JerseyRenderRules.DecideLifecycle(stable, null, 10, 20, 30, outfield.ModelPath, true, false).Action
            != JerseyLifecycleAction.Remove
            || JerseyRenderRules.DecideLifecycle(null, null, 10, 20, 30, outfield.ModelPath, true, false).Action
            != JerseyLifecycleAction.None)
        {
            throw new Exception("Unavailable plans must remove existing children and create none.");
        }

        Console.WriteLine("Jersey renderer checks passed: four profiles, socket gating, GK layout, bounded text, and lifecycle reuse.");
    }
}
