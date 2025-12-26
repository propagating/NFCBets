using NFCBets.Classical.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IBettingStrategyService
{
    List<BetSeries> GenerateBetSeries(List<PiratePrediction> predictions, BetOptimizationMethodEnum methodEnum);
    List<BetSeries> GenerateBetSeriesParallel(List<PiratePrediction> predictions, BetOptimizationMethodEnum methodEnum);
}