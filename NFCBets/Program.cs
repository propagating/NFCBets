using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.Services;
using NFCBets.ML;
using System;
using System.Threading.Tasks;

namespace NFCBets.Testing
{
    class Program
    {
        static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                    services.AddDbContext<NfcbetsContext>();
                    services.AddHttpClient<FoodClubDataService>();
                    services.AddScoped<IFoodAdjustmentService, FoodAdjustmentService>();
                    services.AddScoped<IFoodClubDataService, FoodClubDataService>();
                    services.AddScoped<IFeatureEngineeringService, FeatureEngineeringService>();
                    services.AddScoped<IMLModelTrainingService, MLModelTrainingService>();

                }).ConfigureLogging(logging => { logging.SetMinimumLevel(LogLevel.Warning);
                })
                .Build();

            try
            {
                await RunComprehensiveAnalysis(host.Services);
            }
            catch (Exception ex)
            {
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "❌ Analysis failed");
            }
        }

        static async Task RunComprehensiveAnalysis(IServiceProvider services)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            var dataService = services.GetRequiredService<IFoodClubDataService>();
            var featureService = services.GetRequiredService<IFeatureEngineeringService>();
            var mlService = services.GetRequiredService<IMLModelTrainingService>();

            Console.WriteLine("🚀 COMPREHENSIVE FOOD CLUB ANALYSIS");
            Console.WriteLine("===================================");
            
            // // Step 1: Find full available range
            // logger.LogInformation("\n📊 Step 1: Finding complete data range...");
            // var (minRound, maxRound) = await FindFullDataRange(dataService);
            // logger.LogInformation($"Available data: Round {minRound} to {maxRound} ({maxRound - minRound + 1} rounds)");
            //
            // // Step 2: Collect ALL historical data
            // logger.LogInformation("\n📥 Step 2: Collecting complete historical dataset...");
            // var collectedRounds = await CollectCompleteDataset(dataService, minRound, maxRound, logger);
            // logger.LogInformation($"✅ Collected {collectedRounds.Count} rounds successfully");

            // Step 3: Generate comprehensive features
            logger.LogInformation("\n🔧 Step 3: Engineering features for complete dataset...");
            var allFeatures = await featureService.CreateTrainingFeaturesAsync();
            var validFeatures = allFeatures.Where(f => f.Won.HasValue).ToList();
            logger.LogInformation($"✅ Generated {validFeatures.Count} feature records from {allFeatures.Count} total");

            // Step 4: Comprehensive analysis with multiple validation strategies
            await RunMultipleValidationStrategies(validFeatures, mlService, logger);

            // Step 5: Temporal analysis
            await AnalyzeTrendsByTimePeriod(validFeatures, mlService, logger);

            // Step 6: Pirate performance analysis
            AnalyzePiratePerformanceDistribution(validFeatures, logger);

            logger.LogInformation("\n🎉 Comprehensive analysis complete!");
        }

        static async Task<(int min, int max)> FindFullDataRange(IFoodClubDataService dataService)
        {
            const int startSearch = 6500;
            const int endSearch = 9600;
            
            int minFound = -1, maxFound = -1;
            
            // Find minimum available round
            for (int round = startSearch; round < startSearch + 100; round++)
            {
                if (await TestRoundExists(dataService, round))
                {
                    minFound = round;
                    break;
                }
            }
            
            // Find maximum available round
            for (int round = endSearch; round > endSearch - 200; round--)
            {
                if (await TestRoundExists(dataService, round))
                {
                    maxFound = round;
                    break;
                }
            }
            
            return (minFound > 0 ? minFound : startSearch, maxFound > 0 ? maxFound : 8500);
        }

        static async Task<bool> TestRoundExists(IFoodClubDataService dataService, int roundNumber)
        {
            try
            {
                return await dataService.CollectAndSaveRoundAsync(roundNumber);
            }
            catch
            {
                return false;
            }
        }

        static async Task<List<int>> CollectCompleteDataset(IFoodClubDataService dataService, int minRound, int maxRound, ILogger logger)
        {
            var batchSize = 250;
            var successfulRounds = new List<int>();
            
            for (int batchStart = minRound; batchStart <= maxRound; batchStart += batchSize)
            {
                var batchEnd = Math.Min(batchStart + batchSize - 1, maxRound);
                
                logger.LogInformation($"📦 Collecting batch: {batchStart} to {batchEnd}");
                
                var batchResults = await dataService.CollectRangeAsync(batchStart, batchEnd);
                successfulRounds.AddRange(batchResults);
                
                var progress = (double)(batchStart - minRound) / (maxRound - minRound);
                logger.LogInformation($"Progress: {progress:P1} ({successfulRounds.Count} successful rounds)");
                
                // Brief pause between batches
                await Task.Delay(1000);
            }
            
            return successfulRounds;
        }

        static async Task RunMultipleValidationStrategies(List<PirateFeatureRecord> features, IMLModelTrainingService mlService, ILogger logger)
        {
            logger.LogInformation("\n🎯 Step 4: Multiple Validation Strategies");
            
            // Sort by round for time-based splits
            var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
            var totalRounds = sortedFeatures.Max(f => f.RoundId) - sortedFeatures.Min(f => f.RoundId) + 1;
            
            // Strategy 1: Recent vs Historical (80/20 time split)
            await ValidateRecentVsHistorical(sortedFeatures, mlService, logger);
            
            // Strategy 2: Multiple time periods
            await ValidateMultipleTimePeriods(sortedFeatures, mlService, logger);
            
            // Strategy 3: Rolling window validation
            await ValidateRollingWindows(sortedFeatures, mlService, logger);
        }

        static async Task ValidateRecentVsHistorical(List<PirateFeatureRecord> sortedFeatures, IMLModelTrainingService mlService, ILogger logger)
        {
            logger.LogInformation("\n📈 Validation Strategy 1: Recent vs Historical");
            
            var splitPoint = (int)(sortedFeatures.Count * 0.8);
            var trainData = sortedFeatures.Take(splitPoint).ToList();
            var testData = sortedFeatures.Skip(splitPoint).ToList();
            
            var trainRoundRange = $"{trainData.Min(f => f.RoundId)} to {trainData.Max(f => f.RoundId)}";
            var testRoundRange = $"{testData.Min(f => f.RoundId)} to {testData.Max(f => f.RoundId)}";
            
            logger.LogInformation($"  Training: {trainData.Count} records (rounds {trainRoundRange})");
            logger.LogInformation($"  Testing:  {testData.Count} records (rounds {testRoundRange})");
            
            var result = await mlService.TrainWithCustomDataAsync(trainData);
            var testEvaluation = await mlService.EvaluateModelAsync(result.Model, testData);
            
            DisplayValidationResults("Recent vs Historical", result, testEvaluation, logger);
        }

        static async Task ValidateMultipleTimePeriods(List<PirateFeatureRecord> sortedFeatures, IMLModelTrainingService mlService, ILogger logger)
        {
            logger.LogInformation("\n📊 Validation Strategy 2: Multiple Time Periods");
            
            var totalRounds = sortedFeatures.Max(f => f.RoundId) - sortedFeatures.Min(f => f.RoundId) + 1;
            var periodSize = totalRounds / 4; // Split into 4 quarters
            
            for (int period = 0; period < 3; period++) // Train on periods 0-2, test on period 3
            {
                var minRound = sortedFeatures.Min(f => f.RoundId);
                var trainStart = minRound + (period * periodSize);
                var trainEnd = minRound + ((period + 1) * periodSize) - 1;
                var testStart = minRound + (3 * periodSize);
                var testEnd = sortedFeatures.Max(f => f.RoundId);
                
                var trainData = sortedFeatures.Where(f => f.RoundId >= trainStart && f.RoundId <= trainEnd).ToList();
                var testData = sortedFeatures.Where(f => f.RoundId >= testStart && f.RoundId <= testEnd).ToList();
                
                if (trainData.Count < 100 || testData.Count < 50) continue;
                
                logger.LogInformation($"  Period {period + 1}: Train on rounds {trainStart}-{trainEnd}, test on {testStart}-{testEnd}");
                
                var result = await mlService.TrainWithCustomDataAsync(trainData);
                var testEvaluation = await mlService.EvaluateModelAsync(result.Model, testData);
                
                DisplayValidationResults($"Time Period {period + 1}", result, testEvaluation, logger);
            }
        }

        static async Task ValidateRollingWindows(List<PirateFeatureRecord> sortedFeatures, IMLModelTrainingService mlService, ILogger logger)
        {
            logger.LogInformation("\n🔄 Validation Strategy 3: Rolling Window Validation");
            
            const int windowSize = 1000; // Train on 1000 rounds, test on next 200
            const int testSize = 200;
            const int stepSize = 300;
            
            var results = new List<(string Name, ModelEvaluationResult Evaluation)>();
            
            var minRound = sortedFeatures.Min(f => f.RoundId);
            var maxRound = sortedFeatures.Max(f => f.RoundId);
            
            for (int windowStart = minRound; windowStart + windowSize + testSize <= maxRound; windowStart += stepSize)
            {
                var trainStart = windowStart;
                var trainEnd = windowStart + windowSize - 1;
                var testStart = trainEnd + 1;
                var testEnd = testStart + testSize - 1;
                
                var trainData = sortedFeatures.Where(f => f.RoundId >= trainStart && f.RoundId <= trainEnd).ToList();
                var testData = sortedFeatures.Where(f => f.RoundId >= testStart && f.RoundId <= testEnd).ToList();
                
                if (trainData.Count < 500 || testData.Count < 50) continue;
                
                var result = await mlService.TrainWithCustomDataAsync(trainData);
                var testEvaluation = await mlService.EvaluateModelAsync(result.Model, testData);
                
                results.Add(($"Window {trainStart}-{testEnd}", testEvaluation));
                
                logger.LogInformation($"  Window {trainStart}-{trainEnd}: Accuracy {testEvaluation.Accuracy:P2}, AUC {testEvaluation.AUC:F3}");
            }
            
            // Summarize rolling results
            if (results.Any())
            {
                var avgAccuracy = results.Average(r => r.Evaluation.Accuracy);
                var avgAUC = results.Average(r => r.Evaluation.AUC);
                var avgROI = results.Average(r => r.Evaluation.CustomMetrics.GetValueOrDefault("BettingROI", 0));
                
                logger.LogInformation($"\n📈 Rolling Window Summary:");
                logger.LogInformation($"  Average Accuracy: {avgAccuracy:P2}");
                logger.LogInformation($"  Average AUC: {avgAUC:F3}");
                logger.LogInformation($"  Average ROI: {avgROI:P2}");
                logger.LogInformation($"  Windows tested: {results.Count}");
            }
        }

        static async Task AnalyzeTrendsByTimePeriod(List<PirateFeatureRecord> features, IMLModelTrainingService mlService, ILogger logger)
        {
            logger.LogInformation("\n📅 Step 5: Temporal Trend Analysis");
            
            var roundGroups = features.GroupBy(f => f.RoundId / 500 * 500).OrderBy(g => g.Key); // Group by 500-round periods
            
            logger.LogInformation($"Analyzing {roundGroups.Count()} time periods...");
            
            foreach (var group in roundGroups.Take(10)) // Show first 10 periods
            {
                var periodStart = group.Key;
                var periodEnd = group.Key + 499;
                var periodFeatures = group.ToList();
                
                var winRate = periodFeatures.Count(f => f.Won == true) / (double)periodFeatures.Count;
                var avgOdds = periodFeatures.Average(f => f.CurrentOdds);
                var uniquePirates = periodFeatures.Select(f => f.PirateId).Distinct().Count();
                
                logger.LogInformation($"  Rounds {periodStart}-{periodEnd}: {winRate:P2} win rate, {avgOdds:F1} avg odds, {uniquePirates} unique pirates");
            }
        }

        static void AnalyzePiratePerformanceDistribution(List<PirateFeatureRecord> features, ILogger logger)
        {
            logger.LogInformation("\n🏴‍☠️ Step 6: Pirate Performance Distribution");
            
            var pirateStats = features.GroupBy(f => f.PirateId)
                .Select(g => new
                {
                    PirateId = g.Key,
                    TotalAppearances = g.Count(),
                    Wins = g.Count(f => f.Won == true),
                    WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                    AvgOdds = g.Average(f => f.CurrentOdds)
                })
                .OrderByDescending(p => p.TotalAppearances)
                .ToList();
            
            logger.LogInformation($"Total unique pirates: {pirateStats.Count}");
            
            // Show top performers
            logger.LogInformation("\n🏆 Top 10 Pirates by Appearances:");
            foreach (var pirate in pirateStats.Take(10))
            {
                logger.LogInformation($"  Pirate {pirate.PirateId}: {pirate.TotalAppearances} appearances, {pirate.WinRate:P2} win rate, {pirate.AvgOdds:F1} avg odds");
            }
            
            // Performance distribution
            var winRateRanges = new[]
            {
                (0.0, 0.1, "0-10%"),
                (0.1, 0.2, "10-20%"),
                (0.2, 0.3, "20-30%"),
                (0.3, 0.5, "30-50%"),
                (0.5, 0.7, "50-70%"),
                (0.7, 1.0, "70-100%")
            };
            
            logger.LogInformation("\n📊 Win Rate Distribution:");
            foreach (var (min, max, label) in winRateRanges)
            {
                var count = pirateStats.Count(p => p.WinRate >= min && p.WinRate < max);
                logger.LogInformation($"  {label}: {count} pirates");
            }
        }

        static void DisplayValidationResults(string strategyName, ModelTrainingResult trainingResult, ModelEvaluationResult evaluation, ILogger logger)
        {
            logger.LogInformation($"\n🎯 {strategyName} Results:");
            logger.LogInformation($"  Training Records: {trainingResult.TrainingDataCount:N0}");
            logger.LogInformation($"  Test Records: {evaluation.TestDataCount:N0}");
            logger.LogInformation($"  Accuracy: {evaluation.Accuracy:P2}");
            logger.LogInformation($"  AUC: {evaluation.AUC:F3}");
            logger.LogInformation($"  F1 Score: {evaluation.F1Score:F3}");
            
            if (evaluation.CustomMetrics.TryGetValue("BettingROI", out var roi))
            {
                logger.LogInformation($"  Betting ROI: {roi:P2}");
            }
            
            if (evaluation.CustomMetrics.TryGetValue("ProfitableBets", out var profitableRate))
            {
                logger.LogInformation($"  Profitable Bets: {profitableRate:P2}");
            }

            // Performance assessment
            var assessment = AssessModelPerformance(evaluation);
            logger.LogInformation($"  Assessment: {assessment}");
        }

        static string AssessModelPerformance(ModelEvaluationResult evaluation)
        {
            var messages = new List<string>();
            
            if (evaluation.AUC > 0.7)
                messages.Add("Good predictive power");
            else if (evaluation.AUC > 0.6)
                messages.Add("Moderate predictive power");
            else
                messages.Add("Poor predictive power");
                
            if (evaluation.CustomMetrics.TryGetValue("BettingROI", out var roi))
            {
                if (roi > 0.1)
                    messages.Add("Highly profitable");
                else if (roi > 0.05)
                    messages.Add("Profitable");
                else if (roi > 0)
                    messages.Add("Marginally profitable");
                else
                    messages.Add("Unprofitable");
            }
            
            // Check for overfitting signs
            if (evaluation.Accuracy > 0.9 && evaluation.AUC > 0.95)
                messages.Add("⚠️ Possible overfitting");
                
            return string.Join(", ", messages);
        }
    }
}