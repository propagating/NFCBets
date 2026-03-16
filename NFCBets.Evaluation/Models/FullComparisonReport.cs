namespace NFCBets.Evaluation.Models;

public class FullComparisonReport
{
    public DateTime ReportDate { get; set; }
    public int TotalRoundsTested { get; set; }
    public int TotalMlStrategies { get; set; }
    public int TotalBettingConfigurations { get; set; }
    public int TotalCombinationsTested { get; set; }
    
    public List<StrategyBettingComparison> MlStrategyResults { get; set; } = new();
    public List<BacktestResult> AllResults { get; set; } = new();
    
    public BacktestResult? BestOverallROI { get; set; }
    public BacktestResult? BestRiskAdjusted { get; set; }
    public BacktestResult? MostConsistent { get; set; }
    public BacktestResult? LowestDrawdown { get; set; }
    public BacktestResult? BestProfitFactor { get; set; }
}