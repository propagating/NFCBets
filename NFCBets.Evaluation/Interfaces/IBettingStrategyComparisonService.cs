using NFCBets.Evaluation.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IBettingStrategyComparisonService
{
    Task<StrategyComparisonReport> CompareOptimizationMethodsAsync(int startRound, int endRound, bool includeNaiveBaseline = true);
}