namespace NFCBets.Services.Models;

public class MlPirateFeature
{
    public float Position { get; set; }
    public float CurrentOdds { get; set; }
    public float FoodAdjustment { get; set; }
    
    // Pirate attributes
    public float Strength { get; set; }
    public float Weight { get; set; }
    
    // Historical features
    public float HistoricalWinRate { get; set; }
    public float TotalAppearances { get; set; }
    public float ArenaWinRate { get; set; }
    public float RecentWinRate { get; set; }
    
    // Rival performance
    public float WinRateVsCurrentRivals { get; set; }
    public float MatchesVsCurrentRivals { get; set; }
    public float AvgRivalStrength { get; set; }
    
    public bool Won { get; set; }
}