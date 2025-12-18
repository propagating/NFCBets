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
        var startRound = 9700;
        var currentRound = 9722;
        var modelPath = "Models/foodclub_mp.cd.vd.r.e.cs.c.bt_model.zip";
        
        args = args.Length == 0
            ? new[]
            {
                "--measure-performance",
                //"--parallel", --not currently working properly for data collection
                "--collect-data",
                //"--force-collect" --recollects all data regardless of existing records,
                "--validate-data",
                "--retrain",
                "--evaluate",
                //"--cross-validate", --not needed if we are retraining and evaluating, availabe if we are not doing those things""
                //"--force-cross-validate", --we can use this to force cross validation regardless of whether we're retraining or evaluating'"
                "--compare-strategies",
                "--causal",
                "--backtest"
            }
            : args;
    
        var measurePerformance = args.Contains("--measure-performance");
        
        
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
                services.AddScoped<ICausalInferenceService, CausalInferenceService>();
                services.AddScoped<IBettingStrategyComparisonService, BettingStrategyComparisonService>();
                services.AddScoped<ICrossValidationService, CrossValidationService>();
                services.AddHttpClient<IFoodClubDataService, FoodClubDataService>();
                services.AddScoped<IDataValidationService, DataValidationService>();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            })
            .Build();

        var mlService = host.Services.GetRequiredService<IMlModelService>();
        var evaluator = host.Services.GetRequiredService<IBettingPerformanceEvaluator>();
        var pipeline = host.Services.GetRequiredService<IDailyBettingPipeline>();
        var dataService = host.Services.GetRequiredService<IFoodClubDataService>();

        Console.WriteLine("🏴‍☠️ Welcome to the Food Club Betting Pipeline!");
        if (args.Contains("--collect-data"))
        {
            var forceCollect = args.Contains("--force-collect");
            var useParallel = args.Contains("--parallel");
            var endRound = currentRound;
    
            Console.WriteLine($"📥 Collecting historical Food Club data...");
            Console.WriteLine($"   Force collect: {forceCollect}");
            Console.WriteLine($"   Parallel: {useParallel}");
            Console.WriteLine($"   Range: {startRound} to {endRound}");

            if (measurePerformance)
            {
                Console.WriteLine("   Performance measurement enabled");
                if (useParallel)
                {
                    Console.WriteLine("Dont' do this right now it's not working");
                    await PerformanceHelper.MeasureAsync("Parallel data collection",
                        () => dataService.CollectRangeParallelAsync(startRound, endRound, forceCollect, maxParallel: 10));
                }
                else
                {
                    await PerformanceHelper.MeasureAsync("Sequential data collection",
                        () => dataService.CollectRangeAsync(startRound, endRound, forceCollect));
                }
            }

            if (!useParallel)
            {
                await dataService.CollectRangeAsync(startRound, endRound, forceCollect);
            }
            else
            {
                await dataService.CollectRangeParallelAsync(startRound, endRound, forceCollect, maxParallel: 10);
                
            }

        }

        if (args.Contains("--validate-data"))
        {
            Console.WriteLine("🔍 Validating data quality...");
            var validationService = host.Services.GetRequiredService<IDataValidationService>();

            if (args.Contains("--measure-performance"))
            {
                var report = await PerformanceHelper.MeasureAsync("Validating data quality",
                    () => validationService.ValidateDataQualityAsync(startRound, currentRound));

                // Save report
                Directory.CreateDirectory("Reports");
                var fileName = Path.Combine("Reports", $"data_validation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fileName, json);
                Console.WriteLine($"\n📄 Validation report saved to {fileName}");
            }
            else
            {
                var report = await validationService.ValidateDataQualityAsync(startRound, currentRound);

                // Save report
                Directory.CreateDirectory("Reports");
                var fileName = Path.Combine("Reports", $"data_validation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(fileName, json);
                Console.WriteLine($"\n📄 Validation report saved to {fileName}");
            }
        }

        // Generate today's recommendations
        if (!File.Exists(modelPath) || args.Contains("--retrain"))
        {
            if (args.Contains("--evaluate"))
            {
                Console.WriteLine("🏋️ Training classical model with evaluation...");
                if (args.Contains("--measure-performance"))
                {
                    await PerformanceHelper.MeasureAsync("Find Rounds with multiple winners",
                        () => evaluator.FindRoundsWithMultipleWinnersAsync(startRound, currentRound));
                    await PerformanceHelper.MeasureAsync("Training and evaluating model",
                        () => mlService.TrainAndEvaluateModelAsync());
                    mlService.SaveModel(modelPath);
                }
                else
                {
                    await evaluator.FindRoundsWithMultipleWinnersAsync(startRound, currentRound);
                    await mlService.TrainAndEvaluateModelAsync();
                    mlService.SaveModel(modelPath);
                }
            }
            else
            {
                if (args.Contains("--measure-performance"))
                {
                    await PerformanceHelper.MeasureAsync("Find Rounds with multiple winners",
                        () => evaluator.FindRoundsWithMultipleWinnersAsync(startRound, currentRound));
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
        if (args.Contains("--force-cross-validate") ||
            (!args.Contains("--evaluate") && args.Contains("--cross-validate")))
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
                    () => comparisonService.CompareOptimizationMethodsAsync(startRound, currentRound));
                Console.WriteLine(
                    $"\n🏆 FINAL RECOMMENDATION: Use {comparisonReport.BestBySharpe} for best risk-adjusted returns");
            }

            else
            {
                Console.WriteLine("📊 Comparing all bet optimization strategies...\n");
                var comparisonReport = await comparisonService.CompareOptimizationMethodsAsync(startRound, currentRound);
                Console.WriteLine(
                    $"\n🏆 FINAL RECOMMENDATION: Use {comparisonReport.BestBySharpe} for best risk-adjusted returns");
            }
        }

        if (args.Contains("--backtest"))
        {
            //change method based on reports
            Console.WriteLine("\n💰 Running betting strategy backtest...");
            if (args.Contains("--measure-performance"))
            {
                var backtestReport = await PerformanceHelper.MeasureAsync("Betting backtest",
                    () => evaluator.BacktestBettingStrategyAsync(startRound, currentRound,
                        BetOptimizationMethodEnum.RiskAdjusted));
                SaveBacktestReport(backtestReport);
            }
            else
            {
                var backtestReport =
                    await evaluator.BacktestBettingStrategyAsync(startRound, currentRound, BetOptimizationMethodEnum.RiskAdjusted);
                SaveBacktestReport(backtestReport);
            }
        }

        if (args.Contains("--causal"))
        {
            if (args.Contains("--measure-performance"))
            {
                Console.WriteLine("🧬 Training causally-informed model with evaluation...");
                await PerformanceHelper.MeasureAsync("Training and evaluating causally informed model",
                    () => mlService.TrainAndEvaluateCausallyInformedModelAsync());
                mlService.SaveModel("Models/foodclub_causal_model.zip");
            }
            else
            {
                Console.WriteLine("🧬 Training causally-informed model with evaluation...");
                await mlService.TrainAndEvaluateCausallyInformedModelAsync();
                mlService.SaveModel("Models/foodclub_causal_model.zip");
            }
        }

        if (args.Contains("--measure-performance"))
        {
            Console.WriteLine("\n💰 Generating betting recommendations with performance measurement...");
            var recommendations = await PerformanceHelper.MeasureAsync("Generate Recommendations",
                () => pipeline.GenerateRecommendationsAsync(currentRound, BetOptimizationMethodEnum.RiskAdjusted));

            DisplayRecommendations(recommendations);
            SaveRecommendationsToFile(recommendations);
        }
        else
        {
            var recommendations =
                await pipeline.GenerateRecommendationsAsync(currentRound, BetOptimizationMethodEnum.RiskAdjusted);
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

        if (!recommendations.BetSeries.Any())
        {
            Console.WriteLine("⚠️ No betting strategies generated!");
            Console.WriteLine("   This may indicate:");
            Console.WriteLine("   - No valid predictions available");
            Console.WriteLine("   - All pirates filtered out (check for 1:1 odds)");
            Console.WriteLine("   - Insufficient data for this round");
            return;
        }

        foreach (var series in recommendations.BetSeries)
        {
            Console.WriteLine($"\n🎯 {series.Name.ToUpper()} STRATEGY ({series.RiskLevelEnum})");
            Console.WriteLine($"   {series.Description}");
            Console.WriteLine("   ─────────────────────────────────────────────────");

            // ✅ Safety check for empty bets
            if (!series.Bets.Any())
            {
                Console.WriteLine("   ⚠️ No bets generated for this strategy");
                continue;
            }

            for (var i = 0; i < series.Bets.Count; i++) 
                Console.WriteLine($"   {i + 1,2}. {series.Bets[i]}");

            // ✅ Only calculate if bets exist
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