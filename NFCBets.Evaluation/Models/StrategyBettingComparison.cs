namespace NFCBets.Evaluation.Models;

public class StrategyBettingComparison
{
    public string MlStrategyName { get; set; } = "";
    public Dictionary<string, BacktestResult> BettingResults { get; set; } = new();
    public string BestBettingStrategy { get; set; } = "";
    public decimal BestROI { get; set; }
    public decimal BestSharpe { get; set; }
}
