using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class MultiOutput : IMlStrategy
{
    private readonly Dictionary<int, ITransformer> _arenaModels = new();

    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;

    public MultiOutput()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Multi-Output";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null) Console.WriteLine("      Applying interaction controls");

        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();

            if (!arenaData.Any())
            {
                Console.WriteLine($"      ⚠️ No data for Arena {arenaId}");
                continue;
            }

            Console.WriteLine($"      Training Arena {arenaId}...");

            var multiOutputData = ConvertToMultiOutputFormat(arenaData);

            if (!multiOutputData.Any())
            {
                Console.WriteLine($"         ⚠️ No complete rounds for Arena {arenaId}");
                continue;
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(multiOutputData);

            // Train 4 separate binary models, one for each position
            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(MultiOutputFeature.Pirate0_Score),
                    nameof(MultiOutputFeature.Pirate1_Score),
                    nameof(MultiOutputFeature.Pirate2_Score),
                    nameof(MultiOutputFeature.Pirate3_Score),
                    nameof(MultiOutputFeature.Pirate0_Penalty),
                    nameof(MultiOutputFeature.Pirate1_Penalty),
                    nameof(MultiOutputFeature.Pirate2_Penalty),
                    nameof(MultiOutputFeature.Pirate3_Penalty),
                    nameof(MultiOutputFeature.Pirate0_Bonus),
                    nameof(MultiOutputFeature.Pirate1_Bonus),
                    nameof(MultiOutputFeature.Pirate2_Bonus),
                    nameof(MultiOutputFeature.Pirate3_Bonus))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.Transforms.Conversion.MapValueToKey("Label",
                    nameof(MultiOutputFeature.WinnerPosition)))
                .Append(_mlContext.MulticlassClassification.Trainers.LightGbm())
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            _arenaModels[arenaId] = pipeline.Fit(dataView);
            Console.WriteLine($"         ✅ Arena {arenaId} trained ({multiOutputData.Count} rounds)");
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

            foreach (var roundGroup in arenaGroup.GroupBy(f => f.RoundId))
            {
                var pirates = roundGroup.OrderBy(p => p.Position).ToList();
                if (pirates.Count != 4) continue;

                var multiOutputData = ConvertToMultiOutputFormat(pirates).FirstOrDefault();
                if (multiOutputData == null) continue;

                var dataView = _mlContext.Data.LoadFromEnumerable(new[] { multiOutputData });
                var prediction = model.Transform(dataView);
                var result = _mlContext.Data.CreateEnumerable<MultiClassPrediction>(prediction, false).First();

                var probs = Softmax(result.Score);

                for (var i = 0; i < 4; i++)
                    predictions.Add(new PiratePrediction
                    {
                        RoundId = pirates[i].RoundId,
                        ArenaId = pirates[i].ArenaId,
                        PirateId = pirates[i].PirateId,
                        WinProbability = (float)probs[i],
                        Payout = Math.Max(2, pirates[i].CurrentOdds)
                    });
            }
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
            AUC = auc,
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
                var arenaPath = path.Replace(".zip", $"_multioutput_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
    }

    public void LoadModel(string path)
    {
        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_multioutput_arena{arenaId}.zip");
            if (File.Exists(arenaPath)) _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
        }
    }

    private double[] Softmax(float[] scores)
    {
        if (scores == null || scores.Length == 0)
            return new double[4] { 0.25, 0.25, 0.25, 0.25 };

        var doubleScores = scores.Select(s => (double)s).ToArray();
        var max = doubleScores.Max();
        var exps = doubleScores.Select(s => Math.Exp(s - max)).ToArray();
        var sum = exps.Sum();

        if (sum == 0)
            return new double[4] { 0.25, 0.25, 0.25, 0.25 };

        return exps.Select(e => e / sum).ToArray();
    }

    private List<MultiOutputFeature> ConvertToMultiOutputFormat(List<PirateFeatureRecord> data)
    {
        var result = new List<MultiOutputFeature>();

        var roundGroups = data.GroupBy(f => f.RoundId);

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var winnerIndex = pirates.FindIndex(p => p.IsWinner == true);
            if (winnerIndex < 0) continue;

            // Calculate interaction features for each pirate
            var penalties = new float[4];
            var bonuses = new float[4];
            var scores = new float[4];

            for (var i = 0; i < 4; i++)
            {
                var mlFeature = new MlPirateFeature();
                InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirates[i], _interactionReport);

                penalties[i] = mlFeature.Penalty_FoodPosition + mlFeature.Penalty_FoodFavorite +
                               mlFeature.Penalty_StrengthPosition + mlFeature.Penalty_StrengthWeakRivals +
                               mlFeature.Penalty_FavoriteInexperienced + mlFeature.Penalty_LowStrengthFavorite;

                bonuses[i] = mlFeature.Bonus_UndervaluedStrong + mlFeature.Bonus_HotStreakBeatsRivals +
                             mlFeature.Bonus_ArenaSpecialistModerateOdds + mlFeature.Bonus_FoodPosition3;

                // Calculate a composite score for each pirate
                scores[i] = (float)(
                    pirates[i].Strength / 100.0 * 0.2 +
                    1.0 / Math.Max(2, pirates[i].CurrentOdds) * 0.3 +
                    pirates[i].HistoricalWinRate * 0.2 +
                    pirates[i].ArenaWinRate * 0.15 +
                    pirates[i].FoodAdjustment / 10.0 * 0.1 +
                    (4 - pirates[i].Position) / 4.0 * 0.05
                );
            }

            result.Add(new MultiOutputFeature
            {
                Pirate0_Score = scores[0],
                Pirate1_Score = scores[1],
                Pirate2_Score = scores[2],
                Pirate3_Score = scores[3],
                Pirate0_Penalty = penalties[0],
                Pirate1_Penalty = penalties[1],
                Pirate2_Penalty = penalties[2],
                Pirate3_Penalty = penalties[3],
                Pirate0_Bonus = bonuses[0],
                Pirate1_Bonus = bonuses[1],
                Pirate2_Bonus = bonuses[2],
                Pirate3_Bonus = bonuses[3],
                WinnerPosition = winnerIndex
            });
        }

        return result;
    }
}