using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class MultiClassPairwise : IMlStrategy
{
    private readonly Dictionary<int, ITransformer> _arenaModels = new();
    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;

    public MultiClassPairwise()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Multi-Class Pairwise";

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

            if (!arenaData.Any())
            {
                Console.WriteLine($"      ⚠️ No data for Arena {arenaId}");
                continue;
            }

            Console.WriteLine($"      Training Arena {arenaId}...");

            var pairwiseRounds = ConvertToPairwiseRoundFormat(arenaData);

            if (!pairwiseRounds.Any())
            {
                Console.WriteLine($"         ⚠️ No complete rounds for Arena {arenaId}");
                continue;
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(pairwiseRounds);

            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label", "WinnerPosition")
                .Append(_mlContext.Transforms.Concatenate("Features",
                    // Individual pirate features
                    nameof(PairwiseRoundFeature.Pirate0_Strength),
                    nameof(PairwiseRoundFeature.Pirate0_Odds),
                    nameof(PairwiseRoundFeature.Pirate0_Food),
                    nameof(PairwiseRoundFeature.Pirate0_HistWin),
                    nameof(PairwiseRoundFeature.Pirate0_ArenaWin),
                    nameof(PairwiseRoundFeature.Pirate0_RecentWin),
                    nameof(PairwiseRoundFeature.Pirate0_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pirate0_OddsRank),
                    nameof(PairwiseRoundFeature.Pirate0_InteractionPenalty),
                    nameof(PairwiseRoundFeature.Pirate0_InteractionBonus),
                    
                    nameof(PairwiseRoundFeature.Pirate1_Strength),
                    nameof(PairwiseRoundFeature.Pirate1_Odds),
                    nameof(PairwiseRoundFeature.Pirate1_Food),
                    nameof(PairwiseRoundFeature.Pirate1_HistWin),
                    nameof(PairwiseRoundFeature.Pirate1_ArenaWin),
                    nameof(PairwiseRoundFeature.Pirate1_RecentWin),
                    nameof(PairwiseRoundFeature.Pirate1_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pirate1_OddsRank),
                    nameof(PairwiseRoundFeature.Pirate1_InteractionPenalty),
                    nameof(PairwiseRoundFeature.Pirate1_InteractionBonus),
                    
                    nameof(PairwiseRoundFeature.Pirate2_Strength),
                    nameof(PairwiseRoundFeature.Pirate2_Odds),
                    nameof(PairwiseRoundFeature.Pirate2_Food),
                    nameof(PairwiseRoundFeature.Pirate2_HistWin),
                    nameof(PairwiseRoundFeature.Pirate2_ArenaWin),
                    nameof(PairwiseRoundFeature.Pirate2_RecentWin),
                    nameof(PairwiseRoundFeature.Pirate2_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pirate2_OddsRank),
                    nameof(PairwiseRoundFeature.Pirate2_InteractionPenalty),
                    nameof(PairwiseRoundFeature.Pirate2_InteractionBonus),
                    
                    nameof(PairwiseRoundFeature.Pirate3_Strength),
                    nameof(PairwiseRoundFeature.Pirate3_Odds),
                    nameof(PairwiseRoundFeature.Pirate3_Food),
                    nameof(PairwiseRoundFeature.Pirate3_HistWin),
                    nameof(PairwiseRoundFeature.Pirate3_ArenaWin),
                    nameof(PairwiseRoundFeature.Pirate3_RecentWin),
                    nameof(PairwiseRoundFeature.Pirate3_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pirate3_OddsRank),
                    nameof(PairwiseRoundFeature.Pirate3_InteractionPenalty),
                    nameof(PairwiseRoundFeature.Pirate3_InteractionBonus),
                    
                    // Pairwise comparison features
                    nameof(PairwiseRoundFeature.Pair01_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pair01_OddsDiff),
                    nameof(PairwiseRoundFeature.Pair01_FoodDiff),
                    nameof(PairwiseRoundFeature.Pair01_HistWinDiff),
                    
                    nameof(PairwiseRoundFeature.Pair02_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pair02_OddsDiff),
                    nameof(PairwiseRoundFeature.Pair02_FoodDiff),
                    nameof(PairwiseRoundFeature.Pair02_HistWinDiff),
                    
                    nameof(PairwiseRoundFeature.Pair03_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pair03_OddsDiff),
                    nameof(PairwiseRoundFeature.Pair03_FoodDiff),
                    nameof(PairwiseRoundFeature.Pair03_HistWinDiff),
                    
                    nameof(PairwiseRoundFeature.Pair12_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pair12_OddsDiff),
                    nameof(PairwiseRoundFeature.Pair12_FoodDiff),
                    nameof(PairwiseRoundFeature.Pair12_HistWinDiff),
                    
                    nameof(PairwiseRoundFeature.Pair13_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pair13_OddsDiff),
                    nameof(PairwiseRoundFeature.Pair13_FoodDiff),
                    nameof(PairwiseRoundFeature.Pair13_HistWinDiff),
                    
                    nameof(PairwiseRoundFeature.Pair23_StrengthDiff),
                    nameof(PairwiseRoundFeature.Pair23_OddsDiff),
                    nameof(PairwiseRoundFeature.Pair23_FoodDiff),
                    nameof(PairwiseRoundFeature.Pair23_HistWinDiff)))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.MulticlassClassification.Trainers.LightGbm(
                    numberOfLeaves: 31,
                    numberOfIterations: 100,
                    learningRate: 0.1))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            _arenaModels[arenaId] = pipeline.Fit(dataView);
            Console.WriteLine($"         ✅ Arena {arenaId} trained ({pairwiseRounds.Count} rounds)");
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

                var pairwiseRound = ConvertToPairwiseRoundFormat(pirates).FirstOrDefault();
                if (pairwiseRound == null) continue;

                var dataView = _mlContext.Data.LoadFromEnumerable(new[] { pairwiseRound });
                var prediction = model.Transform(dataView);
                var result = _mlContext.Data.CreateEnumerable<MultiClassPrediction>(prediction, false).First();

                var probs = Softmax(result.Score);

                for (var i = 0; i < 4 && i < probs.Length; i++)
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
                var arenaPath = path.Replace(".zip", $"_pairwise_multiclass_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
    }

    public void LoadModel(string path)
    {
        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_pairwise_multiclass_arena{arenaId}.zip");
            if (File.Exists(arenaPath))
                _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
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

    private List<PairwiseRoundFeature> ConvertToPairwiseRoundFormat(List<PirateFeatureRecord> arenaData)
    {
        var result = new List<PairwiseRoundFeature>();

        // Pre-compute grouped data for feature conversion
        var groupedByRoundArena = arenaData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

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

            // Calculate interaction features using new property names
            var penalties = new float[4];
            var bonuses = new float[4];

            for (var i = 0; i < 4; i++)
            {
                var mlFeature = FeatureConversionHelper.ConvertSingle(
                    pirates[i], groupedByRoundArena, _interactionReport);
                penalties[i] = InteractionCalculator.GetTotalPenalty(mlFeature);
                bonuses[i] = InteractionCalculator.GetTotalBonus(mlFeature);
            }

            result.Add(new PairwiseRoundFeature
            {
                // Pirate 0
                Pirate0_Strength = pirates[0].Strength,
                Pirate0_Odds = (float)Math.Log(Math.Max(2, pirates[0].CurrentOdds)),
                Pirate0_Food = pirates[0].FoodAdjustment,
                Pirate0_HistWin = (float)pirates[0].HistoricalWinRate,
                Pirate0_ArenaWin = (float)pirates[0].ArenaWinRate,
                Pirate0_RecentWin = (float)pirates[0].RecentWinRate,
                Pirate0_StrengthDiff = pirates[0].Strength - avgStrength,
                Pirate0_OddsRank = oddsRanks[0],
                Pirate0_InteractionPenalty = penalties[0],
                Pirate0_InteractionBonus = bonuses[0],

                // Pirate 1
                Pirate1_Strength = pirates[1].Strength,
                Pirate1_Odds = (float)Math.Log(Math.Max(2, pirates[1].CurrentOdds)),
                Pirate1_Food = pirates[1].FoodAdjustment,
                Pirate1_HistWin = (float)pirates[1].HistoricalWinRate,
                Pirate1_ArenaWin = (float)pirates[1].ArenaWinRate,
                Pirate1_RecentWin = (float)pirates[1].RecentWinRate,
                Pirate1_StrengthDiff = pirates[1].Strength - avgStrength,
                Pirate1_OddsRank = oddsRanks[1],
                Pirate1_InteractionPenalty = penalties[1],
                Pirate1_InteractionBonus = bonuses[1],

                // Pirate 2
                Pirate2_Strength = pirates[2].Strength,
                Pirate2_Odds = (float)Math.Log(Math.Max(2, pirates[2].CurrentOdds)),
                Pirate2_Food = pirates[2].FoodAdjustment,
                Pirate2_HistWin = (float)pirates[2].HistoricalWinRate,
                Pirate2_ArenaWin = (float)pirates[2].ArenaWinRate,
                Pirate2_RecentWin = (float)pirates[2].RecentWinRate,
                Pirate2_StrengthDiff = pirates[2].Strength - avgStrength,
                Pirate2_OddsRank = oddsRanks[2],
                Pirate2_InteractionPenalty = penalties[2],
                Pirate2_InteractionBonus = bonuses[2],

                // Pirate 3
                Pirate3_Strength = pirates[3].Strength,
                Pirate3_Odds = (float)Math.Log(Math.Max(2, pirates[3].CurrentOdds)),
                Pirate3_Food = pirates[3].FoodAdjustment,
                Pirate3_HistWin = (float)pirates[3].HistoricalWinRate,
                Pirate3_ArenaWin = (float)pirates[3].ArenaWinRate,
                Pirate3_RecentWin = (float)pirates[3].RecentWinRate,
                Pirate3_StrengthDiff = pirates[3].Strength - avgStrength,
                Pirate3_OddsRank = oddsRanks[3],
                Pirate3_InteractionPenalty = penalties[3],
                Pirate3_InteractionBonus = bonuses[3],

                // Pairwise comparisons
                Pair01_StrengthDiff = pirates[0].Strength - pirates[1].Strength,
                Pair01_OddsDiff = pirates[0].CurrentOdds - pirates[1].CurrentOdds,
                Pair01_FoodDiff = pirates[0].FoodAdjustment - pirates[1].FoodAdjustment,
                Pair01_HistWinDiff = (float)(pirates[0].HistoricalWinRate - pirates[1].HistoricalWinRate),

                Pair02_StrengthDiff = pirates[0].Strength - pirates[2].Strength,
                Pair02_OddsDiff = pirates[0].CurrentOdds - pirates[2].CurrentOdds,
                Pair02_FoodDiff = pirates[0].FoodAdjustment - pirates[2].FoodAdjustment,
                Pair02_HistWinDiff = (float)(pirates[0].HistoricalWinRate - pirates[2].HistoricalWinRate),

                Pair03_StrengthDiff = pirates[0].Strength - pirates[3].Strength,
                Pair03_OddsDiff = pirates[0].CurrentOdds - pirates[3].CurrentOdds,
                Pair03_FoodDiff = pirates[0].FoodAdjustment - pirates[3].FoodAdjustment,
                Pair03_HistWinDiff = (float)(pirates[0].HistoricalWinRate - pirates[3].HistoricalWinRate),

                Pair12_StrengthDiff = pirates[1].Strength - pirates[2].Strength,
                Pair12_OddsDiff = pirates[1].CurrentOdds - pirates[2].CurrentOdds,
                Pair12_FoodDiff = pirates[1].FoodAdjustment - pirates[2].FoodAdjustment,
                Pair12_HistWinDiff = (float)(pirates[1].HistoricalWinRate - pirates[2].HistoricalWinRate),

                Pair13_StrengthDiff = pirates[1].Strength - pirates[3].Strength,
                Pair13_OddsDiff = pirates[1].CurrentOdds - pirates[3].CurrentOdds,
                Pair13_FoodDiff = pirates[1].FoodAdjustment - pirates[3].FoodAdjustment,
                Pair13_HistWinDiff = (float)(pirates[1].HistoricalWinRate - pirates[3].HistoricalWinRate),

                Pair23_StrengthDiff = pirates[2].Strength - pirates[3].Strength,
                Pair23_OddsDiff = pirates[2].CurrentOdds - pirates[3].CurrentOdds,
                Pair23_FoodDiff = pirates[2].FoodAdjustment - pirates[3].FoodAdjustment,
                Pair23_HistWinDiff = (float)(pirates[2].HistoricalWinRate - pirates[3].HistoricalWinRate),

                WinnerPosition = winnerIndex
            });
        }

        return result;
    }
}