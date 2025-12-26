using Microsoft.ML;
using Microsoft.ML.Data;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;


namespace NFCBets.Classical;

/// <summary>
/// Trains 5 separate multi-class classifiers (one per arena)
/// Each predicts which of 4 pirates will win
/// </summary>
public class MultiClassPerArena : IMlStrategy
{
    public string StrategyName => "Multi-Class Per Arena";
    
    private readonly MLContext _mlContext;
    private readonly Dictionary<int, ITransformer> _arenaModels = new();

    public MultiClassPerArena()
    {
        _mlContext = new MLContext(42);
    }

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData)
    {
        Console.WriteLine($"🏋️ Training {StrategyName}...");

        // Train separate model for each arena
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();
            
            if (!arenaData.Any())
            {
                Console.WriteLine($"   ⚠️ No data for Arena {arenaId}");
                continue;
            }

            Console.WriteLine($"   Training Arena {arenaId} model ({arenaData.Count} records)...");

            var mlData = ConvertToMultiClassFormat(arenaData);
            var dataView = _mlContext.Data.LoadFromEnumerable(mlData);

            // Multi-class classification pipeline
            var pipeline = _mlContext.Transforms.Conversion.MapValueToKey("Label", nameof(MultiClassFeature.WinnerPirateId))
                .Append(_mlContext.Transforms.Concatenate("Features",
                    nameof(MultiClassFeature.Position),
                    nameof(MultiClassFeature.CurrentOdds),
                    nameof(MultiClassFeature.FoodAdjustment),
                    nameof(MultiClassFeature.Strength),
                    nameof(MultiClassFeature.Weight),
                    nameof(MultiClassFeature.HistoricalWinRate),
                    nameof(MultiClassFeature.ArenaWinRate),
                    nameof(MultiClassFeature.RecentWinRate)))
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                .Append(_mlContext.MulticlassClassification.Trainers.SdcaMaximumEntropy("Label", "Features"))
                .Append(_mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel"));

            _arenaModels[arenaId] = pipeline.Fit(dataView);
        }

        Console.WriteLine($"   ✅ Trained {_arenaModels.Count} arena-specific models");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        foreach (var arenaGroup in features.GroupBy(f => f.ArenaId))
        {
            var arenaId = arenaGroup.Key;
            
            if (!_arenaModels.TryGetValue(arenaId, out var model))
            {
                Console.WriteLine($"   ⚠️ No model for Arena {arenaId}");
                continue;
            }

            var arenaFeatures = arenaGroup.ToList();
            var mlData = ConvertToMultiClassFormat(arenaFeatures);
            var dataView = _mlContext.Data.LoadFromEnumerable(mlData);
            var prediction = model.Transform(dataView);
            
            // Get probabilities for each pirate
            var probabilities = _mlContext.Data.CreateEnumerable<MultiClassPrediction>(prediction, false).ToList();

            for (int i = 0; i < arenaFeatures.Count; i++)
            {
                predictions.Add(new PiratePrediction
                {
                    RoundId = arenaFeatures[i].RoundId,
                    ArenaId = arenaFeatures[i].ArenaId,
                    PirateId = arenaFeatures[i].PirateId,
                    WinProbability = probabilities[i].Score.Max(), // Probability this pirate wins
                    Payout = arenaFeatures[i].CurrentOdds
                });
            }
        }

        return predictions;
    }

    public async Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        // Evaluation logic per arena
        throw new NotImplementedException();
    }

    public void SaveModel(string path)
    {
        // Save all 5 arena models
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            if (_arenaModels.TryGetValue(arenaId, out var model))
            {
                var arenaPath = path.Replace(".zip", $"_arena{arenaId}.zip");
                _mlContext.Model.Save(model, null, arenaPath);
            }
        }
    }

    public void LoadModel(string path)
    {
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPath = path.Replace(".zip", $"_arena{arenaId}.zip");
            if (File.Exists(arenaPath))
            {
                _arenaModels[arenaId] = _mlContext.Model.Load(arenaPath, out _);
            }
        }
    }

    private List<MultiClassFeature> ConvertToMultiClassFormat(List<PirateFeatureRecord> features)
    {
        // Group by round to identify winner
        var byRound = features.GroupBy(f => f.RoundId);
        var result = new List<MultiClassFeature>();

        foreach (var roundGroup in byRound)
        {
            var winner = roundGroup.FirstOrDefault(f => f.IsWinner == true);
            var winnerPirateId = winner?.PirateId ?? 0;

            foreach (var feature in roundGroup)
            {
                result.Add(new MultiClassFeature
                {
                    RoundId = feature.RoundId,
                    PirateId = feature.PirateId,
                    Position = feature.Position,
                    CurrentOdds = Math.Max(2, feature.CurrentOdds),
                    FoodAdjustment = feature.FoodAdjustment,
                    Strength = feature.Strength,
                    Weight = feature.Weight,
                    HistoricalWinRate = (float)feature.HistoricalWinRate,
                    ArenaWinRate = (float)feature.ArenaWinRate,
                    RecentWinRate = (float)feature.RecentWinRate,
                    WinnerPirateId = winnerPirateId // Which pirate won this arena
                });
            }
        }

        return result;
    }
}

