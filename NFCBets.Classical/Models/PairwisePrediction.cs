using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class PairwisePrediction
{
    [ColumnName("PredictedLabel")] public bool PredictedLabel { get; set; }

    [ColumnName("Probability")] public float Probability { get; set; }

    [ColumnName("Score")] public float Score { get; set; }
}