using NFCBets.Classical;
using NFCBets.Classical.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class NaiveBettingStrategyService
{
    private readonly NaiveOddsBasedStrategy _oddsStrategy;
    private readonly IBettingStrategyService _bettingService;

    public NaiveBettingStrategyService(IBettingStrategyService bettingService)
    {
        _oddsStrategy = new NaiveOddsBasedStrategy();
        _bettingService = bettingService;
    }

    public List<BetSeries> GenerateNaiveBetSeries(List<PirateOdds> pirateOdds)
    {
        // Compute probabilities using only odds
        var probabilities = _oddsStrategy.ComputePirateProbabilities(pirateOdds);

        // Convert to predictions format
        var predictions = probabilities.Select(p => new PiratePrediction
        {
            RoundId = p.RoundId,
            ArenaId = p.ArenaId,
            PirateId = p.PirateId,
            WinProbability = (float)p.Probability,
            Payout = p.Odds
        }).ToList();

        // ✅ Use injected betting service (already has configuration)
        return _bettingService.GenerateBetSeriesParallel(predictions, BetOptimizationMethodEnum.RawEV);
    }
}