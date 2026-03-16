using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Constants;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class BinaryClassification : IMlStrategy
{
    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;
    private ITransformer? _model;

    public BinaryClassification()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Binary Classification (LightGBM)";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName} with {trainingData.Count} records...");

        if (_interactionReport != null)
            Console.WriteLine(
                $"      Applying {_interactionReport.AntagonisticInteractions.Count} antagonistic + {_interactionReport.SynergisticInteractions.Count} synergistic interaction controls");

        var mlData = FeatureConversionHelper.ConvertToMlFormat(trainingData, _interactionReport);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
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
        nameof(MlPirateFeature.PenaltyOddsShortenedLowStrength),
        nameof(MlPirateFeature.PenaltyArenaSpecialistColdStreak),
        // Synergistic bonuses
        nameof(MlPirateFeature.BonusUndervaluedStrong),
        nameof(MlPirateFeature.BonusArenaSpecialistModerateOdds),
        nameof(MlPirateFeature.BonusHotStreakBeatsRivals),
        nameof(MlPirateFeature.BonusFoodPositionThree),
        nameof(MlPirateFeature.BonusOddsShortenedStrong),
        nameof(MlPirateFeature.BonusFavoriteArenaSpecialist),
        nameof(MlPirateFeature.BonusStrengthPlusFood),
        nameof(MlPirateFeature.BonusHotStreakFavorite),
        // Three-way interactions
        nameof(MlPirateFeature.ThreeWayFoodPositionStrength),
        nameof(MlPirateFeature.ThreeWayUndervaluedStrongBeatsRivals),
        nameof(MlPirateFeature.ThreeWayFavoriteSpecialistHotStreak),
        nameof(MlPirateFeature.ThreeWayStrengthFoodPositionThree))
    .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
    .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
        labelColumnName: ColumnNames.Label,
        featureColumnName: ColumnNames.Features,
        numberOfLeaves: 31,
        minimumExampleCountPerLeaf: 20,
        learningRate: 0.1,
        numberOfIterations: 100));

_model = pipeline.Fit(dataView);

        Console.WriteLine($"   ✅ {StrategyName} trained");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (_model == null)
            throw new InvalidOperationException("Model must be trained first");

        var mlData = FeatureConversionHelper.ConvertToMlFormat(features, _interactionReport);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
        var predictions = _model.Transform(dataView);

        var predictionResults = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictions, false).ToList();

        return predictionResults.Zip(features, (pred, feat) => new PiratePrediction
        {
            RoundId = feat.RoundId,
            ArenaId = feat.ArenaId,
            PirateId = feat.PirateId,
            WinProbability = Math.Clamp(pred.Probability, 0.01f, 0.99f),
            Payout = Math.Max(2, feat.CurrentOdds)
        }).ToList();
    }

    public async Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        if (_model == null)
            throw new InvalidOperationException("Model must be trained first");

        var mlTestData = FeatureConversionHelper.ConvertToMlFormat(testData, _interactionReport);
        var testDataView = _mlContext.Data.LoadFromEnumerable(mlTestData);
        var predictions = _model.Transform(testDataView);

        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: ColumnNames.Label);

        return new ModelEvaluationReport
        {
            Accuracy = metrics.Accuracy,
            Auc = metrics.AreaUnderRocCurve,
            F1Score = metrics.F1Score,
            Precision = metrics.PositivePrecision,
            Recall = metrics.PositiveRecall,
            LogLoss = metrics.LogLoss,
            TestDataSize = testData.Count
        };
    }

    public void SaveModel(string path)
    {
        if (_model == null)
            throw new InvalidOperationException("No model to save");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _mlContext.Model.Save(_model, null, path);
    }

    public void LoadModel(string path)
    {
        _model = _mlContext.Model.Load(path, out _);
    }
}