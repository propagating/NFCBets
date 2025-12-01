using Microsoft.ML;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services
{
public class MlModelService : IMlModelService
{
    private readonly MLContext _mlContext;
    private readonly IFeatureEngineeringService _featureService;
    private readonly NfcbetsContext _context;
    private ITransformer? _model;

    public MlModelService(IFeatureEngineeringService featureService, NfcbetsContext context)
    {
        _mlContext = new MLContext(seed: 42);
        _featureService = featureService;
        _context = context;
    }


    public async Task TrainAndEvaluateModelAsync()
    {
        Console.WriteLine("🤖 Training and evaluating ML model...");

        var allData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();

        Console.WriteLine($"Total valid training data: {validData.Count} records");

        // Create evaluation service
        var evaluationService = new ModelEvaluationService();

        // Check for data leakage BEFORE training
        Console.WriteLine("\n🔍 Step 1: Checking for data leakage...");
        var leakageReport = await evaluationService.CheckForDataLeakageAsync(validData, _context);

        if (leakageReport.HasLeakage)
        {
            Console.WriteLine("❌ Data leakage detected! Please fix issues before proceeding.");
            foreach (var issue in leakageReport.LeakageIssues.Take(10))
            {
                Console.WriteLine($"   {issue}");
            }
            return;
        }

        // Time-based split (80% train, 20% test)
        Console.WriteLine("\n📊 Step 2: Splitting data (time-based)...");
        var sortedData = validData.OrderBy(f => f.RoundId).ToList();
        var splitIndex = (int)(sortedData.Count * 0.8);

        var trainData = sortedData.Take(splitIndex).ToList();
        var testData = sortedData.Skip(splitIndex).ToList();

        Console.WriteLine($"   Training set: {trainData.Count} records (rounds {trainData.Min(f => f.RoundId)}-{trainData.Max(f => f.RoundId)})");
        Console.WriteLine($"   Test set:     {testData.Count} records (rounds {testData.Min(f => f.RoundId)}-{testData.Max(f => f.RoundId)})");

        // Convert to ML.NET format and train
        Console.WriteLine("\n🏋️ Step 3: Training model...");
        var mlTrainData = ConvertToMLFormat(trainData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlTrainData);

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
                labelColumnName: nameof(MlPirateFeature.Won),
                featureColumnName: "Features",
                numberOfIterations: 100))
            .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: nameof(MlPirateFeature.Won),
                scoreColumnName: "Score"));

        var startTime = DateTime.Now;
        _model = pipeline.Fit(dataView);
        var trainingTime = DateTime.Now - startTime;

        Console.WriteLine($"   Training completed in {trainingTime.TotalSeconds:F1} seconds");

        // Evaluate on test set
        Console.WriteLine("\n📈 Step 4: Evaluating model on test set...");
        var evaluationReport = await evaluationService.EvaluateModelAsync(_model, testData);

        // Feature importance analysis
        Console.WriteLine("\n🔍 Step 5: Analyzing feature importance...");
        var importanceReport = await evaluationService.AnalyzeFeatureImportanceAsync(trainData);

        Console.WriteLine("\n📊 Feature Importance Ranking:");
        var sortedImportance = importanceReport.FeatureImportance.OrderByDescending(f => f.Importance).ToList();
        
        for (int i = 0; i < sortedImportance.Count; i++)
        {
            var (featureName, importance) = sortedImportance[i];
            var indicator = i < 3 ? "🔴" : i < 6 ? "🟡" : "🟢";
            Console.WriteLine($"   {i + 1,2}. {indicator} {featureName,-30}: {importance:+0.0000;-0.0000}");
        }

        // Recommendations based on evaluation
        Console.WriteLine("\n💡 Recommendations:");
        
        if (evaluationReport.AUC < 0.6)
        {
            Console.WriteLine("   ⚠️ Low AUC - Consider adding more features or collecting more data");
        }
        else if (evaluationReport.AUC > 0.8)
        {
            Console.WriteLine("   ✅ Strong predictive power");
        }

        if (evaluationReport.CalibrationMetrics.OverallCalibrationError > 0.15)
        {
            Console.WriteLine("   ⚠️ Poor probability calibration - Consider using different calibration method");
        }

        var lowImportanceFeatures = sortedImportance.Where(f => Math.Abs(f.Importance) < 0.001).ToList();
        if (lowImportanceFeatures.Any())
        {
            Console.WriteLine($"   💡 Consider removing low-importance features: {string.Join(", ", lowImportanceFeatures.Select(f => f.FeatureName))}");
        }

        Console.WriteLine("\n✅ Model training and evaluation complete");
    }

    public async Task TrainModelAsync()
    {
        Console.WriteLine("🤖 Training ML model (without detailed evaluation)...");

        var trainingData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = trainingData.Where(f => f.IsWinner.HasValue).ToList();

        Console.WriteLine($"Training with {validData.Count} records");

        var mlData = ConvertToMLFormat(validData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var trainTestSplit = _mlContext.Data.TrainTestSplit(dataView, testFraction: 0.2);

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
                labelColumnName: nameof(MlPirateFeature.Won),
                featureColumnName: "Features",
                numberOfIterations: 100))
            .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: nameof(MlPirateFeature.Won),
                scoreColumnName: "Score"));

        _model = pipeline.Fit(trainTestSplit.TrainSet);

        var predictions = _model.Transform(trainTestSplit.TestSet);
        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: nameof(MlPirateFeature.Won));

        Console.WriteLine($"✅ Model trained - Accuracy: {metrics.Accuracy:P2}, AUC: {metrics.AreaUnderRocCurve:F3}");
    }

    private List<MlPirateFeature> ConvertToMLFormat(List<PirateFeatureRecord> features)
    {
        return features.Select(f => new MlPirateFeature
        {
            Position = (float)f.Position,
            CurrentOdds = (float)f.CurrentOdds,
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

public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
{
    if (_model == null)
        throw new InvalidOperationException("Model must be trained first");

    var mlData = features.Select(f => new MlPirateFeature
    {
        Position = (float)f.Position,
        CurrentOdds = (float)f.CurrentOdds,
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
        Won = false
    }).ToList();

    var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
    var predictions = _model.Transform(dataView);

    var predictionResults = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictions, false).ToList();

    return predictionResults.Zip(features, (pred, feat) => new PiratePrediction
    {
        RoundId = feat.RoundId,
        ArenaId = feat.ArenaId,
        PirateId = feat.PirateId,
        WinProbability = pred.Probability,
        Payout = feat.CurrentOdds
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
}

}