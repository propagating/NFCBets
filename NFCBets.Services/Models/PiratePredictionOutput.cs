using Microsoft.ML.Data;

namespace NFCBets.Services.Models;

public class PiratePredictionOutput
{
    [ColumnName("Probability")]
    public float Probability { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }
}