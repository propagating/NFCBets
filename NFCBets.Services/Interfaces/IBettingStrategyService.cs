using NFCBets.Services.Enums;
using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IBettingStrategyService
{
    List<BetSeries> GenerateBetSeries(List<PiratePrediction> predictions, BetOptimizationMethod method);
    List<BetSeries> GenerateBetSeriesParallel(List<PiratePrediction> predictions, BetOptimizationMethod method);
}