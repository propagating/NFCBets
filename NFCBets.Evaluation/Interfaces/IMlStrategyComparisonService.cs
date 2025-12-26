using NFCBets.Evaluation.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IMlStrategyComparisonService
{
    Task<MlStrategyComparisonReport> CompareAllStrategiesAsync();
}