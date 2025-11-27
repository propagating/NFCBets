using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.ML;

namespace NFCBets.Services
{
    public interface IBatchProcessingService
    {
        Task<List<PirateFeatureRecord>> ProcessLargeDatasetAsync(int? maxRounds = null);
        Task<List<ValidationResult>> RunParallelValidationsAsync(List<PirateFeatureRecord> features);
        Task<Dictionary<string, object>> RunParallelAnalysisAsync(List<PirateFeatureRecord> features);
    }

    public class BatchProcessingService : IBatchProcessingService
    {
        private readonly IDbContextFactory<NfcbetsContext> _contextFactory;
        private readonly MultithreadedFeatureEngineeringService _featureService;
        private readonly IMLModelTrainingService _mlService;
        private readonly ILogger<BatchProcessingService> _logger;

        public BatchProcessingService(
            IDbContextFactory<NfcbetsContext> contextFactory,
            MultithreadedFeatureEngineeringService featureService,
            IMLModelTrainingService mlService,
            ILogger<BatchProcessingService> logger)
        {
            _contextFactory = contextFactory;
            _featureService = featureService;
            _mlService = mlService;
            _logger = logger;
        }

        /// <summary>
        /// Process the complete dataset in optimized batches
        /// </summary>
        public async Task<List<PirateFeatureRecord>> ProcessLargeDatasetAsync(int? maxRounds = null)
        {
            _logger.LogInformation("🚀 Starting batch processing for large dataset");
            
            using var context = _contextFactory.CreateDbContext();
            
            // Get total data size first
            var totalResults = await context.RoundResults
                .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
                .CountAsync();
                
            _logger.LogInformation($"📊 Total results to process: {totalResults:N0}");
            
            var allFeatures = new List<PirateFeatureRecord>();
            const int batchSize = 5000; // Process 5000 records at a time
            
            for (int skip = 0; skip < totalResults; skip += batchSize)
            {
                _logger.LogInformation($"📦 Processing batch {skip / batchSize + 1}: records {skip:N0} to {Math.Min(skip + batchSize, totalResults):N0}");
                
                var batchResults = await context.RoundResults
                    .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
                    .OrderBy(rr => rr.RoundId)
                    .Skip(skip)
                    .Take(batchSize)
                    .ToListAsync();
                
                if (maxRounds.HasValue && allFeatures.Count >= maxRounds.Value)
                    break;
                
                var batchFeatures = await ProcessBatchAsync(batchResults);
                allFeatures.AddRange(batchFeatures);
                
                _logger.LogInformation($"✅ Batch complete: {batchFeatures.Count} features generated");
                
                // Memory cleanup between batches
                GC.Collect();
                await Task.Delay(100); // Brief pause
            }
            
            _logger.LogInformation($"✅ Large dataset processing complete: {allFeatures.Count:N0} total features");
            return allFeatures;
        }

        private async Task<List<PirateFeatureRecord>> ProcessBatchAsync(List<RoundResult> batchResults)
        {
            var features = new ConcurrentBag<PirateFeatureRecord>();
            
            // Group by round for efficient processing
            var roundGroups = batchResults.GroupBy(r => r.RoundId!.Value);
            
            var tasks = roundGroups.Select(async roundGroup =>
            {
                using var context = _contextFactory.CreateDbContext();
                
                var roundFeatures = await _featureService.ProcessRoundFeaturesOptimizedAsync(roundGroup.Key, roundGroup.ToList());
                foreach (var feature in roundFeatures)
                {
                    features.Add(feature);
                }
            });
            
            await Task.WhenAll(tasks);
            
            return features.OrderBy(f => f.RoundId).ThenBy(f => f.ArenaId).ToList();
        }

        /// <summary>
        /// Run multiple validation strategies in parallel
        /// </summary>
        public async Task<List<ValidationResult>> RunParallelValidationsAsync(List<PirateFeatureRecord> features)
        {
            _logger.LogInformation("🎯 Running parallel validation strategies");
            
            var validationTasks = new[]
            {
                Task.Run(() => ValidateRecentVsHistoricalAsync(features)),
                Task.Run(() => ValidateTimeBasedSplitsAsync(features)),
                Task.Run(() => ValidateRollingWindowsAsync(features)),
                Task.Run(() => ValidateCrossValidationAsync(features))
            };

            var results = await Task.WhenAll(validationTasks);
            
            _logger.LogInformation("✅ All validation strategies complete");
            return results.ToList();
        }

        /// <summary>
        /// Run comprehensive analysis tasks in parallel
        /// </summary>
        public async Task<Dictionary<string, object>> RunParallelAnalysisAsync(List<PirateFeatureRecord> features)
        {
            _logger.LogInformation("📊 Running parallel analysis tasks");
            
            var analysisTasks = new Dictionary<string, Task<object>>
            {
                ["PiratePerformance"] = Task.Run<object>(async () => await AnalyzePiratePerformanceAsync(features)),
                ["TemporalTrends"] = Task.Run<object>(async () => await AnalyzeTemporalTrendsAsync(features)),
                ["FeatureCorrelations"] = Task.Run<object>(async () => await AnalyzeFeatureCorrelationsAsync(features)),
                ["FoodEffectAnalysis"] = Task.Run<object>(async () => await AnalyzeFoodEffectsAsync(features)),
                ["ArenaEffects"] = Task.Run<object>(async () => await AnalyzeArenaEffectsAsync(features))
            };

            var results = new Dictionary<string, object>();
            
            foreach (var task in analysisTasks)
            {
                results[task.Key] = await task.Value;
                _logger.LogInformation($"✅ {task.Key} analysis complete");
            }
            
            return results;
        }

        // Individual validation methods
        private async Task<ValidationResult> ValidateRecentVsHistoricalAsync(List<PirateFeatureRecord> features)
        {
            var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
            var splitPoint = (int)(sortedFeatures.Count * 0.8);
            
            var trainData = sortedFeatures.Take(splitPoint).ToList();
            var testData = sortedFeatures.Skip(splitPoint).ToList();
            
            var trainingResult = await _mlService.TrainWithCustomDataAsync(trainData);
            var evaluation = await _mlService.EvaluateModelAsync(trainingResult.Model, testData);
            
            return new ValidationResult
            {
                Name = "Recent vs Historical",
                TrainSize = trainData.Count,
                TestSize = testData.Count,
                TrainRoundRange = $"{trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)}",
                TestRoundRange = $"{testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)}",
                Evaluation = evaluation
            };
        }

        private async Task<ValidationResult> ValidateTimeBasedSplitsAsync(List<PirateFeatureRecord> features)
        {
            var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
            var thirdPoint = sortedFeatures.Count / 3;
            
            // Train on first third, test on last third
            var trainData = sortedFeatures.Take(thirdPoint).ToList();
            var testData = sortedFeatures.Skip(thirdPoint * 2).ToList();
            
            var trainingResult = await _mlService.TrainWithCustomDataAsync(trainData);
            var evaluation = await _mlService.EvaluateModelAsync(trainingResult.Model, testData);
            
            return new ValidationResult
            {
                Name = "Time-Based Split",
                TrainSize = trainData.Count,
                TestSize = testData.Count,
                Evaluation = evaluation
            };
        }

        private async Task<ValidationResult> ValidateRollingWindowsAsync(List<PirateFeatureRecord> features)
        {
            const int windowSize = 2000;
            const int testSize = 500;
            const int stepSize = 1000;
            
            var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
            var windowTasks = new List<Task<ModelEvaluationResult>>();
            
            for (int start = 0; start + windowSize + testSize < sortedFeatures.Count; start += stepSize)
            {
                windowTasks.Add(Task.Run(async () =>
                {
                    var trainData = sortedFeatures.Skip(start).Take(windowSize).ToList();
                    var testData = sortedFeatures.Skip(start + windowSize).Take(testSize).ToList();
                    
                    var model = await _mlService.TrainWithCustomDataAsync(trainData);
                    return await _mlService.EvaluateModelAsync(model.Model, testData);
                }));
            }
            
            var windowResults = await Task.WhenAll(windowTasks);
            
            return new ValidationResult
            {
                Name = "Rolling Windows",
                WindowCount = windowResults.Length,
                Evaluation = new ModelEvaluationResult
                {
                    Accuracy = windowResults.Average(r => r.Accuracy),
                    AUC = windowResults.Average(r => r.AUC),
                    F1Score = windowResults.Average(r => r.F1Score),
                    CustomMetrics = new Dictionary<string, double>
                    {
                        {"BettingROI", windowResults.Average(r => r.CustomMetrics.GetValueOrDefault("BettingROI", 0))},
                        {"AccuracyStdDev", CalculateStandardDeviation(windowResults.Select(r => r.Accuracy))}
                    }
                }
            };
        }

        private async Task<ValidationResult> ValidateCrossValidationAsync(List<PirateFeatureRecord> features)
        {
            const int k = 5;
            var foldSize = features.Count / k;
            
            var tasks = Enumerable.Range(0, k).Select(async fold =>
            {
                var testData = features.Skip(fold * foldSize).Take(foldSize).ToList();
                var trainData = features.Take(fold * foldSize)
                                      .Concat(features.Skip((fold + 1) * foldSize))
                                      .ToList();
                
                var model = await _mlService.TrainWithCustomDataAsync(trainData);
                return await _mlService.EvaluateModelAsync(model.Model, testData);
            });
            
            var foldResults = await Task.WhenAll(tasks);
            
            return new ValidationResult
            {
                Name = "K-Fold Cross-Validation",
                FoldCount = k,
                Evaluation = new ModelEvaluationResult
                {
                    Accuracy = foldResults.Average(r => r.Accuracy),
                    AUC = foldResults.Average(r => r.AUC),
                    F1Score = foldResults.Average(r => r.F1Score),
                    CustomMetrics = new Dictionary<string, double>
                    {
                        {"BettingROI", foldResults.Average(r => r.CustomMetrics.GetValueOrDefault("BettingROI", 0))},
                        {"AccuracyStdDev", CalculateStandardDeviation(foldResults.Select(r => r.Accuracy))},
                        {"AUC_StdDev", CalculateStandardDeviation(foldResults.Select(r => r.AUC))}
                    }
                }
            };
        }

        // Analysis methods
        private async Task<object> AnalyzePiratePerformanceAsync(List<PirateFeatureRecord> features)
        {
            return await Task.Run(() =>
            {
                var pirateStats = features.AsParallel()
                    .Where(f => f.Won.HasValue)
                    .GroupBy(f => f.PirateId)
                    .Select(g => new
                    {
                        PirateId = g.Key,
                        TotalAppearances = g.Count(),
                        Wins = g.Count(f => f.Won == true),
                        WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                        AvgOdds = g.Average(f => f.CurrentOdds),
                        AvgFoodAdjustment = g.Average(f => f.FoodAdjustment),
                        FirstAppearance = g.Min(f => f.RoundId),
                        LastAppearance = g.Max(f => f.RoundId)
                    })
                    .OrderByDescending(p => p.TotalAppearances)
                    .ToList();

                _logger.LogInformation($"📈 Analyzed {pirateStats.Count} unique pirates");
                return pirateStats;
            });
        }

        private async Task<object> AnalyzeTemporalTrendsAsync(List<PirateFeatureRecord> features)
        {
            return await Task.Run(() =>
            {
                var trends = features.AsParallel()
                    .Where(f => f.Won.HasValue)
                    .GroupBy(f => f.RoundId / 100 * 100) // Group by ~100 round periods
                    .Select(g => new
                    {
                        Period = g.Key,
                        Records = g.Count(),
                        WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                        AvgOdds = g.Average(f => f.CurrentOdds),
                        AvgFoodImpact = g.Average(f => f.FoodAdjustment),
                        UniquePirates = g.Select(f => f.PirateId).Distinct().Count(),
                        FavoriteWinRate = g.Where(f => f.IsFavorite).Count(f => f.Won == true) / (double)Math.Max(1, g.Count(f => f.IsFavorite))
                    })
                    .OrderBy(t => t.Period)
                    .ToList();

                _logger.LogInformation($"📅 Analyzed {trends.Count} time periods");
                return trends;
            });
        }

        private async Task<object> AnalyzeFeatureCorrelationsAsync(List<PirateFeatureRecord> features)
        {
            return await Task.Run(() =>
            {
                var validFeatures = features.Where(f => f.Won.HasValue).ToList();
                
                var correlations = new Dictionary<string, double>
                {
                    ["FoodAdjustment_vs_WinRate"] = CalculateCorrelation(validFeatures, f => f.FoodAdjustment, f => f.Won == true ? 1.0 : 0.0),
                    ["CurrentOdds_vs_WinRate"] = CalculateCorrelation(validFeatures, f => f.CurrentOdds, f => f.Won == true ? 1.0 : 0.0),
                    ["OverallWinRate_vs_Outcome"] = CalculateCorrelation(validFeatures, f => f.OverallWinRate, f => f.Won == true ? 1.0 : 0.0),
                    ["ArenaWinRate_vs_Outcome"] = CalculateCorrelation(validFeatures, f => f.ArenaWinRate, f => f.Won == true ? 1.0 : 0.0),
                    ["HeadToHeadWinRate_vs_Outcome"] = CalculateCorrelation(validFeatures, f => f.HeadToHeadWinRate, f => f.Won == true ? 1.0 : 0.0)
                };

                _logger.LogInformation($"🔗 Calculated {correlations.Count} feature correlations");
                return correlations;
            });
        }

        private async Task<object> AnalyzeFoodEffectsAsync(List<PirateFeatureRecord> features)
        {
            return await Task.Run(() =>
            {
                var foodEffects = features.AsParallel()
                    .Where(f => f.Won.HasValue)
                    .GroupBy(f => f.FoodAdjustment)
                    .Select(g => new
                    {
                        FoodAdjustment = g.Key,
                        Count = g.Count(),
                        WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                        AvgOdds = g.Average(f => f.CurrentOdds)
                    })
                    .OrderBy(fe => fe.FoodAdjustment)
                    .ToList();

                _logger.LogInformation($"🍕 Analyzed food effects across {foodEffects.Count} adjustment levels");
                return foodEffects;
            });
        }

        private async Task<object> AnalyzeArenaEffectsAsync(List<PirateFeatureRecord> features)
        {
            return await Task.Run(() =>
            {
                var arenaStats = features.AsParallel()
                    .Where(f => f.Won.HasValue)
                    .GroupBy(f => f.ArenaId)
                    .Select(g => new
                    {
                        ArenaId = g.Key,
                        TotalRounds = g.Select(f => f.RoundId).Distinct().Count(),
                        TotalRecords = g.Count(),
                        WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                        AvgOdds = g.Average(f => f.CurrentOdds),
                        UniquePirates = g.Select(f => f.PirateId).Distinct().Count(),
                        FavoriteWinRate = g.Where(f => f.IsFavorite).Count(f => f.Won == true) / (double)Math.Max(1, g.Count(f => f.IsFavorite))
                    })
                    .OrderBy(a => a.ArenaId)
                    .ToList();

                _logger.LogInformation($"🏟️ Analyzed {arenaStats.Count} arenas");
                return arenaStats;
            });
        }

        // Helper methods
        private double CalculateCorrelation(List<PirateFeatureRecord> features, Func<PirateFeatureRecord, double> xSelector, Func<PirateFeatureRecord, double> ySelector)
        {
            var pairs = features.Select(f => new { X = xSelector(f), Y = ySelector(f) }).ToList();
            
            if (pairs.Count < 2) return 0;
            
            var xMean = pairs.Average(p => p.X);
            var yMean = pairs.Average(p => p.Y);
            
            var numerator = pairs.Sum(p => (p.X - xMean) * (p.Y - yMean));
            var xSumSquares = pairs.Sum(p => Math.Pow(p.X - xMean, 2));
            var ySumSquares = pairs.Sum(p => Math.Pow(p.Y - yMean, 2));
            
            var denominator = Math.Sqrt(xSumSquares * ySumSquares);
            
            return denominator == 0 ? 0 : numerator / denominator;
        }

        private double CalculateStandardDeviation(IEnumerable<double> values)
        {
            var valueList = values.ToList();
            var mean = valueList.Average();
            var variance = valueList.Sum(x => Math.Pow(x - mean, 2)) / valueList.Count;
            return Math.Sqrt(variance);
        }
    }
}