namespace NFCBets.Classical.Models;

public class PairwiseRoundFeature
{
    // Pirate 0 individual features
    public float Pirate0_Strength { get; set; }
    public float Pirate0_Odds { get; set; }
    public float Pirate0_Food { get; set; }
    public float Pirate0_HistWin { get; set; }
    public float Pirate0_ArenaWin { get; set; }
    public float Pirate0_RecentWin { get; set; }
    public float Pirate0_StrengthDiff { get; set; }
    public float Pirate0_OddsRank { get; set; }
    public float Pirate0_InteractionPenalty { get; set; }
    public float Pirate0_InteractionBonus { get; set; }
    
    // Pirate 1 individual features
    public float Pirate1_Strength { get; set; }
    public float Pirate1_Odds { get; set; }
    public float Pirate1_Food { get; set; }
    public float Pirate1_HistWin { get; set; }
    public float Pirate1_ArenaWin { get; set; }
    public float Pirate1_RecentWin { get; set; }
    public float Pirate1_StrengthDiff { get; set; }
    public float Pirate1_OddsRank { get; set; }
    public float Pirate1_InteractionPenalty { get; set; }
    public float Pirate1_InteractionBonus { get; set; }
    
    // Pirate 2 individual features
    public float Pirate2_Strength { get; set; }
    public float Pirate2_Odds { get; set; }
    public float Pirate2_Food { get; set; }
    public float Pirate2_HistWin { get; set; }
    public float Pirate2_ArenaWin { get; set; }
    public float Pirate2_RecentWin { get; set; }
    public float Pirate2_StrengthDiff { get; set; }
    public float Pirate2_OddsRank { get; set; }
    public float Pirate2_InteractionPenalty { get; set; }
    public float Pirate2_InteractionBonus { get; set; }
    
    // Pirate 3 individual features
    public float Pirate3_Strength { get; set; }
    public float Pirate3_Odds { get; set; }
    public float Pirate3_Food { get; set; }
    public float Pirate3_HistWin { get; set; }
    public float Pirate3_ArenaWin { get; set; }
    public float Pirate3_RecentWin { get; set; }
    public float Pirate3_StrengthDiff { get; set; }
    public float Pirate3_OddsRank { get; set; }
    public float Pirate3_InteractionPenalty { get; set; }
    public float Pirate3_InteractionBonus { get; set; }
    
    // Pairwise comparison features (0 vs 1)
    public float Pair01_StrengthDiff { get; set; }
    public float Pair01_OddsDiff { get; set; }
    public float Pair01_FoodDiff { get; set; }
    public float Pair01_HistWinDiff { get; set; }
    
    // Pairwise comparison features (0 vs 2)
    public float Pair02_StrengthDiff { get; set; }
    public float Pair02_OddsDiff { get; set; }
    public float Pair02_FoodDiff { get; set; }
    public float Pair02_HistWinDiff { get; set; }
    
    // Pairwise comparison features (0 vs 3)
    public float Pair03_StrengthDiff { get; set; }
    public float Pair03_OddsDiff { get; set; }
    public float Pair03_FoodDiff { get; set; }
    public float Pair03_HistWinDiff { get; set; }
    
    // Pairwise comparison features (1 vs 2)
    public float Pair12_StrengthDiff { get; set; }
    public float Pair12_OddsDiff { get; set; }
    public float Pair12_FoodDiff { get; set; }
    public float Pair12_HistWinDiff { get; set; }
    
    // Pairwise comparison features (1 vs 3)
    public float Pair13_StrengthDiff { get; set; }
    public float Pair13_OddsDiff { get; set; }
    public float Pair13_FoodDiff { get; set; }
    public float Pair13_HistWinDiff { get; set; }
    
    // Pairwise comparison features (2 vs 3)
    public float Pair23_StrengthDiff { get; set; }
    public float Pair23_OddsDiff { get; set; }
    public float Pair23_FoodDiff { get; set; }
    public float Pair23_HistWinDiff { get; set; }
    
    // Label
    public int WinnerPosition { get; set; }
}