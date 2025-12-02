using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IBettingStrategyService
{
    List<BetSeries> GenerateBetSeries(List<PiratePrediction> predictions);
    List<BetSeries> GenerateBetSeriesParallel(List<PiratePrediction> predictions);
}