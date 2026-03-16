using Microsoft.ML.Data;

namespace NFCBets.Utilities.Models;

/// <summary>
/// Feature record for ML.NET model training and prediction.
/// All feature properties must be float for ML.NET concatenation.
/// </summary>
public class MlPirateFeature
{
    #region Identifiers (Not Used as Features)

    [NoColumn]
    public int PirateId { get; set; }

    [NoColumn]
    public int RoundId { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // BASE FEATURES
    // ═══════════════════════════════════════════════════

    #region Core Features

    /// <summary>
    /// Position/seat in arena (1-4)
    /// </summary>
    public float Position { get; set; }

    /// <summary>
    /// Arena identifier (1-5)
    /// </summary>
    public float ArenaId { get; set; }

    /// <summary>
    /// Current betting odds (minimum 2:1)
    /// </summary>
    public float CurrentOdds { get; set; }

    /// <summary>
    /// Opening betting odds
    /// </summary>
    public float OpeningOdds { get; set; }

    /// <summary>
    /// Food adjustment modifier for this round
    /// </summary>
    public float FoodAdjustment { get; set; }

    /// <summary>
    /// Pirate's base strength stat
    /// </summary>
    public float Strength { get; set; }

    /// <summary>
    /// Pirate's weight stat
    /// </summary>
    public float Weight { get; set; }

    #endregion

    #region Historical Performance Features

    /// <summary>
    /// Overall historical win rate
    /// </summary>
    public float HistoricalWinRate { get; set; }

    /// <summary>
    /// Total number of appearances
    /// </summary>
    public float TotalAppearances { get; set; }

    /// <summary>
    /// Win rate in this specific arena
    /// </summary>
    public float ArenaWinRate { get; set; }

    /// <summary>
    /// Recent form win rate (last 20 matches)
    /// </summary>
    public float RecentWinRate { get; set; }

    /// <summary>
    /// Win rate against current opponents
    /// </summary>
    public float WinRateVsCurrentRivals { get; set; }

    /// <summary>
    /// Number of previous matches against current rivals
    /// </summary>
    public float MatchesVsCurrentRivals { get; set; }

    /// <summary>
    /// Average strength of current opponents
    /// </summary>
    public float AvgRivalStrength { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // DERIVED FEATURES
    // ═══════════════════════════════════════════════════

    #region Derived Features

    /// <summary>
    /// Change in odds from opening to current (CurrentOdds - OpeningOdds)
    /// </summary>
    public float OddsMovement { get; set; }

    /// <summary>
    /// Odds movement as percentage of opening odds
    /// </summary>
    public float OddsMovementPercent { get; set; }

    /// <summary>
    /// Implied probability from current odds (1 / CurrentOdds)
    /// </summary>
    public float ImpliedProbability { get; set; }

    /// <summary>
    /// Strength relative to arena average
    /// </summary>
    public float RelativeStrength { get; set; }

    /// <summary>
    /// Effective strength after food adjustment
    /// </summary>
    public float EffectiveStrength { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // BINARY INDICATOR FEATURES
    // ═══════════════════════════════════════════════════

    #region Binary Indicators

    /// <summary>
    /// Whether this pirate has the lowest odds in arena (favorite)
    /// </summary>
    public float IsOddsFavorite { get; set; }

    /// <summary>
    /// Whether this pirate has the highest strength in arena
    /// </summary>
    public float IsStrengthFavorite { get; set; }

    /// <summary>
    /// Whether this pirate has the highest effective strength (strength + food)
    /// </summary>
    public float IsEffectiveStrengthFavorite { get; set; }

    /// <summary>
    /// Whether odds have shortened (become more favorable)
    /// </summary>
    public float HasOddsShortened { get; set; }

    /// <summary>
    /// Whether odds have drifted (become less favorable)
    /// </summary>
    public float HasOddsDrifted { get; set; }

    /// <summary>
    /// Whether food adjustment is positive
    /// </summary>
    public float HasPositiveFoodAdjustment { get; set; }

    /// <summary>
    /// Whether food adjustment is negative
    /// </summary>
    public float HasNegativeFoodAdjustment { get; set; }

    /// <summary>
    /// Whether pirate is in position 1
    /// </summary>
    public float IsPositionOne { get; set; }

    /// <summary>
    /// Whether pirate is in position 2
    /// </summary>
    public float IsPositionTwo { get; set; }

    /// <summary>
    /// Whether pirate is in position 3
    /// </summary>
    public float IsPositionThree { get; set; }

    /// <summary>
    /// Whether pirate is in position 4
    /// </summary>
    public float IsPositionFour { get; set; }

    /// <summary>
    /// Whether pirate is undervalued (high strength, high odds)
    /// </summary>
    public float IsUndervalued { get; set; }

    /// <summary>
    /// Whether pirate is on a hot streak (recent win rate > historical)
    /// </summary>
    public float IsHotStreak { get; set; }

    /// <summary>
    /// Whether pirate is an arena specialist (arena win rate > historical)
    /// </summary>
    public float IsArenaSpecialist { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // ARENA-SPECIFIC FEATURES (One-Hot Encoding)
    // ═══════════════════════════════════════════════════

    #region Arena Indicators

    /// <summary>
    /// Arena 1 (Shipwreck) indicator
    /// </summary>
    public float IsArenaShipwreck { get; set; }

    /// <summary>
    /// Arena 2 (Lagoon) indicator
    /// </summary>
    public float IsArenaLagoon { get; set; }

    /// <summary>
    /// Arena 3 (Treasure Island) indicator
    /// </summary>
    public float IsArenaTreasureIsland { get; set; }

    /// <summary>
    /// Arena 4 (Hidden Cove) indicator
    /// </summary>
    public float IsArenaHiddenCove { get; set; }

    /// <summary>
    /// Arena 5 (Harpoon Harry's) indicator
    /// </summary>
    public float IsArenaHarpoonHarrys { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // ANTAGONISTIC INTERACTION PENALTIES
    // Apply when conditions cancel each other out
    // ═══════════════════════════════════════════════════

    #region Antagonistic Interactions

    /// <summary>
    /// Food × Front Position - food advantage reduced in front positions
    /// </summary>
    public float PenaltyFoodPosition { get; set; }

    /// <summary>
    /// Food × Favorite Status - food advantage already priced in
    /// </summary>
    public float PenaltyFoodFavorite { get; set; }

    /// <summary>
    /// High Strength × Front Position - strength advantage reduced in front
    /// </summary>
    public float PenaltyStrengthPosition { get; set; }

    /// <summary>
    /// Strength × Weak Competition - strength less impactful vs weak field
    /// </summary>
    public float PenaltyStrengthWeakRivals { get; set; }

    /// <summary>
    /// Favorite × Few Appearances - inexperienced favorite (risky)
    /// </summary>
    public float PenaltyFavoriteInexperienced { get; set; }

    /// <summary>
    /// Low Strength × Favorite - potentially overvalued
    /// </summary>
    public float PenaltyLowStrengthFavorite { get; set; }

    /// <summary>
    /// Odds Shortened × Low Strength - smart money wrong?
    /// </summary>
    public float PenaltyOddsShortenedLowStrength { get; set; }

    /// <summary>
    /// Arena Specialist × Poor Recent Form - past success not current
    /// </summary>
    public float PenaltyArenaSpecialistColdStreak { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // SYNERGISTIC INTERACTION BONUSES
    // Apply when conditions amplify each other
    // ═══════════════════════════════════════════════════

    #region Synergistic Interactions

    /// <summary>
    /// Undervalued × High Strength - hidden value
    /// </summary>
    public float BonusUndervaluedStrong { get; set; }

    /// <summary>
    /// Arena Specialist × Moderate Odds - value in expertise
    /// </summary>
    public float BonusArenaSpecialistModerateOdds { get; set; }

    /// <summary>
    /// Hot Streak × Good vs Rivals - momentum + matchup
    /// </summary>
    public float BonusHotStreakBeatsRivals { get; set; }

    /// <summary>
    /// Food × Position 3 - optimal position for food advantage
    /// </summary>
    public float BonusFoodPositionThree { get; set; }

    /// <summary>
    /// Odds Shortened × High Strength - smart money correct
    /// </summary>
    public float BonusOddsShortenedStrong { get; set; }

    /// <summary>
    /// Favorite × Arena Specialist - double confidence
    /// </summary>
    public float BonusFavoriteArenaSpecialist { get; set; }

    /// <summary>
    /// High Strength × Positive Food - compounding advantages
    /// </summary>
    public float BonusStrengthPlusFood { get; set; }

    /// <summary>
    /// Hot Streak × Favorite - form confirmed by odds
    /// </summary>
    public float BonusHotStreakFavorite { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // THREE-WAY INTERACTIONS
    // ═══════════════════════════════════════════════════

    #region Three-Way Interactions

    /// <summary>
    /// Food × Position × Strength - triple factor
    /// </summary>
    public float ThreeWayFoodPositionStrength { get; set; }

    /// <summary>
    /// Undervalued × Strong × Beats Rivals - hidden gem
    /// </summary>
    public float ThreeWayUndervaluedStrongBeatsRivals { get; set; }

    /// <summary>
    /// Favorite × Arena Specialist × Hot Streak - full confidence
    /// </summary>
    public float ThreeWayFavoriteSpecialistHotStreak { get; set; }

    /// <summary>
    /// High Strength × Food × Position 3 - optimal setup
    /// </summary>
    public float ThreeWayStrengthFoodPositionThree { get; set; }

    #endregion

    // ═══════════════════════════════════════════════════
    // LABEL
    // ═══════════════════════════════════════════════════

    /// <summary>
    /// Target label: whether this pirate won
    /// </summary>
    [ColumnName("Label")]
    public bool Won { get; set; }
}