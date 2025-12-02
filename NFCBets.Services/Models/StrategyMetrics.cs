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
    
    public double SharpeRatio { get; set; }
    public double MaxDrawdown { get; set; }
}