using Microsoft.ML;
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

        if (leakageReport.HasLeakage)
        {
            Console.WriteLine("❌ Data leakage detected!");
            return;
        }

        // Step 2: Cross-validation
        Console.WriteLine("\n📊 Step 2: Cross-Validation...");

        // Run time-series cross-validation (recommended for temporal data)
        var timeSeriesCV = await crossValService.PerformTimeSeriesCrossValidationAsync();

        // Optional: Also run k-fold for comparison
        var kFoldCV = await crossValService.PerformKFoldCrossValidationAsync();

        // Step 3: Train final model on all data
        Console.WriteLine("\n🏋️ Step 3: Training final model on full dataset...");
        var sortedData = filteredData.OrderBy(f => f.RoundId).ToList();
        var splitIndex = (int)(sortedData.Count * 0.8);

        var trainData = sortedData.Take(splitIndex).ToList();
        var testData = sortedData.Skip(splitIndex).ToList();

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
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 50,
                learningRate: 0.05,
                numberOfIterations: 50));

        _model = pipeline.Fit(dataView);

        // Step 4: Final evaluation
        Console.WriteLine("\n📈 Step 4: Final model evaluation...");
        var evaluationReport = await evaluationService.EvaluateModelAsync(_model, testData);

        // Step 5: Compare cross-validation vs final model
        Console.WriteLine("\n🔬 Step 5: Model Stability Analysis");
        Console.WriteLine($"   Cross-Val AUC:  {timeSeriesCV.AverageAUC:F4} ± {timeSeriesCV.StdDevAUC:F4}");
        Console.WriteLine($"   Final Model AUC: {evaluationReport.AUC:F4}");

        var aucDiff = Math.Abs(evaluationReport.AUC - timeSeriesCV.AverageAUC);
        if (aucDiff < 0.02)
            Console.WriteLine($"   ✅ Model performance is consistent (diff: {aucDiff:F4})");
        else
            Console.WriteLine($"   ⚠️ Model performance varies (diff: {aucDiff:F4}) - may indicate overfitting");

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