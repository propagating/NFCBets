namespace NFCBets.Utilities.Models;

public class ArenaAnalysis
{
    public int ArenaId { get; set; }
    public int TotalRounds { get; set; }
    public double OverallWinRate { get; set; }
    public double FavoriteWinRate { get; set; }
    public double AverageStrength { get; set; }
    
    // Position effects in this arena
    public Dictionary<int, double> PositionWinRates { get; set; } = new();
    public int BestPosition { get; set; }
    public double PositionVariance { get; set; }
    
    // Food effect in this arena
    public double FoodEffectStrength { get; set; }
    public double PositiveFoodWinRate { get; set; }
    public double NegativeFoodWinRate { get; set; }
    
    // Strength effect in this arena
    public double StrengthEffectStrength { get; set; }
    public double HighStrengthWinRate { get; set; }
    public double LowStrengthWinRate { get; set; }
}