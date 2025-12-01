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

    // Skip early rounds with limited history (potential overfitting source)
    var minRound = validData.Min(f => f.RoundId);
    var filteredData = validData.Where(f => f.RoundId > minRound + 100).ToList(); // Skip first 100 rounds

    Console.WriteLine($"Filtered to {filteredData.Count} records (skipping first 100 rounds for stability)");

    var evaluationService = new ModelEvaluationService();
    var leakageReport = await evaluationService.CheckForDataLeakageAsync(filteredData, _context);

    if (leakageReport.LeakageIssues.Count() > 5)
    {
        foreach (var issue in leakageReport.LeakageIssues)
        {
            Console.WriteLine($"{issue} in Training Round : {leakageReport.TrainRoundRange} | Testing Round : {leakageReport.TestRoundRange}");
        }
    }

    // Time-based split
    var sortedData = filteredData.OrderBy(f => f.RoundId).ToList();
    var splitIndex = (int)(sortedData.Count * 0.8);

    var trainData = sortedData.Take(splitIndex).ToList();
    var testData = sortedData.Skip(splitIndex).ToList();

    Console.WriteLine($"\n📊 Data Split:");
    Console.WriteLine($"   Training: {trainData.Count} records (rounds {trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)})");
    Console.WriteLine($"   Testing:  {testData.Count} records (rounds {testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)})");

    // Convert to ML.NET format
    var mlTrainData = ConvertToMlFormat(trainData);
    var dataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);

    // Simplified pipeline with regularization to prevent overfitting
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
            numberOfLeaves: 20, // Reduced from default to prevent overfitting
            minimumExampleCountPerLeaf: 50, // Increased to prevent overfitting
            learningRate: 0.05, // Lower learning rate
            numberOfIterations: 50)) // Reduced iterations
        .Append(_mlContext.BinaryClassification.Calibrators.Platt(
            labelColumnName: nameof(MlPirateFeature.Won),
            scoreColumnName: "Score"));

    var startTime = DateTime.Now;
    _model = pipeline.Fit(dataView);
    var trainingTime = DateTime.Now - startTime;

    Console.WriteLine($"   Training completed in {trainingTime.TotalSeconds:F1} seconds");

    // Evaluate on test set
    Console.WriteLine("\n📈 Step 4: Evaluating model...");
    var evaluationReport = await evaluationService.EvaluateModelAsync(_model, testData);

    // Feature importance
    Console.WriteLine("\n🔍 Step 5: Feature importance...");
    var importanceReport = await evaluationService.AnalyzeFeatureImportanceAsync(trainData);

    Console.WriteLine("\n📊 Top Features:");
    var sortedImportance = importanceReport.FeatureImportance
        .OrderByDescending(f => Math.Abs(f.Importance))
        .ToList();
    
    for (int i = 0; i < Math.Min(8, sortedImportance.Count); i++)
    {
        var (featureName, importance) = sortedImportance[i];
        Console.WriteLine($"   {i + 1}. {featureName,-30}: Impact {Math.Abs(importance):F4}");
    }

    // Assessment
    Console.WriteLine("\n💡 Model Assessment:");
    
    if (evaluationReport.AUC > 0.95)
    {
        Console.WriteLine("   ⚠️ AUC very high - Model may be overfitting to training data");
        Console.WriteLine("   📌 Recommendation: Reduce model complexity or add more diverse training data");
    }

    if (evaluationReport.CalibrationMetrics.OverallCalibrationError > 0.15)
    {
        Console.WriteLine("   ⚠️ Poor calibration - Predicted probabilities don't match actual win rates");
        Console.WriteLine("   📌 Recommendation: Use isotonic regression calibration or collect more data");
    }

    if (double.IsInfinity(evaluationReport.LogLoss))
    {
        Console.WriteLine("   ⚠️ Infinite log loss - Model predicting 0% or 100% probabilities");
        Console.WriteLine("   📌 Recommendation: Add label smoothing or increase min/max probability bounds");
    }
}

    public async Task TrainModelAsync()
    {
        Console.WriteLine("🤖 Training ML model (without detailed evaluation)...");

        var trainingData = await _featureService.CreateTrainingDataAsync(3800);
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

        var mlData = ConvertToMlFormat(features);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var predictions = _model.Transform(dataView);

        var predictionResults = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictions, false).ToList();

        return predictionResults.Zip(features, (pred, feat) => new PiratePrediction
        {
            RoundId = feat.RoundId,
            ArenaId = feat.ArenaId,
            PirateId = feat.PirateId,
            WinProbability = Math.Clamp(pred.Probability, 0.01f, 0.99f), // Clip probabilities to prevent infinity
            Payout = Math.Max(2, feat.CurrentOdds)
        }).ToList();
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

    private List<MlPirateFeature> ConvertToMlFormat(List<PirateFeatureRecord> features)
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
}