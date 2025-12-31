using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Multi-class classification enhanced with pairwise comparison features
/// Key insight: Include head-to-head comparisons between all pirates in the round
/// </summary>
public class MultiClassPairwise : IMlStrategy
{
    public string StrategyName => "Multi-Class with Pairwise Features";
    
    private readonly MLContext _mlContext;
    private readonly Dictionary<int, ITransformer> _arenaModels = new();
    private InteractionAnalysisReport? _interactionReport;

    public MultiClassPairwise()
    {
        _mlContext = new MLContext(42);
    }

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;
        
        Console.WriteLine($"   Training {StrategyName}...");
        
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();
            if (!arenaData.Any()) continue;

            Console.WriteLine($"      Training Arena {arenaId}...");

            var pairwiseRounds = ConvertToPairwiseRoundFormat(arenaData);
            
            if (!pairwiseRounds.Any())
            {
                Console.WriteLine($"         ⚠️ No complete rounds for Arena {arenaId}");
                continue;
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(pairwiseRounds);

            // Build feature list dynamically
            var featureColumns = GetFeatureColumnNames();

            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(PairwiseRoundFeature.WinnerPosition))
                .Append(_mlContext.Transforms.Concatenate("Features", featureColumns))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.MulticlassClassification.Trainers.LightGbm(
                    labelColumnName: "Label",
                    featureColumnName: "Features",
                    numberOfLeaves: 31,
                    minimumExampleCountPerLeaf: 10,
                    learningRate: 0.05,
                    numberOfIterations: 150))
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

                for (int i = 0; i < 4; i++)
                {
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
            AUC = auc,
            F1Score = accuracy * 0.5,
            TestDataSize = testData.Count,
            LogLoss = logLoss
        };
    }

    private string[] GetFeatureColumnNames()
    {
        var columns = new List<string>();
        
        // Individual pirate features (4 pirates)
        for (int i = 0; i < 4; i++)
        {
            columns.Add($"Pirate{i}_Strength");
            columns.Add($"Pirate{i}_Odds");
            columns.Add($"Pirate{i}_Food");
            columns.Add($"Pirate{i}_HistWin");
            columns.Add($"Pirate{i}_ArenaWin");
            columns.Add($"Pirate{i}_RecentWin");
            columns.Add($"Pirate{i}_StrengthDiff");
            columns.Add($"Pirate{i}_OddsRank");
            columns.Add($"Pirate{i}_InteractionPenalty");
            columns.Add($"Pirate{i}_InteractionBonus");
        }
        
        // Pairwise comparison features (6 pairs: 0v1, 0v2, 0v3, 1v2, 1v3, 2v3)
        columns.Add("Pair01_StrengthDiff");
        columns.Add("Pair01_OddsDiff");
        columns.Add("Pair01_FoodDiff");
        columns.Add("Pair01_HistWinDiff");
        
        columns.Add("Pair02_StrengthDiff");
        columns.Add("Pair02_OddsDiff");
        columns.Add("Pair02_FoodDiff");
        columns.Add("Pair02_HistWinDiff");
        
        columns.Add("Pair03_StrengthDiff");
        columns.Add("Pair03_OddsDiff");
        columns.Add("Pair03_FoodDiff");
        columns.Add("Pair03_HistWinDiff");
        
        columns.Add("Pair12_StrengthDiff");
        columns.Add("Pair12_OddsDiff");
        columns.Add("Pair12_FoodDiff");
        columns.Add("Pair12_HistWinDiff");
        
        columns.Add("Pair13_StrengthDiff");
        columns.Add("Pair13_OddsDiff");
        columns.Add("Pair13_FoodDiff");
        columns.Add("Pair13_HistWinDiff");
        
        columns.Add("Pair23_StrengthDiff");
        columns.Add("Pair23_OddsDiff");
        columns.Add("Pair23_FoodDiff");
        columns.Add("Pair23_HistWinDiff");
        
        return columns.ToArray();
    }

    private List<PairwiseRoundFeature> ConvertToPairwiseRoundFormat(List<PirateFeatureRecord> data)
    {
        var result = new List<PairwiseRoundFeature>();
        
        var roundGroups = data.GroupBy(f => f.RoundId);

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

            // Calculate interaction features for each pirate
            var penalties = new float[4];
            var bonuses = new float[4];
            
            for (int i = 0; i < 4; i++)
            {
                var mlFeature = new MlPirateFeature();
                InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirates[i], _interactionReport);
                penalties[i] = mlFeature.Penalty_FoodPosition + mlFeature.Penalty_FoodFavorite + 
                               mlFeature.Penalty_StrengthPosition + mlFeature.Penalty_StrengthWeakRivals;
                bonuses[i] = mlFeature.Bonus_UndervaluedStrong + mlFeature.Bonus_HotStreakBeatsRivals +
                             mlFeature.Bonus_ArenaSpecialistModerateOdds + mlFeature.Bonus_FoodPosition3;
            }

            result.Add(new PairwiseRoundFeature
            {
                // Pirate 0 individual features
                Pirate0_Strength = pirates[0].Strength,
                Pirate0_Odds = (float)Math.Log(Math.Max(2, pirates[0].CurrentOdds)),
                Pirate0_Food = pirates[0].FoodAdjustment,
                Pirate0_HistWin = (float)pirates[0].HistoricalWinRate,
                Pirate0_ArenaWin = (float)pirates[0].ArenaWinRate,
                Pirate0_RecentWin = (float)pirates[0].RecentWinRate,
                Pirate0_StrengthDiff = pirates[0].Strength - (float)avgStrength,
                Pirate0_OddsRank = oddsRanks[0],
                Pirate0_InteractionPenalty = penalties[0],
                Pirate0_InteractionBonus = bonuses[0],
                
                // Pirate 1 individual features
                Pirate1_Strength = pirates[1].Strength,
                Pirate1_Odds = (float)Math.Log(Math.Max(2, pirates[1].CurrentOdds)),
                Pirate1_Food = pirates[1].FoodAdjustment,
                Pirate1_HistWin = (float)pirates[1].HistoricalWinRate,
                Pirate1_ArenaWin = (float)pirates[1].ArenaWinRate,
                Pirate1_RecentWin = (float)pirates[1].RecentWinRate,
                Pirate1_StrengthDiff = pirates[1].Strength - (float)avgStrength,
                Pirate1_OddsRank = oddsRanks[1],
                Pirate1_InteractionPenalty = penalties[1],
                Pirate1_InteractionBonus = bonuses[1],
                
                // Pirate 2 individual features
                Pirate2_Strength = pirates[2].Strength,
                Pirate2_Odds = (float)Math.Log(Math.Max(2, pirates[2].CurrentOdds)),
                Pirate2_Food = pirates[2].FoodAdjustment,
                Pirate2_HistWin = (float)pirates[2].HistoricalWinRate,
                Pirate2_ArenaWin = (float)pirates[2].ArenaWinRate,
                Pirate2_RecentWin = (float)pirates[2].RecentWinRate,
                Pirate2_StrengthDiff = pirates[2].Strength - (float)avgStrength,
                Pirate2_OddsRank = oddsRanks[2],
                Pirate2_InteractionPenalty = penalties[2],
                Pirate2_InteractionBonus = bonuses[2],
                
                // Pirate 3 individual features
                Pirate3_Strength = pirates[3].Strength,
                Pirate3_Odds = (float)Math.Log(Math.Max(2, pirates[3].CurrentOdds)),
                Pirate3_Food = pirates[3].FoodAdjustment,
                Pirate3_HistWin = (float)pirates[3].HistoricalWinRate,
                Pirate3_ArenaWin = (float)pirates[3].ArenaWinRate,
                Pirate3_RecentWin = (float)pirates[3].RecentWinRate,
                Pirate3_StrengthDiff = pirates[3].Strength - (float)avgStrength,
                Pirate3_OddsRank = oddsRanks[3],
                Pirate3_InteractionPenalty = penalties[3],
                Pirate3_InteractionBonus = bonuses[3],
                
                // Pairwise comparison features (0 vs 1)
                Pair01_StrengthDiff = pirates[0].Strength - pirates[1].Strength,
                Pair01_OddsDiff = (float)(Math.Log(Math.Max(2, pirates[0].CurrentOdds)) - Math.Log(Math.Max(2, pirates[1].CurrentOdds))),
                Pair01_FoodDiff = pirates[0].FoodAdjustment - pirates[1].FoodAdjustment,
                Pair01_HistWinDiff = (float)(pirates[0].HistoricalWinRate - pirates[1].HistoricalWinRate),
                
                // Pairwise comparison features (0 vs 2)
                Pair02_StrengthDiff = pirates[0].Strength - pirates[2].Strength,
                Pair02_OddsDiff = (float)(Math.Log(Math.Max(2, pirates[0].CurrentOdds)) - Math.Log(Math.Max(2, pirates[2].CurrentOdds))),
                Pair02_FoodDiff = pirates[0].FoodAdjustment - pirates[2].FoodAdjustment,
                Pair02_HistWinDiff = (float)(pirates[0].HistoricalWinRate - pirates[2].HistoricalWinRate),
                
                // Pairwise comparison features (0 vs 3)
                Pair03_StrengthDiff = pirates[0].Strength - pirates[3].Strength,
                Pair03_OddsDiff = (float)(Math.Log(Math.Max(2, pirates[0].CurrentOdds)) - Math.Log(Math.Max(2, pirates[3].CurrentOdds))),
                Pair03_FoodDiff = pirates[0].FoodAdjustment - pirates[3].FoodAdjustment,
                Pair03_HistWinDiff = (float)(pirates[0].HistoricalWinRate - pirates[3].HistoricalWinRate),
                
                // Pairwise comparison features (1 vs 2)
                Pair12_StrengthDiff = pirates[1].Strength - pirates[2].Strength,
                Pair12_OddsDiff = (float)(Math.Log(Math.Max(2, pirates[1].CurrentOdds)) - Math.Log(Math.Max(2, pirates[2].CurrentOdds))),
                Pair12_FoodDiff = pirates[1].FoodAdjustment - pirates[2].FoodAdjustment,
                Pair12_HistWinDiff = (float)(pirates[1].HistoricalWinRate - pirates[2].HistoricalWinRate),
                
                // Pairwise comparison features (1 vs 3)
                Pair13_StrengthDiff = pirates[1].Strength - pirates[3].Strength,
                Pair13_OddsDiff = (float)(Math.Log(Math.Max(2, pirates[1].CurrentOdds)) - Math.Log(Math.Max(2, pirates[3].CurrentOdds))),
                Pair13_FoodDiff = pirates[1].FoodAdjustment - pirates[3].FoodAdjustment,
                Pair13_HistWinDiff = (float)(pirates[1].HistoricalWinRate - pirates[3].HistoricalWinRate),
                
                // Pairwise comparison features (2 vs 3)
                Pair23_StrengthDiff = pirates[2].Strength - pirates[3].Strength,
                Pair23_OddsDiff = (float)(Math.Log(Math.Max(2, pirates[2].CurrentOdds)) - Math.Log(Math.Max(2, pirates[3].CurrentOdds))),
                Pair23_FoodDiff = pirates[2].FoodAdjustment - pirates[3].FoodAdjustment,
                Pair23_HistWinDiff = (float)(pirates[2].HistoricalWinRate - pirates[3].HistoricalWinRate),
                
                WinnerPosition = winnerIndex
            });
        }

        return result;
    }

    private double[] Softmax(float[] scores)
    {
        if (scores == null || scores.Length == 0)
            return new double[4] { 0.25, 0.25, 0.25, 0.25 };
            
        var doubleScores = scores.Select(s => (double)s).ToArray();
        var max = doubleScores.Max();
        var exps = doubleScores.Select(s => Math.Exp(s - max)).ToArray();
        var sum = exps.Sum();
        
        return sum > 0 ? exps.Select(e => e / sum).ToArray() : new double[4] { 0.25, 0.25, 0.25, 0.25 };
    }

    public void SaveModel(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            if (_arenaModels.TryGetValue(arenaId, out var model))
            {
                _mlContext.Model.Save(model, null, path.Replace(".zip", $"_mcpairwise_arena{arenaId}.zip"));
            }
        }
    }

    public void LoadModel(string path)
    {
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_mcpairwise_arena{arenaId}.zip");
            if (File.Exists(arenaPath))
            {
                _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
            }
        }
    }
}