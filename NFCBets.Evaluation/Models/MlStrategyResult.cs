namespace NFCBets.Evaluation.Models;

public class MlStrategyResult
{
    public string StrategyName { get; set; } = "";
    public double Auc { get; set; }
    public double Accuracy { get; set; }
    public double LogLoss { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public TimeSpan TrainingTime { get; set; }
    public int Rank { get; set; }
    public bool IsRecommended { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Backtest metrics (if available)
    public decimal? BacktestROI { get; set; }
    public decimal? BacktestWinRate { get; set; }
    public decimal? BacktestProfit { get; set; }
    public decimal? BacktestMaxDrawdown { get; set; }
    public decimal? BacktestSharpeRatio { get; set; }
    public int? BacktestRank { get; set; }
}