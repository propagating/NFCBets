using NFCBets.Classical.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IBettingStrategyComparisonService
{
    Task<StrategyComparisonReport> CompareOptimizationMethodsAsync(int startRound, int endRound,
        bool includeNaiveBaseline = true);
    
    Task<List<BettingStrategyComparisonResult>> CompareBettingStrategiesForMlModelAsync(
        IMlStrategy mlStrategy,
        List<PirateFeatureRecord> historicalData,
        decimal startingBankroll = 10000m,
        int rounds = 1000);

    /// <summary>
    /// Compare all ML models with all betting strategies
    /// </summary>
    Task<List<BettingStrategyComparisonResult>> CompareAllMlModelsWithBettingStrategiesAsync(
        List<PirateFeatureRecord> historicalData,
        decimal startingBankroll = 10000m,
        int rounds = 1000);
}