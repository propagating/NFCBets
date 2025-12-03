namespace NFCBets.Services.Models;

public class StrategyMetrics
{
    public string StrategyName { get; set; } = "";
    public int TotalDays { get; set; }
    public int TotalBets { get; set; }
    public int TotalWinningBets { get; set; }
    public double HitRate { get; set; }
    
    public double TotalCost { get; set; }
    public double TotalWinnings { get; set; }
    public double NetProfit { get; set; }
    public double ROI { get; set; }
    
    public int WinningDays { get; set; }
    public double WinningDaysPercentage { get; set; }
    
    public double AverageDailyROI { get; set; }
    public double BestDayROI { get; set; }
    public double WorstDayROI { get; set; }
    
    // Risk metrics
    public double SharpeRatio { get; set; }
    public double SortinoRatio { get; set; }
    public double MaxDrawdown { get; set; }
    public double VolatilityStdDev { get; set; }
    
    // Consistency metrics
    public double MedianDailyROI { get; set; }
    public double WinStreakMax { get; set; }
    public double LossStreakMax { get; set; }
    public double ProfitFactor { get; set; } // Gross profit / Gross loss
    
    // Risk-adjusted scores
    public double ConsistencyScore { get; set; }
    public double RiskAdjustedScore { get; set; }
}