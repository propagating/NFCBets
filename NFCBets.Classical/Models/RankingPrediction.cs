using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class RankingPrediction
{
    [ColumnName("Score")]
    public float Score { get; set; }
}