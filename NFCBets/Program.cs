using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NFCBets.Causal;
using NFCBets.Causal.Interfaces;
using NFCBets.EF.Models;
using NFCBets.Evaluation;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Services;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;
using NFCBets.Utilities;

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
                services.AddScoped<IBettingPerformanceEvaluator, BettingPerformanceEvaluator>();
                services.AddScoped<ICrossValidationService, CrossValidationService>();
                services.AddScoped<ICausalInferenceService, CausalInferenceService>();
                services.AddScoped<IBettingStrategyComparisonService, BettingStrategyComparisonService>();
                services.AddScoped<ICrossValidationService, CrossValidationService>();
                services.AddHttpClient<IFoodClubDataService, FoodClubDataService>();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            })
            .Build();

        var mlService = host.Services.GetRequiredService<IMlModelService>();
        var evaluator = host.Services.GetRequiredService<IBettingPerformanceEvaluator>();
        var pipeline = host.Services.GetRequiredService<IDailyBettingPipeline>();
        var dataService = host.Services.GetRequiredService<IFoodClubDataService>();


        var modelPath = "Models/foodclub_bt_cv_model.zip";
        var currentRound = 9703;
        args = args.Length == 0 ? new[] {"--retrain","--evaluate","--force-oss-validate","--backtest","--measure-performance"} : args;

        if (args.Contains("--collect-data"))
        {
            Console.WriteLine("📥 Collecting historical Food Club data...");
            await dataService.CollectRangeAsync(5300, currentRound);
        }
        
        
        // Generate today's recommendations
        if (!File.Exists(modelPath) || args.Contains("--retrain"))
        {
            if (args.Contains("--evaluate"))
            {

                if (args.Contains("--causal"))
                {
                    Console.WriteLine("🧬 Training causally-informed model with evaluation...");
                    await PerformanceHelper.MeasureAsync("Training and evaluating causally informed model",
                        ()=> await mlService.TrainAndEvaluateCausallyInformedModelAsync());
                    mlService.SaveModel("Models/foodclub_causal_model.zip");
                }
                else
                {
                    Console.WriteLine("🏋️ Training classical model with evaluation...");
                    if (args.Contains("--measure-performance"))
                    {
                        await PerformanceHelper.MeasureAsync("Find Rounds with multiple winners",
                            () => evaluator.FindRoundsWithMultipleWinnersAsync(5300, 9705));
                        await PerformanceHelper.MeasureAsync("Training and evaluating model",
                            () => mlService.TrainAndEvaluateModelAsync());
                        mlService.SaveModel(modelPath);
                    }
                    else
                    {
                        await evaluator.FindRoundsWithMultipleWinnersAsync(5300, 9705);
                        await mlService.TrainAndEvaluateModelAsync();
                        mlService.SaveModel(modelPath);
                    
                    }
                } 

            }
            else
            {
                if (args.Contains("--measure-performance"))
                {
                    await PerformanceHelper.MeasureAsync("Find Rounds with multiple winners",
                        () => evaluator.FindRoundsWithMultipleWinnersAsync(5300, 9705));
                    await PerformanceHelper.MeasureAsync("Training model", mlService.TrainModelAsync);
                    mlService.SaveModel(modelPath);
                    
                }
                else
                {
                    await mlService.TrainModelAsync();
                    mlService.SaveModel(modelPath);
                }
            }

        }
        else
        {
            Console.WriteLine("📂 Loading existing model...");
            mlService.LoadModel(modelPath);
        }

        //This isnt' really needed when running evaluate since both of these are run as part of the evaluation method
        //so we can skip them if we're already running evaluation unless they indicate forced cross validation
        if (args.Contains("--force-cross-validate") || (!args.Contains("--evaluate") && args.Contains("--cross-validate")))
        {
            var crossValService = host.Services.GetRequiredService<ICrossValidationService>();

            Console.WriteLine("Running comprehensive cross-validation...\n");

            if (args.Contains("--measure-performance"))
            {
                var kFoldCV = await PerformanceHelper.MeasureAsync("K Folds Cross Validation",
                    () => crossValService.PerformKFoldCrossValidationAsync());
                var timeSeriesCV = await PerformanceHelper.MeasureAsync("Time Series Cross Validation",
                    () => crossValService.PerformTimeSeriesCrossValidationAsync());

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
            }
            else
            {
                var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync();
                var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync();

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
            }
        }

        if (args.Contains("--compare-strategies"))
        {
            var comparisonService = host.Services.GetRequiredService<IBettingStrategyComparisonService>();
            
            if (args.Contains("--measure-performance"))
            {
                Console.WriteLine("📊 Comparing all bet optimization strategies...\n");
                var comparisonReport = await PerformanceHelper.MeasureAsync("Comparing Optimization Methods",
                    ()=> await comparisonService.CompareOptimizationMethodsAsync(8300, 9705));
                Console.WriteLine($"\n🏆 FINAL RECOMMENDATION: Use {comparisonReport.BestBySharpe} for best risk-adjusted returns");
            }

            else
            {
                Console.WriteLine("📊 Comparing all bet optimization strategies...\n");
                var comparisonReport = await comparisonService.CompareOptimizationMethodsAsync(8300, 9705);
                Console.WriteLine($"\n🏆 FINAL RECOMMENDATION: Use {comparisonReport.BestBySharpe} for best risk-adjusted returns");
                
            }
        }

        if (args.Contains("--backtest"))
        {
            //change method based on reports
            Console.WriteLine("\n💰 Running betting strategy backtest...");
            if (args.Contains("--measure-performance"))
            {
                var backtestReport = await PerformanceHelper.MeasureAsync("Betting backtest",
                    () => evaluator.BacktestBettingStrategyAsync(5305, 9705,
                        BetOptimizationMethod.ConsistencyWeighted));
                SaveBacktestReport(backtestReport);
            }
            else
            {
                var backtestReport =
                    await evaluator.BacktestBettingStrategyAsync(5305, 9705, BetOptimizationMethod.ConsistencyWeighted);
                SaveBacktestReport(backtestReport);
            }


        }

        if (args.Contains("--measure-performance"))
        {
            Console.WriteLine("\n💰 Generating betting recommendations with performance measurement...");
            var recommendations = await PerformanceHelper.MeasureAsync("Generate Recommendations",
                () => pipeline.GenerateRecommendationsAsync(currentRound, BetOptimizationMethod.ConsistencyWeighted));

            DisplayRecommendations(recommendations);
            SaveRecommendationsToFile(recommendations);
        }
        else
        {
            var recommendations =
                await pipeline.GenerateRecommendationsAsync(currentRound, BetOptimizationMethod.ConsistencyWeighted);
            DisplayRecommendations(recommendations);
            SaveRecommendationsToFile(recommendations);
        } //change method based on reports
    }

    private static void SaveBacktestReport(BettingPerformanceReport report)
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