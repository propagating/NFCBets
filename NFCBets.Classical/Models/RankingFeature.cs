using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class RankingFeature
{
    [ColumnName("GroupId")]
    public uint GroupId { get; set; } // Round ID (all pirates in same round compete)
    
    [ColumnName("Label")]
    public float Label { get; set; } // 1 if winner, 0 otherwise
    
    public float Position { get; set; }
    public float CurrentOdds { get; set; }
    public float FoodAdjustment { get; set; }
    public float Strength { get; set; }
    public float HistoricalWinRate { get; set; }
    public float ArenaWinRate { get; set; }
}
