using System.Text.Json;
using NFCBets.Causal.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Causal;

public class InteractionEffectAnalyzer
{
    public async Task<InteractionAnalysisReport> AnalyzeAllInteractionsAsync(List<PirateFeatureRecord> data)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🔬 COMPREHENSIVE INTERACTION EFFECT ANALYSIS");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var report = new InteractionAnalysisReport
        {
            AnalysisDate = DateTime.UtcNow,
            TotalRecords = data.Count
        };

        var validData = data.Where(f => f.IsWinner.HasValue).ToList();

        // ════════════════════════════════════════════════════════
        // CATEGORY 1: Food Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("📊 CATEGORY 1: Food Interactions");

        report.Interactions.Add(await TestInteraction(validData,
            "Food x Position",
            f => f.FoodAdjustment >= 1,
            f => f.Position <= 1,
            "Positive food AND front position"));

        report.Interactions.Add(await TestInteraction(validData,
            "Food x Favorite",
            f => f.FoodAdjustment >= 1,
            f => f.CurrentOdds <= 2,
            "Positive food AND favorite status"));

        report.Interactions.Add(await TestInteraction(validData,
            "Food x Strength",
            f => f.FoodAdjustment >= 1,
            f => f.Strength >= 60,
            "Positive food AND high strength"));

        report.Interactions.Add(await TestInteraction(validData,
            "Food x Weak Rivals",
            f => f.FoodAdjustment >= 1,
            f => f.AvgRivalStrength < 40,
            "Positive food AND weak competition"));

        report.Interactions.Add(await TestInteraction(validData,
            "Negative Food x Back Position",
            f => f.FoodAdjustment <= -1,
            f => f.Position >= 3,
            "Negative food AND back position"));

        report.Interactions.Add(await TestInteraction(validData,
            "Food x Historical Winner",
            f => f.FoodAdjustment >= 1,
            f => f.HistoricalWinRate >= 0.3,
            "Positive food AND strong history"));

        // ════════════════════════════════════════════════════════
        // CATEGORY 2: Position Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("\n📊 CATEGORY 2: Position Interactions");

        report.Interactions.Add(await TestInteraction(validData,
            "Front Position x High Strength",
            f => f.Position <= 1,
            f => f.Strength >= 60,
            "Front position AND high strength"));

        report.Interactions.Add(await TestInteraction(validData,
            "Front Position x Favorite",
            f => f.Position <= 1,
            f => f.CurrentOdds <= 2,
            "Front position AND favorite"));

        report.Interactions.Add(await TestInteraction(validData,
            "Back Position x Longshot",
            f => f.Position >= 3,
            f => f.CurrentOdds >= 10,
            "Back position AND longshot odds"));

        report.Interactions.Add(await TestInteraction(validData,
            "Position x Rival Strength",
            f => f.Position <= 1,
            f => f.AvgRivalStrength >= 50,
            "Front position AND strong rivals"));

        // ════════════════════════════════════════════════════════
        // CATEGORY 3: Strength/Odds Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("\n📊 CATEGORY 3: Strength/Odds Interactions");

        report.Interactions.Add(await TestInteraction(validData,
            "High Strength x Undervalued",
            f => f.Strength >= 60,
            f => f.CurrentOdds >= 5,
            "High strength but undervalued by odds"));

        report.Interactions.Add(await TestInteraction(validData,
            "Low Strength x Favorite",
            f => f.Strength <= 40,
            f => f.CurrentOdds <= 2,
            "Low strength but favorite (overvalued?)"));

        report.Interactions.Add(await TestInteraction(validData,
            "Strength x Weak Rivals",
            f => f.Strength >= 60,
            f => f.AvgRivalStrength < 40,
            "High strength AND weak competition"));

        report.Interactions.Add(await TestInteraction(validData,
            "Strength Advantage x Front Position",
            f => f.Strength - f.AvgRivalStrength >= 15,
            f => f.Position <= 1,
            "Large strength advantage AND front position"));

        // ════════════════════════════════════════════════════════
        // CATEGORY 4: Historical Performance Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("\n📊 CATEGORY 4: Historical Performance Interactions");

        report.Interactions.Add(await TestInteraction(validData,
            "Hot Streak x Favorite",
            f => f.RecentWinRate >= 0.4,
            f => f.CurrentOdds <= 2,
            "Recent hot streak AND favorite status"));

        report.Interactions.Add(await TestInteraction(validData,
            "Cold Streak x Undervalued",
            f => f.RecentWinRate <= 0.1,
            f => f.CurrentOdds >= 8,
            "Recent cold streak AND undervalued"));

        report.Interactions.Add(await TestInteraction(validData,
            "Good vs Rivals x Front Position",
            f => f.WinRateVsCurrentRivals >= 0.35,
            f => f.Position <= 1,
            "Beats these rivals AND front position"));

        report.Interactions.Add(await TestInteraction(validData,
            "Arena Specialist x Favorite",
            f => f.ArenaWinRate >= 0.35,
            f => f.CurrentOdds <= 3,
            "Arena specialist AND favorite"));

        report.Interactions.Add(await TestInteraction(validData,
            "Inexperienced x Favorite",
            f => f.TotalAppearances <= 50,
            f => f.CurrentOdds <= 2,
            "Few appearances AND favorite (risky?)"));

        // ════════════════════════════════════════════════════════
        // CATEGORY 5: Arena-Specific Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("\n📊 CATEGORY 5: Arena-Specific Interactions");

        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            report.Interactions.Add(await TestInteraction(validData,
                $"Arena {arenaId} x Food Bonus",
                f => f.ArenaId == arenaId,
                f => f.FoodAdjustment >= 1,
                $"Arena {arenaId} AND positive food"));

            report.Interactions.Add(await TestInteraction(validData,
                $"Arena {arenaId} x Front Position",
                f => f.ArenaId == arenaId,
                f => f.Position <= 1,
                $"Arena {arenaId} AND front position"));
        }

        // ════════════════════════════════════════════════════════
        // CATEGORY 6: Three-Way Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("\n📊 CATEGORY 6: Three-Way Interactions");

        report.Interactions.Add(await TestThreeWayInteraction(validData,
            "Food x Position x Strength",
            f => f.FoodAdjustment >= 1,
            f => f.Position <= 1,
            f => f.Strength >= 60,
            "Positive food AND front position AND high strength"));

        report.Interactions.Add(await TestThreeWayInteraction(validData,
            "Food x Favorite x Arena Specialist",
            f => f.FoodAdjustment >= 1,
            f => f.CurrentOdds <= 2,
            f => f.ArenaWinRate >= 0.35,
            "Positive food AND favorite AND arena specialist"));

        report.Interactions.Add(await TestThreeWayInteraction(validData,
            "Front Position x Hot Streak x Strong Rivals",
            f => f.Position <= 1,
            f => f.RecentWinRate >= 0.35,
            f => f.AvgRivalStrength >= 50,
            "Front position AND hot streak AND strong rivals"));

        report.Interactions.Add(await TestThreeWayInteraction(validData,
            "Undervalued x High Strength x Good vs Rivals",
            f => f.CurrentOdds >= 5,
            f => f.Strength >= 55,
            f => f.WinRateVsCurrentRivals >= 0.3,
            "Undervalued AND strong AND beats rivals"));

        // ════════════════════════════════════════════════════════
        // CATEGORY 7: Synergistic (Positive) Interactions
        // ════════════════════════════════════════════════════════
        Console.WriteLine("\n📊 CATEGORY 7: Testing for Synergistic Interactions");

        report.Interactions.Add(await TestInteraction(validData,
            "Food x Position 3 (Synergy?)",
            f => f.FoodAdjustment >= 1,
            f => f.Position == 3,
            "Positive food AND position 3"));

        report.Interactions.Add(await TestInteraction(validData,
            "Moderate Odds x Arena Specialist",
            f => f.CurrentOdds >= 3 && f.CurrentOdds <= 6,
            f => f.ArenaWinRate >= 0.3,
            "Moderate odds AND arena specialist"));

        // ════════════════════════════════════════════════════════
        // SUMMARIZE AND RANK
        // ════════════════════════════════════════════════════════
        ClassifyInteractions(report);
        DisplayInteractionReport(report);
        SaveInteractionReport(report);

        return report;
    }

    private async Task<InteractionAnalysisEffect> TestInteraction(
        List<PirateFeatureRecord> data,
        string name,
        Func<PirateFeatureRecord, bool> condition1,
        Func<PirateFeatureRecord, bool> condition2,
        string description)
    {
        // Group 1: Neither condition
        var group00 = data.Where(f => !condition1(f) && !condition2(f)).ToList();
        var winRate00 = group00.Any() ? group00.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        // Group 2: Only condition 1
        var group10 = data.Where(f => condition1(f) && !condition2(f)).ToList();
        var winRate10 = group10.Any() ? group10.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        // Group 3: Only condition 2
        var group01 = data.Where(f => !condition1(f) && condition2(f)).ToList();
        var winRate01 = group01.Any() ? group01.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        // Group 4: Both conditions
        var group11 = data.Where(f => condition1(f) && condition2(f)).ToList();
        var winRate11 = group11.Any() ? group11.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        // Calculate main effects
        var effect1 = (winRate10 + winRate11) / 2 - (winRate00 + winRate01) / 2;
        var effect2 = (winRate01 + winRate11) / 2 - (winRate00 + winRate10) / 2;

        // Calculate interaction effect
        var expectedAdditive = winRate00 + effect1 + effect2;
        var interactionStrength = winRate11 - expectedAdditive;

        // Statistical significance
        var n11 = Math.Max(1, group11.Count);
        var se = Math.Sqrt(winRate11 * (1 - winRate11) / n11 + 0.0001);
        var zScore = Math.Abs(interactionStrength) / se;
        var pValue = 2 * (1 - NormalCDF(zScore));

        var icon = interactionStrength > 0.02 ? "✅" : interactionStrength < -0.02 ? "⚠️" : "➖";
        Console.WriteLine($"   {icon} {name}: {interactionStrength:+0.0%;-0.0%} (n={group11.Count}, p={pValue:F3})");

        return new InteractionAnalysisEffect
        {
            Name = name,
            Description = description,
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1,
            Effect2Alone = effect2,
            CombinedEffect = winRate11 - winRate00,
            ExpectedAdditiveEffect = expectedAdditive - winRate00,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            PValue = pValue,
            IsSignificant = pValue < 0.05 && Math.Abs(interactionStrength) > 0.02,
            IsAntagonistic = interactionStrength < -0.02 && pValue < 0.05,
            IsSynergistic = interactionStrength > 0.02 && pValue < 0.05
        };
    }

    private async Task<InteractionAnalysisEffect> TestThreeWayInteraction(
        List<PirateFeatureRecord> data,
        string name,
        Func<PirateFeatureRecord, bool> condition1,
        Func<PirateFeatureRecord, bool> condition2,
        Func<PirateFeatureRecord, bool> condition3,
        string description)
    {
        // All three conditions
        var groupAll = data.Where(f => condition1(f) && condition2(f) && condition3(f)).ToList();
        var winRateAll = groupAll.Any() ? groupAll.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        // Baseline: none of the conditions
        var groupNone = data.Where(f => !condition1(f) && !condition2(f) && !condition3(f)).ToList();
        var winRateNone = groupNone.Any() ? groupNone.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        // Individual effects
        var group1 = data.Where(f => condition1(f) && !condition2(f) && !condition3(f)).ToList();
        var winRate1 = group1.Any() ? group1.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        var group2 = data.Where(f => !condition1(f) && condition2(f) && !condition3(f)).ToList();
        var winRate2 = group2.Any() ? group2.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        var group3 = data.Where(f => !condition1(f) && !condition2(f) && condition3(f)).ToList();
        var winRate3 = group3.Any() ? group3.Average(f => f.IsWinner == true ? 1.0 : 0.0) : 0.25;

        var effect1 = winRate1 - winRateNone;
        var effect2 = winRate2 - winRateNone;
        var effect3 = winRate3 - winRateNone;

        var expectedAdditive = winRateNone + effect1 + effect2 + effect3;
        var interactionStrength = winRateAll - expectedAdditive;

        var n = Math.Max(1, groupAll.Count);
        var se = Math.Sqrt(winRateAll * (1 - winRateAll) / n + 0.0001);
        var zScore = Math.Abs(interactionStrength) / se;
        var pValue = 2 * (1 - NormalCDF(zScore));

        var icon = interactionStrength > 0.03 ? "✅" : interactionStrength < -0.03 ? "⚠️" : "➖";
        Console.WriteLine($"   {icon} {name}: {interactionStrength:+0.0%;-0.0%} (n={groupAll.Count}, p={pValue:F3})");

        return new InteractionAnalysisEffect
        {
            Name = name,
            Description = description,
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1,
            Effect2Alone = effect2,
            CombinedEffect = winRateAll - winRateNone,
            ExpectedAdditiveEffect = expectedAdditive - winRateNone,
            Group11Count = groupAll.Count,
            Group00Count = groupNone.Count,
            WinRate00 = winRateNone,
            WinRate11 = winRateAll,
            PValue = pValue,
            IsSignificant = pValue < 0.1 && Math.Abs(interactionStrength) > 0.03,
            IsAntagonistic = interactionStrength < -0.03 && pValue < 0.1,
            IsSynergistic = interactionStrength > 0.03 && pValue < 0.1,
            IsThreeWay = true
        };
    }

    private void ClassifyInteractions(InteractionAnalysisReport report)
    {
        report.AntagonisticInteractions = report.Interactions
            .Where(i => i.IsAntagonistic)
            .OrderBy(i => i.InteractionStrength)
            .ToList();

        report.SynergisticInteractions = report.Interactions
            .Where(i => i.IsSynergistic)
            .OrderByDescending(i => i.InteractionStrength)
            .ToList();

        report.NeutralInteractions = report.Interactions
            .Where(i => !i.IsAntagonistic && !i.IsSynergistic)
            .ToList();
    }

    private void DisplayInteractionReport(InteractionAnalysisReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 INTERACTION ANALYSIS SUMMARY");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine("⚠️ ANTAGONISTIC INTERACTIONS (factors cancel out):");
        Console.WriteLine("   Use these to REDUCE predicted probability\n");
        foreach (var interaction in report.AntagonisticInteractions.Take(10))
        {
            Console.WriteLine($"   ❌ {interaction.Name}");
            Console.WriteLine(
                $"      Effect: {interaction.InteractionStrength:+0.0%;-0.0%} | p={interaction.PValue:F3}");
            Console.WriteLine($"      {interaction.Description}");
            Console.WriteLine(
                $"      Individual effects: {interaction.Effect1Alone:+0.0%;-0.0%} + {interaction.Effect2Alone:+0.0%;-0.0%}");
            Console.WriteLine(
                $"      Combined actual: {interaction.CombinedEffect:+0.0%;-0.0%} (expected: {interaction.ExpectedAdditiveEffect:+0.0%;-0.0%})");
            Console.WriteLine();
        }

        Console.WriteLine("\n✅ SYNERGISTIC INTERACTIONS (factors amplify each other):");
        Console.WriteLine("   Use these to INCREASE predicted probability\n");
        foreach (var interaction in report.SynergisticInteractions.Take(10))
        {
            Console.WriteLine($"   ✅ {interaction.Name}");
            Console.WriteLine(
                $"      Effect: {interaction.InteractionStrength:+0.0%;-0.0%} | p={interaction.PValue:F3}");
            Console.WriteLine($"      {interaction.Description}");
            Console.WriteLine(
                $"      Individual effects: {interaction.Effect1Alone:+0.0%;-0.0%} + {interaction.Effect2Alone:+0.0%;-0.0%}");
            Console.WriteLine(
                $"      Combined actual: {interaction.CombinedEffect:+0.0%;-0.0%} (expected: {interaction.ExpectedAdditiveEffect:+0.0%;-0.0%})");
            Console.WriteLine();
        }

        Console.WriteLine("\n📋 RECOMMENDED MODEL ADJUSTMENTS:");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        if (report.AntagonisticInteractions.Any())
        {
            Console.WriteLine("   PENALTY FEATURES (reduce probability when present):");
            foreach (var ant in report.AntagonisticInteractions.Take(5))
                Console.WriteLine($"   • {ant.Name}_Penalty = {Math.Abs(ant.InteractionStrength):P1}");
        }

        if (report.SynergisticInteractions.Any())
        {
            Console.WriteLine("\n   BONUS FEATURES (increase probability when present):");
            foreach (var syn in report.SynergisticInteractions.Take(5))
                Console.WriteLine($"   • {syn.Name}_Bonus = {syn.InteractionStrength:P1}");
        }
    }

    private void SaveInteractionReport(InteractionAnalysisReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"interaction_analysis_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);
        Console.WriteLine($"\n📄 Interaction analysis saved to {fileName}");
    }

    private double NormalCDF(double x)
    {
        return 0.5 * (1 + Erf(x / Math.Sqrt(2)));
    }

    private double Erf(double x)
    {
        double a1 = 0.254829592, a2 = -0.284496736, a3 = 1.421413741;
        double a4 = -1.453152027, a5 = 1.061405429, p = 0.3275911;
        var sign = x < 0 ? -1 : 1;
        x = Math.Abs(x);
        var t = 1.0 / (1.0 + p * x);
        var y = 1.0 - ((((a5 * t + a4) * t + a3) * t + a2) * t + a1) * t * Math.Exp(-x * x);
        return sign * y;
    }
}