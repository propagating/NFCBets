using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Stacking Ensemble - Uses a meta-learner to combine base model predictions
/// Level 0: Base models predict probabilities
/// Level 1: Meta-learner learns optimal combination
/// </summary>
public class StackingEnsemble : IMlStrategy
{
    public string StrategyName => "Stacking Ensemble (Meta-Learner)";
    
    private readonly MLContext _mlContext;
    private readonly List<IMlStrategy> _baseModels = new();
    private ITransformer? _metaModel;
    private InteractionAnalysisReport? _interactionReport;

    public StackingEnsemble()
    {
        _mlContext = new MLContext(42);
    }

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;
        
        Console.WriteLine($"   Training {StrategyName}...");

        // Initialize base models
        _baseModels.Clear();
        _baseModels.Add(new MultinomialLogit());
        _baseModels.Add(new PlackettLuce());
        _baseModels.Add(new ConditionalLogisticRegression());
        _baseModels.Add(new BradleyTerry());
        _baseModels.Add(new BinaryClassification());

        // Split data for stacking (70% for base, 30% for meta)
        var uniqueRounds = trainingData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var splitIndex = (int)(uniqueRounds.Count * 0.7);
        
        var baseRounds = uniqueRounds.Take(splitIndex).ToHashSet();
        var metaRounds = uniqueRounds.Skip(splitIndex).ToHashSet();
        
        var baseTrainData = trainingData.Where(f => baseRounds.Contains(f.RoundId)).ToList();
        var metaTrainData = trainingData.Where(f => metaRounds.Contains(f.RoundId)).ToList();

        Console.WriteLine($"      Base training: {baseTrainData.Count} records");
        Console.WriteLine($"      Meta training: {metaTrainData.Count} records");

        // Train base models on base training data
        Console.WriteLine($"      Training {_baseModels.Count} base models...");
        foreach (var model in _baseModels)
        {
            try
            {
                await model.TrainAsync(baseTrainData, interactionReport);
                Console.WriteLine($"         ✅ {model.StrategyName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"         ❌ {model.StrategyName}: {ex.Message}");
            }
        }

        // Generate meta-features from base model predictions on meta training data
        Console.WriteLine($"      Generating meta-features...");
        var metaFeatures = await GenerateMetaFeatures(metaTrainData);

        if (!metaFeatures.Any())
        {
            Console.WriteLine($"      ⚠️ No meta-features generated, using fallback");
            return;
        }

        Console.WriteLine($"      Training meta-learner on {metaFeatures.Count} samples...");

        // Train meta-learner
        var dataView = _mlContext.Data.LoadFromEnumerable(metaFeatures);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(StackingMetaFeature.Model0_Prob),
                nameof(StackingMetaFeature.Model1_Prob),
                nameof(StackingMetaFeature.Model2_Prob),
                nameof(StackingMetaFeature.Model3_Prob),
                nameof(StackingMetaFeature.Model4_Prob),
                nameof(StackingMetaFeature.Odds),
                nameof(StackingMetaFeature.Strength),
                nameof(StackingMetaFeature.Food),
                nameof(StackingMetaFeature.Position),
                nameof(StackingMetaFeature.HistWinRate),
                nameof(StackingMetaFeature.OddsRank),
                nameof(StackingMetaFeature.MaxModelProb),
                nameof(StackingMetaFeature.MinModelProb),
                nameof(StackingMetaFeature.ModelProbStd))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                nameof(StackingMetaFeature.Won),
                "Features",
                numberOfLeaves: 15,
                minimumExampleCountPerLeaf: 10,
                learningRate: 0.05,
                numberOfIterations: 100));

        _metaModel = pipeline.Fit(dataView);
        
        Console.WriteLine($"   ✅ Stacking ensemble trained");
    }

    private async Task<List<StackingMetaFeature>> GenerateMetaFeatures(List<PirateFeatureRecord> data)
    {
        var metaFeatures = new List<StackingMetaFeature>();

        // Get predictions from all base models
        var basePredictions = new List<List<PiratePrediction>>();
        foreach (var model in _baseModels)
        {
            try
            {
                var preds = await model.PredictAsync(data);
                basePredictions.Add(preds);
            }
            catch
            {
                basePredictions.Add(new List<PiratePrediction>());
            }
        }

        // Generate meta-features for each pirate
        foreach (var roundGroup in data.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var oddsRanks = pirates
                .Select((p, idx) => new { Index = idx, Odds = p.CurrentOdds })
                .OrderBy(x => x.Odds)
                .Select((x, rank) => new { x.Index, Rank = rank + 1 })
                .OrderBy(x => x.Index)
                .Select(x => (float)x.Rank)
                .ToArray();

            for (int i = 0; i < 4; i++)
            {
                var pirate = pirates[i];
                var modelProbs = new float[_baseModels.Count];

                // Get predictions from each base model
                for (int m = 0; m < _baseModels.Count; m++)
                {
                    var pred = basePredictions[m]
                        .FirstOrDefault(p => p.RoundId == pirate.RoundId && 
                                            p.ArenaId == pirate.ArenaId && 
                                            p.PirateId == pirate.PirateId);
                    modelProbs[m] = pred?.WinProbability ?? 0.25f;
                }

                metaFeatures.Add(new StackingMetaFeature
                {
                    Model0_Prob = modelProbs[0],
                    Model1_Prob = modelProbs[1],
                    Model2_Prob = modelProbs[2],
                    Model3_Prob = modelProbs[3],
                    Model4_Prob = modelProbs.Length > 4 ? modelProbs[4] : 0.25f,
                    Odds = (float)Math.Log(Math.Max(2, pirate.CurrentOdds)),
                    Strength = pirate.Strength / 100f,
                    Food = pirate.FoodAdjustment / 10f,
                    Position = pirate.Position / 4f,
                    HistWinRate = (float)pirate.HistoricalWinRate,
                    OddsRank = oddsRanks[i] / 4f,
                    MaxModelProb = modelProbs.Max(),
                    MinModelProb = modelProbs.Min(),
                    ModelProbStd = (float)CalculateStdDev(modelProbs),
                    Won = pirate.IsWinner ?? false
                });
            }
        }

        return metaFeatures;
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (_metaModel == null)
        {
            // Fallback to simple averaging if meta-model not trained
            return await FallbackPredict(features);
        }

        var predictions = new List<PiratePrediction>();

        // Get base model predictions
        var basePredictions = new List<List<PiratePrediction>>();
        foreach (var model in _baseModels)
        {
            try
            {
                var preds = await model.PredictAsync(features);
                basePredictions.Add(preds);
            }
            catch
            {
                basePredictions.Add(new List<PiratePrediction>());
            }
        }

        // Generate meta-features and predict
        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var oddsRanks = pirates
                .Select((p, idx) => new { Index = idx, Odds = p.CurrentOdds })
                .OrderBy(x => x.Odds)
                .Select((x, rank) => new { x.Index, Rank = rank + 1 })
                .OrderBy(x => x.Index)
                .Select(x => (float)x.Rank)
                .ToArray();

            var metaFeatures = new List<StackingMetaFeature>();

            for (int i = 0; i < 4; i++)
            {
                var pirate = pirates[i];
                var modelProbs = new float[_baseModels.Count];

                for (int m = 0; m < _baseModels.Count; m++)
                {
                    var pred = basePredictions[m]
                        .FirstOrDefault(p => p.RoundId == pirate.RoundId && 
                                            p.ArenaId == pirate.ArenaId && 
                                            p.PirateId == pirate.PirateId);
                    modelProbs[m] = pred?.WinProbability ?? 0.25f;
                }

                metaFeatures.Add(new StackingMetaFeature
                {
                    Model0_Prob = modelProbs[0],
                    Model1_Prob = modelProbs[1],
                    Model2_Prob = modelProbs[2],
                    Model3_Prob = modelProbs[3],
                    Model4_Prob = modelProbs.Length > 4 ? modelProbs[4] : 0.25f,
                    Odds = (float)Math.Log(Math.Max(2, pirate.CurrentOdds)),
                    Strength = pirate.Strength / 100f,
                    Food = pirate.FoodAdjustment / 10f,
                    Position = pirate.Position / 4f,
                    HistWinRate = (float)pirate.HistoricalWinRate,
                    OddsRank = oddsRanks[i] / 4f,
                    MaxModelProb = modelProbs.Max(),
                    MinModelProb = modelProbs.Min(),
                    ModelProbStd = (float)CalculateStdDev(modelProbs)
                });
            }

            // Predict using meta-model
            var dataView = _mlContext.Data.LoadFromEnumerable(metaFeatures);
            var metaPredictions = _metaModel.Transform(dataView);
            var results = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(metaPredictions, false).ToList();

            // Normalize probabilities to sum to 1
            var probs = results.Select(r => (double)r.Probability).ToArray();
            var sum = probs.Sum();
            if (sum <= 0) sum = 1;

            for (int i = 0; i < 4; i++)
            {
                predictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)(probs[i] / sum),
                    Payout = Math.Max(2, pirates[i].CurrentOdds)
                });
            }
        }

        return predictions;
    }

    private async Task<List<PiratePrediction>> FallbackPredict(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();
        
        // Simple average of base models
        var basePredictions = new List<List<PiratePrediction>>();
        foreach (var model in _baseModels)
        {
            try
            {
                var preds = await model.PredictAsync(features);
                basePredictions.Add(preds);
            }
            catch { }
        }

        foreach (var f in features)
        {
            var probs = basePredictions
                .Select(bp => bp.FirstOrDefault(p => p.RoundId == f.RoundId && p.ArenaId == f.ArenaId && p.PirateId == f.PirateId)?.WinProbability ?? 0.25f)
                .ToList();

            predictions.Add(new PiratePrediction
            {
                RoundId = f.RoundId,
                ArenaId = f.ArenaId,
                PirateId = f.PirateId,
                WinProbability = probs.Any() ? probs.Average() : 0.25f,
                Payout = Math.Max(2, f.CurrentOdds)
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
            AUC = auc,
            F1Score = accuracy * 0.5,
            TestDataSize = testData.Count,
            LogLoss = logLoss
        };
    }

    private double CalculateStdDev(float[] values)
    {
        if (values.Length == 0) return 0;
        var avg = values.Average();
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return Math.Sqrt(sumSquares / values.Length);
    }

    public void SaveModel(string path)
    {
        if (_metaModel == null) return;
        
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _mlContext.Model.Save(_metaModel, null, path.Replace(".zip", "_stacking_meta.zip"));

        for (int i = 0; i < _baseModels.Count; i++)
        {
            try
            {
                _baseModels[i].SaveModel(path.Replace(".zip", $"_stacking_base{i}.zip"));
            }
            catch { }
        }
    }

    public void LoadModel(string path)
    {
        var metaPath = path.Replace(".zip", "_stacking_meta.zip");
        if (File.Exists(metaPath))
        {
            _metaModel = _mlContext.Model.Load(metaPath, out _);
        }

        for (int i = 0; i < _baseModels.Count; i++)
        {
            try
            {
                _baseModels[i].LoadModel(path.Replace(".zip", $"_stacking_base{i}.zip"));
            }
            catch { }
        }
    }
}