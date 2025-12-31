using NFCBets.Causal.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Utilities;

public static class InteractionCalculator
{
    public static void ApplyInteractionFeatures(
        MlPirateFeature feature,
        PirateFeatureRecord record,
        InteractionAnalysisReport? interactionReport = null)
    {
        var antagonistic = interactionReport?.AntagonisticInteractions ?? new List<InteractionAnalysisEffect>();
        var synergistic = interactionReport?.SynergisticInteractions ?? new List<InteractionAnalysisEffect>();

        // ═══════════════════════════════════════════════════
        // ANTAGONISTIC PENALTIES
        // These reduce win probability when conditions co-occur
        // ═══════════════════════════════════════════════════

        // Food × Front Position
        feature.Penalty_FoodPosition = CalculatePenalty(
            record.FoodAdjustment >= 1,
            record.Position <= 1,
            "Food x Position",
            antagonistic,
            0.045f);

        // Food × Favorite Status
        feature.Penalty_FoodFavorite = CalculatePenalty(
            record.FoodAdjustment >= 1,
            record.CurrentOdds <= 2,
            "Food x Favorite",
            antagonistic,
            0.045f);

        // High Strength × Front Position
        feature.Penalty_StrengthPosition = CalculatePenalty(
            record.Strength >= 60,
            record.Position <= 1,
            "Front Position x High Strength",
            antagonistic,
            0.048f);

        // Strength × Weak Rivals
        feature.Penalty_StrengthWeakRivals = CalculatePenalty(
            record.Strength >= 60,
            record.AvgRivalStrength < 40,
            "Strength x Weak Rivals",
            antagonistic,
            0.03f);

        // Favorite × Inexperienced
        feature.Penalty_FavoriteInexperienced = CalculatePenalty(
            record.CurrentOdds <= 2,
            record.TotalAppearances <= 50,
            "Inexperienced x Favorite",
            antagonistic,
            0.04f);

        // Low Strength × Favorite (overvalued)
        feature.Penalty_LowStrengthFavorite = CalculatePenalty(
            record.Strength <= 40,
            record.CurrentOdds <= 2,
            "Low Strength x Favorite",
            antagonistic,
            0.05f);

        // ═══════════════════════════════════════════════════
        // SYNERGISTIC BONUSES
        // These increase win probability when conditions co-occur
        // ═══════════════════════════════════════════════════

        // Undervalued × High Strength
        feature.Bonus_UndervaluedStrong = CalculateBonus(
            record.CurrentOdds >= 5,
            record.Strength >= 55,
            "High Strength x Undervalued",
            synergistic,
            0.04f);

        // Arena Specialist × Moderate Odds
        feature.Bonus_ArenaSpecialistModerateOdds = CalculateBonus(
            record.ArenaWinRate >= 0.3,
            record.CurrentOdds >= 3 && record.CurrentOdds <= 6,
            "Moderate Odds x Arena Specialist",
            synergistic,
            0.035f);

        // Hot Streak × Beats Rivals
        feature.Bonus_HotStreakBeatsRivals = CalculateBonus(
            record.RecentWinRate >= 0.35,
            record.WinRateVsCurrentRivals >= 0.3,
            "Hot Streak x Good vs Rivals",
            synergistic,
            0.04f);

        // Food × Position 3 (often synergistic)
        feature.Bonus_FoodPosition3 = CalculateBonus(
            record.FoodAdjustment >= 1,
            record.Position == 3,
            "Food x Position 3",
            synergistic,
            0.03f);

        // ═══════════════════════════════════════════════════
        // THREE-WAY INTERACTIONS
        // ═══════════════════════════════════════════════════

        // Food × Position × Strength
        feature.ThreeWay_FoodPositionStrength = CalculateThreeWayInteraction(
            record.FoodAdjustment >= 1,
            record.Position <= 1,
            record.Strength >= 60,
            "Food x Position x Strength",
            interactionReport,
            -0.05f); // Usually antagonistic

        // Undervalued × Strong × Beats Rivals
        feature.ThreeWay_UndervaluedStrongBeatsRivals = CalculateThreeWayInteraction(
            record.CurrentOdds >= 5,
            record.Strength >= 55,
            record.WinRateVsCurrentRivals >= 0.3,
            "Undervalued x High Strength x Good vs Rivals",
            interactionReport,
            0.06f); // Usually synergistic
    }

    private static float CalculatePenalty(
        bool condition1,
        bool condition2,
        string interactionName,
        List<InteractionAnalysisEffect> antagonisticInteractions,
        float defaultStrength)
    {
        if (!condition1 || !condition2)
            return 0f;

        var discovered = antagonisticInteractions
            .FirstOrDefault(i => i.Name.Contains(interactionName, StringComparison.OrdinalIgnoreCase));

        if (discovered != null) return (float)Math.Abs(discovered.InteractionStrength);

        return defaultStrength;
    }

    private static float CalculateBonus(
        bool condition1,
        bool condition2,
        string interactionName,
        List<InteractionAnalysisEffect> synergisticInteractions,
        float defaultStrength)
    {
        if (!condition1 || !condition2)
            return 0f;

        var discovered = synergisticInteractions
            .FirstOrDefault(i => i.Name.Contains(interactionName, StringComparison.OrdinalIgnoreCase));

        if (discovered != null) return (float)discovered.InteractionStrength;

        return defaultStrength;
    }

    private static float CalculateThreeWayInteraction(
        bool condition1,
        bool condition2,
        bool condition3,
        string interactionName,
        InteractionAnalysisReport? report,
        float defaultStrength)
    {
        if (!condition1 || !condition2 || !condition3)
            return 0f;

        if (report == null)
            return defaultStrength;

        // Look in both antagonistic and synergistic lists
        var discovered = report.AntagonisticInteractions
            .Concat(report.SynergisticInteractions)
            .FirstOrDefault(i => i.Name.Contains(interactionName, StringComparison.OrdinalIgnoreCase) && i.IsThreeWay);

        if (discovered != null) return (float)discovered.InteractionStrength;

        return defaultStrength;
    }

    /// <summary>
    ///     Calculate the net interaction adjustment for a pirate
    ///     Positive = boost probability, Negative = reduce probability
    /// </summary>
    public static float CalculateNetInteractionAdjustment(MlPirateFeature feature)
    {
        // Sum all penalties (negative effect)
        var penalties = feature.Penalty_FoodPosition
                        + feature.Penalty_FoodFavorite
                        + feature.Penalty_StrengthPosition
                        + feature.Penalty_StrengthWeakRivals
                        + feature.Penalty_FavoriteInexperienced
                        + feature.Penalty_LowStrengthFavorite;

        // Sum all bonuses (positive effect)
        var bonuses = feature.Bonus_UndervaluedStrong
                      + feature.Bonus_ArenaSpecialistModerateOdds
                      + feature.Bonus_HotStreakBeatsRivals
                      + feature.Bonus_FoodPosition3;

        // Three-way interactions (can be positive or negative)
        var threeWay = feature.ThreeWay_FoodPositionStrength
                       + feature.ThreeWay_UndervaluedStrongBeatsRivals;

        return bonuses - penalties + threeWay;
    }

    /// <summary>
    ///     Apply interaction adjustments directly to a probability
    /// </summary>
    public static float AdjustProbability(float baseProbability, MlPirateFeature feature)
    {
        var adjustment = CalculateNetInteractionAdjustment(feature);

        // Apply as multiplicative adjustment
        var adjusted = baseProbability * (1 + adjustment);

        return Math.Clamp(adjusted, 0.01f, 0.99f);
    }
}