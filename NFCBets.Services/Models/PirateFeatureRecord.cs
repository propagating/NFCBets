namespace NFCBets.Services.Models;

public class PirateFeatureRecord
{
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public int Position { get; set; }
    public int StartingOdds { get; set; }
    public int CurrentOdds { get; set; }
    public int FoodAdjustment { get; set; }

    // Pirate attributes
    public int Strength { get; set; }
    public int Weight { get; set; }

    // Historical features
    public double HistoricalWinRate { get; set; }
    public int TotalAppearances { get; set; }
    public double AverageOdds { get; set; }

    // Arena-specific
    public double ArenaWinRate { get; set; }

    // Recent form
    public double RecentWinRate { get; set; }

    // Rival performance
    public double WinRateVsCurrentRivals { get; set; }
    public int MatchesVsCurrentRivals { get; set; }
    public double AvgRivalStrength { get; set; }

    // Target
    public bool? IsWinner { get; set; }
}