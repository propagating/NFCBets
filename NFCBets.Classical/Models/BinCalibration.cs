namespace NFCBets.Classical.Models;

public class BinCalibration
{
    public string ProbabilityRange { get; set; } = "";
    public int Count { get; set; }
    public double AveragePredictedProbability { get; set; }
    public double ActualWinRate { get; set; }
    public double Calibration { get; set; }
}