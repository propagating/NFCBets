using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class LearnToRank : IMlStrategy
{
    private readonly MLContext _mlContext;
    private InteractionAnalysisReport? _interactionReport;
    private ITransformer? _model;

    public LearnToRank()
    {
        _mlContext = new MLContext(42);
    }

    public string StrategyName => "Learn to Rank";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null) 
            Console.WriteLine("      Applying interaction controls");

        var rankingData = ConvertToRankingFormat(trainingData);

        Console.WriteLine($"      Created {rankingData.Count} ranking records");

        var dataView = _mlContext.Data.LoadFromEnumerable(rankingData);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(RankingFeature.Position),
                nameof(RankingFeature.CurrentOdds),
                nameof(RankingFeature.FoodAdjustment),
                nameof(RankingFeature.Strength),
                nameof(RankingFeature.HistoricalWinRate),
                nameof(RankingFeature.ArenaWinRate),
                nameof(RankingFeature.RecentWinRate),
                nameof(RankingFeature.WinRateVsCurrentRivals),
                nameof(RankingFeature.AvgRivalStrength),
                nameof(RankingFeature.StrengthDiff),
                nameof(RankingFeature.OddsRank),
                nameof(RankingFeature.InteractionPenalty),
                nameof(RankingFeature.InteractionBonus))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.Ranking.Trainers.LightGbm(numberOfLeaves: 31,
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

        var predictions = new List<PiratePrediction>();

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var rankingData = ConvertToRankingFormat(pirates);
            var dataView = _mlContext.Data.LoadFromEnumerable(rankingData);
            var prediction = _model.Transform(dataView);

            var results = _mlContext.Data.CreateEnumerable<RankingPrediction>(prediction, false).ToList();

            // Convert ranking scores to probabilities via softmax
            var scores = results.Select(r => (double)r.Score).ToArray();
            var probs = Softmax(scores);

            for (var i = 0; i < pirates.Count && i < probs.Length; i++)
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
        if (_model == null)
            throw new InvalidOperationException("No model to save");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _mlContext.Model.Save(_model, null, path.Replace(".zip", "_ranking.zip"));
    }

    public void LoadModel(string path)
    {
        _model = _mlContext.Model.Load(path.Replace(".zip", "_ranking.zip"), out _);
    }

    private double[] Softmax(double[] scores)
    {
        if (scores == null || scores.Length == 0)
            return new double[4] { 0.25, 0.25, 0.25, 0.25 };

        var max = scores.Max();
        var exps = scores.Select(s => Math.Exp(s - max)).ToArray();
        var sum = exps.Sum();

        if (sum == 0)
            return new double[4] { 0.25, 0.25, 0.25, 0.25 };

        return exps.Select(e => e / sum).ToArray();
    }

    private List<RankingFeature> ConvertToRankingFormat(List<PirateFeatureRecord> data)
    {
        var result = new List<RankingFeature>();

        // Pre-compute grouped data for feature conversion
        var groupedByRoundArena = data
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        var roundGroups = data.GroupBy(f => (f.RoundId, f.ArenaId));

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var groupId = round.Key.RoundId * 10 + round.Key.ArenaId;
            var avgStrength = pirates.Average(p => p.Strength);

            var oddsRanks = pirates
                .Select((p, idx) => new { Index = idx, Odds = p.CurrentOdds })
                .OrderBy(x => x.Odds)
                .Select((x, rank) => new { x.Index, Rank = rank + 1 })
                .OrderBy(x => x.Index)
                .Select(x => (float)x.Rank)
                .ToArray();

            for (var i = 0; i < pirates.Count; i++)
            {
                var pirate = pirates[i];

                // Calculate interaction features using new property names
                var mlFeature = FeatureConversionHelper.ConvertSingle(
                    pirate, groupedByRoundArena, _interactionReport);

                var penalty = InteractionCalculator.GetTotalPenalty(mlFeature);
                var bonus = InteractionCalculator.GetTotalBonus(mlFeature);

                // Label: winner = 3, others = 0 (higher is better in ranking)
                var label = pirate.IsWinner == true ? 3u : 0u;

                result.Add(new RankingFeature
                {
                    GroupId = groupId,
                    Label = label,
                    Position = pirate.Position,
                    CurrentOdds = (float)Math.Log(Math.Max(2, pirate.CurrentOdds)),
                    FoodAdjustment = pirate.FoodAdjustment,
                    Strength = pirate.Strength,
                    HistoricalWinRate = (float)pirate.HistoricalWinRate,
                    ArenaWinRate = (float)pirate.ArenaWinRate,
                    RecentWinRate = (float)pirate.RecentWinRate,
                    WinRateVsCurrentRivals = (float)pirate.WinRateVsCurrentRivals,
                    AvgRivalStrength = (float)pirate.AvgRivalStrength,
                    StrengthDiff = pirate.Strength - avgStrength,
                    OddsRank = oddsRanks[i],
                    InteractionPenalty = penalty,
                    InteractionBonus = bonus
                });
            }
        }

        return result;
    }
}