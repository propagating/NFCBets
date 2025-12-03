using NFCBets.Services.Enums;

namespace NFCBets.Evaluation.Models;

public class StrategyComparisonReport
{
    public int StartRound { get; set; }
    public int EndRound { get; set; }
    public int TotalRounds { get; set; }
    public Dictionary<BetOptimizationMethod, OptimizationMethodResults> MethodResults { get; set; } = new();
    public BetOptimizationMethod BestByROI { get; set; }
    public BetOptimizationMethod BestBySharpe { get; set; }
    public BetOptimizationMethod BestByConsistency { get; set; }
    public BetOptimizationMethod BestByProfitFactor { get; set; }
}