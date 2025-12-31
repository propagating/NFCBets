using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class SoftmaxPerArena : IMlStrategy
{
    private readonly Dictionary<int, ITransformer> _arenaModels = new();

    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;

    public SoftmaxPerArena()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Softmax Per Arena";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null) Console.WriteLine("      Applying interaction controls");

        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();
            if (!arenaData.Any()) continue;

            Console.WriteLine($"      Training Arena {arenaId}...");

            var arenaRounds = ConvertToArenaRoundFormat(arenaData);

            if (!arenaRounds.Any())
            {
                Console.WriteLine($"         ⚠️ No complete rounds for Arena {arenaId}");
                continue;
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(arenaRounds);

            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label", "Label")
                .Append(_mlContext.Transforms.Concatenate("Features",
                    // Pirate 0 features
                    nameof(ArenaRoundFeatureImproved.Pirate0_Strength),
                    nameof(ArenaRoundFeatureImproved.Pirate0_Odds),
                    nameof(ArenaRoundFeatureImproved.Pirate0_Food),
                    nameof(ArenaRoundFeatureImproved.Pirate0_HistWin),
                    nameof(ArenaRoundFeatureImproved.Pirate0_StrengthDiff),
                    nameof(ArenaRoundFeatureImproved.Pirate0_OddsRank),
                    nameof(ArenaRoundFeatureImproved.Pirate0_FoodRank),
                    nameof(ArenaRoundFeatureImproved.Pirate0_FoodPositionInteraction),
                    nameof(ArenaRoundFeatureImproved.Pirate0_InteractionPenalty),
                    nameof(ArenaRoundFeatureImproved.Pirate0_InteractionBonus),
                    // Pirate 1 features
                    nameof(ArenaRoundFeatureImproved.Pirate1_Strength),
                    nameof(ArenaRoundFeatureImproved.Pirate1_Odds),
                    nameof(ArenaRoundFeatureImproved.Pirate1_Food),
                    nameof(ArenaRoundFeatureImproved.Pirate1_HistWin),
                    nameof(ArenaRoundFeatureImproved.Pirate1_StrengthDiff),
                    nameof(ArenaRoundFeatureImproved.Pirate1_OddsRank),
                    nameof(ArenaRoundFeatureImproved.Pirate1_FoodRank),
                    nameof(ArenaRoundFeatureImproved.Pirate1_FoodPositionInteraction),
                    nameof(ArenaRoundFeatureImproved.Pirate1_InteractionPenalty),
                    nameof(ArenaRoundFeatureImproved.Pirate1_InteractionBonus),
                    // Pirate 2 features
                    nameof(ArenaRoundFeatureImproved.Pirate2_Strength),
                    nameof(ArenaRoundFeatureImproved.Pirate2_Odds),
                    nameof(ArenaRoundFeatureImproved.Pirate2_Food),
                    nameof(ArenaRoundFeatureImproved.Pirate2_HistWin),
                    nameof(ArenaRoundFeatureImproved.Pirate2_StrengthDiff),
                    nameof(ArenaRoundFeatureImproved.Pirate2_OddsRank),
                    nameof(ArenaRoundFeatureImproved.Pirate2_FoodRank),
                    nameof(ArenaRoundFeatureImproved.Pirate2_FoodPositionInteraction),
                    nameof(ArenaRoundFeatureImproved.Pirate2_InteractionPenalty),
                    nameof(ArenaRoundFeatureImproved.Pirate2_InteractionBonus),
                    // Pirate 3 features
                    nameof(ArenaRoundFeatureImproved.Pirate3_Strength),
                    nameof(ArenaRoundFeatureImproved.Pirate3_Odds),
                    nameof(ArenaRoundFeatureImproved.Pirate3_Food),
                    nameof(ArenaRoundFeatureImproved.Pirate3_HistWin),
                    nameof(ArenaRoundFeatureImproved.Pirate3_StrengthDiff),
                    nameof(ArenaRoundFeatureImproved.Pirate3_OddsRank),
                    nameof(ArenaRoundFeatureImproved.Pirate3_FoodRank),
                    nameof(ArenaRoundFeatureImproved.Pirate3_FoodPositionInteraction),
                    nameof(ArenaRoundFeatureImproved.Pirate3_InteractionPenalty),
                    nameof(ArenaRoundFeatureImproved.Pirate3_InteractionBonus)))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy())
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            _arenaModels[arenaId] = pipeline.Fit(dataView);
            Console.WriteLine($"         ✅ Arena {arenaId} trained ({arenaRounds.Count} rounds)");
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

                var arenaRound = ConvertToArenaRoundFormat(pirates).FirstOrDefault();
                if (arenaRound == null) continue;

                var dataView = _mlContext.Data.LoadFromEnumerable(new[] { arenaRound });
                var prediction = model.Transform(dataView);
                var result = _mlContext.Data.CreateEnumerable<MultiClassPrediction>(prediction, false).First();

                var probabilities = Softmax(result.Score);

                for (var i = 0; i < 4; i++)
                    predictions.Add(new PiratePrediction
                    {
                        RoundId = pirates[i].RoundId,
                        ArenaId = pirates[i].ArenaId,
                        PirateId = pirates[i].PirateId,
                        WinProbability = (float)probabilities[i],
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
                var arenaPath = path.Replace(".zip", $"_softmax_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
    }

    public void LoadModel(string path)
    {
        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_softmax_arena{arenaId}.zip");
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

    private List<ArenaRoundFeatureImproved> ConvertToArenaRoundFormat(List<PirateFeatureRecord> arenaData)
    {
        var result = new List<ArenaRoundFeatureImproved>();

        var roundGroups = arenaData.GroupBy(f => f.RoundId);

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var winnerIndex = pirates.FindIndex(p => p.IsWinner == true);
            if (winnerIndex < 0) continue;

            var avgStrength = pirates.Average(p => p.Strength);

            var oddsRanks = pirates
                .Select((p, idx) => new { Index = idx, Odds = p.CurrentOdds })
                .OrderBy(x => x.Odds)
                .Select((x, rank) => new { x.Index, Rank = rank + 1 })
                .OrderBy(x => x.Index)
                .Select(x => (float)x.Rank)
                .ToArray();

            var foodRanks = pirates
                .Select((p, idx) => new { Index = idx, Food = p.FoodAdjustment })
                .OrderByDescending(x => x.Food)
                .Select((x, rank) => new { x.Index, Rank = rank + 1 })
                .OrderBy(x => x.Index)
                .Select(x => (float)x.Rank)
                .ToArray();

            var penalties = new float[4];
            var bonuses = new float[4];

            for (var i = 0; i < 4; i++)
            {
                var mlFeature = new MlPirateFeature();
                InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirates[i], _interactionReport);
                penalties[i] = mlFeature.Penalty_FoodPosition + mlFeature.Penalty_FoodFavorite +
                               mlFeature.Penalty_StrengthPosition + mlFeature.Penalty_StrengthWeakRivals;
                bonuses[i] = mlFeature.Bonus_UndervaluedStrong + mlFeature.Bonus_HotStreakBeatsRivals +
                             mlFeature.Bonus_ArenaSpecialistModerateOdds + mlFeature.Bonus_FoodPosition3;
            }

            result.Add(new ArenaRoundFeatureImproved
            {
                Pirate0_Strength = pirates[0].Strength,
                Pirate0_Odds = Math.Max(2, pirates[0].CurrentOdds),
                Pirate0_Food = pirates[0].FoodAdjustment,
                Pirate0_HistWin = (float)pirates[0].HistoricalWinRate,
                Pirate0_StrengthDiff = pirates[0].Strength - (float)avgStrength,
                Pirate0_OddsRank = oddsRanks[0],
                Pirate0_FoodRank = foodRanks[0],
                Pirate0_FoodPositionInteraction = pirates[0].FoodAdjustment * pirates[0].Position,
                Pirate0_InteractionPenalty = penalties[0],
                Pirate0_InteractionBonus = bonuses[0],

                Pirate1_Strength = pirates[1].Strength,
                Pirate1_Odds = Math.Max(2, pirates[1].CurrentOdds),
                Pirate1_Food = pirates[1].FoodAdjustment,
                Pirate1_HistWin = (float)pirates[1].HistoricalWinRate,
                Pirate1_StrengthDiff = pirates[1].Strength - (float)avgStrength,
                Pirate1_OddsRank = oddsRanks[1],
                Pirate1_FoodRank = foodRanks[1],
                Pirate1_FoodPositionInteraction = pirates[1].FoodAdjustment * pirates[1].Position,
                Pirate1_InteractionPenalty = penalties[1],
                Pirate1_InteractionBonus = bonuses[1],

                Pirate2_Strength = pirates[2].Strength,
                Pirate2_Odds = Math.Max(2, pirates[2].CurrentOdds),
                Pirate2_Food = pirates[2].FoodAdjustment,
                Pirate2_HistWin = (float)pirates[2].HistoricalWinRate,
                Pirate2_StrengthDiff = pirates[2].Strength - (float)avgStrength,
                Pirate2_OddsRank = oddsRanks[2],
                Pirate2_FoodRank = foodRanks[2],
                Pirate2_FoodPositionInteraction = pirates[2].FoodAdjustment * pirates[2].Position,
                Pirate2_InteractionPenalty = penalties[2],
                Pirate2_InteractionBonus = bonuses[2],

                Pirate3_Strength = pirates[3].Strength,
                Pirate3_Odds = Math.Max(2, pirates[3].CurrentOdds),
                Pirate3_Food = pirates[3].FoodAdjustment,
                Pirate3_HistWin = (float)pirates[3].HistoricalWinRate,
                Pirate3_StrengthDiff = pirates[3].Strength - (float)avgStrength,
                Pirate3_OddsRank = oddsRanks[3],
                Pirate3_FoodRank = foodRanks[3],
                Pirate3_FoodPositionInteraction = pirates[3].FoodAdjustment * pirates[3].Position,
                Pirate3_InteractionPenalty = penalties[3],
                Pirate3_InteractionBonus = bonuses[3],

                WinnerPosition = winnerIndex
            });
        }

        return result;
    }
}