using NFCBets.EF.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;
using NFCBets.Utilities;

namespace NFCBets.Services;

public class DailyBettingPipeline : IDailyBettingPipeline
{
    private readonly IBettingStrategyService _bettingService;
    private readonly NfcbetsContext _context;
    private readonly IFeatureEngineeringService _featureService;
    private readonly IMlModelService _mlService;

    public DailyBettingPipeline(
        IFeatureEngineeringService featureService,
        IMlModelService mlService,
        IBettingStrategyService bettingService,
        NfcbetsContext context)
    {
        _featureService = featureService;
        _mlService = mlService;
        _bettingService = bettingService;
        _context = context;
    }

    public async Task<DailyBettingRecommendations> GenerateRecommendationsAsync(int roundId,
        BetOptimizationMethod method = BetOptimizationMethod.ConsistencyWeighted)
    {
        Console.WriteLine($"🎯 Generating betting recommendations for Round {roundId}");

        // Step 1: Create features
        Console.WriteLine("📊 Step 1: Engineering features...");
        var todayFeatures = await PerformanceHelper.MeasureAsync("Create Features",
            () => _featureService.CreateFeaturesForRoundAsync(roundId));
        //var todayFeatures = await _featureService.CreateFeaturesForRoundAsync(roundId);
        Console.WriteLine($"   Generated {todayFeatures.Count} pirate features");

        // Step 2: Predict win probabilities
        Console.WriteLine("🔮 Step 2: Predicting win probabilities...");
        var predictions = await PerformanceHelper.MeasureAsync("Predict Win Probabilities",
            () => _mlService.PredictAsync(todayFeatures));
        //var predictions = await _mlService.PredictAsync(todayFeatures);
        Console.WriteLine($"   Generated {predictions.Count} predictions");

        // Display prediction summary
        foreach (var arenaGroup in predictions.GroupBy(p => p.ArenaId).OrderBy(g => g.Key))
        {
            Console.WriteLine($"   Arena {arenaGroup.Key}:");
            foreach (var pred in arenaGroup.OrderByDescending(p => p.WinProbability))
                Console.WriteLine(
                    $"      Pirate {pred.PirateId}: {pred.WinProbability:P2} win chance, {pred.Payout}:1 odds");
        }

        // Step 3: Generate bet series (SEQUENTIAL - no DbContext needed)
        Console.WriteLine("💰 Step 3: Generating betting strategies...");
        var betSeries = _bettingService.GenerateBetSeriesParallel(predictions, method);

        var recommendations = new DailyBettingRecommendations
        {
            RoundId = roundId,
            GeneratedAt = DateTime.UtcNow,
            BetSeries = betSeries,
            TotalBets = betSeries.Sum(s => s.Bets.Count)
        };

        Console.WriteLine($"✅ Generated {recommendations.TotalBets} total bets across {betSeries.Count} strategies");

        return recommendations;
    }
}