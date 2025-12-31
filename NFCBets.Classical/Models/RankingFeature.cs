using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class RankingFeature
{
    [LoadColumn(0)] public int GroupId { get; set; }

    [LoadColumn(1)] public uint Label { get; set; }

    public float Position { get; set; }
    public float CurrentOdds { get; set; }
    public float FoodAdjustment { get; set; }
    public float Strength { get; set; }
    public float HistoricalWinRate { get; set; }
    public float ArenaWinRate { get; set; }
    public float RecentWinRate { get; set; }
    public float WinRateVsCurrentRivals { get; set; }
    public float AvgRivalStrength { get; set; }
    public float StrengthDiff { get; set; }
    public float OddsRank { get; set; }
    public float InteractionPenalty { get; set; }
    public float InteractionBonus { get; set; }
}