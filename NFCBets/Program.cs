using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.Services;
using NFCBets.Services.Interfaces;

namespace NFCBets;

internal class Program
{
    private static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                services.AddDbContext<NfcbetsContext>();
                services.AddScoped<IFoodAdjustmentService, FoodAdjustmentService>();
                services.AddScoped<IFeatureEngineeringService, FeatureEngineeringService>();
                services.AddScoped<IMlModelService, MlModelService>();
                services.AddScoped<IBettingStrategyService, BettingStrategyService>();
                services.AddScoped<IDailyBettingPipeline, DailyBettingPipeline>();
                services.AddHttpClient<IFoodClubDataService, FoodClubDataService>();
            }).ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning))
            .Build();

        var mlService = host.Services.GetRequiredService<IMlModelService>();
        var pipeline = host.Services.GetRequiredService<IDailyBettingPipeline>();
        var dataService = host.Services.GetRequiredService<IFoodClubDataService>();

        var modelPath = "Models/foodclub_evaluated_model.zip";

        if (!File.Exists(modelPath) || args.Contains("--retrain"))
        {
            Console.WriteLine("🏋️ Training new model with comprehensive evaluation...");

            // Use the evaluation version instead of basic training
            await mlService.TrainAndEvaluateModelAsync();

            mlService.SaveModel(modelPath);
        }
        else
        {
            Console.WriteLine("📂 Loading existing model...");
            mlService.LoadModel(modelPath);
        }

        // Generate today's recommendations
        var currentRound = 9705;
        Console.WriteLine("Gathering last 100 rounds of data...");
        await dataService.CollectRangeAsync(currentRound - 100, currentRound);
        var recommendations = await pipeline.GenerateRecommendationsAsync(currentRound);

        // Validate all series have exactly 10 unique bets
        Console.WriteLine("\n✅ Bet Series Validation:");
        var allValid = true;
        foreach (var series in recommendations.BetSeries)
        {
            var isValid = series.Bets.Count == 10;
            var status = isValid ? "✅" : "❌";
            Console.WriteLine($"   {status} {series.Name}: {series.Bets.Count} unique bets");

            if (!isValid) allValid = false;
        }

        if (!allValid)
            Console.WriteLine("\n⚠️ Warning: Some series don't have exactly 10 bets. Adjust strategy parameters.");

        DisplayRecommendations(recommendations);
        SaveRecommendationsToFile(recommendations);
    }

    private static void DisplayRecommendations(DailyBettingRecommendations recommendations)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine($"🎲 FOOD CLUB BETTING RECOMMENDATIONS - Round {recommendations.RoundId}");
        Console.WriteLine($"📅 Generated: {recommendations.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("📌 Note: All odds shown are corrected to minimum 2:1");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        foreach (var series in recommendations.BetSeries)
        {
            Console.WriteLine($"\n🎯 {series.Name.ToUpper()} STRATEGY ({series.RiskLevel})");
            Console.WriteLine($"   {series.Description}");
            Console.WriteLine("   ─────────────────────────────────────────────────");

            for (var i = 0; i < series.Bets.Count; i++) Console.WriteLine($"   {i + 1,2}. {series.Bets[i]}");

            var totalEV = series.Bets.Sum(b => b.ExpectedValue);
            var avgEV = series.Bets.Average(b => b.ExpectedValue);
            Console.WriteLine("   ─────────────────────────────────────────────────");
            Console.WriteLine($"   Total EV: {totalEV:+0.00;-0.00}, Average EV: {avgEV:+0.00;-0.00}");
        }
    }

    private static void SaveRecommendationsToFile(DailyBettingRecommendations recommendations)
    {
        var fileName = $"Recommendations/round_{recommendations.RoundId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
        Directory.CreateDirectory("Recommendations");

        var json = JsonSerializer.Serialize(recommendations, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"\n💾 Recommendations saved to {fileName}");
    }
}