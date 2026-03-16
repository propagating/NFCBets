using NFCBets.Causal.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Utilities;

public static class InteractionCalculator
{
    /// <summary>
    /// Fully populates all derived, binary, and interaction features on MlPirateFeature
    /// </summary>
    public static void ApplyAllFeatures(
        MlPirateFeature feature,
        PirateFeatureRecord record,
        List<PirateFeatureRecord> arenaContext,
        InteractionAnalysisReport? interactionReport = null)
    {
        // First populate derived and binary features
        ApplyDerivedFeatures(feature, record, arenaContext);
        
        // Then apply interaction features
        ApplyInteractionFeatures(feature, record, interactionReport);
    }

    /// <summary>
    /// Populates derived and binary indicator features based on arena context
    /// </summary>
    public static void ApplyDerivedFeatures(
        MlPirateFeature feature,
        PirateFeatureRecord record,
        List<PirateFeatureRecord> arenaContext)
    {
        // Calculate arena-relative values
        var minOdds = arenaContext.Min(x => x.CurrentOdds);
        var maxStrength = arenaContext.Max(x => x.Strength);
        var avgStrength = arenaContext.Average(x => x.Strength);
        var maxEffectiveStrength = arenaContext.Max(x => x.Strength + x.FoodAdjustment);
        var effectiveStrength = record.Strength + record.FoodAdjustment;

        // Opening odds (use current if not available)
        var openingOdds = record.OpeningOdds > 0 ? record.OpeningOdds : record.CurrentOdds;
        
        // ═══════════════════════════════════════════════════
        // DERIVED FEATURES
        // ═══════════════════════════════════════════════════
        
        feature.OpeningOdds = openingOdds;
        feature.OddsMovement = record.CurrentOdds - openingOdds;
        feature.OddsMovementPercent = openingOdds > 0 
            ? (float)(record.CurrentOdds - openingOdds) / openingOdds 
            : 0f;
        feature.ImpliedProbability = 1f / Math.Max(2f, record.CurrentOdds);
        feature.RelativeStrength = avgStrength > 0 
            ? record.Strength / avgStrength 
            : 1f;
        feature.EffectiveStrength = effectiveStrength;

        // ═══════════════════════════════════════════════════
        // BINARY INDICATOR FEATURES
        // ═══════════════════════════════════════════════════
        
        // Favorite indicators
        feature.IsOddsFavorite = record.CurrentOdds == minOdds ? 1f : 0f;
        feature.IsStrengthFavorite = Math.Abs(record.Strength - maxStrength) < 0.001f ? 1f : 0f;
        feature.IsEffectiveStrengthFavorite = Math.Abs(effectiveStrength - maxEffectiveStrength) < 0.001f ? 1f : 0f;

        // Odds movement indicators
        feature.HasOddsShortened = feature.OddsMovement < 0 ? 1f : 0f;
        feature.HasOddsDrifted = feature.OddsMovement > 0 ? 1f : 0f;

        // Food adjustment indicators
        feature.HasPositiveFoodAdjustment = record.FoodAdjustment > 0 ? 1f : 0f;
        feature.HasNegativeFoodAdjustment = record.FoodAdjustment < 0 ? 1f : 0f;

        // Position indicators (one-hot)
        feature.IsPositionOne = record.Position == 1 ? 1f : 0f;
        feature.IsPositionTwo = record.Position == 2 ? 1f : 0f;
        feature.IsPositionThree = record.Position == 3 ? 1f : 0f;
        feature.IsPositionFour = record.Position == 4 ? 1f : 0f;

        // Derived condition indicators
        feature.IsUndervalued = (record.CurrentOdds > 3 && record.Strength >= avgStrength * 1.1f) ? 1f : 0f;
        feature.IsHotStreak = (record.RecentWinRate > record.HistoricalWinRate * 1.2) ? 1f : 0f;
        feature.IsArenaSpecialist = (record.ArenaWinRate > record.HistoricalWinRate * 1.2) ? 1f : 0f;

        // ═══════════════════════════════════════════════════
        // ARENA INDICATORS (One-Hot Encoding)
        // ═══════════════════════════════════════════════════
        
        feature.IsArenaShipwreck = record.ArenaId == 1 ? 1f : 0f;
        feature.IsArenaLagoon = record.ArenaId == 2 ? 1f : 0f;
        feature.IsArenaTreasureIsland = record.ArenaId == 3 ? 1f : 0f;
        feature.IsArenaHiddenCove = record.ArenaId == 4 ? 1f : 0f;
        feature.IsArenaHarpoonHarrys = record.ArenaId == 5 ? 1f : 0f;
    }

    /// <summary>
    /// Applies interaction features (penalties, bonuses, three-way interactions)
    /// </summary>
    public static void ApplyInteractionFeatures(
        MlPirateFeature feature,
        PirateFeatureRecord record,
        InteractionAnalysisReport? interactionReport = null)
    {
        var antagonistic = interactionReport?.AntagonisticInteractions ?? new List<InteractionAnalysisEffect>();
        var synergistic = interactionReport?.SynergisticInteractions ?? new List<InteractionAnalysisEffect>();

        // Pre-calculate common conditions
        var isFrontPosition = record.Position <= 2;
        var isHighStrength = record.Strength >= 60;
        var isLowStrength = record.Strength <= 40;
        var isFavorite = record.CurrentOdds <= 2;
        var isUndervalued = record.CurrentOdds >= 5;
        var isModerateOdds = record.CurrentOdds >= 3 && record.CurrentOdds <= 6;
        var hasPositiveFood = record.FoodAdjustment >= 1;
        var isInexperienced = record.TotalAppearances <= 50;
        var isArenaSpecialist = record.ArenaWinRate >= 0.3;
        var isHotStreak = record.RecentWinRate >= 0.35;
        var isColdStreak = record.RecentWinRate < record.HistoricalWinRate * 0.8;
        var beatsRivals = record.WinRateVsCurrentRivals >= 0.3;
        var hasOddsShortened = feature.HasOddsShortened > 0.5f;
        var hasOddsDrifted = feature.HasOddsDrifted > 0.5f;

        // ═══════════════════════════════════════════════════
        // ANTAGONISTIC PENALTIES
        // These reduce win probability when conditions co-occur
        // ═══════════════════════════════════════════════════

        // Food × Front Position
        feature.PenaltyFoodPosition = CalculatePenalty(
            hasPositiveFood,
            isFrontPosition,
            "Food x Position",
            antagonistic,
            0.045f);

        // Food × Favorite Status
        feature.PenaltyFoodFavorite = CalculatePenalty(
            hasPositiveFood,
            isFavorite,
            "Food x Favorite",
            antagonistic,
            0.045f);

        // High Strength × Front Position
        feature.PenaltyStrengthPosition = CalculatePenalty(
            isHighStrength,
            isFrontPosition,
            "Front Position x High Strength",
            antagonistic,
            0.048f);

        // Strength × Weak Rivals
        feature.PenaltyStrengthWeakRivals = CalculatePenalty(
            isHighStrength,
            record.AvgRivalStrength < 40,
            "Strength x Weak Rivals",
            antagonistic,
            0.03f);

        // Favorite × Inexperienced
        feature.PenaltyFavoriteInexperienced = CalculatePenalty(
            isFavorite,
            isInexperienced,
            "Inexperienced x Favorite",
            antagonistic,
            0.04f);

        // Low Strength × Favorite (overvalued)
        feature.PenaltyLowStrengthFavorite = CalculatePenalty(
            isLowStrength,
            isFavorite,
            "Low Strength x Favorite",
            antagonistic,
            0.05f);

        // Odds Shortened × Low Strength
        feature.PenaltyOddsShortenedLowStrength = CalculatePenalty(
            hasOddsShortened,
            isLowStrength,
            "Odds Shortened x Low Strength",
            antagonistic,
            0.035f);

        // Arena Specialist × Cold Streak
        feature.PenaltyArenaSpecialistColdStreak = CalculatePenalty(
            isArenaSpecialist,
            isColdStreak,
            "Arena Specialist x Cold Streak",
            antagonistic,
            0.03f);

        // ═══════════════════════════════════════════════════
        // SYNERGISTIC BONUSES
        // These increase win probability when conditions co-occur
        // ═══════════════════════════════════════════════════

        // Undervalued × High Strength
        feature.BonusUndervaluedStrong = CalculateBonus(
            isUndervalued,
            isHighStrength,
            "High Strength x Undervalued",
            synergistic,
            0.04f);

        // Arena Specialist × Moderate Odds
        feature.BonusArenaSpecialistModerateOdds = CalculateBonus(
            isArenaSpecialist,
            isModerateOdds,
            "Moderate Odds x Arena Specialist",
            synergistic,
            0.035f);

        // Hot Streak × Beats Rivals
        feature.BonusHotStreakBeatsRivals = CalculateBonus(
            isHotStreak,
            beatsRivals,
            "Hot Streak x Good vs Rivals",
            synergistic,
            0.04f);

        // Food × Position 3
        feature.BonusFoodPositionThree = CalculateBonus(
            hasPositiveFood,
            record.Position == 3,
            "Food x Position 3",
            synergistic,
            0.03f);

        // Odds Shortened × High Strength (smart money)
        feature.BonusOddsShortenedStrong = CalculateBonus(
            hasOddsShortened,
            isHighStrength,
            "Odds Shortened x High Strength",
            synergistic,
            0.035f);

        // Favorite × Arena Specialist
        feature.BonusFavoriteArenaSpecialist = CalculateBonus(
            isFavorite,
            isArenaSpecialist,
            "Favorite x Arena Specialist",
            synergistic,
            0.04f);

        // High Strength × Positive Food
        feature.BonusStrengthPlusFood = CalculateBonus(
            isHighStrength,
            hasPositiveFood,
            "High Strength x Food",
            synergistic,
            0.03f);

        // Hot Streak × Favorite
        feature.BonusHotStreakFavorite = CalculateBonus(
            isHotStreak,
            isFavorite,
            "Hot Streak x Favorite",
            synergistic,
            0.035f);

        // ═══════════════════════════════════════════════════
        // THREE-WAY INTERACTIONS
        // ═══════════════════════════════════════════════════

        // Food × Position × Strength (antagonistic)
        feature.ThreeWayFoodPositionStrength = CalculateThreeWayInteraction(
            hasPositiveFood,
            isFrontPosition,
            isHighStrength,
            "Food x Position x Strength",
            interactionReport,
            -0.05f);

        // Undervalued × Strong × Beats Rivals (synergistic)
        feature.ThreeWayUndervaluedStrongBeatsRivals = CalculateThreeWayInteraction(
            isUndervalued,
            isHighStrength,
            beatsRivals,
            "Undervalued x High Strength x Good vs Rivals",
            interactionReport,
            0.06f);

        // Favorite × Arena Specialist × Hot Streak (synergistic)
        feature.ThreeWayFavoriteSpecialistHotStreak = CalculateThreeWayInteraction(
            isFavorite,
            isArenaSpecialist,
            isHotStreak,
            "Favorite x Arena Specialist x Hot Streak",
            interactionReport,
            0.05f);

        // High Strength × Food × Position 3 (synergistic)
        feature.ThreeWayStrengthFoodPositionThree = CalculateThreeWayInteraction(
            isHighStrength,
            hasPositiveFood,
            record.Position == 3,
            "Strength x Food x Position 3",
            interactionReport,
            0.04f);
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

        if (discovered != null) 
            return (float)Math.Abs(discovered.InteractionStrength);

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

        if (discovered != null) 
            return (float)discovered.InteractionStrength;

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

        var discovered = report.AntagonisticInteractions
            .Concat(report.SynergisticInteractions)
            .FirstOrDefault(i => i.Name.Contains(interactionName, StringComparison.OrdinalIgnoreCase) && i.IsThreeWay);

        if (discovered != null) 
            return (float)discovered.InteractionStrength;

        return defaultStrength;
    }

    /// <summary>
    /// Calculate the net interaction adjustment for a pirate
    /// Positive = boost probability, Negative = reduce probability
    /// </summary>
    public static float CalculateNetInteractionAdjustment(MlPirateFeature feature)
    {
        // Sum all penalties (negative effect)
        var penalties = feature.PenaltyFoodPosition
                        + feature.PenaltyFoodFavorite
                        + feature.PenaltyStrengthPosition
                        + feature.PenaltyStrengthWeakRivals
                        + feature.PenaltyFavoriteInexperienced
                        + feature.PenaltyLowStrengthFavorite
                        + feature.PenaltyOddsShortenedLowStrength
                        + feature.PenaltyArenaSpecialistColdStreak;

        // Sum all bonuses (positive effect)
        var bonuses = feature.BonusUndervaluedStrong
                      + feature.BonusArenaSpecialistModerateOdds
                      + feature.BonusHotStreakBeatsRivals
                      + feature.BonusFoodPositionThree
                      + feature.BonusOddsShortenedStrong
                      + feature.BonusFavoriteArenaSpecialist
                      + feature.BonusStrengthPlusFood
                      + feature.BonusHotStreakFavorite;

        // Three-way interactions (can be positive or negative)
        var threeWay = feature.ThreeWayFoodPositionStrength
                       + feature.ThreeWayUndervaluedStrongBeatsRivals
                       + feature.ThreeWayFavoriteSpecialistHotStreak
                       + feature.ThreeWayStrengthFoodPositionThree;

        return bonuses - penalties + threeWay;
    }

    /// <summary>
    /// Apply interaction adjustments directly to a probability
    /// </summary>
    public static float AdjustProbability(float baseProbability, MlPirateFeature feature)
    {
        var adjustment = CalculateNetInteractionAdjustment(feature);
        var adjusted = baseProbability * (1 + adjustment);
        return Math.Clamp(adjusted, 0.01f, 0.99f);
    }

    /// <summary>
    /// Helper to get total penalty for models that aggregate
    /// </summary>
    public static float GetTotalPenalty(MlPirateFeature feature)
    {
        return feature.PenaltyFoodPosition 
               + feature.PenaltyFoodFavorite
               + feature.PenaltyStrengthPosition 
               + feature.PenaltyStrengthWeakRivals
               + feature.PenaltyFavoriteInexperienced 
               + feature.PenaltyLowStrengthFavorite
               + feature.PenaltyOddsShortenedLowStrength
               + feature.PenaltyArenaSpecialistColdStreak;
    }

    /// <summary>
    /// Helper to get total bonus for models that aggregate
    /// </summary>
    public static float GetTotalBonus(MlPirateFeature feature)
    {
        return feature.BonusUndervaluedStrong 
               + feature.BonusArenaSpecialistModerateOdds
               + feature.BonusHotStreakBeatsRivals 
               + feature.BonusFoodPositionThree
               + feature.BonusOddsShortenedStrong
               + feature.BonusFavoriteArenaSpecialist
               + feature.BonusStrengthPlusFood
               + feature.BonusHotStreakFavorite;
    }
}