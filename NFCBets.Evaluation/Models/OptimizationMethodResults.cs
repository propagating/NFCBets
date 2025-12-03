using NFCBets.Services.Enums;

namespace NFCBets.Evaluation.Models;

public class OptimizationMethodResults
{
    public BetOptimizationMethod Method { get; set; }
    public double OverallROI { get; set; }
    public double SharpeRatio { get; set; }
    public double SortinoRatio { get; set; }
    public int WinningDays { get; set; }
    public double WinningDaysPercentage { get; set; }
    public double MaxDrawdown { get; set; }
    public double ProfitFactor { get; set; }
    public double AverageDailyROI { get; set; }
    public double MedianDailyROI { get; set; }
}