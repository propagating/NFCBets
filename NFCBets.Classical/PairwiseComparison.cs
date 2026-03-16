using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class PairwiseComparison : IMlStrategy
{
    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;
    private ITransformer? _pairwiseModel;

    public PairwiseComparison()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Pairwise Comparison";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null) 
            Console.WriteLine("      Applying interaction controls");

        var pairwiseData = CreatePairwiseData(trainingData);

        Console.WriteLine($"      Created {pairwiseData.Count} pairwise comparisons");

        var dataView = _mlContext.Data.LoadFromEnumerable(pairwiseData);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
                // Pirate A features
                "A_Strength", "A_Odds", "A_Food", "A_HistWin", "A_Position",
                "A_InteractionPenalty", "A_InteractionBonus",
                // Pirate B features  
                "B_Strength", "B_Odds", "B_Food", "B_HistWin", "B_Position",
                "B_InteractionPenalty", "B_InteractionBonus",
                // Difference features (A - B)
                "Diff_Strength", "Diff_Odds", "Diff_Food", "Diff_HistWin",
                "Diff_InteractionPenalty", "Diff_InteractionBonus")
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                "A_Wins",
                numberOfLeaves: 15,
                minimumExampleCountPerLeaf: 20,
                learningRate: 0.05,
                numberOfIterations: 100));

        _pairwiseModel = pipeline.Fit(dataView);

        Console.WriteLine("   ✅ Pairwise model trained");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (_pairwiseModel == null)
            throw new InvalidOperationException("Model must be trained first");

        var predictions = new List<PiratePrediction>();

        // Pre-compute grouped data for feature conversion
        var groupedByRoundArena = features
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            // Calculate interaction features using new property names
            var mlFeatures = new MlPirateFeature[4];
            for (var i = 0; i < 4; i++)
            {
                mlFeatures[i] = FeatureConversionHelper.ConvertSingle(
                    pirates[i], groupedByRoundArena, _interactionReport);
            }

            var pairwiseProbs = new double[4, 4];

            for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
            {
                if (i == j)
                {
                    pairwiseProbs[i, j] = 0.5;
                    continue;
                }

                var pirateA = pirates[i];
                var pirateB = pirates[j];

                var penaltyA = InteractionCalculator.GetTotalPenalty(mlFeatures[i]);
                var bonusA = InteractionCalculator.GetTotalBonus(mlFeatures[i]);

                var penaltyB = InteractionCalculator.GetTotalPenalty(mlFeatures[j]);
                var bonusB = InteractionCalculator.GetTotalBonus(mlFeatures[j]);

                var pairFeature = new PairwiseFeature
                {
                    A_Strength = pirateA.Strength,
                    A_Odds = (float)Math.Log(Math.Max(2, pirateA.CurrentOdds)),
                    A_Food = pirateA.FoodAdjustment,
                    A_HistWin = (float)pirateA.HistoricalWinRate,
                    A_Position = pirateA.Position,
                    A_InteractionPenalty = penaltyA,
                    A_InteractionBonus = bonusA,

                    B_Strength = pirateB.Strength,
                    B_Odds = (float)Math.Log(Math.Max(2, pirateB.CurrentOdds)),
                    B_Food = pirateB.FoodAdjustment,
                    B_HistWin = (float)pirateB.HistoricalWinRate,
                    B_Position = pirateB.Position,
                    B_InteractionPenalty = penaltyB,
                    B_InteractionBonus = bonusB,

                    Diff_Strength = pirateA.Strength - pirateB.Strength,
                    Diff_Odds = (float)(Math.Log(Math.Max(2, pirateA.CurrentOdds)) -
                                        Math.Log(Math.Max(2, pirateB.CurrentOdds))),
                    Diff_Food = pirateA.FoodAdjustment - pirateB.FoodAdjustment,
                    Diff_HistWin = (float)(pirateA.HistoricalWinRate - pirateB.HistoricalWinRate),
                    Diff_InteractionPenalty = penaltyA - penaltyB,
                    Diff_InteractionBonus = bonusA - bonusB
                };

                var dataView = _mlContext.Data.LoadFromEnumerable(new[] { pairFeature });
                var prediction = _pairwiseModel.Transform(dataView);
                var result = _mlContext.Data.CreateEnumerable<PairwisePrediction>(prediction, false).First();

                pairwiseProbs[i, j] = result.Probability;
            }

            // Aggregate pairwise probabilities using Copeland-style aggregation
            var scores = new double[4];
            for (var i = 0; i < 4; i++)
            for (var j = 0; j < 4; j++)
                if (i != j)
                    scores[i] += pairwiseProbs[i, j];

            var probs = Softmax(scores);

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
        if (_pairwiseModel == null)
            throw new InvalidOperationException("No model to save");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _mlContext.Model.Save(_pairwiseModel, null, path.Replace(".zip", "_pairwise.zip"));
    }

    public void LoadModel(string path)
    {
        _pairwiseModel = _mlContext.Model.Load(path.Replace(".zip", "_pairwise.zip"), out _);
    }

    private List<PairwiseFeature> CreatePairwiseData(List<PirateFeatureRecord> data)
    {
        var pairwise = new List<PairwiseFeature>();

        // Pre-compute grouped data for feature conversion
        var groupedByRoundArena = data
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var roundGroups = data.GroupBy(f => (f.RoundId, f.ArenaId));

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var winnerIdx = pirates.FindIndex(p => p.IsWinner == true);
            if (winnerIdx < 0) continue;

            // Calculate interaction features for each pirate using new property names
            var mlFeatures = new MlPirateFeature[4];
            for (var i = 0; i < 4; i++)
            {
                mlFeatures[i] = FeatureConversionHelper.ConvertSingle(
                    pirates[i], groupedByRoundArena, _interactionReport);
            }

            // Create all 6 pairwise comparisons (4 choose 2)
            for (var i = 0; i < 4; i++)
            for (var j = i + 1; j < 4; j++)
            {
                var pirateA = pirates[i];
                var pirateB = pirates[j];

                var aWins = i == winnerIdx;

                var penaltyA = InteractionCalculator.GetTotalPenalty(mlFeatures[i]);
                var bonusA = InteractionCalculator.GetTotalBonus(mlFeatures[i]);

                var penaltyB = InteractionCalculator.GetTotalPenalty(mlFeatures[j]);
                var bonusB = InteractionCalculator.GetTotalBonus(mlFeatures[j]);

                pairwise.Add(new PairwiseFeature
                {
                    A_Strength = pirateA.Strength,
                    A_Odds = (float)Math.Log(Math.Max(2, pirateA.CurrentOdds)),
                    A_Food = pirateA.FoodAdjustment,
                    A_HistWin = (float)pirateA.HistoricalWinRate,
                    A_Position = pirateA.Position,
                    A_InteractionPenalty = penaltyA,
                    A_InteractionBonus = bonusA,

                    B_Strength = pirateB.Strength,
                    B_Odds = (float)Math.Log(Math.Max(2, pirateB.CurrentOdds)),
                    B_Food = pirateB.FoodAdjustment,
                    B_HistWin = (float)pirateB.HistoricalWinRate,
                    B_Position = pirateB.Position,
                    B_InteractionPenalty = penaltyB,
                    B_InteractionBonus = bonusB,

Diff_Strength = pirateA.Strength - pirateB.Strength,
                    Diff_Odds = (float)(Math.Log(Math.Max(2, pirateA.CurrentOdds)) -
                                        Math.Log(Math.Max(2, pirateB.CurrentOdds))),
                    Diff_Food = pirateA.FoodAdjustment - pirateB.FoodAdjustment,
                    Diff_HistWin = (float)(pirateA.HistoricalWinRate - pirateB.HistoricalWinRate),
                    Diff_InteractionPenalty = penaltyA - penaltyB,
                    Diff_InteractionBonus = bonusA - bonusB,

                    A_Wins = aWins
                });

                // Reverse comparison
                pairwise.Add(new PairwiseFeature
                {
                    A_Strength = pirateB.Strength,
                    A_Odds = (float)Math.Log(Math.Max(2, pirateB.CurrentOdds)),
                    A_Food = pirateB.FoodAdjustment,
                    A_HistWin = (float)pirateB.HistoricalWinRate,
                    A_Position = pirateB.Position,
                    A_InteractionPenalty = penaltyB,
                    A_InteractionBonus = bonusB,

                    B_Strength = pirateA.Strength,
                    B_Odds = (float)Math.Log(Math.Max(2, pirateA.CurrentOdds)),
                    B_Food = pirateA.FoodAdjustment,
                    B_HistWin = (float)pirateA.HistoricalWinRate,
                    B_Position = pirateA.Position,
                    B_InteractionPenalty = penaltyA,
                    B_InteractionBonus = bonusA,

                    Diff_Strength = pirateB.Strength - pirateA.Strength,
                    Diff_Odds = (float)(Math.Log(Math.Max(2, pirateB.CurrentOdds)) -
                                        Math.Log(Math.Max(2, pirateA.CurrentOdds))),
                    Diff_Food = pirateB.FoodAdjustment - pirateA.FoodAdjustment,
                    Diff_HistWin = (float)(pirateB.HistoricalWinRate - pirateA.HistoricalWinRate),
                    Diff_InteractionPenalty = penaltyB - penaltyA,
                    Diff_InteractionBonus = bonusB - bonusA,

                    A_Wins = !aWins
                });
            }
        }

        return pairwise;
    }

    private double[] Softmax(double[] scores)
    {
        var max = scores.Max();
        var exps = scores.Select(s => Math.Exp(s - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(e => e / sum).ToArray();
    }
}