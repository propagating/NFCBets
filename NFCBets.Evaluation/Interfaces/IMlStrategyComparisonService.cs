using NFCBets.Evaluation.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IMlStrategyComparisonService
{
    Task<MlStrategyComparisonReport> CompareAllStrategiesAsync(
        InteractionAnalysisReport? interactionReport = null,
        bool includeBacktest = true,
        BacktestConfiguration? backtestConfig = null);
}