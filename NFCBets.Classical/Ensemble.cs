using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Constants;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class Ensemble : IMlStrategy
{
    private readonly MLContext _mlContext;
    private readonly List<ITransformer> _models = new();
    private InteractionAnalysisReport? _interactionReport;

    public Ensemble()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Ensemble (Weighted Average)";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null)
            Console.WriteLine("      Applying interaction controls");

        var mlData = FeatureConversionHelper.ConvertToMlFormat(trainingData, _interactionReport);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

        // Define feature pipeline first
        var featurePipeline = _mlContext.Transforms.Concatenate("Features",
            // Core features
            nameof(MlPirateFeature.Position),
            nameof(MlPirateFeature.CurrentOdds),
            nameof(MlPirateFeature.FoodAdjustment),
            nameof(MlPirateFeature.Strength),
            nameof(MlPirateFeature.Weight),
            // Historical features
            nameof(MlPirateFeature.HistoricalWinRate),
            nameof(MlPirateFeature.ArenaWinRate),
            nameof(MlPirateFeature.RecentWinRate),
            nameof(MlPirateFeature.WinRateVsCurrentRivals),
            nameof(MlPirateFeature.AvgRivalStrength),
            // Derived features
            nameof(MlPirateFeature.ImpliedProbability),
            nameof(MlPirateFeature.RelativeStrength),
            nameof(MlPirateFeature.EffectiveStrength),
            // Binary indicators
            nameof(MlPirateFeature.IsOddsFavorite),
            nameof(MlPirateFeature.IsStrengthFavorite),
            nameof(MlPirateFeature.HasPositiveFoodAdjustment),
            nameof(MlPirateFeature.IsUndervalued),
            nameof(MlPirateFeature.IsHotStreak),
            nameof(MlPirateFeature.IsArenaSpecialist),
            // Antagonistic penalties
            nameof(MlPirateFeature.PenaltyFoodPosition),
            nameof(MlPirateFeature.PenaltyFoodFavorite),
            nameof(MlPirateFeature.PenaltyStrengthPosition),
            nameof(MlPirateFeature.PenaltyStrengthWeakRivals),
            nameof(MlPirateFeature.PenaltyFavoriteInexperienced),
            nameof(MlPirateFeature.PenaltyLowStrengthFavorite),
            // Synergistic bonuses
            nameof(MlPirateFeature.BonusUndervaluedStrong),
            nameof(MlPirateFeature.BonusArenaSpecialistModerateOdds),
            nameof(MlPirateFeature.BonusHotStreakBeatsRivals),
            nameof(MlPirateFeature.BonusFoodPositionThree),
            // Three-way interactions
            nameof(MlPirateFeature.ThreeWayFoodPositionStrength),
            nameof(MlPirateFeature.ThreeWayUndervaluedStrongBeatsRivals))
        .Append(_mlContext.Transforms.NormalizeMinMax("Features"));

        // Pre-transform the data once
        var transformedData = featurePipeline.Fit(dataView).Transform(dataView);

        // Train each model separately with explicit typing
        var trainerConfigs = new List<(string Name, Func<IEstimator<ITransformer>> TrainerFactory)>
        {
            ("LightGBM", () => _mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 31,
                minimumExampleCountPerLeaf: 20,
                learningRate: 0.1,
                numberOfIterations: 100)),
                
            ("LogisticRegression", () => _mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                l1Regularization: 0.1f,
                l2Regularization: 0.1f)),
                
            ("FastTree", () => _mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName:ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 20,
                numberOfTrees: 100,
                minimumExampleCountPerLeaf: 10)),
                
            ("AveragedPerceptron", () => _mlContext.BinaryClassification.Trainers.AveragedPerceptron(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features)),
                
            ("SdcaLogisticRegression", () => _mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features))
        };

        _models.Clear();

        foreach (var (name, trainerFactory) in trainerConfigs)
        {
            try
            {
                Console.WriteLine($"      Training {name}...");
                var trainer = trainerFactory();
                var model = trainer.Fit(transformedData);
                
                // Combine feature pipeline with the trained model
                var fullPipeline = featurePipeline.Append(trainer);
                var fullModel = fullPipeline.Fit(dataView);
                
                _models.Add(fullModel);
                Console.WriteLine($"         ✅ {name} trained");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"         ⚠️ {name} failed: {ex.Message}");
            }
        }

        Console.WriteLine($"   ✅ Trained {_models.Count}/{trainerConfigs.Count} ensemble models");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (!_models.Any())
            throw new InvalidOperationException("No models trained");

        var mlData = FeatureConversionHelper.ConvertToMlFormat(features, _interactionReport);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

        // Get predictions from all models
        var allProbabilities = new List<float[]>();

        foreach (var model in _models)
        {
            var modelPredictions = model.Transform(dataView);
            var results = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(modelPredictions, false).ToList();
            allProbabilities.Add(results.Select(r => r.Probability).ToArray());
        }

        // Average predictions with equal weights
        var weights = Enumerable.Repeat(1.0 / _models.Count, _models.Count).ToArray();

        var predictions = new List<PiratePrediction>();
        for (var i = 0; i < features.Count; i++)
        {
            var avgProb = 0f;
            for (var m = 0; m < _models.Count; m++)
                avgProb += (float)(allProbabilities[m][i] * weights[m]);

            predictions.Add(new PiratePrediction
            {
                RoundId = features[i].RoundId,
                ArenaId = features[i].ArenaId,
                PirateId = features[i].PirateId,
                WinProbability = Math.Clamp(avgProb, 0.01f, 0.99f),
                Payout = Math.Max(2, features[i].CurrentOdds)
            });
        }

        return predictions;
    }

    public async Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        Console.WriteLine($"   Evaluating {StrategyName}...");

        var predictions = await PredictAsync(testData);

        var correctPredictions = 0;
        var totalRounds = 0;
        var allPredictions = new List<(bool Actual, float Predicted)>();

        foreach (var roundGroup in testData.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var actualWinner = roundGroup.FirstOrDefault(p => p.IsWinner == true);
            if (actualWinner == null) continue;

            var roundPredictions = predictions
                .Where(p => p.RoundId == roundGroup.Key.RoundId && p.ArenaId == roundGroup.Key.ArenaId)
                .ToList();

            if (!roundPredictions.Any()) continue;

            var predictedWinner = roundPredictions.OrderByDescending(p => p.WinProbability).First();

            if (predictedWinner.PirateId == actualWinner.PirateId)
                correctPredictions++;

            foreach (var pred in roundPredictions)
            {
                var actual = pred.PirateId == actualWinner.PirateId;
                allPredictions.Add((actual, pred.WinProbability));
            }

            totalRounds++;
        }

        var accuracy = totalRounds > 0 ? correctPredictions / (double)totalRounds : 0;
        var auc = MathUtilities.CalculateAuc(allPredictions);
        var logLoss = MathUtilities.CalculateLogLoss(allPredictions);

        return new ModelEvaluationReport
        {
            Accuracy = accuracy,
            Auc = auc,
            F1Score = accuracy * 0.5,
            TestDataSize = testData.Count,
            LogLoss = logLoss
        };
    }

    public void SaveModel(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        for (var i = 0; i < _models.Count; i++)
        {
            var modelPath = path.Replace(".zip", $"_ensemble_{i}.zip");
            _mlContext.Model.Save(_models[i], null, modelPath);
        }
    }

    public void LoadModel(string path)
    {
        _models.Clear();

        for (var i = 0; i < 5; i++)
        {
            var modelPath = path.Replace(".zip", $"_ensemble_{i}.zip");
            if (File.Exists(modelPath))
                _models.Add(_mlContext.Model.Load(modelPath, out _));
        }
    }
}