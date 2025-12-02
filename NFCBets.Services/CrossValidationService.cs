using Microsoft.ML;
using Microsoft.ML.Data;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services
{
    public class CrossValidationService : ICrossValidationService
    {
        private readonly IFeatureEngineeringService _featureService;
        private readonly MLContext _mlContext;

        public CrossValidationService(IFeatureEngineeringService featureService)
        {
            _featureService = featureService;
            _mlContext = new MLContext(seed: 42);
        }

        public async Task<CrossValidationReport> PerformKFoldCrossValidationAsync(int k = 5)
        {
            Console.WriteLine($"🔄 Performing {k}-Fold Cross-Validation...");

            var allData = await _featureService.CreateTrainingDataAsync(4000);
            var validData = allData.Where(f => f.IsWinner.HasValue).OrderBy(f => f.RoundId).ToList();

            Console.WriteLine($"   Total data: {validData.Count} records");

            var foldResults = new List<FoldResult>();
            var foldSize = validData.Count / k;

            for (int fold = 0; fold < k; fold++)
            {
                Console.WriteLine($"   Processing fold {fold + 1}/{k}...");

                // Time-aware k-fold: each fold is a contiguous time period
                var testStart = fold * foldSize;
                var testEnd = (fold == k - 1) ? validData.Count : (fold + 1) * foldSize;
                
                var testData = validData.Skip(testStart).Take(testEnd - testStart).ToList();
                var trainData = validData.Take(testStart).Concat(validData.Skip(testEnd)).ToList();

                if (trainData.Count < 100)
                {
                    Console.WriteLine($"   ⚠️ Skipping fold {fold + 1} - insufficient training data");
                    continue;
                }

                var foldResult = await TrainAndEvaluateFold(trainData, testData, fold + 1);
                foldResults.Add(foldResult);
            }

            var report = new CrossValidationReport
            {
                Method = "K-Fold",
                NumFolds = k,
                FoldResults = foldResults,
                AverageAccuracy = foldResults.Average(f => f.Accuracy),
                AverageAUC = foldResults.Average(f => f.AUC),
                AverageF1Score = foldResults.Average(f => f.F1Score),
                StdDevAccuracy = CalculateStandardDeviation(foldResults.Select(f => f.Accuracy)),
                StdDevAUC = CalculateStandardDeviation(foldResults.Select(f => f.AUC))
            };

            DisplayCrossValidationReport(report);
            return report;
        }

        public async Task<CrossValidationReport> PerformTimeSeriesCrossValidationAsync(int numFolds = 5)
        {
            Console.WriteLine($"📅 Performing Time-Series Cross-Validation ({numFolds} folds)...");

            var allData = await _featureService.CreateTrainingDataAsync(4000);
            var validData = allData.Where(f => f.IsWinner.HasValue).OrderBy(f => f.RoundId).ToList();

            Console.WriteLine($"   Total data: {validData.Count} records");

            var foldResults = new List<FoldResult>();
            var initialTrainSize = validData.Count / (numFolds + 1);
            var testSize = validData.Count / (numFolds + 1);

            for (int fold = 0; fold < numFolds; fold++)
            {
                Console.WriteLine($"   Processing fold {fold + 1}/{numFolds}...");

                // Growing training window (walk-forward validation)
                var trainEnd = initialTrainSize + (fold * testSize);
                var testStart = trainEnd;
                var testEnd = Math.Min(testStart + testSize, validData.Count);

                var trainData = validData.Take(trainEnd).ToList();
                var testData = validData.Skip(testStart).Take(testEnd - testStart).ToList();

                if (trainData.Count < 100 || testData.Count < 10)
                {
                    Console.WriteLine($"   ⚠️ Skipping fold {fold + 1} - insufficient data");
                    continue;
                }

                var trainRounds = $"{trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)}";
                var testRounds = $"{testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)}";
                
                Console.WriteLine($"      Train: {trainData.Count} records (rounds {trainRounds})");
                Console.WriteLine($"      Test:  {testData.Count} records (rounds {testRounds})");

                var foldResult = await TrainAndEvaluateFold(trainData, testData, fold + 1);
                foldResults.Add(foldResult);
            }

            var report = new CrossValidationReport
            {
                Method = "Time-Series",
                NumFolds = numFolds,
                FoldResults = foldResults,
                AverageAccuracy = foldResults.Average(f => f.Accuracy),
                AverageAUC = foldResults.Average(f => f.AUC),
                AverageF1Score = foldResults.Average(f => f.F1Score),
                StdDevAccuracy = CalculateStandardDeviation(foldResults.Select(f => f.Accuracy)),
                StdDevAUC = CalculateStandardDeviation(foldResults.Select(f => f.AUC))
            };

            DisplayCrossValidationReport(report);
            return report;
        }

        private async Task<FoldResult> TrainAndEvaluateFold(List<PirateFeatureRecord> trainData, List<PirateFeatureRecord> testData, int foldNumber)
        {
            // Convert to ML format
            var mlTrainData = ConvertToMLFormat(trainData);
            var mlTestData = ConvertToMLFormat(testData);

            var trainDataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);
            var testDataView = _mlContext.Data.LoadFromEnumerable(mlTestData);

            // Build and train pipeline
            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(MlPirateFeature.CurrentOdds),
                    nameof(MlPirateFeature.FoodAdjustment),
                    nameof(MlPirateFeature.Strength),
                    nameof(MlPirateFeature.Weight),
                    nameof(MlPirateFeature.HistoricalWinRate),
                    nameof(MlPirateFeature.ArenaWinRate),
                    nameof(MlPirateFeature.RecentWinRate),
                    nameof(MlPirateFeature.WinRateVsCurrentRivals))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                    labelColumnName: nameof(MlPirateFeature.Won),
                    featureColumnName: "Features",
                    numberOfLeaves: 20,
                    minimumExampleCountPerLeaf: 50,
                    learningRate: 0.05,
                    numberOfIterations: 50));

            var model = pipeline.Fit(trainDataView);
            var predictions = model.Transform(testDataView);
            var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: nameof(MlPirateFeature.Won));

            return new FoldResult
            {
                FoldNumber = foldNumber,
                TrainSize = trainData.Count,
                TestSize = testData.Count,
                Accuracy = metrics.Accuracy,
                AUC = metrics.AreaUnderRocCurve,
                F1Score = metrics.F1Score,
                Precision = metrics.PositivePrecision,
                Recall = metrics.PositiveRecall,
                LogLoss = metrics.LogLoss
            };
        }

        private List<MlPirateFeature> ConvertToMLFormat(List<PirateFeatureRecord> features)
        {
            return features.Select(f => new MlPirateFeature
            {
                Position = (float)f.Position,
                CurrentOdds = (float)Math.Max(2, f.CurrentOdds),
                FoodAdjustment = (float)f.FoodAdjustment,
                Strength = (float)f.Strength,
                Weight = (float)f.Weight,
                HistoricalWinRate = (float)f.HistoricalWinRate,
                TotalAppearances = (float)f.TotalAppearances,
                ArenaWinRate = (float)f.ArenaWinRate,
                RecentWinRate = (float)f.RecentWinRate,
                WinRateVsCurrentRivals = (float)f.WinRateVsCurrentRivals,
                MatchesVsCurrentRivals = (float)f.MatchesVsCurrentRivals,
                AvgRivalStrength = (float)f.AvgRivalStrength,
                Won = f.IsWinner ?? false
            }).ToList();
        }

        private void DisplayCrossValidationReport(CrossValidationReport report)
        {
            Console.WriteLine("\n═══════════════════════════════════════════════════");
            Console.WriteLine($"📊 CROSS-VALIDATION REPORT ({report.Method})");
            Console.WriteLine("═══════════════════════════════════════════════════\n");

            Console.WriteLine($"Number of Folds: {report.NumFolds}");
            Console.WriteLine($"\nPer-Fold Results:");
            
            foreach (var fold in report.FoldResults)
            {
                Console.WriteLine($"   Fold {fold.FoldNumber}:");
                Console.WriteLine($"      Train: {fold.TrainSize,5} | Test: {fold.TestSize,5}");
                Console.WriteLine($"      Accuracy: {fold.Accuracy:P2} | AUC: {fold.AUC:F4} | F1: {fold.F1Score:F4}");
            }

            Console.WriteLine($"\n📈 Aggregate Metrics:");
            Console.WriteLine($"   Average Accuracy:  {report.AverageAccuracy:P2} ± {report.StdDevAccuracy:P2}");
            Console.WriteLine($"   Average AUC:       {report.AverageAUC:F4} ± {report.StdDevAUC:F4}");
            Console.WriteLine($"   Average F1 Score:  {report.AverageF1Score:F4}");

            Console.WriteLine($"\n💡 Assessment:");
            
            if (report.StdDevAUC < 0.02)
                Console.WriteLine($"   ✅ Model is stable across folds (low variance)");
            else
                Console.WriteLine($"   ⚠️ Model shows instability across folds (high variance)");

            if (report.AverageAUC > 0.7)
                Console.WriteLine($"   ✅ Good predictive performance");
            else
                Console.WriteLine($"   ⚠️ Weak predictive performance");
        }

        private double CalculateStandardDeviation(IEnumerable<double> values)
        {
            var valueList = values.ToList();
            if (valueList.Count < 2) return 0;

            var mean = valueList.Average();
            var variance = valueList.Sum(v => Math.Pow(v - mean, 2)) / valueList.Count;
            return Math.Sqrt(variance);
        }
    }
}