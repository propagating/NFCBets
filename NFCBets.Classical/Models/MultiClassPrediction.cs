using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;


public class MultiClassPrediction
{
    [ColumnName("Score")]
    public float[] Score { get; set; } = Array.Empty<float>();
}