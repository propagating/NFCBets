using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class PiratePredictionOutput
{
    [ColumnName("Probability")] public float Probability { get; set; }

    [ColumnName("Score")] public float Score { get; set; }
}