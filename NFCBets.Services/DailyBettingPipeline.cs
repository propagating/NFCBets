using NFCBets.Classical.Constants;
using NFCBets.EF.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class DailyBettingPipeline : IDailyBettingPipeline
{
    private readonly NfcbetsContext _context;
    private readonly IFeatureEngineeringService _featureService;
    private readonly IMlModelService _mlService;
    private readonly IBettingStrategyService _bettingService;

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

    public async Task<DailyBettingRecommendations> GenerateRecommendationsAsync(
        int roundId,
        BetOptimizationMethodEnum method = BetOptimizationMethodEnum.ConsistencyWeighted)
    {
        Console.WriteLine($"🎯 Generating betting recommendations for Round {roundId}");

        // Step 1: Create features
        Console.WriteLine("📊 Step 1: Engineering features...");
        var todayFeatures = await _featureService.CreateFeaturesForRoundAsync(roundId);
        Console.WriteLine($"   Generated {todayFeatures.Count} pirate features");

        if (!todayFeatures.Any())
        {
            Console.WriteLine("⚠️  No valid features generated for this round");
            Console.WriteLine("   Possible causes:");
            Console.WriteLine("   - Round data not in database");
            Console.WriteLine("   - All pirates have 1:1 odds (filtered out as placeholders)");
            Console.WriteLine("   - Database query returned no results");

            return new DailyBettingRecommendations
            {
                RoundId = roundId,
                GeneratedAt = DateTime.UtcNow,
                BetSeries = new List<BetSeries>(),
                TotalBets = 0
            };
        }

        // Display feature summary by arena
        Console.WriteLine("   Features by arena:");
        var featuresByArena = todayFeatures.GroupBy(f => f.ArenaId).OrderBy(g => g.Key);
        foreach (var arenaGroup in featuresByArena)
        {
            var arenaName = ArenaConstants.GetArenaName(arenaGroup.Key);
            Console.WriteLine($"      {arenaName}: {arenaGroup.Count()} pirates");
        }

        // Step 2: Predict win probabilities (now includes pirate names)
        Console.WriteLine("🔮 Step 2: Predicting win probabilities...");
        var predictions = await _mlService.PredictRoundAsync(roundId);
        Console.WriteLine($"   Generated {predictions.Count} predictions");

        if (!predictions.Any())
        {
            Console.WriteLine("⚠️  No predictions generated!");
            return new DailyBettingRecommendations
            {
                RoundId = roundId,
                GeneratedAt = DateTime.UtcNow,
                BetSeries = new List<BetSeries>(),
                TotalBets = 0
            };
        }

        // Display prediction summary by arena (now with names!)
        Console.WriteLine("   Predictions by arena:");
        foreach (var arenaGroup in predictions.GroupBy(p => p.ArenaId).OrderBy(g => g.Key))
        {
            var arenaName = arenaGroup.First().ArenaName;
            Console.WriteLine($"      {arenaName}:");
            foreach (var pred in arenaGroup.OrderByDescending(p => p.WinProbability))
            {
                var evStr = pred.ExpectedValue >= 0 ? $"+{pred.ExpectedValue:F2}" : $"{pred.ExpectedValue:F2}";
                Console.WriteLine(
                    $"         {pred.PirateName}: {pred.WinProbability:P1} win chance, {pred.CorrectedPayout}:1 odds, EV: {evStr}");
            }
        }

        // Step 3: Generate bet series
        Console.WriteLine("💰 Step 3: Generating betting strategies...");
        var betSeries = _bettingService.GenerateBetSeriesParallel(predictions, method);

        // Diagnostic output
        Console.WriteLine($"   Generated {betSeries.Count} bet series");
        foreach (var series in betSeries)
        {
            Console.WriteLine($"      {series.Name}: {series.Bets.Count} bets");
            if (!series.Bets.Any()) 
                Console.WriteLine($"         ⚠️ No bets generated for {series.Name}!");
        }

        var recommendations = new DailyBettingRecommendations
        {
            RoundId = roundId,
            GeneratedAt = DateTime.UtcNow,
            BetSeries = betSeries,
            TotalBets = betSeries.Sum(s => s.Bets.Count)
        };

        if (recommendations.TotalBets == 0)
        {
            Console.WriteLine("⚠️  WARNING: No bets were generated across all strategies!");
            Console.WriteLine("   Check bet generation logic and filtering criteria");
        }
        else
        {
            Console.WriteLine(
                $"✅ Generated {recommendations.TotalBets} total bets across {betSeries.Count} strategies");
        }

        return recommendations;
    }
}