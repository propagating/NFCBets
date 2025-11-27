using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.ML;
using NFCBets.Services;

public class ParallelMLAnalysisService
{
    private readonly IDbContextFactory<NfcbetsContext> _contextFactory;
    private readonly IMLModelTrainingService _mlService;
    private readonly ILogger<ParallelMLAnalysisService> _logger;

    public ParallelMLAnalysisService(
        IDbContextFactory<NfcbetsContext> contextFactory,
        IMLModelTrainingService mlService,
        ILogger<ParallelMLAnalysisService> logger)
    {
        _contextFactory = contextFactory;
        _mlService = mlService;
        _logger = logger;
    }

    public async Task<ComprehensiveAnalysisResult> RunParallelAnalysisAsync()
    {
        _logger.LogInformation("🚀 Starting parallel comprehensive analysis");
        
        // Step 1: Generate all features (multithreaded)
        var featureService = new MultithreadedFeatureEngineeringService(_contextFactory);
        var allFeatures = await featureService.CreateTrainingFeaturesAsync();
        var validFeatures = allFeatures.Where(f => f.Won.HasValue).ToList();
        
        _logger.LogInformation($"✅ Generated {validFeatures.Count} features");

        // Step 2: Run multiple validation strategies in parallel
        var validationTasks = new List<Task<ValidationResult>>
        {
            Task.Run(() => ValidateRecentVsHistoricalAsync(validFeatures)),
            Task.Run(() => ValidateMultipleTimePeriodsAsync(validFeatures)),
            Task.Run(() => ValidateRollingWindowsAsync(validFeatures)),
            Task.Run(() => ValidateCrossValidationAsync(validFeatures))
        };

        // Step 3: Run analysis tasks in parallel
        var analysisTasks = new List<Task>
        {
            Task.Run(() => AnalyzePiratePerformanceAsync(validFeatures)),
            Task.Run(() => AnalyzeTemporalTrendsAsync(validFeatures)),
            Task.Run(() => AnalyzeFeatureDistributionsAsync(validFeatures)),
            Task.Run(() => AnalyzeArenaEffectsAsync(validFeatures)),
            Task.Run(() => AnalyzeFoodAdjustmentDistribution(validFeatures))
        };

        // Wait for validation results
        var validationResults = await Task.WhenAll(validationTasks);
        
        // Wait for analysis to complete
        await Task.WhenAll(analysisTasks);

        return new ComprehensiveAnalysisResult
        {
            TotalFeatures = allFeatures.Count,
            ValidFeatures = validFeatures.Count,
            ValidationResults = validationResults.ToList(),
            ProcessingTime = DateTime.Now.Subtract(DateTime.Now) // This would be tracked properly
        };
    }

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

    private async Task<ValidationResult> ValidateMultipleTimePeriodsAsync(List<PirateFeatureRecord> features)
    {
        var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
        var quarterSize = sortedFeatures.Count / 4;
        
        // Train on first 3 quarters, test on last quarter
        var trainData = sortedFeatures.Take(quarterSize * 3).ToList();
        var testData = sortedFeatures.Skip(quarterSize * 3).ToList();
        
        var trainingResult = await _mlService.TrainWithCustomDataAsync(trainData);
        var evaluation = await _mlService.EvaluateModelAsync(trainingResult.Model, testData);
        
        return new ValidationResult
        {
            Name = "Multiple Time Periods",
            TrainSize = trainData.Count,
            TestSize = testData.Count,
            Evaluation = evaluation
        };
    }

    private async Task<ValidationResult> ValidateRollingWindowsAsync(List<PirateFeatureRecord> features)
    {
        _logger.LogInformation("🔄 Running rolling window validation...");
        
        var results = new List<ModelEvaluationResult>();
        const int windowSize = 2000;
        const int testSize = 500;
        const int stepSize = 1000;
        
        var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
        var totalFeatures = sortedFeatures.Count;
        
        var tasks = new List<Task<ModelEvaluationResult>>();
        
        for (int start = 0; start + windowSize + testSize < totalFeatures; start += stepSize)
        {
            var windowStart = start;
            tasks.Add(Task.Run(async () =>
            {
                var trainData = sortedFeatures.Skip(windowStart).Take(windowSize).ToList();
                var testData = sortedFeatures.Skip(windowStart + windowSize).Take(testSize).ToList();
                
                var model = await _mlService.TrainWithCustomDataAsync(trainData);
                return await _mlService.EvaluateModelAsync(model.Model, testData);
            }));
        }
        
        var windowResults = await Task.WhenAll(tasks);
        
        // Average the results
        var avgAccuracy = windowResults.Average(r => r.Accuracy);
        var avgAUC = windowResults.Average(r => r.AUC);
        var avgROI = windowResults.Average(r => r.CustomMetrics.GetValueOrDefault("BettingROI", 0));
        
        return new ValidationResult
        {
            Name = "Rolling Windows",
            TrainSize = windowSize,
            TestSize = testSize,
            Evaluation = new ModelEvaluationResult
            {
                Accuracy = avgAccuracy,
                AUC = avgAUC,
                CustomMetrics = new Dictionary<string, double> { {"BettingROI", avgROI} },
                TestDataCount = testSize
            },
            WindowCount = windowResults.Length
        };
    }

    private async Task<ValidationResult> ValidateCrossValidationAsync(List<PirateFeatureRecord> features)
    {
        _logger.LogInformation("🔀 Running k-fold cross-validation...");
        
        const int k = 5;
        var foldSize = features.Count / k;
        var results = new List<ModelEvaluationResult>();
        
        var tasks = Enumerable.Range(0, k).Select(async fold =>
        {
            var testStart = fold * foldSize;
            var testData = features.Skip(testStart).Take(foldSize).ToList();
            var trainData = features.Take(testStart).Concat(features.Skip(testStart + foldSize)).ToList();
            
            var model = await _mlService.TrainWithCustomDataAsync(trainData);
            return await _mlService.EvaluateModelAsync(model.Model, testData);
        });
        
        var foldResults = await Task.WhenAll(tasks);
        
        return new ValidationResult
        {
            Name = "K-Fold Cross-Validation",
            TrainSize = features.Count - foldSize,
            TestSize = foldSize,
            Evaluation = new ModelEvaluationResult
            {
                Accuracy = foldResults.Average(r => r.Accuracy),
                AUC = foldResults.Average(r => r.AUC),
                F1Score = foldResults.Average(r => r.F1Score),
                CustomMetrics = new Dictionary<string, double>
                {
                    {"BettingROI", foldResults.Average(r => r.CustomMetrics.GetValueOrDefault("BettingROI", 0))},
                    {"StandardDeviation", CalculateStandardDeviation(foldResults.Select(r => r.Accuracy))}
                }
            },
            FoldCount = k
        };
    }

    // Analysis methods running in parallel
    private async Task AnalyzePiratePerformanceAsync(List<PirateFeatureRecord> features)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation("🏴‍☠️ Analyzing pirate performance distribution...");
            
            var pirateStats = features.AsParallel()
                .GroupBy(f => f.PirateId)
                .Select(g => new
                {
                    PirateId = g.Key,
                    TotalAppearances = g.Count(),
                    Wins = g.Count(f => f.Won == true),
                    WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                    AvgOdds = g.Average(f => f.CurrentOdds),
                    AvgFoodAdjustment = g.Average(f => f.FoodAdjustment)
                })
                .OrderByDescending(p => p.TotalAppearances)
                .ToList();
            
            // Save detailed pirate analysis
            SavePirateAnalysisReport(pirateStats);
        });
    }

    private async Task AnalyzeTemporalTrendsAsync(List<PirateFeatureRecord> features)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation("📅 Analyzing temporal trends...");
            
            var monthlyStats = features.AsParallel()
                .GroupBy(f => f.RoundId / 100 * 100) // Group by ~100 round periods
                .Select(g => new
                {
                    PeriodStart = g.Key,
                    Records = g.Count(),
                    WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                    AvgOdds = g.Average(f => f.CurrentOdds),
                    AvgFoodImpact = g.Average(f => f.FoodAdjustment),
                    UniquePirates = g.Select(f => f.PirateId).Distinct().Count()
                })
                .OrderBy(x => x.PeriodStart)
                .ToList();
            
            SaveTemporalTrendsReport(monthlyStats);
        });
    }
    /// <summary>
    /// Analyze the distribution and statistics of all features
    /// </summary>
    private async Task AnalyzeFeatureDistributionsAsync(List<PirateFeatureRecord> features)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation("📈 Analyzing feature distributions...");
            
            var validFeatures = features.Where(f => f.Won.HasValue).ToList();
            
            var distributions = new
            {
                OddsDistribution = AnalyzeOddsDistribution(validFeatures),
                FoodAdjustmentDistribution = AnalyzeFoodAdjustmentDistribution(validFeatures),
                WinRateDistributions = AnalyzeWinRateDistributions(validFeatures),
                PositionalAnalysis = AnalyzePositionalEffects(validFeatures),
                FeatureRanges = CalculateFeatureRanges(validFeatures)
            };
            
            SaveFeatureDistributionReport(distributions);
            _logger.LogInformation($"📊 Feature distribution analysis complete");
        });
    }
    
        /// <summary>
    /// Analyze arena-specific patterns and effects
    /// </summary>
    private async Task AnalyzeArenaEffectsAsync(List<PirateFeatureRecord> features)
    {
        await Task.Run(() =>
        {
            _logger.LogInformation("🏟️ Analyzing arena-specific effects...");
            
            var validFeatures = features.Where(f => f.Won.HasValue).ToList();
            
            var arenaAnalysis = validFeatures.AsParallel()
                .GroupBy(f => f.ArenaId)
                .Select(g => new ArenaAnalysisResult
                {
                    ArenaId = g.Key,
                    TotalRounds = g.Select(f => f.RoundId).Distinct().Count(),
                    TotalRecords = g.Count(),
                    
                    // Basic statistics
                    WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                    AvgOdds = g.Average(f => f.CurrentOdds),
                    MedianOdds = CalculateMedian(g.Select(f => (double)f.CurrentOdds)),
                    
                    // Pirate diversity
                    UniquePirates = g.Select(f => f.PirateId).Distinct().Count(),
                    
                    // Position effects
                    Position0WinRate = CalculatePositionWinRate(g, 0),
                    Position1WinRate = CalculatePositionWinRate(g, 1),
                    Position2WinRate = CalculatePositionWinRate(g, 2),
                    Position3WinRate = CalculatePositionWinRate(g, 3),
                    
                    // Favorite/underdog performance
                    FavoriteWinRate = CalculateFavoriteWinRate(g),
                    UnderdogWinRate = CalculateUnderdogWinRate(g),
                    
                    // Food effects
                    AvgFoodAdjustment = g.Average(f => f.FoodAdjustment),
                    FoodEffectRange = new
                    {
                        Min = g.Min(f => f.FoodAdjustment),
                        Max = g.Max(f => f.FoodAdjustment)
                    },
                    
                    // Competitive dynamics
                    AvgFieldStrengthRank = g.Average(f => f.FieldStrengthRank),
                    AvgHeadToHeadAppearances = g.Average(f => f.HeadToHeadAppearances),
                    
                    // Volatility measures
                    OddsVolatility = CalculateStandardDeviation(g.Select(f => (double)f.CurrentOdds)),
                    WinRateVolatility = CalculateWinRateVolatilityByPeriod(g)
                })
                .OrderBy(a => a.ArenaId)
                .ToList();
            
            SaveArenaAnalysisReport(arenaAnalysis);
            _logger.LogInformation($"🏟️ Arena analysis complete - analyzed {arenaAnalysis.Count} arenas");
        });
    }
        
      // Helper methods for arena analysis
    private double CalculatePositionWinRate(IGrouping<int, PirateFeatureRecord> arenaGroup, int position)
    {
        var positionRecords = arenaGroup.Where(f => f.Position == position).ToList();
        return positionRecords.Any() ? positionRecords.Count(f => f.Won == true) / (double)positionRecords.Count : 0;
    }

    private double CalculateFavoriteWinRate(IGrouping<int, PirateFeatureRecord> arenaGroup)
    {
        var favoriteRecords = arenaGroup.Where(f => f.IsFavorite).ToList();
        return favoriteRecords.Any() ? favoriteRecords.Count(f => f.Won == true) / (double)favoriteRecords.Count : 0;
    }

    private double CalculateUnderdogWinRate(IGrouping<int, PirateFeatureRecord> arenaGroup)
    {
        var underdogRecords = arenaGroup.Where(f => f.IsUnderdog).ToList();
        return underdogRecords.Any() ? underdogRecords.Count(f => f.Won == true) / (double)underdogRecords.Count : 0;
    }

    private double CalculateWinRateVolatilityByPeriod(IGrouping<int, PirateFeatureRecord> arenaGroup)
    {
        // Group by time periods and calculate win rate variance
        var periodWinRates = arenaGroup
            .GroupBy(f => f.RoundId / 100) // Group by ~100 round periods
            .Where(g => g.Count() > 10) // Only periods with sufficient data
            .Select(g => g.Count(f => f.Won == true) / (double)g.Count())
            .ToList();

        return periodWinRates.Count > 1 ? CalculateStandardDeviation(periodWinRates) : 0;
    }

    // Helper methods for feature distributions
    private object AnalyzeOddsDistribution(List<PirateFeatureRecord> features)
    {
        var odds = features.Select(f => f.CurrentOdds).ToList();
        
        return new
        {
            Mean = odds.Average(),
            Median = CalculateMedian(odds.Select(o => (double)o)),
            Min = odds.Min(),
            Max = odds.Max(),
            StandardDeviation = CalculateStandardDeviation(odds.Select(o => (double)o)),
            
            // Odds ranges
            Favorites = odds.Count(o => o == 1), // 1:1 odds
            LowOdds = odds.Count(o => o >= 2 && o <= 5),
            MediumOdds = odds.Count(o => o >= 6 && o <= 10),
            HighOdds = odds.Count(o => o > 10),
            
            // Win rates by odds range
            FavoriteWinRate = CalculateWinRateForOddsRange(features, 1, 1),
            LowOddsWinRate = CalculateWinRateForOddsRange(features, 2, 5),
            MediumOddsWinRate = CalculateWinRateForOddsRange(features, 6, 10),
            HighOddsWinRate = CalculateWinRateForOddsRange(features, 11, 100)
        };
    }

    private object AnalyzeFoodAdjustmentDistribution(List<PirateFeatureRecord> features)
    {
        var foodAdjustments = features.Select(f => f.FoodAdjustment).ToList();
        
        return new
        {
            Mean = foodAdjustments.Average(),
            Min = foodAdjustments.Min(),
            Max = foodAdjustments.Max(),
            
            // Distribution by adjustment level
            Negative3 = foodAdjustments.Count(f => f == -3),
            Negative2 = foodAdjustments.Count(f => f == -2),
            Negative1 = foodAdjustments.Count(f => f == -1),
            Neutral = foodAdjustments.Count(f => f == 0),
            Positive1 = foodAdjustments.Count(f => f == 1),
            Positive2 = foodAdjustments.Count(f => f == 2),
            Positive3 = foodAdjustments.Count(f => f == 3),
            
            // Win rates by food adjustment
            WinRatesByAdjustment = Enumerable.Range(-3, 7)
                .ToDictionary(
                    adj => adj,
                    adj => CalculateWinRateForFoodAdjustment(features, adj)
                )
        };
    }

    private object AnalyzeWinRateDistributions(List<PirateFeatureRecord> features)
    {
        return new
        {
            OverallWinRateStats = AnalyzeWinRateFeature(features, f => f.OverallWinRate),
            RecentWinRateStats = AnalyzeWinRateFeature(features, f => f.RecentWinRate),
            ArenaWinRateStats = AnalyzeWinRateFeature(features, f => f.ArenaWinRate),
            HeadToHeadWinRateStats = AnalyzeWinRateFeature(features, f => f.HeadToHeadWinRate),
            
            // Correlation with actual outcomes
            OverallWinRateCorrelation = CalculateCorrelation(features, f => f.OverallWinRate, f => f.Won == true ? 1.0 : 0.0),
            ArenaWinRateCorrelation = CalculateCorrelation(features, f => f.ArenaWinRate, f => f.Won == true ? 1.0 : 0.0)
        };
    }
 private object AnalyzePositionalEffects(List<PirateFeatureRecord> features)
    {
        return features.AsParallel()
            .GroupBy(f => f.Position)
            .Select(g => new
            {
                Position = g.Key,
                Count = g.Count(),
                WinRate = g.Count(f => f.Won == true) / (double)g.Count(),
                AvgOdds = g.Average(f => f.CurrentOdds),
                AvgFoodAdjustment = g.Average(f => f.FoodAdjustment)
            })
            .OrderBy(p => p.Position)
            .ToList();
    }

    private object CalculateFeatureRanges(List<PirateFeatureRecord> features)
    {
        return new
        {
            OddsRange = new { Min = features.Min(f => f.CurrentOdds), Max = features.Max(f => f.CurrentOdds) },
            FoodAdjustmentRange = new { Min = features.Min(f => f.FoodAdjustment), Max = features.Max(f => f.FoodAdjustment) },
            WinStreakRange = new { Min = features.Min(f => f.WinStreak), Max = features.Max(f => f.WinStreak) },
            AppearancesRange = new { Min = features.Min(f => f.TotalAppearances), Max = features.Max(f => f.TotalAppearances) }
        };
    }

    // Utility methods
    private double CalculateWinRateForOddsRange(List<PirateFeatureRecord> features, int minOdds, int maxOdds)
    {
        var rangeFeatures = features.Where(f => f.CurrentOdds >= minOdds && f.CurrentOdds <= maxOdds && f.Won.HasValue).ToList();
        return rangeFeatures.Any() ? rangeFeatures.Count(f => f.Won == true) / (double)rangeFeatures.Count : 0;
    }

    private double CalculateWinRateForFoodAdjustment(List<PirateFeatureRecord> features, int adjustment)
    {
        var adjustmentFeatures = features.Where(f => f.FoodAdjustment == adjustment && f.Won.HasValue).ToList();
        return adjustmentFeatures.Any() ? adjustmentFeatures.Count(f => f.Won == true) / (double)adjustmentFeatures.Count : 0;
    }

    private object AnalyzeWinRateFeature(List<PirateFeatureRecord> features, Func<PirateFeatureRecord, double> selector)
    {
        var values = features.Select(selector).ToList();
        
        return new
        {
            Mean = values.Average(),
            Median = CalculateMedian(values),
            Min = values.Min(),
            Max = values.Max(),
            StandardDeviation = CalculateStandardDeviation(values),
            
            // Percentiles
            P25 = CalculatePercentile(values, 0.25),
            P75 = CalculatePercentile(values, 0.75),
            P90 = CalculatePercentile(values, 0.90)
        };
    }

    private double CalculateMedian(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        
        int middle = sorted.Count / 2;
        if (sorted.Count % 2 == 0)
            return (sorted[middle - 1] + sorted[middle]) / 2;
        else
            return sorted[middle];
    }

    private double CalculatePercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (sorted.Count == 0) return 0;
        
        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));
        
        return sorted[index];
    }

    private double CalculateCorrelation(List<PirateFeatureRecord> features, Func<PirateFeatureRecord, double> xSelector, Func<PirateFeatureRecord, double> ySelector)
    {
        var validFeatures = features.Where(f => f.Won.HasValue).ToList();
        var pairs = validFeatures.Select(f => new { X = xSelector(f), Y = ySelector(f) }).ToList();
        
        if (pairs.Count < 2) return 0;
        
        var xMean = pairs.Average(p => p.X);
        var yMean = pairs.Average(p => p.Y);
        
        var numerator = pairs.Sum(p => (p.X - xMean) * (p.Y - yMean));
        var xSumSquares = pairs.Sum(p => Math.Pow(p.X - xMean, 2));
        var ySumSquares = pairs.Sum(p => Math.Pow(p.Y - yMean, 2));
        
        var denominator = Math.Sqrt(xSumSquares * ySumSquares);
        
        return denominator == 0 ? 0 : numerator / denominator;
    }

    // Report saving methods
    private void SaveFeatureDistributionReport(object distributions)
    {
        try
        {
            var json = JsonSerializer.Serialize(distributions, new JsonSerializerOptions { WriteIndented = true });
            var fileName = $"feature_distributions_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            File.WriteAllText(Path.Combine("Reports", fileName), json);
            
            _logger.LogInformation($"📄 Feature distribution report saved to {fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save feature distribution report");
        }
    }
    
    private void SaveArenaAnalysisReport(List<ArenaAnalysisResult> arenaAnalysis)
    {
        try
        {
            var report = new
            {
                GeneratedAt = DateTime.UtcNow,
                Summary = new
                {
                    TotalArenas = arenaAnalysis.Count,
                    OverallWinRate = arenaAnalysis.Average(a => a.WinRate),
                    MostCompetitiveArena = arenaAnalysis.OrderBy(a => a.FavoriteWinRate).First().ArenaId,
                    MostPredictableArena = arenaAnalysis.OrderByDescending(a => a.FavoriteWinRate).First().ArenaId
                },
                ArenaDetails = arenaAnalysis
            };
            
            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            var fileName = $"arena_analysis_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            
            Directory.CreateDirectory("Reports");
            File.WriteAllText(Path.Combine("Reports", fileName), json);
            
            _logger.LogInformation($"📄 Arena analysis report saved to {fileName}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save arena analysis report");
        }
    }

    // Data models for arena analysis
    public class ArenaAnalysisResult
    {
        public int ArenaId { get; set; }
        public int TotalRounds { get; set; }
        public int TotalRecords { get; set; }
        public double WinRate { get; set; }
        public double AvgOdds { get; set; }
        public double MedianOdds { get; set; }
        public int UniquePirates { get; set; }
        
        // Position effects
        public double Position0WinRate { get; set; }
        public double Position1WinRate { get; set; }
        public double Position2WinRate { get; set; }
        public double Position3WinRate { get; set; }
        
        // Favorite/underdog performance
        public double FavoriteWinRate { get; set; }
        public double UnderdogWinRate { get; set; }
        
        // Food effects
        public double AvgFoodAdjustment { get; set; }
        public object FoodEffectRange { get; set; } = new();
        
        // Competitive dynamics
        public double AvgFieldStrengthRank { get; set; }
        public double AvgHeadToHeadAppearances { get; set; }
        
        // Volatility
        public double OddsVolatility { get; set; }
        public double WinRateVolatility { get; set; }
    }
    

    private void SavePirateAnalysisReport(dynamic pirateStats)
    {
        // Implementation for saving detailed reports
        _logger.LogInformation($"📊 Pirate analysis complete - analyzed {pirateStats.Count} pirates");
    }

    private void SaveTemporalTrendsReport(dynamic monthlyStats)
    {
        _logger.LogInformation($"📈 Temporal analysis complete - analyzed {monthlyStats.Count} time periods");
    }

    private double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var valueList = values.ToList();
        var mean = valueList.Average();
        var variance = valueList.Sum(x => Math.Pow(x - mean, 2)) / valueList.Count;
        return Math.Sqrt(variance);
    }
}

// Result classes
public class ValidationResult
{
    public string Name { get; set; } = "";
    public int TrainSize { get; set; }
    public int TestSize { get; set; }
    public string TrainRoundRange { get; set; } = "";
    public string TestRoundRange { get; set; } = "";
    public ModelEvaluationResult Evaluation { get; set; } = new();
    public int? WindowCount { get; set; }
    public int? FoldCount { get; set; }
}

public class ComprehensiveAnalysisResult
{
    public int TotalFeatures { get; set; }
    public int ValidFeatures { get; set; }
    public List<ValidationResult> ValidationResults { get; set; } = new();
    public TimeSpan ProcessingTime { get; set; }
}