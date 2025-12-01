using NFCBets.Services.Models;

namespace NFCBets.Services;

public interface IBettingStrategyService
{
    List<BetSeries> GenerateBetSeries(List<PiratePrediction> predictions);
    List<BetSeries> GenerateBetSeriesParallel(List<PiratePrediction> predictions);
}