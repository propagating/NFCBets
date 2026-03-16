using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.ML;
using NFCBets.Causal;
using NFCBets.Causal.Models;
using NFCBets.Classical.Constants;
using NFCBets.Classical.Models;
using NFCBets.EF.Models;
using NFCBets.Services;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;
using NFCBets.Utilities.Constants;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation;

public class MlModelService : IMlModelService
{
    private readonly NfcbetsContext _context;
    private readonly IFeatureEngineeringService _featureService;
    private readonly MLContext _mlContext;
    private readonly Dictionary<int, List<PiratePrediction>> _predictionCache = new();
    private ITransformer? _model;

    // Cached pirate names for performance
    private Dictionary<int, string>? _pirateNamesCache;

    public MlModelService(IFeatureEngineeringService featureService, NfcbetsContext context)
    {
        _mlContext = new MLContext(42);
        _featureService = featureService;
        _context = context;
    }

    #region Training Methods

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
        foreach (var feature in featureSelectionResult.SelectedFeatures.Take(10))
        {
            var causalEffect = featureSelectionResult.FeatureEffects.GetValueOrDefault(feature, 0);
            Console.WriteLine($"   ✅ {feature,-35} (causal effect: {causalEffect:+0.0%;-0.0%})");
        }

        if (featureSelectionResult.SelectedFeatures.Count > 10)
            Console.WriteLine($"   ... and {featureSelectionResult.SelectedFeatures.Count - 10} more");

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

        if (leakageReport.HasLeakage) Console.WriteLine("❌ Critical data leakage detected!");

        // Step 5: Cross-validation with causal features
        Console.WriteLine("\n🔄 Step 5: Cross-Validation (Causal Features Only)...");
        var crossValService = new CrossValidationService(_featureService);

        var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync();
        var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync();

        Console.WriteLine($"   Time-Series CV: Auc {timeSeriesCV.AverageAUC:F4} ± {timeSeriesCV.StdDevAUC:F4}");
        Console.WriteLine($"   K-Fold CV:      Auc {kFoldCV.AverageAUC:F4} ± {kFoldCV.StdDevAUC:F4}");

        // Step 6: Train final causal model
        Console.WriteLine("\n🏋️ Step 6: Training Final Causal Model...");

        var sortedData = filteredData.OrderBy(f => f.RoundId).ToList();
        var uniqueRounds = sortedData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var roundSplitIndex = (int)(uniqueRounds.Count * 0.8);

        var trainRoundIds = uniqueRounds.Take(roundSplitIndex).ToHashSet();
        var testRoundIds = uniqueRounds.Skip(roundSplitIndex).ToHashSet();

        var trainData = sortedData.Where(f => trainRoundIds.Contains(f.RoundId)).ToList();
        var testData = sortedData.Where(f => testRoundIds.Contains(f.RoundId)).ToList();

        Console.WriteLine(
            $"   Training: {trainData.Count} records (rounds {trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)})");
        Console.WriteLine(
            $"   Testing:  {testData.Count} records (rounds {testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)})");

        var mlTrainData = ConvertToMlFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);

        // Build pipeline with only causal features
        var pipeline = BuildCausalPipeline(featureSelectionResult.SelectedFeatures);

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
        Console.WriteLine($"      Auc:      {evaluationReport.Auc:F4}");
        Console.WriteLine($"      Accuracy: {evaluationReport.Accuracy:P2}");
        Console.WriteLine("   Standard Model (All Features):");
        Console.WriteLine($"      Auc:      {standardEval.Auc:F4}");
        Console.WriteLine($"      Accuracy: {standardEval.Accuracy:P2}");

        var aucDifference = evaluationReport.Auc - standardEval.Auc;
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

        // Step 0: DATA VALIDATION
        Console.WriteLine("\n🔍 Step 0: Data Quality Validation...");
        var validationService = new DataValidationService(_context);
        var validationReport = await validationService.ValidateDataQualityAsync(5300, 9705);

        if (!validationReport.ValidationPassed)
        {
            Console.WriteLine("\n❌ Data validation failed! Please fix critical issues before training.");
            throw new InvalidOperationException("Data validation failed with critical issues");
        }

        var allData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();

        Console.WriteLine($"Total valid training data: {validData.Count} records");

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
        var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync();

        Console.WriteLine("   Running K-Fold Cross-Validation...");
        var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync();

        // Compare cross-validation methods
        Console.WriteLine("\n🔬 Cross-Validation Comparison:");
        Console.WriteLine("   Time-Series CV:");
        Console.WriteLine($"      Average Auc:      {timeSeriesCV.AverageAUC:F4} ± {timeSeriesCV.StdDevAUC:F4}");
        Console.WriteLine($"      Average Accuracy: {timeSeriesCV.AverageAccuracy:P2} ± {timeSeriesCV.StdDevAccuracy:P2}");
        Console.WriteLine("   K-Fold CV:");
        Console.WriteLine($"      Average Auc:      {kFoldCV.AverageAUC:F4} ± {kFoldCV.StdDevAUC:F4}");
        Console.WriteLine($"      Average Accuracy: {kFoldCV.AverageAccuracy:P2} ± {kFoldCV.StdDevAccuracy:P2}");

        var aucDifference = Math.Abs(timeSeriesCV.AverageAUC - kFoldCV.AverageAUC);
        Console.WriteLine($"   Difference in Auc: {aucDifference:F4}");

        if (aucDifference < 0.02)
            Console.WriteLine("   ✅ Both methods show consistent results - model is stable");
        else
            Console.WriteLine("   ⚠️ Methods differ - may indicate temporal drift or overfitting");

        var expectedAUC = Math.Min(timeSeriesCV.AverageAUC, kFoldCV.AverageAUC);
        Console.WriteLine($"   Expected Real-World Auc: {expectedAUC:F4}");

        // Step 3: Train final model on all data
        Console.WriteLine("\n🏋️ Step 3: Training final model on full dataset...");
        var sortedData = filteredData.OrderBy(f => f.RoundId).ToList();
        var uniqueRounds = sortedData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var roundSplitIndex = (int)(uniqueRounds.Count * 0.8);

        var trainRoundIds = uniqueRounds.Take(roundSplitIndex).ToHashSet();
        var testRoundIds = uniqueRounds.Skip(roundSplitIndex).ToHashSet();

        var trainData = sortedData.Where(f => trainRoundIds.Contains(f.RoundId)).ToList();
        var testData = sortedData.Where(f => testRoundIds.Contains(f.RoundId)).ToList();

        Console.WriteLine(
            $"   Training: {trainData.Count} records (rounds {trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)})");
        Console.WriteLine(
            $"   Testing:  {testData.Count} records (rounds {testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)})");

        // Verify no overlap
        var trainMax = trainData.Max(f => f.RoundId);
        var testMin = testData.Min(f => f.RoundId);

        if (trainMax >= testMin)
            throw new InvalidOperationException(
                $"Train/test overlap detected: train max={trainMax}, test min={testMin}");

        var mlTrainData = ConvertToMlFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);

        var pipeline = BuildStandardPipeline();

        var startTime = DateTime.Now;
        _model = pipeline.Fit(dataView);
        var trainingTime = DateTime.Now - startTime;

        Console.WriteLine($"   Training completed in {trainingTime.TotalSeconds:F1} seconds");

        // Step 4: Final evaluation
        Console.WriteLine("\n📈 Step 4: Final model evaluation on holdout test set...");
        var evaluationReport = await evaluationService.EvaluateModelAsync(_model, testData);

        // Step 5: Model stability analysis
        Console.WriteLine("\n🎯 Step 5: Model Stability Analysis");
        var finalVsCVDiff = Math.Abs(evaluationReport.Auc - timeSeriesCV.AverageAUC);
        var timeSeriesVariance = timeSeriesCV.StdDevAUC;

        if (finalVsCVDiff < 0.03 && timeSeriesVariance < 0.02)
            Console.WriteLine("   ✅ Model is stable and consistent");
        else if (finalVsCVDiff > 0.05)
            Console.WriteLine("   ⚠️ Model performance differs from CV");
        else
            Console.WriteLine("   ✅ Model shows acceptable stability");

        // Save cross-validation results
        SaveCrossValidationResults(timeSeriesCV, kFoldCV, evaluationReport);

        Console.WriteLine("\n✅ Model training and evaluation complete");
    }

    public async Task TrainModelAsync()
    {
        Console.WriteLine("🤖 Training ML model (without detailed evaluation)...");

        var trainingData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = trainingData.Where(f => f.IsWinner.HasValue).ToList();

        Console.WriteLine($"Training with {validData.Count} records");

        var mlData = ConvertToMlFormat(validData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var trainTestSplit = _mlContext.Data.TrainTestSplit(dataView, 0.2);

        var pipeline = BuildStandardPipeline();

        _model = pipeline.Fit(trainTestSplit.TrainSet);

        var predictions = _model.Transform(trainTestSplit.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions);

        Console.WriteLine($"✅ Model trained - Accuracy: {metrics.Accuracy:P2}, Auc: {metrics.AreaUnderRocCurve:F3}");
    }

    #endregion

    #region Pipeline Builders

    private IEstimator<ITransformer> BuildStandardPipeline()
    {
        var allFeatures = GetAllFeatureNames();

        Console.WriteLine($"   Using {allFeatures.Length} features for training");

        return _mlContext.Transforms.Concatenate("Features", allFeatures)
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 31,
                minimumExampleCountPerLeaf: 20,
                learningRate: 0.05,
                numberOfIterations: 100));
    }

private IEstimator<ITransformer> BuildCausalPipeline(List<string> selectedFeatures)
    {
        Console.WriteLine($"   Using {selectedFeatures.Count} causally-selected features");

        return _mlContext.Transforms.Concatenate("Features", selectedFeatures.ToArray())
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 50,
                learningRate: 0.05,
                numberOfIterations: 50));
    }

    private string[] GetAllFeatureNames()
    {
        // Core features
        var coreFeatures = new[]
        {
            nameof(MlPirateFeature.Position),
            nameof(MlPirateFeature.ArenaId),
            nameof(MlPirateFeature.CurrentOdds),
            nameof(MlPirateFeature.OpeningOdds),
            nameof(MlPirateFeature.FoodAdjustment),
            nameof(MlPirateFeature.Strength),
            nameof(MlPirateFeature.Weight)
        };

        // Historical performance features
        var historicalFeatures = new[]
        {
            nameof(MlPirateFeature.HistoricalWinRate),
            nameof(MlPirateFeature.TotalAppearances),
            nameof(MlPirateFeature.ArenaWinRate),
            nameof(MlPirateFeature.RecentWinRate),
            nameof(MlPirateFeature.WinRateVsCurrentRivals),
            nameof(MlPirateFeature.MatchesVsCurrentRivals),
            nameof(MlPirateFeature.AvgRivalStrength)
        };

        // Derived features
        var derivedFeatures = new[]
        {
            nameof(MlPirateFeature.OddsMovement),
            nameof(MlPirateFeature.OddsMovementPercent),
            nameof(MlPirateFeature.ImpliedProbability),
            nameof(MlPirateFeature.RelativeStrength),
            nameof(MlPirateFeature.EffectiveStrength)
        };

        // Binary indicators
        var binaryFeatures = new[]
        {
            nameof(MlPirateFeature.IsOddsFavorite),
            nameof(MlPirateFeature.IsStrengthFavorite),
            nameof(MlPirateFeature.IsEffectiveStrengthFavorite),
            nameof(MlPirateFeature.HasOddsShortened),
            nameof(MlPirateFeature.HasOddsDrifted),
            nameof(MlPirateFeature.HasPositiveFoodAdjustment),
            nameof(MlPirateFeature.HasNegativeFoodAdjustment),
            nameof(MlPirateFeature.IsPositionOne),
            nameof(MlPirateFeature.IsPositionTwo),
            nameof(MlPirateFeature.IsPositionThree),
            nameof(MlPirateFeature.IsPositionFour),
            nameof(MlPirateFeature.IsUndervalued),
            nameof(MlPirateFeature.IsHotStreak),
            nameof(MlPirateFeature.IsArenaSpecialist)
        };

        // Arena indicators
        var arenaFeatures = new[]
        {
            nameof(MlPirateFeature.IsArenaShipwreck),
            nameof(MlPirateFeature.IsArenaLagoon),
            nameof(MlPirateFeature.IsArenaTreasureIsland),
            nameof(MlPirateFeature.IsArenaHiddenCove),
            nameof(MlPirateFeature.IsArenaHarpoonHarrys)
        };

        // Antagonistic penalties
        var penaltyFeatures = new[]
        {
            nameof(MlPirateFeature.PenaltyFoodPosition),
            nameof(MlPirateFeature.PenaltyFoodFavorite),
            nameof(MlPirateFeature.PenaltyStrengthPosition),
            nameof(MlPirateFeature.PenaltyStrengthWeakRivals),
            nameof(MlPirateFeature.PenaltyFavoriteInexperienced),
            nameof(MlPirateFeature.PenaltyLowStrengthFavorite),
            nameof(MlPirateFeature.PenaltyOddsShortenedLowStrength),
            nameof(MlPirateFeature.PenaltyArenaSpecialistColdStreak)
        };

        // Synergistic bonuses
        var bonusFeatures = new[]
        {
            nameof(MlPirateFeature.BonusUndervaluedStrong),
            nameof(MlPirateFeature.BonusArenaSpecialistModerateOdds),
            nameof(MlPirateFeature.BonusHotStreakBeatsRivals),
            nameof(MlPirateFeature.BonusFoodPositionThree),
            nameof(MlPirateFeature.BonusOddsShortenedStrong),
            nameof(MlPirateFeature.BonusFavoriteArenaSpecialist),
            nameof(MlPirateFeature.BonusStrengthPlusFood),
            nameof(MlPirateFeature.BonusHotStreakFavorite)
        };

        // Three-way interactions
        var threeWayFeatures = new[]
        {
            nameof(MlPirateFeature.ThreeWayFoodPositionStrength),
            nameof(MlPirateFeature.ThreeWayUndervaluedStrongBeatsRivals),
            nameof(MlPirateFeature.ThreeWayFavoriteSpecialistHotStreak),
            nameof(MlPirateFeature.ThreeWayStrengthFoodPositionThree)
        };

        // Combine all features
        return coreFeatures
            .Concat(historicalFeatures)
            .Concat(derivedFeatures)
            .Concat(binaryFeatures)
            .Concat(arenaFeatures)
            .Concat(penaltyFeatures)
            .Concat(bonusFeatures)
            .Concat(threeWayFeatures)
            .ToArray();
    }

    #endregion

    #region Prediction Methods

    /// <summary>
    /// Predict for a specific round (loads features from database and enriches with names)
    /// </summary>
    public async Task<List<PiratePrediction>> PredictRoundAsync(int roundId)
    {
        if (_model == null)
            throw new InvalidOperationException("Model must be trained first");

        // Check cache first
        if (_predictionCache.TryGetValue(roundId, out var cachedPredictions))
            return cachedPredictions;

        // Get features for the round (returns PirateFeatureRecord)
        var featureRecords = await _featureService.CreateFeaturesForRoundAsync(roundId);

        if (!featureRecords.Any())
            return new List<PiratePrediction>();

        // Use the PirateFeatureRecord overload
        var predictions = await PredictAsync(featureRecords, false);

        // Cache the predictions
        _predictionCache[roundId] = predictions;

        return predictions;
    }

    /// <summary>
    /// Predict from pre-loaded PirateFeatureRecord (for backtesting/evaluation)
    /// </summary>
    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features, bool useCache = true)
    {
        if (_model == null)
            throw new InvalidOperationException("Model must be trained first");

        if (!features.Any())
            return new List<PiratePrediction>();

        // Check cache first
        var roundId = features[0].RoundId;
        if (useCache && _predictionCache.TryGetValue(roundId, out var cachedPredictions))
            return cachedPredictions;

        // Ensure pirate names are cached
        await EnsurePirateNamesCachedAsync();

        var mlData = ConvertToMlFormat(features);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var predictions = _model.Transform(dataView);

        var predictionResults = _mlContext.Data
            .CreateEnumerable<PiratePredictionOutput>(predictions, false)
            .ToList();

        var piratePredictions = predictionResults.Zip(features, (pred, feat) => new PiratePrediction
        {
            RoundId = feat.RoundId,
            ArenaId = feat.ArenaId,
            ArenaName = ArenaConstants.GetArenaName(feat.ArenaId),
            PirateId = feat.PirateId,
            PirateName = GetPirateName(feat.PirateId),
            WinProbability = Math.Clamp(pred.Probability, 0.01f, 0.99f),
            Payout = Math.Max(2, feat.CurrentOdds)
        }).ToList();

        // Cache the predictions
        if (useCache)
            _predictionCache[roundId] = piratePredictions;

        return piratePredictions;
    }

    /// <summary>
    /// Predict from pre-loaded MlPirateFeature (for strategy comparison)
    /// </summary>
    public async Task<List<PiratePrediction>> PredictAsync(List<MlPirateFeature> features, bool useCache = true)
    {
        if (_model == null)
            throw new InvalidOperationException("Model must be trained first");

        if (!features.Any())
            return new List<PiratePrediction>();

        // Get RoundId from first feature for caching
        var roundId = features[0].RoundId;

        if (useCache && roundId > 0 && _predictionCache.TryGetValue(roundId, out var cachedPredictions))
            return cachedPredictions;

        // Ensure pirate names are cached
        await EnsurePirateNamesCachedAsync();

        var dataView = _mlContext.Data.LoadFromEnumerable(features);
        var predictions = _model.Transform(dataView);

        var predictionResults = _mlContext.Data
            .CreateEnumerable<PiratePredictionOutput>(predictions, false)
            .ToList();

        var piratePredictions = predictionResults.Zip(features, (pred, feat) => new PiratePrediction
        {
            RoundId = feat.RoundId,
            ArenaId = (int)feat.ArenaId,
            ArenaName = ArenaConstants.GetArenaName((int)feat.ArenaId),
            PirateId = feat.PirateId,
            PirateName = GetPirateName(feat.PirateId),
            WinProbability = Math.Clamp(pred.Probability, 0.01f, 0.99f),
            Payout = Math.Max(2, (int)feat.CurrentOdds)
        }).ToList();

        // Cache the predictions if we have a valid round ID
        if (useCache && roundId > 0)
            _predictionCache[roundId] = piratePredictions;

        return piratePredictions;
    }

    public void ClearPredictionCache()
    {
        _predictionCache.Clear();
    }

    #endregion

    #region Model Persistence

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

    #endregion

    #region Helper Methods

    private async Task EnsurePirateNamesCachedAsync()
    {
        if (_pirateNamesCache == null)
        {
            _pirateNamesCache = await _context.Pirates
                .AsNoTracking()
                .ToDictionaryAsync(p => p.Id, p => p.PirateName);
        }
    }

    private string GetPirateName(int pirateId)
    {
        if (_pirateNamesCache != null && _pirateNamesCache.TryGetValue(pirateId, out var name))
        {
            return name;
        }
        return $"Pirate #{pirateId}";
    }

    private List<MlPirateFeature> ConvertToMlFormat(List<PirateFeatureRecord> features)
    {
        // Group by round and arena to calculate relative features
        var groupedByRoundArena = features
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        return features.Select(f =>
        {
            var arenaGroup = groupedByRoundArena[(f.RoundId, f.ArenaId)];

            // Calculate arena-relative values
            var minOdds = arenaGroup.Min(x => x.CurrentOdds);
            var maxStrength = arenaGroup.Max(x => x.Strength);
            var avgStrength = arenaGroup.Average(x => x.Strength);
            var maxEffectiveStrength = arenaGroup.Max(x => x.Strength + x.FoodAdjustment);
            var avgRivalStrengthInArena = arenaGroup.Average(x => (float)x.AvgRivalStrength);
            var effectiveStrength = f.Strength + f.FoodAdjustment;

            // Odds movement
            var openingOdds = f.OpeningOdds > 0 ? f.OpeningOdds : f.CurrentOdds;
            var oddsMovement = f.CurrentOdds - openingOdds;
            var oddsMovementPercent = openingOdds > 0
                ? (float)oddsMovement / openingOdds
                : 0f;

            // Binary indicators
            var isOddsFavorite = f.CurrentOdds == minOdds ? 1f : 0f;
            var isStrengthFavorite = Math.Abs(f.Strength - maxStrength) < 0.001f ? 1f : 0f;
            var isEffectiveStrengthFavorite = Math.Abs(effectiveStrength - maxEffectiveStrength) < 0.001f ? 1f : 0f;
            var hasOddsShortened = oddsMovement < 0 ? 1f : 0f;
            var hasOddsDrifted = oddsMovement > 0 ? 1f : 0f;
            var hasPositiveFoodAdjustment = f.FoodAdjustment > 0 ? 1f : 0f;
            var hasNegativeFoodAdjustment = f.FoodAdjustment < 0 ? 1f : 0f;

            // Position indicators
            var isPositionOne = f.Position == 1 ? 1f : 0f;
            var isPositionTwo = f.Position == 2 ? 1f : 0f;
            var isPositionThree = f.Position == 3 ? 1f : 0f;
            var isPositionFour = f.Position == 4 ? 1f : 0f;
            var isFrontPosition = f.Position <= 2 ? 1f : 0f;

            // Relative strength
            var relativeStrength = avgStrength > 0
                ? f.Strength / avgStrength
                : 1f;

            // Derived conditions
            var isUndervalued = (f.CurrentOdds > 3 && f.Strength >= avgStrength * 1.1f) ? 1f : 0f;
            var isHotStreak = (f.RecentWinRate > f.HistoricalWinRate * 1.2) ? 1f : 0f;
            var isColdStreak = (f.RecentWinRate < f.HistoricalWinRate * 0.8) ? 1f : 0f;
            var isArenaSpecialist = (f.ArenaWinRate > f.HistoricalWinRate * 1.2) ? 1f : 0f;
            var isInexperienced = f.TotalAppearances < 20 ? 1f : 0f;
            var isLowStrength = f.Strength < avgStrength ? 1f : 0f;
            var isHighStrength = f.Strength > avgStrength * 1.1f ? 1f : 0f;
            var isModerateOdds = (f.CurrentOdds >= 3 && f.CurrentOdds <= 6) ? 1f : 0f;
            var beatsRivals = (f.WinRateVsCurrentRivals > 0.3) ? 1f : 0f;
            var weakRivals = avgRivalStrengthInArena < avgStrength * 0.9f ? 1f : 0f;

            return new MlPirateFeature
            {
                // Identifiers
                RoundId = f.RoundId,
                PirateId = f.PirateId,

                // Core features (all as float)
                Position = f.Position,
                ArenaId = f.ArenaId,
                CurrentOdds = Math.Max(2f, f.CurrentOdds),
                OpeningOdds = openingOdds,
                FoodAdjustment = f.FoodAdjustment,
                Strength = f.Strength,
                Weight = f.Weight,

                // Historical performance
                HistoricalWinRate = (float)f.HistoricalWinRate,
                TotalAppearances = f.TotalAppearances,
                ArenaWinRate = (float)f.ArenaWinRate,
                RecentWinRate = (float)f.RecentWinRate,
                WinRateVsCurrentRivals = (float)f.WinRateVsCurrentRivals,
                MatchesVsCurrentRivals = f.MatchesVsCurrentRivals,
                AvgRivalStrength = (float)f.AvgRivalStrength,

                // Derived features
                OddsMovement = oddsMovement,
                OddsMovementPercent = oddsMovementPercent,
                ImpliedProbability = 1f / Math.Max(2f, f.CurrentOdds),
                RelativeStrength = relativeStrength,
                EffectiveStrength = effectiveStrength,

                // Binary indicators
                IsOddsFavorite = isOddsFavorite,
                IsStrengthFavorite = isStrengthFavorite,
                IsEffectiveStrengthFavorite = isEffectiveStrengthFavorite,
                HasOddsShortened = hasOddsShortened,
                HasOddsDrifted = hasOddsDrifted,
                HasPositiveFoodAdjustment = hasPositiveFoodAdjustment,
                HasNegativeFoodAdjustment = hasNegativeFoodAdjustment,
                IsPositionOne = isPositionOne,
                IsPositionTwo = isPositionTwo,
                IsPositionThree = isPositionThree,
                IsPositionFour = isPositionFour,
                IsUndervalued = isUndervalued,
                IsHotStreak = isHotStreak,
                IsArenaSpecialist = isArenaSpecialist,

                // Arena indicators (one-hot encoding)
                IsArenaShipwreck = f.ArenaId == 1 ? 1f : 0f,
                IsArenaLagoon = f.ArenaId == 2 ? 1f : 0f,
                IsArenaTreasureIsland = f.ArenaId == 3 ? 1f : 0f,
                IsArenaHiddenCove = f.ArenaId == 4 ? 1f : 0f,
                IsArenaHarpoonHarrys = f.ArenaId == 5 ? 1f : 0f,

                // ═══════════════════════════════════════════════════
                // ANTAGONISTIC INTERACTION PENALTIES
                // ═══════════════════════════════════════════════════
                PenaltyFoodPosition = hasPositiveFoodAdjustment * isFrontPosition * f.FoodAdjustment,
                PenaltyFoodFavorite = hasPositiveFoodAdjustment * isOddsFavorite * f.FoodAdjustment,
                PenaltyStrengthPosition = isHighStrength * isFrontPosition * relativeStrength,
                PenaltyStrengthWeakRivals = isHighStrength * weakRivals * relativeStrength,
                PenaltyFavoriteInexperienced = isOddsFavorite * isInexperienced,
                PenaltyLowStrengthFavorite = isLowStrength * isOddsFavorite * (1f - relativeStrength),
                PenaltyOddsShortenedLowStrength = hasOddsShortened * isLowStrength,
                PenaltyArenaSpecialistColdStreak = isArenaSpecialist * isColdStreak,

                // ═══════════════════════════════════════════════════
                // SYNERGISTIC INTERACTION BONUSES
                // ═══════════════════════════════════════════════════
                BonusUndervaluedStrong = isUndervalued * isHighStrength * relativeStrength,
                BonusArenaSpecialistModerateOdds = isArenaSpecialist * isModerateOdds * (float)f.ArenaWinRate,
                BonusHotStreakBeatsRivals = isHotStreak * beatsRivals * (float)f.RecentWinRate,
                BonusFoodPositionThree = hasPositiveFoodAdjustment * isPositionThree * f.FoodAdjustment,
                BonusOddsShortenedStrong = hasOddsShortened * isHighStrength * Math.Abs(oddsMovement),
                BonusFavoriteArenaSpecialist = isOddsFavorite * isArenaSpecialist * (float)f.ArenaWinRate,
                BonusStrengthPlusFood = isHighStrength * hasPositiveFoodAdjustment * effectiveStrength,
                BonusHotStreakFavorite = isHotStreak * isOddsFavorite * (float)f.RecentWinRate,

                // ═══════════════════════════════════════════════════
                // THREE-WAY INTERACTIONS
                // ═══════════════════════════════════════════════════
                ThreeWayFoodPositionStrength = f.FoodAdjustment * f.Position * f.Strength / 100f,
                ThreeWayUndervaluedStrongBeatsRivals = isUndervalued * isHighStrength * beatsRivals * relativeStrength,
                ThreeWayFavoriteSpecialistHotStreak = isOddsFavorite * isArenaSpecialist * isHotStreak,
                ThreeWayStrengthFoodPositionThree = isHighStrength * hasPositiveFoodAdjustment * isPositionThree * effectiveStrength,

                // Label
                Won = f.IsWinner ?? false
            };
        }).ToList();
    }

    #endregion

    #region Causal Analysis Helpers

   private FeatureSelectionResult SelectFeaturesBasedOnCausalAnalysis(ComprehensiveCausalReport causalReport)
    {
        var result = new FeatureSelectionResult();
        var featureEffects = new Dictionary<string, double>();

        // ═══════════════════════════════════════════════════
        // ALWAYS INCLUDE: Core proven features
        // ═══════════════════════════════════════════════════
        var coreFeatures = new[]
        {
            nameof(MlPirateFeature.CurrentOdds),
            nameof(MlPirateFeature.HistoricalWinRate),
            nameof(MlPirateFeature.ArenaWinRate),
            nameof(MlPirateFeature.RecentWinRate),
            nameof(MlPirateFeature.ImpliedProbability),
            nameof(MlPirateFeature.TotalAppearances)
        };
        result.SelectedFeatures.AddRange(coreFeatures);

        // ═══════════════════════════════════════════════════
        // FOOD ADJUSTMENT EFFECTS
        // ═══════════════════════════════════════════════════
        if (causalReport.FoodAdjustmentEffect.IsSignificant)
        {
            // Core food features
            result.SelectedFeatures.Add(nameof(MlPirateFeature.FoodAdjustment));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.HasPositiveFoodAdjustment));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.HasNegativeFoodAdjustment));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.EffectiveStrength));

            // Food-related penalties
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyFoodPosition));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyFoodFavorite));

            // Food-related bonuses
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusFoodPositionThree));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusStrengthPlusFood));

            // Food three-way interactions
            result.SelectedFeatures.Add(nameof(MlPirateFeature.ThreeWayFoodPositionStrength));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.ThreeWayStrengthFoodPositionThree));

            featureEffects[nameof(MlPirateFeature.FoodAdjustment)] =
                causalReport.FoodAdjustmentEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.FoodAdjustment));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.PenaltyFoodPosition));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.PenaltyFoodFavorite));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.BonusFoodPositionThree));
        }

        // ═══════════════════════════════════════════════════
        // SEAT POSITION EFFECTS
        // ═══════════════════════════════════════════════════
        if (causalReport.OverallSeatPositionJointTest?.IsSignificant == true)
        {
            // Core position features
            result.SelectedFeatures.Add(nameof(MlPirateFeature.Position));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsPositionOne));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsPositionTwo));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsPositionThree));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsPositionFour));

            // Position-related penalties
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyStrengthPosition));

            if (causalReport.EachSeatVsOthersEffects.Any())
            {
                var strongestEffect = causalReport.EachSeatVsOthersEffects.Values
                    .OrderByDescending(e => Math.Abs(e.AverageTreatmentEffect))
                    .First();
                featureEffects[nameof(MlPirateFeature.Position)] = strongestEffect.AverageTreatmentEffect;
            }
            else if (causalReport.OverallSeatPositionJointTest != null)
            {
                featureEffects[nameof(MlPirateFeature.Position)] =
                    causalReport.OverallSeatPositionJointTest.AverageTreatmentEffect;
            }
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.Position));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsPositionOne));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsPositionTwo));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsPositionThree));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsPositionFour));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.PenaltyStrengthPosition));
        }

        // ═══════════════════════════════════════════════════
        // ARENA EFFECTS
        // ═══════════════════════════════════════════════════
        if (causalReport.OverallArenaJointTest?.IsSignificant == true)
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.ArenaId));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsArenaShipwreck));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsArenaLagoon));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsArenaTreasureIsland));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsArenaHiddenCove));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsArenaHarpoonHarrys));

            if (causalReport.IndividualArenaEffects.Any())
            {
                var strongestArenaEffect = causalReport.IndividualArenaEffects.Values
                    .OrderByDescending(e => Math.Abs(e.AverageTreatmentEffect))
                    .First();
                featureEffects[nameof(MlPirateFeature.ArenaId)] = strongestArenaEffect.AverageTreatmentEffect;
            }
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.ArenaId));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsArenaShipwreck));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsArenaLagoon));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsArenaTreasureIsland));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsArenaHiddenCove));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.IsArenaHarpoonHarrys));
        }

        // ═══════════════════════════════════════════════════
        // RIVAL STRENGTH EFFECTS
        // ═══════════════════════════════════════════════════
        if (causalReport.RivalStrengthEffect.IsSignificant)
        {
            // Core rival features
            result.SelectedFeatures.Add(nameof(MlPirateFeature.AvgRivalStrength));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.WinRateVsCurrentRivals));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.MatchesVsCurrentRivals));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.RelativeStrength));

            // Rival-related penalties
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyStrengthWeakRivals));

            // Rival-related bonuses
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusHotStreakBeatsRivals));

            // Rival three-way interactions
            result.SelectedFeatures.Add(nameof(MlPirateFeature.ThreeWayUndervaluedStrongBeatsRivals));

            featureEffects[nameof(MlPirateFeature.AvgRivalStrength)] =
                causalReport.RivalStrengthEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.AvgRivalStrength));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.WinRateVsCurrentRivals));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.PenaltyStrengthWeakRivals));
        }

        // ═══════════════════════════════════════════════════
        // ODDS EFFECTS
        // ═══════════════════════════════════════════════════
        if (causalReport.OddsEffect.IsSignificant)
        {
            // Core odds features
            result.SelectedFeatures.Add(nameof(MlPirateFeature.OpeningOdds));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.OddsMovement));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.OddsMovementPercent));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsOddsFavorite));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.HasOddsShortened));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.HasOddsDrifted));

            // Odds-related penalties
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyFavoriteInexperienced));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyLowStrengthFavorite));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyOddsShortenedLowStrength));

            // Odds-related bonuses
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusOddsShortenedStrong));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusFavoriteArenaSpecialist));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusHotStreakFavorite));

            // Odds three-way interactions
            result.SelectedFeatures.Add(nameof(MlPirateFeature.ThreeWayFavoriteSpecialistHotStreak));

            featureEffects[nameof(MlPirateFeature.CurrentOdds)] = causalReport.OddsEffect.AverageTreatmentEffect;
        }
        else
        {
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.OddsMovement));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.OddsMovementPercent));
            result.ExcludedFeatures.Add(nameof(MlPirateFeature.PenaltyOddsShortenedLowStrength));
        }

        // ═══════════════════════════════════════════════════
        // STRENGTH FEATURES (include if any causal effects)
        // ═══════════════════════════════════════════════════
        if (featureEffects.Values.Any(v => Math.Abs(v) > 0.01))
        {
            result.SelectedFeatures.Add(nameof(MlPirateFeature.Strength));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.Weight));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsStrengthFavorite));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsEffectiveStrengthFavorite));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.IsUndervalued));
            result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusUndervaluedStrong));
        }

        // ═══════════════════════════════════════════════════
        // FORM AND SPECIALIZATION (always include)
        // ═══════════════════════════════════════════════════
        result.SelectedFeatures.Add(nameof(MlPirateFeature.IsHotStreak));
        result.SelectedFeatures.Add(nameof(MlPirateFeature.IsArenaSpecialist));
        result.SelectedFeatures.Add(nameof(MlPirateFeature.BonusArenaSpecialistModerateOdds));
        result.SelectedFeatures.Add(nameof(MlPirateFeature.PenaltyArenaSpecialistColdStreak));

// ═══════════════════════════════════════════════════
        // CLEAN UP: Remove duplicates and finalize
        // ═══════════════════════════════════════════════════
        result.SelectedFeatures = result.SelectedFeatures.Distinct().ToList();
        result.ExcludedFeatures = result.ExcludedFeatures
            .Where(f => !result.SelectedFeatures.Contains(f))
            .Distinct()
            .ToList();
        result.FeatureEffects = featureEffects;

        // ═══════════════════════════════════════════════════
        // LOG FEATURE SELECTION SUMMARY
        // ═══════════════════════════════════════════════════
        LogFeatureSelectionSummary(result, causalReport);

        return result;
    }

    private void LogFeatureSelectionSummary(FeatureSelectionResult result, ComprehensiveCausalReport causalReport)
    {
        Console.WriteLine("\n   ┌─────────────────────────────────────────────────────────────┐");
        Console.WriteLine("   │            CAUSAL FEATURE SELECTION SUMMARY                 │");
        Console.WriteLine("   └─────────────────────────────────────────────────────────────┘");

        // Causal significance summary
        Console.WriteLine("\n   📊 CAUSAL SIGNIFICANCE:");
        Console.WriteLine($"      Food Adjustment:  {(causalReport.FoodAdjustmentEffect.IsSignificant ? "✅ Significant" : "❌ Not Significant")} " +
                          $"({causalReport.FoodAdjustmentEffect.AverageTreatmentEffect:+0.0%;-0.0%})");
        Console.WriteLine($"      Seat Position:    {(causalReport.OverallSeatPositionJointTest?.IsSignificant == true ? "✅ Significant" : "❌ Not Significant")} " +
                          $"({causalReport.OverallSeatPositionJointTest?.AverageTreatmentEffect ?? 0:+0.0%;-0.0%})");
        Console.WriteLine($"      Arena Effects:    {(causalReport.OverallArenaJointTest?.IsSignificant == true ? "✅ Significant" : "❌ Not Significant")} " +
                          $"({causalReport.OverallArenaJointTest?.AverageTreatmentEffect ?? 0:+0.0%;-0.0%})");
        Console.WriteLine($"      Rival Strength:   {(causalReport.RivalStrengthEffect.IsSignificant ? "✅ Significant" : "❌ Not Significant")} " +
                          $"({causalReport.RivalStrengthEffect.AverageTreatmentEffect:+0.0%;-0.0%})");
        Console.WriteLine($"      Odds Effect:      {(causalReport.OddsEffect.IsSignificant ? "✅ Significant" : "❌ Not Significant")} " +
                          $"({causalReport.OddsEffect.AverageTreatmentEffect:+0.0%;-0.0%})");

        // Feature counts by category
        var coreCount = result.SelectedFeatures.Count(f =>
            f.Contains("Odds") || f.Contains("WinRate") || f.Contains("Appearances") || f.Contains("Implied"));
        var penaltyCount = result.SelectedFeatures.Count(f => f.StartsWith("Penalty"));
        var bonusCount = result.SelectedFeatures.Count(f => f.StartsWith("Bonus"));
        var threeWayCount = result.SelectedFeatures.Count(f => f.StartsWith("ThreeWay"));
        var binaryCount = result.SelectedFeatures.Count(f =>
            f.StartsWith("Is") || f.StartsWith("Has"));
        var arenaCount = result.SelectedFeatures.Count(f => f.Contains("Arena"));

        Console.WriteLine("\n   📈 SELECTED FEATURES BY CATEGORY:");
        Console.WriteLine($"      Core Features:        {coreCount}");
        Console.WriteLine($"      Binary Indicators:    {binaryCount}");
        Console.WriteLine($"      Arena Features:       {arenaCount}");
        Console.WriteLine($"      Penalty Interactions: {penaltyCount}");
        Console.WriteLine($"      Bonus Interactions:   {bonusCount}");
        Console.WriteLine($"      Three-Way:            {threeWayCount}");
        Console.WriteLine("      ─────────────────────────────");
        Console.WriteLine($"      TOTAL SELECTED:       {result.SelectedFeatures.Count}");
        Console.WriteLine($"      TOTAL EXCLUDED:       {result.ExcludedFeatures.Count}");

        // Top causal effects
        if (result.FeatureEffects.Any())
        {
            Console.WriteLine("\n   🎯 STRONGEST CAUSAL EFFECTS:");
            var topEffects = result.FeatureEffects
                .OrderByDescending(kv => Math.Abs(kv.Value))
                .Take(5);

            foreach (var (feature, effect) in topEffects)
            {
                var direction = effect > 0 ? "↑" : "↓";
                Console.WriteLine($"      {direction} {feature,-30} {effect:+0.00%;-0.00%}");
            }
        }

        // Interaction effects summary
        if (causalReport.InteractionEffects?.Any() == true)
        {
            var synergies = causalReport.InteractionEffects.Count(ie => ie.Value.IsSynergistic);
            var antagonisms = causalReport.InteractionEffects.Count(ie => ie.Value.IsAntagonistic);

            Console.WriteLine("\n   🔗 INTERACTION EFFECTS:");
            Console.WriteLine($"      Synergistic:   {synergies} (bonuses that amplify)");
            Console.WriteLine($"      Antagonistic:  {antagonisms} (penalties that reduce)");
        }
    }

    private async Task<ITransformer> TrainStandardModelForComparison(List<PirateFeatureRecord> trainData)
    {
        var mlData = ConvertToMlFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

        var pipeline = BuildStandardPipeline();

        return await Task.FromResult(pipeline.Fit(dataView));
    }

    private void GenerateCausalInsights(
        ComprehensiveCausalReport causalReport,
        ModelEvaluationReport evalReport,
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
            recommendations.Add("Use BonusStrengthPlusFood interaction for strong pirates with positive food");
        }
        else if (!causalReport.FoodAdjustmentEffect.IsSignificant)
        {
            findings.Add("Food adjustment shows correlation but weak causal evidence");
            recommendations.Add("Use food adjustment cautiously - may be confounded with other factors");
        }

        // Rival strength insights
        if (causalReport.RivalStrengthEffect.IsSignificant &&
            causalReport.RivalStrengthEffect.AverageTreatmentEffect < -0.03)
        {
            findings.Add(
                $"Strong rivals significantly reduce win probability ({causalReport.RivalStrengthEffect.AverageTreatmentEffect:+0.00%;-0.00%;0.00%})");
            recommendations.Add("Head-to-head matchups are critical - include rival analysis in all strategies");
            recommendations.Add("Look for ThreeWayUndervaluedStrongBeatsRivals opportunities");
        }

        // Odds insights
        if (causalReport.OddsEffect.IsSignificant)
        {
            findings.Add(
                $"Favorite status has causal effect ({causalReport.OddsEffect.AverageTreatmentEffect:+0.00%;-0.00%;0.00%})");

            if (causalReport.OddsEffect.DoseResponse != null)
            {
                var doseEffects = causalReport.OddsEffect.DoseResponse.OrderBy(kv => kv.Key).ToList();
                var efficiency = doseEffects.FirstOrDefault(kv => kv.Value / (1.0 / kv.Key) > 1.2);

                if (efficiency.Key > 0)
                    recommendations.Add(
                        $"Pirates at {efficiency.Key}:1 odds show best value (win rate: {efficiency.Value:P1})");
            }
        }

        // Position insights
        if (causalReport.OverallSeatPositionJointTest?.IsSignificant == true)
        {
            findings.Add("Seat position has significant effect on outcomes");
            
            if (causalReport.EachSeatVsOthersEffects.Any())
            {
                var bestPosition = causalReport.EachSeatVsOthersEffects
                    .OrderByDescending(kv => kv.Value.AverageTreatmentEffect)
                    .First();
                recommendations.Add($"Position {bestPosition.Key} shows best advantage ({bestPosition.Value.AverageTreatmentEffect:+P1})");
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
                foreach (var (key, effect) in synergies.Take(3))
                    recommendations.Add($"Combine {effect.Name} for {effect.InteractionStrength:+P1} bonus");
            }

            if (antagonisms.Any())
            {
                findings.Add($"Found {antagonisms.Count} antagonistic effect combinations");
                foreach (var (key, effect) in antagonisms.Take(3))
                    recommendations.Add(
                        $"Avoid combining {effect.Name} (reduces effect by {-effect.InteractionStrength:P1})");
            }
        }

        // Model performance insights
        if (evalReport.Auc > 0.75)
            findings.Add($"Causal model achieves strong performance (Auc: {evalReport.Auc:F3})");
        else if (evalReport.Auc > 0.65)
            findings.Add($"Causal model achieves moderate performance (Auc: {evalReport.Auc:F3})");
        else
            findings.Add($"Causal model shows weak performance (Auc: {evalReport.Auc:F3}) - consider more features");

        if (evalReport.CalibrationMetrics?.OverallCalibrationError < 0.10)
            findings.Add("Model probabilities are well-calibrated for betting decisions");
        else
            recommendations.Add("Apply additional probability calibration before betting");

        // Feature selection insights
        findings.Add($"Selected {featureSelection.SelectedFeatures.Count} features based on causal analysis");
        findings.Add($"Excluded {featureSelection.ExcludedFeatures.Count} non-causal features");

        causalReport.KeyFindings = findings;
        causalReport.Recommendations = recommendations;

        Console.WriteLine("\n📋 KEY FINDINGS:");
        foreach (var finding in findings)
            Console.WriteLine($"   • {finding}");

        Console.WriteLine("\n💡 RECOMMENDATIONS:");
        foreach (var rec in recommendations)
            Console.WriteLine($"   → {rec}");
    }

    #endregion

    #region Report Saving

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
            CausalAnalysis = new
            {
                causalReport.FoodAdjustmentEffect,
                causalReport.OddsEffect,
                causalReport.RivalStrengthEffect,
                PositionEffect = causalReport.OverallSeatPositionJointTest,
                ArenaEffect = causalReport.OverallArenaJointTest,
                InteractionCount = causalReport.InteractionEffects?.Count ?? 0,
                causalReport.KeyFindings,
                causalReport.Recommendations
            },
            FeatureSelection = new
            {
                SelectedCount = featureSelection.SelectedFeatures.Count,
                ExcludedCount = featureSelection.ExcludedFeatures.Count,
                featureSelection.SelectedFeatures,
                featureSelection.ExcludedFeatures,
                featureSelection.FeatureEffects
            },
            CrossValidation = new
            {
                TimeSeries = new
                {
                    timeSeriesCV.AverageAUC,
                    timeSeriesCV.StdDevAUC,
                    timeSeriesCV.AverageAccuracy,
                    timeSeriesCV.StdDevAccuracy,
                    timeSeriesCV.FoldResults
                },
                KFold = new
                {
                    kFoldCV.AverageAUC,
                    kFoldCV.StdDevAUC,
                    kFoldCV.AverageAccuracy,
                    kFoldCV.StdDevAccuracy,
                    kFoldCV.FoldResults
                }
            },
            ModelEvaluation = new
            {
                evalReport.Accuracy,
                AUC = evalReport.Auc,
                evalReport.F1Score,
                evalReport.LogLoss,
                evalReport.CalibrationMetrics
            },
            Summary = new
            {
                TotalFeaturesAnalyzed =
                    featureSelection.SelectedFeatures.Count + featureSelection.ExcludedFeatures.Count,
                CausallySignificantFeatures = featureSelection.SelectedFeatures.Count,
                StrongestCausalEffect = featureSelection.FeatureEffects.Any()
                    ? featureSelection.FeatureEffects.Values.Max(v => Math.Abs(v))
                    : 0,
                ModelStability = timeSeriesCV.StdDevAUC < 0.02 ? "Stable" : "Variable",
                RecommendedOptimization = DetermineRecommendedOptimization(causalReport),
                OverallAssessment = GetOverallAssessment(evalReport, timeSeriesCV)
            }
        };

        var fileName = Path.Combine("Reports", $"causal_model_report_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(comprehensiveReport, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"\n📄 Comprehensive causal report saved to {fileName}");
    }

    private string DetermineRecommendedOptimization(ComprehensiveCausalReport causalReport)
    {
        if (causalReport.FoodAdjustmentEffect.IsSignificant &&
            causalReport.InteractionEffects.Any(ie => ie.Value.IsSynergistic))
            return "ConsistencyWeighted - Multiple causal factors suggest focusing on reliable combinations";

        if (causalReport.OddsEffect.IsSignificant &&
            Math.Abs(causalReport.OddsEffect.AverageTreatmentEffect) > 0.1)
            return "Kelly - Strong odds effects suggest Kelly criterion for bet sizing";

        if (causalReport.RivalStrengthEffect.IsSignificant)
            return "RiskAdjusted - Rival effects suggest risk-adjusted approach";

        return "ConsistencyWeighted - Default safe choice";
    }

    private string GetOverallAssessment(ModelEvaluationReport evalReport, CrossValidationReport cvReport)
    {
        if (evalReport.Auc > 0.75 && cvReport.StdDevAUC < 0.02)
            return "Excellent - Strong and stable performance";

        if (evalReport.Auc > 0.70 && cvReport.StdDevAUC < 0.03)
            return "Good - Solid performance with acceptable variance";

        if (evalReport.Auc > 0.65)
            return "Moderate - Useful but room for improvement";

        return "Weak - Consider alternative approaches or more data";
    }

    private void SaveCrossValidationResults(
        CrossValidationReport timeSeriesCV,
        CrossValidationReport kFoldCV,
        ModelEvaluationReport finalEval)
    {
        Directory.CreateDirectory("Reports");

        var report = new
        {
            GeneratedAt = DateTime.UtcNow,
            TimeSeriesCrossValidation = new
            {
                timeSeriesCV.AverageAUC,
                timeSeriesCV.StdDevAUC,
                timeSeriesCV.AverageAccuracy,
                timeSeriesCV.StdDevAccuracy,
                FoldCount = timeSeriesCV.FoldResults?.Count ?? 0,
                timeSeriesCV.FoldResults
            },
            KFoldCrossValidation = new
            {
                kFoldCV.AverageAUC,
                kFoldCV.StdDevAUC,
                kFoldCV.AverageAccuracy,
                kFoldCV.StdDevAccuracy,
                FoldCount = kFoldCV.FoldResults?.Count ?? 0,
                kFoldCV.FoldResults
            },
            FinalModelEvaluation = new
            {
                finalEval.Accuracy,
                AUC = finalEval.Auc,
                finalEval.F1Score,
                finalEval.LogLoss,
                finalEval.Precision,
                finalEval.Recall
            },
            StabilityMetrics = new
            {
                TimeSeriesVariance = timeSeriesCV.StdDevAUC,
                KFoldVariance = kFoldCV.StdDevAUC,
                FinalVsCVDifference = Math.Abs(finalEval.Auc - timeSeriesCV.AverageAUC),
                IsStable = Math.Abs(finalEval.Auc - timeSeriesCV.AverageAUC) < 0.03 && timeSeriesCV.StdDevAUC < 0.02,
                Recommendation = GetStabilityRecommendation(timeSeriesCV, kFoldCV, finalEval)
            },
            Comparison = new
            {
                TimeSeriesVsKFoldDiff = Math.Abs(timeSeriesCV.AverageAUC - kFoldCV.AverageAUC),
                PreferredMethod = timeSeriesCV.AverageAUC < kFoldCV.AverageAUC
                    ? "Time-Series (more conservative)"
                    : "K-Fold",
                ExpectedRealWorldAUC = Math.Min(timeSeriesCV.AverageAUC, kFoldCV.AverageAUC)
            }
        };

        var fileName = Path.Combine("Reports", $"cross_validation_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"📄 Cross-validation report saved to {fileName}");
    }

    private string GetStabilityRecommendation(
        CrossValidationReport timeSeriesCV,
        CrossValidationReport kFoldCV,
        ModelEvaluationReport finalEval)
    {
        var finalVsCVDiff = Math.Abs(finalEval.Auc - timeSeriesCV.AverageAUC);

        if (finalVsCVDiff < 0.02 && timeSeriesCV.StdDevAUC < 0.02)
            return "Model is stable - safe to deploy";

        if (finalVsCVDiff > 0.05)
            return "High variance between CV and final - may overfit to test set";

        if (timeSeriesCV.StdDevAUC > 0.04)
            return "High variance across folds - consider more regularization";

        if (Math.Abs(timeSeriesCV.AverageAUC - kFoldCV.AverageAUC) > 0.03)
            return "Time-series and K-fold differ significantly - temporal patterns present";

        return "Model shows acceptable stability";
    }

    #endregion
}

/// <summary>
/// ML.NET prediction output
/// </summary>
public class PiratePredictionOutput
{
    public bool PredictedLabel { get; set; }
    public float Probability { get; set; }
    public float Score { get; set; }
}

/// <summary>
/// Result of causal-based feature selection
/// </summary>
public class FeatureSelectionResult
{
    /// <summary>
    /// Features selected based on causal significance
    /// </summary>
    public List<string> SelectedFeatures { get; set; } = new();

    /// <summary>
    /// Features excluded due to lack of causal evidence
    /// </summary>
    public List<string> ExcludedFeatures { get; set; } = new();

    /// <summary>
    /// Estimated causal effect for each significant feature
    /// </summary>
    public Dictionary<string, double> FeatureEffects { get; set; } = new();

    /// <summary>
    /// Summary statistics
    /// </summary>
    public int TotalFeaturesAnalyzed => SelectedFeatures.Count + ExcludedFeatures.Count;

    public double SelectionRate => TotalFeaturesAnalyzed > 0
        ? (double)SelectedFeatures.Count / TotalFeaturesAnalyzed
        : 0;

    public double StrongestEffect => FeatureEffects.Any()
        ? FeatureEffects.Values.Max(v => Math.Abs(v))
        : 0;

    public override string ToString()
    {
        return $"Selected: {SelectedFeatures.Count}, Excluded: {ExcludedFeatures.Count}, " +
               $"Selection Rate: {SelectionRate:P1}, Strongest Effect: {StrongestEffect:P1}";
    }
}