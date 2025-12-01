namespace NFCBets.Services;

public class ModelEvaluationReport
{
    public double Accuracy { get; set; }
    public double AUC { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double LogLoss { get; set; }
    public int TestDataSize { get; set; }
    public Dictionary<string, PerformanceMetrics> PerformanceByOdds { get; set; } = new();
    public Dictionary<int, PerformanceMetrics> PerformanceByFoodAdjustment { get; set; } = new();
    public CalibrationMetrics CalibrationMetrics { get; set; } = new();
}