namespace NFCBets.Services;

public class PerformanceMetrics
{
    public int Count { get; set; }
    public double Accuracy { get; set; }
    public double AveragePredictedProbability { get; set; }
    public double ActualWinRate { get; set; }
    public double Calibration { get; set; }
}