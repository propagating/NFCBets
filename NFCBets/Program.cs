using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.Services;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;
using NFCBets.Utilities;

namespace NFCBets;

internal class Program
{
static async Task Main(string[] args)
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
            services.AddScoped<IBettingPerformanceEvaluator, BettingPerformanceEvaluator>();
            services.AddHttpClient<IFoodClubDataService, FoodClubDataService>();
            services.AddScoped<ICrossValidationService, CrossValidationService>();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        })
        .Build();

    var mlService = host.Services.GetRequiredService<IMlModelService>();
    var evaluator = host.Services.GetRequiredService<IBettingPerformanceEvaluator>();
    var pipeline = host.Services.GetRequiredService<IDailyBettingPipeline>();
    var dataService = host.Services.GetRequiredService<IFoodClubDataService>();
    

    var modelPath = "Models/foodclub_backtest_model.zip";
    var currentRound = 9703;
    
    if(args.Contains("--collect-data"))
    {
        Console.WriteLine("📥 Collecting historical Food Club data...");
        await dataService.CollectRangeAsync(5300, currentRound);
        return;
    }
    
    await dataService.CollectRangeAsync(5300, currentRound);

    // Generate today's recommendations
    if (!File.Exists(modelPath) || args.Contains("--retrain"))
    {

        if (args.Contains("--evaluate"))
        {
            await evaluator.FindRoundsWithMultipleWinnersAsync(5300, 9705);
            Console.WriteLine("🏋️ Training model with evaluation...");
            await PerformanceHelper.MeasureAsync("Training and evaluating model",
                () => mlService.TrainAndEvaluateModelAsync());        // await mlService.TrainAndEvaluateModelAsync();
        
        }
        else
        {
            await PerformanceHelper.MeasureAsync("Training model", mlService.TrainModelAsync);
        } 
        
        mlService.SaveModel(modelPath);  
    }
    else
    {
        Console.WriteLine("📂 Loading existing model...");
        mlService.LoadModel(modelPath);
    }
    
    if (args.Contains("--cross-validate"))
    {
        var crossValService = host.Services.GetRequiredService<ICrossValidationService>();
        
        Console.WriteLine("Running comprehensive cross-validation...\n");
        
        var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync(numFolds: 5);
        var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync(k: 5);
        
        // Save results
        var cvReport = new
        {
            TimeSeriesCV = timeSeriesCV,
            KFoldCV = kFoldCV,
            Recommendation = timeSeriesCV.AverageAUC > kFoldCV.AverageAUC 
                ? "Use Time-Series CV results (better for temporal data)" 
                : "Both methods show similar performance"
        };
        
        Directory.CreateDirectory("Reports");
        var json = JsonSerializer.Serialize(cvReport, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText("Reports/cross_validation_report.json", json);
        
        return;
    }


    if (args.Contains("--backtest"))
    {
        Console.WriteLine("\n💰 Running betting strategy backtest...");
        var backtestReport = await PerformanceHelper.MeasureAsync("Betting backtest",
            () => evaluator.BacktestBettingStrategyAsync(5305, 9705));
    SaveBacktestReport(backtestReport);
        
    }

    var recommendations = await pipeline.GenerateRecommendationsAsync(currentRound);

    DisplayRecommendations(recommendations);
    SaveRecommendationsToFile(recommendations);
}

static void SaveBacktestReport(BettingPerformanceReport report)
{
    Directory.CreateDirectory("Reports");
    var fileName = Path.Combine("Reports", $"backtest_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
    
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(fileName, json);
    
    Console.WriteLine($"\n📄 Backtest report saved to {fileName}");
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