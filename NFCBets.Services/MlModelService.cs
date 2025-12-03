using System.Text.Json;
using Microsoft.ML;
using NFCBets.Causal;
using NFCBets.Causal.Models;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class MlModelService : IMlModelService
{
    private readonly NfcbetsContext _context;
    private readonly IFeatureEngineeringService _featureService;
    private readonly MLContext _mlContext;
    private readonly Dictionary<int, List<PiratePrediction>> _predictionCache = new();
    private ITransformer? _model;

    public MlModelService(IFeatureEngineeringService featureService, NfcbetsContext context)
    {
        _mlContext = new MLContext(42);
        _featureService = featureService;
        _context = context;
    }

    public async Task TrainAndEvaluateCausallyInformedModelAsync()
    {
        Console.WriteLine("🧬 Training and Evaluating Causally-Informed ML Model");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        // Step 1: Run comprehensive causal analysis
        Console.WriteLine("🔬 Step 1: Comprehensive Causal Analysis...");
        var causalService = new CausalInferenceService(_context);
        var causalReport = await causalService.AnalyzeAllTreatmentEffectsAsync();

        // Step 2: Feature selection based on causal significance
        Console.WriteLine("\n📊 Step 2: Causal Feature Selection...");
        var featureSelectionResult = SelectFeaturesBasedOnCausalAnalysis(causalReport);

        Console.WriteLine($"   Selected {featureSelectionResult.SelectedFeatures.Count} causally-validated features:");
        foreach (var feature in featureSelectionResult.SelectedFeatures)
        {
            var causalEffect = featureSelectionResult.FeatureEffects.GetValueOrDefault(feature, 0);
            Console.WriteLine($"   ✅ {feature,-30} (causal effect: {causalEffect:+0.0%;-0.0%})");
        }

        if (featureSelectionResult.ExcludedFeatures.Any())
        {
            Console.WriteLine($"\n   Excluded {featureSelectionResult.ExcludedFeatures.Count} non-causal features:");
            foreach (var feature in featureSelectionResult.ExcludedFeatures)
                Console.WriteLine($"   ❌ {feature,-30} (not causally significant)");
        }

        // Step 3: Load and prepare training data
        Console.WriteLine("\n📥 Step 3: Loading Training Data...");
        var allData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();

        var minRound = validData.Min(f => f.RoundId);
        var filteredData = validData.Where(f => f.RoundId > minRound + 100).ToList();

        Console.WriteLine($"   Total: {validData.Count} records");
        Console.WriteLine($"   After filtering: {filteredData.Count} records");

        // Step 4: Data leakage check
        Console.WriteLine("\n🔍 Step 4: Data Leakage Check...");
        var evaluationService = new ModelEvaluationService();
        var leakageReport = await evaluationService.CheckForDataLeakageAsync(filteredData, _context);

        if (leakageReport.HasLeakage)
        {
            Console.WriteLine("❌ Critical data leakage detected!");
            throw new InvalidOperationException("Data leakage in training data");
        }

        // Step 5: Cross-validation with causal features
        Console.WriteLine("\n🔄 Step 5: Cross-Validation (Causal Features Only)...");
        var crossValService = new CrossValidationService(_featureService);

        var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync(5);
        var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync(5);

        Console.WriteLine($"   Time-Series CV: AUC {timeSeriesCV.AverageAUC:F4} ± {timeSeriesCV.StdDevAUC:F4}");
        Console.WriteLine($"   K-Fold CV:      AUC {kFoldCV.AverageAUC:F4} ± {kFoldCV.StdDevAUC:F4}");

        // Step 6: Train final causal model
        Console.WriteLine("\n🏋️ Step 6: Training Final Causal Model...");
        var sortedData = filteredData.OrderBy(f => f.RoundId).ToList();
        var splitIndex = (int)(sortedData.Count * 0.8);

        var trainData = sortedData.Take(splitIndex).ToList();
        var testData = sortedData.Skip(splitIndex).ToList();

        var mlTrainData = ConvertToMlFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);

        // Build pipeline with only causal features
        var pipeline = _mlContext.Transforms.Concatenate("Features", featureSelectionResult.SelectedFeatures.ToArray())
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                nameof(MlPirateFeature.Won),
                "Features",
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 50,
                learningRate: 0.05,
                numberOfIterations: 50));

        var startTime = DateTime.Now;
        _model = pipeline.Fit(dataView);
        var trainingTime = DateTime.Now - startTime;

        Console.WriteLine($"   ✅ Training completed in {trainingTime.TotalSeconds:F1}s");

        // Step 7: Evaluate causal model
        Console.WriteLine("\n📈 Step 7: Model Evaluation...");
        var evaluationReport = await evaluationService.EvaluateModelAsync(_model, testData);

        // Step 8: Compare causal vs standard model
        Console.WriteLine("\n⚖️ Step 8: Causal vs Standard Model Comparison...");
        var standardModel = await TrainStandardModelForComparison(trainData);
        var standardEval = await evaluationService.EvaluateModelAsync(standardModel, testData);

        Console.WriteLine("   Causal Model:");
        Console.WriteLine($"      AUC:      {evaluationReport.AUC:F4}");
        Console.WriteLine($"      Accuracy: {evaluationReport.Accuracy:P2}");
        Console.WriteLine("   Standard Model (All Features):");
        Console.WriteLine($"      AUC:      {standardEval.AUC:F4}");
        Console.WriteLine($"      Accuracy: {standardEval.Accuracy:P2}");

        var aucDifference = evaluationReport.AUC - standardEval.AUC;
        Console.WriteLine($"   Difference: {aucDifference:+0.0000;-0.0000}");

        if (aucDifference > -0.01)
            Console.WriteLine("   ✅ Causal model performs similarly with fewer features (better generalization)");
        else if (aucDifference < -0.03)
            Console.WriteLine("   ⚠️ Causal model underperforms - some excluded features may be important");

        // Step 9: Generate key findings and recommendations
        Console.WriteLine("\n💡 Step 9: Generating Insights...");
        GenerateCausalInsights(causalReport, evaluationReport, featureSelectionResult);

        // Save comprehensive report
        SaveComprehensiveCausalReport(causalReport, evaluationReport, featureSelectionResult, timeSeriesCV, kFoldCV);

        Console.WriteLine("\n✅ Causally-informed model training complete");
    }


    public async Task TrainAndEvaluateModelAsync()
    {
        Console.WriteLine("🤖 Training and evaluating ML model...");

        var allData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();

        Console.WriteLine($"Total valid training data: {validData.Count} records");

        // Skip first 100 rounds
        var minRound = validData.Min(f => f.RoundId);
        var filteredData = validData.Where(f => f.RoundId > minRound + 100).ToList();

        Console.WriteLine($"Filtered to {filteredData.Count} records (skipping first 100 rounds)");

        var evaluationService = new ModelEvaluationService();
        var crossValService = new CrossValidationService(_featureService);

        // Step 1: Data leakage check
        Console.WriteLine("\n🔍 Step 1: Checking for data leakage...");
        var leakageReport = await evaluationService.CheckForDataLeakageAsync(filteredData, _context);

        if (leakageReport.HasLeakage) Console.WriteLine("❌ Data leakage detected!");

        // Step 2: Cross-validation (both methods)
        Console.WriteLine("\n📊 Step 2: Cross-Validation...");

        Console.WriteLine("   Running Time-Series Cross-Validation...");
        var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync(5);

        Console.WriteLine("   Running K-Fold Cross-Validation...");
        var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync(5);

        // Compare cross-validation methods
        Console.WriteLine("\n🔬 Cross-Validation Comparison:");
        Console.WriteLine("   Time-Series CV:");
        Console.WriteLine($"      Average AUC:      {timeSeriesCV.AverageAUC:F4} ± {timeSeriesCV.StdDevAUC:F4}");
        Console.WriteLine(
            $"      Average Accuracy: {timeSeriesCV.AverageAccuracy:P2} ± {timeSeriesCV.StdDevAccuracy:P2}");
        Console.WriteLine("   K-Fold CV:");
        Console.WriteLine($"      Average AUC:      {kFoldCV.AverageAUC:F4} ± {kFoldCV.StdDevAUC:F4}");
        Console.WriteLine($"      Average Accuracy: {kFoldCV.AverageAccuracy:P2} ± {kFoldCV.StdDevAccuracy:P2}");

        var aucDifference = Math.Abs(timeSeriesCV.AverageAUC - kFoldCV.AverageAUC);
        Console.WriteLine($"   Difference in AUC: {aucDifference:F4}");

        if (aucDifference < 0.02)
        {
            Console.WriteLine("   ✅ Both methods show consistent results - model is stable");
        }
        else
        {
            Console.WriteLine("   ⚠️ Methods differ - may indicate temporal drift or overfitting");
            if (timeSeriesCV.AverageAUC < kFoldCV.AverageAUC)
                Console.WriteLine("   📌 Time-Series CV lower suggests model may not generalize well to future data");
        }

        // Use the more conservative estimate
        var expectedAUC = Math.Min(timeSeriesCV.AverageAUC, kFoldCV.AverageAUC);
        Console.WriteLine($"   Expected Real-World AUC: {expectedAUC:F4}");

        // Step 3: Train final model on all data
        Console.WriteLine("\n🏋️ Step 3: Training final model on full dataset...");
        var sortedData = filteredData.OrderBy(f => f.RoundId).ToList();
        var splitIndex = (int)(sortedData.Count * 0.8);

        var trainData = sortedData.Take(splitIndex).ToList();
        var testData = sortedData.Skip(splitIndex).ToList();

        Console.WriteLine(
            $"   Training: {trainData.Count} records (rounds {trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)})");
        Console.WriteLine(
            $"   Testing:  {testData.Count} records (rounds {testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)})");

        var mlTrainData = ConvertToMlFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);

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
                nameof(MlPirateFeature.Won),
                "Features",
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 50,
                learningRate: 0.05,
                numberOfIterations: 50));

        var startTime = DateTime.Now;
        _model = pipeline.Fit(dataView);
        var trainingTime = DateTime.Now - startTime;

        Console.WriteLine($"   Training completed in {trainingTime.TotalSeconds:F1} seconds");

        // Step 4: Final evaluation
        Console.WriteLine("\n📈 Step 4: Final model evaluation on holdout test set...");
        var evaluationReport = await evaluationService.EvaluateModelAsync(_model, testData);

        // Step 5: Model stability analysis
        Console.WriteLine("\n🎯 Step 5: Model Stability Analysis");
        Console.WriteLine("   Cross-Val Performance (Time-Series):");
        Console.WriteLine($"      AUC:      {timeSeriesCV.AverageAUC:F4} ± {timeSeriesCV.StdDevAUC:F4}");
        Console.WriteLine($"      Accuracy: {timeSeriesCV.AverageAccuracy:P2} ± {timeSeriesCV.StdDevAccuracy:P2}");

        Console.WriteLine("   Cross-Val Performance (K-Fold):");
        Console.WriteLine($"      AUC:      {kFoldCV.AverageAUC:F4} ± {kFoldCV.StdDevAUC:F4}");
        Console.WriteLine($"      Accuracy: {kFoldCV.AverageAccuracy:P2} ± {kFoldCV.StdDevAccuracy:P2}");

        Console.WriteLine("   Final Model (Holdout Test Set):");
        Console.WriteLine($"      AUC:      {evaluationReport.AUC:F4}");
        Console.WriteLine($"      Accuracy: {evaluationReport.Accuracy:P2}");

        // Check consistency
        var finalVsCVDiff = Math.Abs(evaluationReport.AUC - timeSeriesCV.AverageAUC);
        var kFoldVariance = kFoldCV.StdDevAUC;
        var timeSeriesVariance = timeSeriesCV.StdDevAUC;

        Console.WriteLine("\n   Stability Assessment:");
        if (finalVsCVDiff < 0.03 && timeSeriesVariance < 0.02)
        {
            Console.WriteLine("   ✅ Model is stable and consistent");
            Console.WriteLine($"      Final vs CV difference: {finalVsCVDiff:F4} (good)");
            Console.WriteLine($"      Time-Series variance: {timeSeriesVariance:F4} (low)");
        }
        else if (finalVsCVDiff > 0.05)
        {
            Console.WriteLine("   ⚠️ Model performance differs from CV");
            Console.WriteLine($"      Final vs CV difference: {finalVsCVDiff:F4}");
            Console.WriteLine("      This may indicate overfitting to recent data");
        }
        else if (timeSeriesVariance > 0.05 || kFoldVariance > 0.05)
        {
            Console.WriteLine("   ⚠️ Model shows high variance across folds");
            Console.WriteLine($"      Time-Series variance: {timeSeriesVariance:F4}");
            Console.WriteLine($"      K-Fold variance: {kFoldVariance:F4}");
            Console.WriteLine("      Performance may be unstable on new data");
        }
        else
        {
            Console.WriteLine("   ✅ Model shows acceptable stability");
        }

        // Save cross-validation results
        SaveCrossValidationResults(timeSeriesCV, kFoldCV, evaluationReport);

        Console.WriteLine("\n✅ Model training and evaluation complete");
    }

    public async Task TrainModelAsync()
    {
        Console.WriteLine("🤖 Training ML model (without detailed evaluation)...");

        var trainingData = await _featureService.CreateTrainingDataAsync();
        var validData = trainingData.Where(f => f.IsWinner.HasValue).ToList();

        Console.WriteLine($"Training with {validData.Count} records");

        var mlData = ConvertToMlFormat(validData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var trainTestSplit = _mlContext.Data.TrainTestSplit(dataView, 0.2);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
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
                nameof(MlPirateFeature.AvgRivalStrength))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                nameof(MlPirateFeature.Won)))
            .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                nameof(MlPirateFeature.Won)));

        _model = pipeline.Fit(trainTestSplit.TrainSet);

        var predictions = _model.Transform(trainTestSplit.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, nameof(MlPirateFeature.Won));

        Console.WriteLine($"✅ Model trained - Accuracy: {metrics.Accuracy:P2}, AUC: {metrics.AreaUnderRocCurve:F3}");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (_model == null)
            throw new InvalidOperationException("Model must be trained first");

        // Check cache first
        if (features.Any() && _predictionCache.TryGetValue(features[0].RoundId, out var cachedPredictions))
        {
            Console.WriteLine($"📦 Using cached predictions for round {features[0].RoundId}");
            return cachedPredictions;
        }

        var mlData = ConvertToMlFormat(features);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var predictions = _model.Transform(dataView);

        var predictionResults = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictions, false).ToList();

        var piratePredictions = predictionResults.Zip(features, (pred, feat) => new PiratePrediction
        {
            RoundId = feat.RoundId,
            ArenaId = feat.ArenaId,
            PirateId = feat.PirateId,
            WinProbability = Math.Clamp(pred.Probability, 0.01f, 0.99f),
            Payout = Math.Max(2, feat.CurrentOdds)
        }).ToList();

        // Cache the predictions
        if (features.Any()) _predictionCache[features[0].RoundId] = piratePredictions;

        return piratePredictions;
    }

    public void SaveModel(string path)
    {
        if (_model == null)
            throw new InvalidOperationException("No model to save");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _mlContext.Model.Save(_model, null, path);
        Console.WriteLine($"💾 Model saved to {path}");
    }

    public void LoadModel(string path)
    {
        _model = _mlContext.Model.Load(path, out _);
        Console.WriteLine($"📂 Model loaded from {path}");
    }

    private FeatureSelectionResult SelectFeaturesBasedOnCausalAnalysis(ComprehensiveCausalReport causalReport)
    {
        var result = new FeatureSelectionResult();
        var featureEffects = new Dictionary<string, double>();

        // Map causal effects to ML features
        if (causalReport.FoodAdjustmentEffect.IsSignificant)
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.FoodAdjustment));
            featureEffects[nameof(MlPirateFeature.FoodAdjustment)] =
                causalReport.FoodAdjustmentEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.FoodAdjustment));
        }

        if (causalReport.SeatPositionEffect.IsSignificant)
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.Position));
            featureEffects[nameof(MlPirateFeature.Position)] = causalReport.SeatPositionEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.Position));
        }

        if (causalReport.RivalStrengthEffect.IsSignificant)
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.AvgRivalStrength));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.WinRateVsCurrentRivals));
            featureEffects[nameof(MlPirateFeature.AvgRivalStrength)] =
                causalReport.RivalStrengthEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.AvgRivalStrength));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.WinRateVsCurrentRivals));
        }

        if (causalReport.OddsEffect.IsSignificant)
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.CurrentOdds));
            featureEffects[nameof(MlPirateFeature.CurrentOdds)] = causalReport.OddsEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.CurrentOdds));
        }

        // Always include proven predictors (may not be causal but are predictive)
        result.SelectedFeatures.Add(nameof(MlPirateFeature.HistoricalWinRate));
        result.SelectedFeatures.Add(nameof(MlPirateFeature.ArenaWinRate));
        result.SelectedFeatures.Add(nameof(MlPirateFeature.RecentWinRate));

        // Include pirate attributes if they have effects
        if (Math.Abs(featureEffects.Values.DefaultIfEmpty(0).Average()) > 0.01)
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.Strength));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.Weight));
        }

        result.FeatureEffects = featureEffects;

        return result;
    }

    private async Task<ITransformer> TrainStandardModelForComparison(List<PirateFeatureRecord> trainData)
    {
        var mlData = ConvertToMlFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(MlPirateFeature.Position),
                nameof(MlPirateFeature.CurrentOdds),
                nameof(MlPirateFeature.FoodAdjustment),
                nameof(MlPirateFeature.Strength),
                nameof(MlPirateFeature.Weight),
                nameof(MlPirateFeature.HistoricalWinRate),
                nameof(MlPirateFeature.ArenaWinRate),
                nameof(MlPirateFeature.RecentWinRate),
                nameof(MlPirateFeature.WinRateVsCurrentRivals),
                nameof(MlPirateFeature.AvgRivalStrength))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                nameof(MlPirateFeature.Won),
                "Features",
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 50,
                learningRate: 0.05,
                numberOfIterations: 50));

        return pipeline.Fit(dataView);
    }

    private void GenerateCausalInsights(ComprehensiveCausalReport causalReport, ModelEvaluationReport evalReport,
        FeatureSelectionResult featureSelection)
    {
        var findings = new List<string>();
        var recommendations = new List<string>();

        // Food adjustment insights
        if (causalReport.FoodAdjustmentEffect.IsSignificant &&
            causalReport.FoodAdjustmentEffect.AverageTreatmentEffect > 0.05)
        {
            findings.Add(
                $"Food adjustment has strong positive causal effect (+{causalReport.FoodAdjustmentEffect.AverageTreatmentEffect:P1})");
            recommendations.Add("Prioritize pirates with positive food adjustments in betting strategies");
        }
        else if (!causalReport.FoodAdjustmentEffect.IsSignificant)
        {
            findings.Add("Food adjustment shows correlation but weak causal evidence");
            recommendations.Add("Use food adjustment cautiously - may be confounded with other factors");
        }

        // Position insights
        if (causalReport.SeatPositionEffect.IsSignificant)
        {
            findings.Add(
                $"Seat position has causal impact ({causalReport.SeatPositionEffect.AverageTreatmentEffect:+P1})");

            if (causalReport.SeatPositionEffect.PositionEffects != null)
            {
                var bestPosition = causalReport.SeatPositionEffect.PositionEffects.OrderByDescending(kv => kv.Value)
                    .First();
                recommendations.Add(
                    $"Position {bestPosition.Key} shows {bestPosition.Value:+P1} advantage - weight heavily in model");
            }
        }

        // Rival strength insights
        if (causalReport.RivalStrengthEffect.IsSignificant &&
            causalReport.RivalStrengthEffect.AverageTreatmentEffect < -0.03)
        {
            findings.Add(
                $"Strong rivals significantly reduce win probability ({causalReport.RivalStrengthEffect.AverageTreatmentEffect:P1})");
            recommendations.Add("Head-to-head matchups are critical - include rival analysis in all strategies");
        }

        // Odds insights
        if (causalReport.OddsEffect.IsSignificant)
        {
            findings.Add($"Favorite status has causal effect ({causalReport.OddsEffect.AverageTreatmentEffect:+P1})");

            if (causalReport.OddsEffect.DoseResponse != null)
            {
                var doseEffects = causalReport.OddsEffect.DoseResponse.OrderBy(kv => kv.Key).ToList();
                var efficiency = doseEffects.FirstOrDefault(kv => kv.Value / (1.0 / kv.Key) > 1.2);

                if (efficiency.Key > 0)
                    recommendations.Add(
                        $"Pirates at {efficiency.Key}:1 odds show best value (win rate: {efficiency.Value:P1})");
            }
        }

        // Interaction insights
        if (causalReport.InteractionEffects.Any())
        {
            var synergies = causalReport.InteractionEffects.Where(ie => ie.Value.IsSynergistic).ToList();
            var antagonisms = causalReport.InteractionEffects.Where(ie => ie.Value.IsAntagonistic).ToList();

            if (synergies.Any())
            {
                findings.Add($"Found {synergies.Count} synergistic effect combinations");
                foreach (var (key, effect) in synergies)
                    recommendations.Add($"Combine {effect.Name} for {effect.InteractionStrength:+P1} bonus");
            }

            if (antagonisms.Any())
            {
                findings.Add($"Found {antagonisms.Count} antagonistic effect combinations");
                foreach (var (key, effect) in antagonisms)
                    recommendations.Add(
                        $"Avoid combining {effect.Name} (reduces effect by {-effect.InteractionStrength:P1})");
            }
        }

        // Model performance insights
        if (evalReport.AUC > 0.75) findings.Add($"Causal model achieves strong performance (AUC: {evalReport.AUC:F3})");

        if (evalReport.CalibrationMetrics.OverallCalibrationError < 0.10)
            findings.Add("Model probabilities are well-calibrated for betting decisions");
        else
            recommendations.Add("Apply additional probability calibration before betting");

        causalReport.KeyFindings = findings;
        causalReport.Recommendations = recommendations;

        Console.WriteLine("\n📋 KEY FINDINGS:");
        foreach (var finding in findings) Console.WriteLine($"   • {finding}");

        Console.WriteLine("\n💡 RECOMMENDATIONS:");
        foreach (var rec in recommendations) Console.WriteLine($"   → {rec}");
    }

    private void SaveComprehensiveCausalReport(
        ComprehensiveCausalReport causalReport,
        ModelEvaluationReport evalReport,
        FeatureSelectionResult featureSelection,
        CrossValidationReport timeSeriesCV,
        CrossValidationReport kFoldCV)
    {
        Directory.CreateDirectory("Reports");

        var comprehensiveReport = new
        {
            GeneratedAt = DateTime.UtcNow,
            CausalAnalysis = causalReport,
            FeatureSelection = featureSelection,
            CrossValidation = new
            {
                TimeSeries = timeSeriesCV,
                KFold = kFoldCV
            },
            ModelEvaluation = new
            {
                evalReport.Accuracy,
                evalReport.AUC,
                evalReport.F1Score,
                evalReport.LogLoss,
                evalReport.CalibrationMetrics
            },
            Summary = new
            {
                TotalFeaturesAnalyzed =
                    featureSelection.SelectedFeatures.Count + featureSelection.ExcludedFeatures.Count,
                CausallySignificantFeatures = featureSelection.SelectedFeatures.Count,
                StrongestCausalEffect = causalReport.FoodAdjustmentEffect.AverageTreatmentEffect,
                ModelStability = timeSeriesCV.StdDevAUC < 0.02 ? "Stable" : "Unstable",
                RecommendedOptimization = DetermineRecommendedOptimization(causalReport)
            }
        };

        var fileName = Path.Combine("Reports", $"causal_model_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(comprehensiveReport, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"\n📄 Comprehensive causal report saved to {fileName}");
    }

    private string DetermineRecommendedOptimization(ComprehensiveCausalReport causalReport)
    {
        // Based on causal analysis, recommend optimization method
        if (causalReport.FoodAdjustmentEffect.IsSignificant &&
            causalReport.SeatPositionEffect.IsSignificant &&
            causalReport.InteractionEffects.Any(ie => ie.Value.IsSynergistic))
            return "ConsistencyWeighted - Multiple causal factors suggest focusing on reliable combinations";

        if (causalReport.OddsEffect.IsSignificant &&
            Math.Abs(causalReport.OddsEffect.AverageTreatmentEffect) > 0.1)
            return "Kelly - Strong odds effects suggest Kelly criterion for bet sizing";

        return "ConsistencyWeighted - Default safe choice";
    }

    private void SaveCrossValidationResults(CrossValidationReport timeSeriesCV, CrossValidationReport kFoldCV,
        ModelEvaluationReport finalEval)
    {
        Directory.CreateDirectory("Reports");

        var report = new
        {
            GeneratedAt = DateTime.UtcNow,
            TimeSeriesCrossValidation = timeSeriesCV,
            KFoldCrossValidation = kFoldCV,
            FinalModelEvaluation = new
            {
                finalEval.Accuracy,
                finalEval.AUC,
                finalEval.F1Score,
                finalEval.LogLoss
            },
            StabilityMetrics = new
            {
                TimeSeriesVariance = timeSeriesCV.StdDevAUC,
                KFoldVariance = kFoldCV.StdDevAUC,
                FinalVsCVDifference = Math.Abs(finalEval.AUC - timeSeriesCV.AverageAUC),
                IsStable = Math.Abs(finalEval.AUC - timeSeriesCV.AverageAUC) < 0.03 && timeSeriesCV.StdDevAUC < 0.02
            }
        };

        var fileName = Path.Combine("Reports", $"cross_validation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"📄 Cross-validation report saved to {fileName}");
    }

    public void ClearPredictionCache()
    {
        _predictionCache.Clear();
    }

    private List<MlPirateFeature> ConvertToMlFormat(List<PirateFeatureRecord> features)
    {
        return features.Select(f => new MlPirateFeature
        {
            Position = f.Position,
            CurrentOdds = Math.Max(2, f.CurrentOdds),
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
}