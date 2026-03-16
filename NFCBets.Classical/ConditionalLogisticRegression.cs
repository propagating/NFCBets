using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class ConditionalLogisticRegression : IMlStrategy
{
    private readonly Dictionary<int, ITransformer> _arenaModels = new();
    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;

    public ConditionalLogisticRegression()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Conditional Logistic (Per-Arena)";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null)
            Console.WriteLine("      Applying interaction controls");

        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();
            if (!arenaData.Any()) continue;

            Console.WriteLine($"      Training Arena {arenaId} ({arenaData.Count} records)...");

            var mlData = FeatureConversionHelper.ConvertToMlFormat(arenaData, _interactionReport);
            var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    // Core features
                    nameof(MlPirateFeature.Position),
                    nameof(MlPirateFeature.CurrentOdds),
                    nameof(MlPirateFeature.FoodAdjustment),
                    nameof(MlPirateFeature.Strength),
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
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(l1Regularization: 0.05f,
                    l2Regularization: 0.1f));

            _arenaModels[arenaId] = pipeline.Fit(dataView);
            Console.WriteLine($"         ✅ Arena {arenaId} model trained");
        }

        Console.WriteLine($"   ✅ Trained {_arenaModels.Count}/5 arena models");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        foreach (var arenaGroup in features.GroupBy(f => f.ArenaId))
        {
            var arenaId = arenaGroup.Key;
            if (!_arenaModels.TryGetValue(arenaId, out var model)) continue;

            var arenaFeatures = arenaGroup.ToList();
            var mlData = FeatureConversionHelper.ConvertToMlFormat(arenaFeatures, _interactionReport);
            var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
            var predictionResults = model.Transform(dataView);

            var results = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictionResults, false).ToList();

            predictions.AddRange(results.Zip(arenaFeatures, (pred, feat) => new PiratePrediction
            {
                RoundId = feat.RoundId,
                ArenaId = feat.ArenaId,
                PirateId = feat.PirateId,
                WinProbability = Math.Clamp(pred.Probability, 0.01f, 0.99f),
                Payout = Math.Max(2, feat.CurrentOdds)
            }));
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

        foreach (var arenaGroup in testData.GroupBy(f => f.ArenaId))
        foreach (var roundGroup in arenaGroup.GroupBy(f => f.RoundId))
        {
            var actualWinner = roundGroup.FirstOrDefault(p => p.IsWinner == true);
            if (actualWinner == null) continue;

            var roundPredictions = predictions
                .Where(p => p.RoundId == roundGroup.Key && p.ArenaId == arenaGroup.Key)
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

        for (var arenaId = 1; arenaId <= 5; arenaId++)
            if (_arenaModels.TryGetValue(arenaId, out var model))
            {
                var arenaPath = path.Replace(".zip", $"_conditional_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
    }

    public void LoadModel(string path)
    {
        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_conditional_arena{arenaId}.zip");
            if (File.Exists(arenaPath))
                _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
        }
    }
}