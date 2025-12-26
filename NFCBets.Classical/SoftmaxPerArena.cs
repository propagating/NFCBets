using Microsoft.ML;
using Microsoft.ML.Data;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;

namespace NFCBets.Classical;

/// <summary>
/// Uses neural networks with softmax output for each arena
/// Ensures probabilities sum to 1 per arena
/// </summary>
public class SoftmaxPerArena : IMlStrategy
{
    public string StrategyName => "Softmax Neural Network Per Arena";
    
    private readonly MLContext _mlContext;
    private readonly Dictionary<int, ITransformer> _arenaModels = new();

    public SoftmaxPerArena()
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

            Console.WriteLine($"   Training Arena {arenaId} neural network...");

            // Group by round - each round is one training example with 4 pirates
            var roundGroups = arenaData.GroupBy(f => f.RoundId);
            var arenaRounds = new List<ArenaRoundFeature>();

            foreach (var round in roundGroups)
            {
                var pirates = round.OrderBy(p => p.Position).ToList();
                if (pirates.Count != 4) continue; // Must have all 4 pirates

                var winnerIndex = pirates.FindIndex(p => p.IsWinner == true);
                if (winnerIndex < 0) continue;

                arenaRounds.Add(new ArenaRoundFeature
                {
                    // Features for all 4 pirates (flattened)
                    Pirate0_Strength = pirates[0].Strength,
                    Pirate0_Odds = pirates[0].CurrentOdds,
                    Pirate0_Food = pirates[0].FoodAdjustment,
                    Pirate0_HistWin = (float)pirates[0].HistoricalWinRate,
                    
                    Pirate1_Strength = pirates[1].Strength,
                    Pirate1_Odds = pirates[1].CurrentOdds,
                    Pirate1_Food = pirates[1].FoodAdjustment,
                    Pirate1_HistWin = (float)pirates[1].HistoricalWinRate,
                    
                    Pirate2_Strength = pirates[2].Strength,
                    Pirate2_Odds = pirates[2].CurrentOdds,
                    Pirate2_Food = pirates[2].FoodAdjustment,
                    Pirate2_HistWin = (float)pirates[2].HistoricalWinRate,
                    
                    Pirate3_Strength = pirates[3].Strength,
                    Pirate3_Odds = pirates[3].CurrentOdds,
                    Pirate3_Food = pirates[3].FoodAdjustment,
                    Pirate3_HistWin = (float)pirates[3].HistoricalWinRate,
                    
                    WinnerPosition = (uint)winnerIndex
                });
            }

            var dataView = _mlContext.Data.LoadFromEnumerable(arenaRounds);

            // Neural network with softmax
            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(ArenaRoundFeature.WinnerPosition))
                .Append(_mlContext.Transforms.Concatenate("Features",
                    nameof(ArenaRoundFeature.Pirate0_Strength), nameof(ArenaRoundFeature.Pirate0_Odds), 
                    nameof(ArenaRoundFeature.Pirate0_Food), nameof(ArenaRoundFeature.Pirate0_HistWin),
                    nameof(ArenaRoundFeature.Pirate1_Strength), nameof(ArenaRoundFeature.Pirate1_Odds),
                    nameof(ArenaRoundFeature.Pirate1_Food), nameof(ArenaRoundFeature.Pirate1_HistWin),
                    nameof(ArenaRoundFeature.Pirate2_Strength), nameof(ArenaRoundFeature.Pirate2_Odds),
                    nameof(ArenaRoundFeature.Pirate2_Food), nameof(ArenaRoundFeature.Pirate2_HistWin),
                    nameof(ArenaRoundFeature.Pirate3_Strength), nameof(ArenaRoundFeature.Pirate3_Odds),
                    nameof(ArenaRoundFeature.Pirate3_Food), nameof(ArenaRoundFeature.Pirate3_HistWin)))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

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

            var pirates = arenaGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            // Create arena round feature
            var arenaFeature = new ArenaRoundFeature
            {
                Pirate0_Strength = pirates[0].Strength,
                Pirate0_Odds = pirates[0].CurrentOdds,
                Pirate0_Food = pirates[0].FoodAdjustment,
                Pirate0_HistWin = (float)pirates[0].HistoricalWinRate,
                // ... repeat for pirates 1-3 ...
            };

            var dataView = _mlContext.Data.LoadFromEnumerable(new[] { arenaFeature });
            var prediction = model.Transform(dataView);
            var result = _mlContext.Data.CreateEnumerable<MultiClassPrediction>(prediction, false).First();

            // Softmax probabilities for all 4 pirates
            for (int i = 0; i < 4; i++)
            {
                predictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = result.Score[i],
                    Payout = pirates[i].CurrentOdds
                });
            }
        }

        return predictions;
    }

    public async Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        // TODO: Implement evaluation
        throw new NotImplementedException();
    }

    public void SaveModel(string path)
    {
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            if (_arenaModels.TryGetValue(arenaId, out var model))
            {
                var arenaPath = path.Replace(".zip", $"_multiclass_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
        }
    }

    public void LoadModel(string path)
    {
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_multiclass_arena{arenaId}.zip");
            if (File.Exists(arenaPath))
            {
                _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
            }
        }
    }

    private List<MultiClassFeature> ConvertToMultiClassFormat(List<PirateFeatureRecord> features)
    {
        // Implementation similar to above
        return features.Select(f => new MultiClassFeature
        {
            RoundId = f.RoundId,
            PirateId = f.PirateId,
            Position = f.Position,
            CurrentOdds = Math.Max(2, f.CurrentOdds),
            FoodAdjustment = f.FoodAdjustment,
            Strength = f.Strength,
            Weight = f.Weight,
            HistoricalWinRate = (float)f.HistoricalWinRate,
            ArenaWinRate = (float)f.ArenaWinRate,
            RecentWinRate = (float)f.RecentWinRate,
            WinnerPirateId = f.IsWinner == true ? f.PirateId : 0
        }).ToList();
    }
}

