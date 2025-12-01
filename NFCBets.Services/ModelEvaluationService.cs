using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class ModelEvaluationService : IModelEvaluationService
{
    private readonly MLContext _mlContext;

    public ModelEvaluationService()
    {
        _mlContext = new MLContext(42);
    }

    public async Task<ModelEvaluationReport> EvaluateModelAsync(ITransformer model, List<PirateFeatureRecord> testData)
    {
        Console.WriteLine("📊 Evaluating model performance...");

        var mlTestData = ConvertToMLFormat(testData);
        var testDataView = _mlContext.Data.LoadFromEnumerable(mlTestData);
        var predictions = model.Transform(testDataView);

        // Get binary classification metrics
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, nameof(MlPirateFeature.Won));

        // Get detailed predictions for analysis
        var predictionResults = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictions, false).ToList();

        // Analyze by different groups
        var byOdds = AnalyzeByOddsRange(mlTestData, predictionResults);
        var byFoodAdjustment = AnalyzeByFoodAdjustment(mlTestData, predictionResults);
        var calibration = AnalyzeCalibration(mlTestData, predictionResults);

        var report = new ModelEvaluationReport
        {
            // Overall metrics
            Accuracy = metrics.Accuracy,
            AUC = metrics.AreaUnderRocCurve,
            F1Score = metrics.F1Score,
            Precision = metrics.PositivePrecision,
            Recall = metrics.PositiveRecall,
            LogLoss = metrics.LogLoss,

            // Detailed analysis
            PerformanceByOdds = byOdds,
            PerformanceByFoodAdjustment = byFoodAdjustment,
            CalibrationMetrics = calibration,

            TestDataSize = testData.Count
        };

        DisplayEvaluationReport(report);
        return report;
    }

    public async Task<FeatureImportanceReport> AnalyzeFeatureImportanceAsync(List<PirateFeatureRecord> trainingData)
    {
        Console.WriteLine("🔍 Analyzing feature importance...");

        var report = new FeatureImportanceReport();
        var mlData = ConvertToMLFormat(trainingData);

        // Test each feature individually
        var featureTests = new List<(string FeatureName, double AUC)>();

        // Baseline: All features
        var baselineAUC = await TrainAndGetAUC(mlData, null);
        Console.WriteLine($"   Baseline AUC (all features): {baselineAUC:F4}");

        // Test removing each feature
        var featureNames = new[]
        {
            nameof(MlPirateFeature.Position),
            nameof(MlPirateFeature.CurrentOdds),
            nameof(MlPirateFeature.FoodAdjustment),
            nameof(MlPirateFeature.Strength),
            nameof(MlPirateFeature.Weight),
            nameof(MlPirateFeature.HistoricalWinRate),
            nameof(MlPirateFeature.TotalAppearances),
            nameof(MlPirateFeature.ArenaWinRate),
            nameof(MlPirateFeature.RecentWinRate),
            nameof(MlPirateFeature.WinRateVsCurrentRivals),
            nameof(MlPirateFeature.MatchesVsCurrentRivals),
            nameof(MlPirateFeature.AvgRivalStrength)
        };

        foreach (var featureName in featureNames)
        {
            var aucWithoutFeature = await TrainAndGetAUC(mlData, featureName);
            var importance = baselineAUC - aucWithoutFeature;

            featureTests.Add((featureName, importance));
            Console.WriteLine($"   {featureName}: {importance:+0.0000;-0.0000} (lower = more important)");
        }

        report.FeatureImportance = featureTests.OrderByDescending(f => f.AUC).ToList();
        return report;
    }

    public async Task<DataLeakageReport> CheckForDataLeakageAsync(List<PirateFeatureRecord> features,
        NfcbetsContext context)
    {
        Console.WriteLine("🚨 Checking for data leakage...");

        var report = new DataLeakageReport();
        var leakageIssues = new List<string>();

        // Check 1: Ensure historical features only use past data
        foreach (var feature in features.Where(f => f.IsWinner.HasValue).Take(50)) // Sample check
            if (feature.IsWinner.Value)
            {
                // Check for suspiciously perfect correlations
                if (feature.HistoricalWinRate > 0.95)
                    leakageIssues.Add(
                        $"Round {feature.RoundId}, Pirate {feature.PirateId}: Suspiciously high historical win rate ({feature.HistoricalWinRate:P2})");

                if (feature.WinRateVsCurrentRivals > 0.95)
                    leakageIssues.Add(
                        $"Round {feature.RoundId}, Pirate {feature.PirateId}: Suspiciously high rival win rate ({feature.WinRateVsCurrentRivals:P2})");
            }

        // Check 2: Temporal validation
        var roundGroups = features
            .Where(f => f.IsWinner.HasValue)
            .GroupBy(f => f.RoundId)
            .OrderBy(g => g.Key)
            .ToList();

        // Sample check: verify historical counts don't include current round
        foreach (var roundGroup in roundGroups.Take(10))
        {
            var roundId = roundGroup.Key;

            foreach (var feature in roundGroup.Take(5)) // Sample 5 pirates per round
            {
                var actualHistoricalCount = await context.RoundResults
                    .Where(rr => rr.PirateId == feature.PirateId &&
                                 rr.RoundId < roundId &&
                                 rr.IsComplete)
                    .CountAsync();

                if (feature.TotalAppearances > actualHistoricalCount + 1)
                    leakageIssues.Add(
                        $"Round {roundId}, Pirate {feature.PirateId}: TotalAppearances={feature.TotalAppearances} exceeds historical={actualHistoricalCount}");
            }
        }

        // Check 3: Train/test split validation
        var sortedFeatures = features.Where(f => f.IsWinner.HasValue).OrderBy(f => f.RoundId).ToList();

        if (sortedFeatures.Count >= 100)
        {
            var splitPoint = (int)(sortedFeatures.Count * 0.8);
            var trainRounds = sortedFeatures.Take(splitPoint).Select(f => f.RoundId).Distinct().OrderBy(r => r)
                .ToList();
            var testRounds = sortedFeatures.Skip(splitPoint).Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();

            var overlap = trainRounds.Intersect(testRounds).ToList();

            if (overlap.Any()) leakageIssues.Add($"Train/test overlap: {overlap.Count} rounds appear in both sets");

            // Ensure test rounds come AFTER train rounds
            if (trainRounds.Any() && testRounds.Any() && trainRounds.Max() >= testRounds.Min())
                leakageIssues.Add(
                    $"Temporal violation: Train rounds ({trainRounds.Max()}) overlap with test rounds ({testRounds.Min()})");

            report.TrainRoundRange = trainRounds.Any() ? $"{trainRounds.Min()} to {trainRounds.Max()}" : "N/A";
            report.TestRoundRange = testRounds.Any() ? $"{testRounds.Min()} to {testRounds.Max()}" : "N/A";
        }

        report.LeakageIssues = leakageIssues;
        report.HasLeakage = leakageIssues.Any();

        if (report.HasLeakage)
        {
            Console.WriteLine($"⚠️ Found {leakageIssues.Count} potential issues:");
            foreach (var issue in leakageIssues.Take(5)) Console.WriteLine($"   - {issue}");
        }
        else
        {
            Console.WriteLine("✅ No data leakage detected");
        }

        return report;
    }

    private List<MlPirateFeature> ConvertToMLFormat(List<PirateFeatureRecord> features)
    {
        return features.Select(f => new MlPirateFeature
        {
            Position = f.Position,
            CurrentOdds = f.CurrentOdds,
            FoodAdjustment = f.FoodAdjustment,
            Strength = f.Strength,
            Weight = f.Weight,
            HistoricalWinRate = (float)f.HistoricalWinRate,
            TotalAppearances = f.TotalAppearances,
            ArenaWinRate = (float)f.ArenaWinRate,
            RecentWinRate = (float)f.RecentWinRate,
            WinRateVsCurrentRivals = (float)f.WinRateVsCurrentRivals,
            MatchesVsCurrentRivals = f.MatchesVsCurrentRivals,
            AvgRivalStrength = (float)f.AvgRivalStrength,
            Won = f.IsWinner ?? false
        }).ToList();
    }

    private async Task<double> TrainAndGetAUC(List<MlPirateFeature> data, string? excludedFeature)
    {
        var dataView = _mlContext.Data.LoadFromEnumerable(data);
        var split = _mlContext.Data.TrainTestSplit(dataView, 0.2);

        var featureColumns = new List<string>
        {
            nameof(MlPirateFeature.Position),
            nameof(MlPirateFeature.CurrentOdds),
            nameof(MlPirateFeature.FoodAdjustment),
            nameof(MlPirateFeature.Strength),
            nameof(MlPirateFeature.Weight),
            nameof(MlPirateFeature.HistoricalWinRate),
            nameof(MlPirateFeature.TotalAppearances),
            nameof(MlPirateFeature.ArenaWinRate),
            nameof(MlPirateFeature.RecentWinRate),
            nameof(MlPirateFeature.WinRateVsCurrentRivals),
            nameof(MlPirateFeature.MatchesVsCurrentRivals),
            nameof(MlPirateFeature.AvgRivalStrength)
        };

        if (excludedFeature != null) featureColumns.Remove(excludedFeature);

        var pipeline = _mlContext.Transforms.Concatenate("Features", featureColumns.ToArray())
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                nameof(MlPirateFeature.Won),
                numberOfIterations: 50));

        var model = pipeline.Fit(split.TrainSet);
        var predictions = model.Transform(split.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, nameof(MlPirateFeature.Won));

        return metrics.AreaUnderRocCurve;
    }

    private Dictionary<string, PerformanceMetrics> AnalyzeByOddsRange(List<MlPirateFeature> actual,
        List<PiratePredictionOutput> predictions)
    {
        var results = new Dictionary<string, PerformanceMetrics>();
        var oddsRanges = new[] { (1, 2, "Favorites"), (3, 5, "Medium"), (6, 10, "Longshots"), (11, 99, "Very Long") };

        foreach (var (min, max, label) in oddsRanges)
        {
            var indices = actual
                .Select((a, i) => new { Actual = a, Index = i })
                .Where(x => x.Actual.CurrentOdds >= min && x.Actual.CurrentOdds <= max)
                .Select(x => x.Index)
                .ToList();

            if (!indices.Any()) continue;

            var actualInRange = indices.Select(i => actual[i].Won).ToList();
            var predictionsInRange = indices.Select(i => predictions[i].Probability).ToList();

            var accuracy = CalculateAccuracy(actualInRange, predictionsInRange);
            var avgPredProb = predictionsInRange.Average();
            var actualWinRate = actualInRange.Count(w => w) / (double)actualInRange.Count;

            results[label] = new PerformanceMetrics
            {
                Count = indices.Count,
                Accuracy = accuracy,
                AveragePredictedProbability = avgPredProb,
                ActualWinRate = actualWinRate,
                Calibration = Math.Abs(avgPredProb - actualWinRate)
            };
        }

        return results;
    }

    private Dictionary<int, PerformanceMetrics> AnalyzeByFoodAdjustment(List<MlPirateFeature> actual,
        List<PiratePredictionOutput> predictions)
    {
        var results = new Dictionary<int, PerformanceMetrics>();

        for (var adj = -3; adj <= 3; adj++)
        {
            var indices = actual
                .Select((a, i) => new { Actual = a, Index = i })
                .Where(x => (int)x.Actual.FoodAdjustment == adj)
                .Select(x => x.Index)
                .ToList();

            if (!indices.Any()) continue;

            var actualInRange = indices.Select(i => actual[i].Won).ToList();
            var predictionsInRange = indices.Select(i => predictions[i].Probability).ToList();

            results[adj] = new PerformanceMetrics
            {
                Count = indices.Count,
                Accuracy = CalculateAccuracy(actualInRange, predictionsInRange),
                AveragePredictedProbability = predictionsInRange.Average(),
                ActualWinRate = actualInRange.Count(w => w) / (double)actualInRange.Count,
                Calibration = Math.Abs(predictionsInRange.Average() -
                                       actualInRange.Count(w => w) / (double)actualInRange.Count)
            };
        }

        return results;
    }

    private CalibrationMetrics AnalyzeCalibration(List<MlPirateFeature> actual,
        List<PiratePredictionOutput> predictions)
    {
        var bins = 10;
        var binMetrics = new List<BinCalibration>();

        for (var i = 0; i < bins; i++)
        {
            var minProb = i / (double)bins;
            var maxProb = (i + 1) / (double)bins;

            var indices = predictions
                .Select((p, idx) => new { Prediction = p, Index = idx })
                .Where(x => x.Prediction.Probability >= minProb && x.Prediction.Probability < maxProb)
                .Select(x => x.Index)
                .ToList();

            if (!indices.Any()) continue;

            var actualWins = indices.Count(idx => actual[idx].Won);
            var avgPredictedProb = indices.Average(idx => predictions[idx].Probability);
            var actualWinRate = actualWins / (double)indices.Count;

            binMetrics.Add(new BinCalibration
            {
                ProbabilityRange = $"{minProb:P0}-{maxProb:P0}",
                Count = indices.Count,
                AveragePredictedProbability = avgPredictedProb,
                ActualWinRate = actualWinRate,
                Calibration = Math.Abs(avgPredictedProb - actualWinRate)
            });
        }

        return new CalibrationMetrics
        {
            Bins = binMetrics,
            OverallCalibrationError = binMetrics.Average(b => b.Calibration)
        };
    }

    private double CalculateAccuracy(List<bool> actual, List<float> predictions)
    {
        var correct = 0;
        for (var i = 0; i < actual.Count; i++)
        {
            var predicted = predictions[i] > 0.5f;
            if (predicted == actual[i])
                correct++;
        }

        return correct / (double)actual.Count;
    }

    private void DisplayEvaluationReport(ModelEvaluationReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 MODEL EVALUATION REPORT");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"\n📈 Overall Performance (n={report.TestDataSize}):");
        Console.WriteLine($"   Accuracy:  {report.Accuracy:P2}");
        Console.WriteLine($"   AUC:       {report.AUC:F4}");
        Console.WriteLine($"   F1 Score:  {report.F1Score:F4}");
        Console.WriteLine($"   Precision: {report.Precision:F4}");
        Console.WriteLine($"   Recall:    {report.Recall:F4}");
        Console.WriteLine($"   Log Loss:  {report.LogLoss:F4}");

        Console.WriteLine("\n📊 Performance by Odds Range:");
        foreach (var (range, metrics) in report.PerformanceByOdds)
            Console.WriteLine(
                $"   {range,-12}: Acc={metrics.Accuracy:P2}, Pred={metrics.AveragePredictedProbability:P2}, Actual={metrics.ActualWinRate:P2}, Cal={metrics.Calibration:F4}");

        Console.WriteLine("\n🍕 Performance by Food Adjustment:");
        foreach (var (adj, metrics) in report.PerformanceByFoodAdjustment.OrderBy(kv => kv.Key))
            Console.WriteLine(
                $"   {adj,3}: Acc={metrics.Accuracy:P2}, Pred={metrics.AveragePredictedProbability:P2}, Actual={metrics.ActualWinRate:P2}, n={metrics.Count}");

        Console.WriteLine("\n🎯 Calibration Analysis:");
        Console.WriteLine($"   Overall Calibration Error: {report.CalibrationMetrics.OverallCalibrationError:F4}");
        foreach (var bin in report.CalibrationMetrics.Bins)
            Console.WriteLine(
                $"   {bin.ProbabilityRange,-10}: Pred={bin.AveragePredictedProbability:P2}, Actual={bin.ActualWinRate:P2}, Error={bin.Calibration:F4}, n={bin.Count}");

        // Warnings
        Console.WriteLine("\n⚠️ Model Assessment:");
        if (report.AUC < 0.6)
            Console.WriteLine("   ⚠️ Low AUC - Model has weak predictive power");
        if (report.Accuracy > 0.9)
            Console.WriteLine("   ⚠️ Very high accuracy - Check for data leakage");
        if (report.CalibrationMetrics.OverallCalibrationError > 0.1)
            Console.WriteLine("   ⚠️ Poor calibration - Probabilities may not be reliable");
    }
}

// Report classes