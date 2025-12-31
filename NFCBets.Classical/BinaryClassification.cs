using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
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

        var mlData = ConvertToMlFormat(trainingData);
        var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
                // Base features
                nameof(MlPirateFeature.Position),
                nameof(MlPirateFeature.CurrentOdds),
                nameof(MlPirateFeature.FoodAdjustment),
                nameof(MlPirateFeature.Strength),
                nameof(MlPirateFeature.Weight),
                nameof(MlPirateFeature.HistoricalWinRate),
                nameof(MlPirateFeature.ArenaWinRate),
                nameof(MlPirateFeature.RecentWinRate),
                nameof(MlPirateFeature.WinRateVsCurrentRivals),
                nameof(MlPirateFeature.AvgRivalStrength),
                // Antagonistic penalties
                nameof(MlPirateFeature.Penalty_FoodPosition),
                nameof(MlPirateFeature.Penalty_FoodFavorite),
                nameof(MlPirateFeature.Penalty_StrengthPosition),
                nameof(MlPirateFeature.Penalty_StrengthWeakRivals),
                nameof(MlPirateFeature.Penalty_FavoriteInexperienced),
                nameof(MlPirateFeature.Penalty_LowStrengthFavorite),
                // Synergistic bonuses
                nameof(MlPirateFeature.Bonus_UndervaluedStrong),
                nameof(MlPirateFeature.Bonus_ArenaSpecialistModerateOdds),
                nameof(MlPirateFeature.Bonus_HotStreakBeatsRivals),
                nameof(MlPirateFeature.Bonus_FoodPosition3),
                // Three-way interactions
                nameof(MlPirateFeature.ThreeWay_FoodPositionStrength),
                nameof(MlPirateFeature.ThreeWay_UndervaluedStrongBeatsRivals))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                nameof(MlPirateFeature.Won),
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

        var mlData = ConvertToMlFormat(features);
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

        var mlTestData = ConvertToMlFormat(testData);
        var testDataView = _mlContext.Data.LoadFromEnumerable(mlTestData);
        var predictions = _model.Transform(testDataView);

        var metrics = _mlContext.BinaryClassification.Evaluate(predictions, nameof(MlPirateFeature.Won));

        return new ModelEvaluationReport
        {
            Accuracy = metrics.Accuracy,
            AUC = metrics.AreaUnderRocCurve,
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

    private List<MlPirateFeature> ConvertToMlFormat(List<PirateFeatureRecord> features)
    {
        return features.Select(f =>
        {
            var mlFeature = new MlPirateFeature
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
            };

            // Apply interaction features
            InteractionCalculator.ApplyInteractionFeatures(mlFeature, f, _interactionReport);

            return mlFeature;
        }).ToList();
    }
}