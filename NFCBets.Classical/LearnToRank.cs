using Microsoft.ML;
using Microsoft.ML.Data;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;

namespace NFCBets.Classical;


/// <summary>
/// Uses LightGBM ranking to rank pirates within each arena
/// Optimizes for getting the winner in top position
/// </summary>
public class LearnToRank : IMlStrategy
{
    public string StrategyName => "Learn to Rank (LightGBM Ranker)";
    
    private readonly MLContext _mlContext;
    private readonly Dictionary<int, ITransformer> _arenaModels = new();

    public LearnToRank()
    {
        _mlContext = new MLContext(42);
    }

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData)
    {
        Console.WriteLine($"🏋️ Training {StrategyName}...");

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();
            
            if (!arenaData.Any()) continue;

            Console.WriteLine($"   Training Arena {arenaId} ranker...");

            var rankingData = ConvertToRankingFormat(arenaData);
            var dataView = _mlContext.Data.LoadFromEnumerable(rankingData);

            // Ranking pipeline
            var pipeline = _mlContext.Transforms.Concatenate("Features",
                    nameof(RankingFeature.Position),
                    nameof(RankingFeature.CurrentOdds),
                    nameof(RankingFeature.FoodAdjustment),
                    nameof(RankingFeature.Strength),
                    nameof(RankingFeature.HistoricalWinRate),
                    nameof(RankingFeature.ArenaWinRate))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.Ranking.Trainers.LightGbm(
                    labelColumnName: nameof(RankingFeature.Label),
                    featureColumnName: "Features",
                    rowGroupColumnName: nameof(RankingFeature.GroupId)));

            _arenaModels[arenaId] = pipeline.Fit(dataView);
        }
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        foreach (var arenaGroup in features.GroupBy(f => f.ArenaId))
        {
            var arenaId = arenaGroup.Key;
            if (!_arenaModels.TryGetValue(arenaId, out var model))
                continue;

            var pirates = arenaGroup.ToList();
            var rankingData = ConvertToRankingFormat(pirates);
            var dataView = _mlContext.Data.LoadFromEnumerable(rankingData);
            var prediction = model.Transform(dataView);
            
            var scores = _mlContext.Data.CreateEnumerable<RankingPrediction>(prediction, false).ToList();

            // Convert ranking scores to probabilities using softmax
            var rawScores = scores.Select(s => (double)s.Score).ToArray();
            var probabilities = Softmax(rawScores);

            for (int i = 0; i < pirates.Count; i++)
            {
                predictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)probabilities[i],
                    Payout = pirates[i].CurrentOdds
                });
            }
        }

        return predictions;
    }

    private double[] Softmax(double[] scores)
    {
        var max = scores.Max();
        var exps = scores.Select(s => Math.Exp(s - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(e => e / sum).ToArray();
    }

    private List<RankingFeature> ConvertToRankingFormat(List<PirateFeatureRecord> features)
    {
        return features.Select(f => new RankingFeature
        {
            GroupId = (uint)f.RoundId, // Group by round
            Label = f.IsWinner == true ? 1f : 0f, // Winner gets label 1
            Position = f.Position,
            CurrentOdds = Math.Max(2, f.CurrentOdds),
            FoodAdjustment = f.FoodAdjustment,
            Strength = f.Strength,
            HistoricalWinRate = (float)f.HistoricalWinRate,
            ArenaWinRate = (float)f.ArenaWinRate
        }).ToList();
    }

    public async Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        throw new NotImplementedException();
    }

    public void SaveModel(string path)
    {
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            if (_arenaModels.TryGetValue(arenaId, out var model))
            {
                var arenaPath = path.Replace(".zip", $"_ranker_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
        }
    }

    public void LoadModel(string path)
    {
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_ranker_arena{arenaId}.zip");
            if (File.Exists(arenaPath))
            {
                _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
            }
        }
    }
}
