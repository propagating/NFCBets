using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class MultiClassFeature
{
    public int RoundId { get; set; }
    public int PirateId { get; set; }
    public float Position { get; set; }
    public float CurrentOdds { get; set; }
    public float FoodAdjustment { get; set; }
    public float Strength { get; set; }
    public float Weight { get; set; }
    public float HistoricalWinRate { get; set; }
    public float ArenaWinRate { get; set; }
    public float RecentWinRate { get; set; }

    [ColumnName("Label")] public int WinnerPirateId { get; set; } // ✅ This is actually winner POSITION (0-3)
}