namespace NFCBets.Evaluation.Models;


public class MlStrategyComparisonReport
{
    public DateTime ComparisonDate { get; set; }
    public int TrainingRecords { get; set; }
    public int TestRecords { get; set; }
    public int TrainingRounds { get; set; }
    public int TestRounds { get; set; }
    public List<MlStrategyResult> Results { get; set; } = new();
    public string RecommendedStrategy { get; set; } = "";
    public double BestAuc { get; set; }
    public double BestAccuracy { get; set; }
    public double BestLogLoss { get; set; }
    public int TotalStrategiesTested { get; set; }
    public int SuccessfulStrategies { get; set; }
    public TimeSpan TotalComparisonTime { get; set; }
    
    // Interaction analysis info
    public int AntagonisticInteractionsFound { get; set; }
    public int SynergisticInteractionsFound { get; set; }
    
    // Backtest results (if run)
    public bool BacktestIncluded { get; set; }
    public BacktestConfiguration? BacktestConfig { get; set; }
    public List<BacktestResult> BacktestResults { get; set; } = new();
    public string BestBacktestStrategy { get; set; } = "";
    public decimal BestBacktestROI { get; set; }
}