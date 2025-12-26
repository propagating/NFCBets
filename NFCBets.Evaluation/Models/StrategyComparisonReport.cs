using NFCBets.Services.Enums;

namespace NFCBets.Evaluation.Models;

public class StrategyComparisonReport
{
    public int StartRound { get; set; }
    public int EndRound { get; set; }
    public int TotalRounds { get; set; }
    public Dictionary<BetOptimizationMethodEnum, OptimizationMethodResults> MethodResults { get; set; } = new();
    
    // ✅ NEW: Naive baseline results
    public OptimizationMethodResults? NaiveBaselineResults { get; set; }
    
    public BetOptimizationMethodEnum BestByROI { get; set; }
    public BetOptimizationMethodEnum BestBySharpe { get; set; }
    public BetOptimizationMethodEnum BestByConsistency { get; set; }
    public BetOptimizationMethodEnum BestByProfitFactor { get; set; }
}