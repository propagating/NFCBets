using NFCBets.Classical.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IBacktestService
{
    Task<BacktestResult> RunBacktestAsync(
        IMlStrategy strategy,
        List<PirateFeatureRecord> historicalData,
        BacktestConfiguration? config = null);

    Task<List<BacktestResult>> CompareStrategiesBacktestAsync(
        Dictionary<string, IMlStrategy> strategies,
        List<PirateFeatureRecord> historicalData,
        BacktestConfiguration? config = null);

    void DisplayBacktestResults(BacktestResult result);
    void DisplayComparisonResults(List<BacktestResult> results);
}