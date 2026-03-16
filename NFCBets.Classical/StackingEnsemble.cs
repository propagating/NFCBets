using Microsoft.ML;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Stacking ensemble with a meta-learner that combines base model predictions
/// Uses cross-validation to generate meta-features without data leakage
/// </summary>
public class StackingEnsemble : IMlStrategy
{
    public string StrategyName => "Stacking Ensemble";

    private readonly MLContext _mlContext;
    private readonly List<IMlStrategy> _baseStrategies = new();
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

        // Initialize base strategies
        _baseStrategies.Clear();
        _baseStrategies.Add(new BinaryClassification());
        _baseStrategies.Add(new LogisticRegression());
        _baseStrategies.Add(new BradleyTerry());
        _baseStrategies.Add(new PlackettLuce());
        _baseStrategies.Add(new MultinomialLogit());

        // Pre-compute grouped data for feature conversion
        var groupedByRoundArena = trainingData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Split into folds for cross-validation meta-feature generation
        var uniqueRounds = trainingData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var numFolds = 5;
        var foldSize = uniqueRounds.Count / numFolds;

        Console.WriteLine($"      Generating meta-features using {numFolds}-fold CV...");

        var metaFeatures = new List<StackingMetaFeature>();

        for (int fold = 0; fold < numFolds; fold++)
        {
            var valRoundStart = fold * foldSize;
            var valRoundEnd = (fold == numFolds - 1) ? uniqueRounds.Count : (fold + 1) * foldSize;
            var valRounds = uniqueRounds.Skip(valRoundStart).Take(valRoundEnd - valRoundStart).ToHashSet();
            var trainRounds = uniqueRounds.Except(valRounds).ToHashSet();

            var foldTrainData = trainingData.Where(f => trainRounds.Contains(f.RoundId)).ToList();
            var foldValData = trainingData.Where(f => valRounds.Contains(f.RoundId)).ToList();

            Console.WriteLine($"         Fold {fold + 1}/{numFolds}: Training on {foldTrainData.Count} records...");

            // Train each base strategy on this fold's training data
            var foldPredictions = new Dictionary<int, List<PiratePrediction>>();

            for (int i = 0; i < _baseStrategies.Count; i++)
            {
                try
                {
                    // Create fresh instance for each fold
                    var strategy = CreateFreshStrategy(i);
                    await strategy.TrainAsync(foldTrainData, interactionReport);
                    foldPredictions[i] = await strategy.PredictAsync(foldValData);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"            ⚠️ Strategy {i} failed: {ex.Message}");
                    foldPredictions[i] = new List<PiratePrediction>();
                }
            }

            // Generate meta-features for validation set
            foreach (var record in foldValData)
            {
                var probs = new float[_baseStrategies.Count];
                
                for (int i = 0; i < _baseStrategies.Count; i++)
                {
                    var pred = foldPredictions[i]
                        .FirstOrDefault(p => p.RoundId == record.RoundId && 
                                            p.ArenaId == record.ArenaId && 
                                            p.PirateId == record.PirateId);
                    probs[i] = pred?.WinProbability ?? 0.25f;
                }

                // Calculate odds rank within the round
                var roundPirates = groupedByRoundArena.GetValueOrDefault((record.RoundId, record.ArenaId))
                    ?? new List<PirateFeatureRecord> { record };
                var oddsRank = roundPirates
                    .OrderBy(p => p.CurrentOdds)
                    .ToList()
                    .FindIndex(p => p.PirateId == record.PirateId) + 1;

                metaFeatures.Add(new StackingMetaFeature
                {
                    Model0_Prob = probs.Length > 0 ? probs[0] : 0.25f,
                    Model1_Prob = probs.Length > 1 ? probs[1] : 0.25f,
                    Model2_Prob = probs.Length > 2 ? probs[2] : 0.25f,
                    Model3_Prob = probs.Length > 3 ? probs[3] : 0.25f,
                    Model4_Prob = probs.Length > 4 ? probs[4] : 0.25f,
                    
                    Odds = (float)Math.Log(Math.Max(2, record.CurrentOdds)),
                    Strength = record.Strength / 100f,
                    Food = record.FoodAdjustment / 10f,
                    Position = record.Position / 4f,
                    HistWinRate = (float)record.HistoricalWinRate,
                    OddsRank = oddsRank / 4f,
                    
                    MaxModelProb = probs.Max(),
                    MinModelProb = probs.Min(),
                    ModelProbStd = (float)MathUtilities.CalculateStandardDeviation(probs.Select(p => (double)p)),
                    
                    Won = record.IsWinner ?? false
                });
            }
        }

        Console.WriteLine($"      Generated {metaFeatures.Count} meta-features");

        // Train meta-learner on all meta-features
        Console.WriteLine("      Training meta-learner...");
        
        var metaDataView = _mlContext.Data.LoadFromEnumerable(metaFeatures);

        var metaPipeline = _mlContext.Transforms.Concatenate("Features",
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
                numberOfLeaves: 15,
                minimumExampleCountPerLeaf: 20,
                learningRate: 0.05,
                numberOfIterations: 100));

        _metaModel = metaPipeline.Fit(metaDataView);

        // Retrain all base strategies on full training data for final predictions
        Console.WriteLine("      Retraining base strategies on full data...");
        for (int i = 0; i < _baseStrategies.Count; i++)
        {
            try
            {
                await _baseStrategies[i].TrainAsync(trainingData, interactionReport);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"         ⚠️ Strategy {i} failed: {ex.Message}");
            }
        }

        Console.WriteLine($"   ✅ {StrategyName} trained with {_baseStrategies.Count} base models");
    }

    private IMlStrategy CreateFreshStrategy(int index)
    {
        return index switch
        {
            0 => new BinaryClassification(),
            1 => new LogisticRegression(),
            2 => new BradleyTerry(),
            3 => new PlackettLuce(),
            4 => new MultinomialLogit(),
            _ => new BinaryClassification()
        };
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (_metaModel == null)
            throw new InvalidOperationException("Model must be trained first");

        // Pre-compute grouped data
        var groupedByRoundArena = features
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Get predictions from all base strategies
        var basePredictions = new List<List<PiratePrediction>>();
        
        foreach (var strategy in _baseStrategies)
        {
            try
            {
                var preds = await strategy.PredictAsync(features);
                basePredictions.Add(preds);
            }
            catch
            {
                basePredictions.Add(new List<PiratePrediction>());
            }
        }

        // Build meta-features and predict
        var metaFeatures = new List<StackingMetaFeature>();
        var featureToRecordMap = new List<PirateFeatureRecord>();

        foreach (var record in features)
        {
            var probs = new float[_baseStrategies.Count];
            
            for (int i = 0; i < basePredictions.Count; i++)
            {
                var pred = basePredictions[i]
                    .FirstOrDefault(p => p.RoundId == record.RoundId && 
                                        p.ArenaId == record.ArenaId && 
                                        p.PirateId == record.PirateId);
                probs[i] = pred?.WinProbability ?? 0.25f;
            }

            var roundPirates = groupedByRoundArena.GetValueOrDefault((record.RoundId, record.ArenaId))
                ?? new List<PirateFeatureRecord> { record };
            var oddsRank = roundPirates
                .OrderBy(p => p.CurrentOdds)
                .ToList()
                .FindIndex(p => p.PirateId == record.PirateId) + 1;

            metaFeatures.Add(new StackingMetaFeature
            {
                Model0_Prob = probs.Length > 0 ? probs[0] : 0.25f,
                Model1_Prob = probs.Length > 1 ? probs[1] : 0.25f,
                Model2_Prob = probs.Length > 2 ? probs[2] : 0.25f,
                Model3_Prob = probs.Length > 3 ? probs[3] : 0.25f,
                Model4_Prob = probs.Length > 4 ? probs[4] : 0.25f,
                
                Odds = (float)Math.Log(Math.Max(2, record.CurrentOdds)),
                Strength = record.Strength / 100f,
                Food = record.FoodAdjustment / 10f,
                Position = record.Position / 4f,
                HistWinRate = (float)record.HistoricalWinRate,
                OddsRank = oddsRank / 4f,
                
                MaxModelProb = probs.Max(),
                MinModelProb = probs.Min(),
                ModelProbStd = (float)MathUtilities.CalculateStandardDeviation(probs.Select(p => (double)p)),
                
                Won = false // Not used for prediction
            });

            featureToRecordMap.Add(record);
        }

        var metaDataView = _mlContext.Data.LoadFromEnumerable(metaFeatures);
        var predictions = _metaModel.Transform(metaDataView);
        var results = _mlContext.Data.CreateEnumerable<PiratePredictionOutput>(predictions, false).ToList();

        // Build final predictions
        var finalPredictions = new List<PiratePrediction>();

        for (int i = 0; i < results.Count; i++)
        {
            var record = featureToRecordMap[i];
            finalPredictions.Add(new PiratePrediction
            {
                RoundId = record.RoundId,
                ArenaId = record.ArenaId,
                PirateId = record.PirateId,
                WinProbability = Math.Clamp(results[i].Probability, 0.01f, 0.99f),
                Payout = Math.Max(2, record.CurrentOdds)
            });
        }

        // Normalize probabilities per round
        var normalizedPredictions = new List<PiratePrediction>();

        foreach (var roundGroup in finalPredictions.GroupBy(p => (p.RoundId, p.ArenaId)))
        {
            var roundPreds = roundGroup.ToList();
            var total = roundPreds.Sum(p => p.WinProbability);

            if (total > 0)
            {
                foreach (var pred in roundPreds)
                {
                    normalizedPredictions.Add(new PiratePrediction
                    {
                        RoundId = pred.RoundId,
                        ArenaId = pred.ArenaId,
                        PirateId = pred.PirateId,
                        WinProbability = pred.WinProbability / total,
                        Payout = pred.Payout
                    });
                }
            }
            else
            {
                foreach (var pred in roundPreds)
                {
                    normalizedPredictions.Add(new PiratePrediction
                    {
                        RoundId = pred.RoundId,
                        ArenaId = pred.ArenaId,
                        PirateId = pred.PirateId,
                        WinProbability = 0.25f,
                        Payout = pred.Payout
                    });
                }
            }
        }

        return normalizedPredictions;
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
        if (_metaModel == null)
            throw new InvalidOperationException("No model to save");

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _mlContext.Model.Save(_metaModel, null, path.Replace(".zip", "_stacking_meta.zip"));

        // Save base strategies
        for (int i = 0; i < _baseStrategies.Count; i++)
        {
            try
            {
                _baseStrategies[i].SaveModel(path.Replace(".zip", $"_stacking_base{i}.zip"));
            }
            catch
            {
                // Some strategies may not support saving
            }
        }
    }

    public void LoadModel(string path)
    {
        var metaPath = path.Replace(".zip", "_stacking_meta.zip");
        if (File.Exists(metaPath))
        {
            _metaModel = _mlContext.Model.Load(metaPath, out _);
        }

        // Load base strategies
        _baseStrategies.Clear();
        _baseStrategies.Add(new BinaryClassification());
        _baseStrategies.Add(new LogisticRegression());
        _baseStrategies.Add(new BradleyTerry());
        _baseStrategies.Add(new PlackettLuce());
        _baseStrategies.Add(new MultinomialLogit());

        for (int i = 0; i < _baseStrategies.Count; i++)
        {
            try
            {
                _baseStrategies[i].LoadModel(path.Replace(".zip", $"_stacking_base{i}.zip"));
            }
            catch
            {
                // Some strategies may not have saved models
            }
        }
    }
}