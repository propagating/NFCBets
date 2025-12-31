using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class ArenaRoundFeatureImproved
{
    // Pirate 0 - Absolute features
    public float Pirate0_Strength { get; set; }
    public float Pirate0_Odds { get; set; }
    public float Pirate0_Food { get; set; }
    public float Pirate0_HistWin { get; set; }

    // Pirate 0 - Relative features
    public float Pirate0_StrengthDiff { get; set; }
    public float Pirate0_OddsRank { get; set; }
    public float Pirate0_FoodRank { get; set; }
    public float Pirate0_FoodPositionInteraction { get; set; }

    // Pirate 0 - Interaction controls
    public float Pirate0_InteractionPenalty { get; set; }
    public float Pirate0_InteractionBonus { get; set; }

    // Pirate 1 - Absolute features
    public float Pirate1_Strength { get; set; }
    public float Pirate1_Odds { get; set; }
    public float Pirate1_Food { get; set; }
    public float Pirate1_HistWin { get; set; }

    // Pirate 1 - Relative features
    public float Pirate1_StrengthDiff { get; set; }
    public float Pirate1_OddsRank { get; set; }
    public float Pirate1_FoodRank { get; set; }
    public float Pirate1_FoodPositionInteraction { get; set; }

    // Pirate 1 - Interaction controls
    public float Pirate1_InteractionPenalty { get; set; }
    public float Pirate1_InteractionBonus { get; set; }

    // Pirate 2 - Absolute features
    public float Pirate2_Strength { get; set; }
    public float Pirate2_Odds { get; set; }
    public float Pirate2_Food { get; set; }
    public float Pirate2_HistWin { get; set; }

    // Pirate 2 - Relative features
    public float Pirate2_StrengthDiff { get; set; }
    public float Pirate2_OddsRank { get; set; }
    public float Pirate2_FoodRank { get; set; }
    public float Pirate2_FoodPositionInteraction { get; set; }

    // Pirate 2 - Interaction controls
    public float Pirate2_InteractionPenalty { get; set; }
    public float Pirate2_InteractionBonus { get; set; }

    // Pirate 3 - Absolute features
    public float Pirate3_Strength { get; set; }
    public float Pirate3_Odds { get; set; }
    public float Pirate3_Food { get; set; }
    public float Pirate3_HistWin { get; set; }

    // Pirate 3 - Relative features
    public float Pirate3_StrengthDiff { get; set; }
    public float Pirate3_OddsRank { get; set; }
    public float Pirate3_FoodRank { get; set; }
    public float Pirate3_FoodPositionInteraction { get; set; }

    // Pirate 3 - Interaction controls
    public float Pirate3_InteractionPenalty { get; set; }
    public float Pirate3_InteractionBonus { get; set; }

    [ColumnName("Label")] public int WinnerPosition { get; set; }
}