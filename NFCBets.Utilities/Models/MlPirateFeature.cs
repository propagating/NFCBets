using Microsoft.ML.Data;

namespace NFCBets.Utilities.Models;

public class MlPirateFeature
{
    [NoColumn]
    public int PirateId { get; set; }
    [NoColumn]
    public int RoundId { get; set; }
    
    // ═══════════════════════════════════════════════════
    // BASE FEATURES
    // ═══════════════════════════════════════════════════
    public int Position { get; set; }
    public int ArenaId { get; set; }
    public float CurrentOdds { get; set; }
    public float FoodAdjustment { get; set; }
    public float Strength { get; set; }
    public float Weight { get; set; }
    public float HistoricalWinRate { get; set; }
    public int TotalAppearances { get; set; }
    public float ArenaWinRate { get; set; }
    public float RecentWinRate { get; set; }
    public float WinRateVsCurrentRivals { get; set; }
    public int MatchesVsCurrentRivals { get; set; }
    public float AvgRivalStrength { get; set; }

    // ═══════════════════════════════════════════════════
    // ANTAGONISTIC INTERACTION PENALTIES
    // Apply when conditions cancel each other out
    // ═══════════════════════════════════════════════════
    public float Penalty_FoodPosition { get; set; } // Food × Front Position
    public float Penalty_FoodFavorite { get; set; } // Food × Favorite Status
    public float Penalty_StrengthPosition { get; set; } // High Strength × Front Position
    public float Penalty_StrengthWeakRivals { get; set; } // Strength × Weak Competition
    public float Penalty_FavoriteInexperienced { get; set; } // Favorite × Few Appearances
    public float Penalty_LowStrengthFavorite { get; set; } // Low Strength × Favorite (overvalued)

    // ═══════════════════════════════════════════════════
    // SYNERGISTIC INTERACTION BONUSES
    // Apply when conditions amplify each other
    // ═══════════════════════════════════════════════════
    public float Bonus_UndervaluedStrong { get; set; } // Undervalued × High Strength
    public float Bonus_ArenaSpecialistModerateOdds { get; set; } // Arena Specialist × Moderate Odds
    public float Bonus_HotStreakBeatsRivals { get; set; } // Hot Streak × Good vs Rivals
    public float Bonus_FoodPosition3 { get; set; } // Food × Position 3 (if synergistic)

    // ═══════════════════════════════════════════════════
    // THREE-WAY INTERACTIONS
    // ═══════════════════════════════════════════════════
    public float ThreeWay_FoodPositionStrength { get; set; } // Food × Position × Strength
    public float ThreeWay_UndervaluedStrongBeatsRivals { get; set; } // Undervalued × Strong × Beats Rivals
    
    [ColumnName("Label")]
    public bool Won { get; set; }
    
}