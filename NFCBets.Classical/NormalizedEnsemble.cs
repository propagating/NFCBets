using System.Text.Json;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Ensemble that normalizes probabilities per round to sum to 1
/// Addresses the independence assumption violation in binary classifiers
/// </summary>
public class NormalizedEnsemble : IMlStrategy
{
    public string StrategyName => "Normalized Ensemble";
    
    private readonly List<IMlStrategy> _baseStrategies = new();
    private readonly Dictionary<string, double> _strategyWeights = new();
    private InteractionAnalysisReport? _interactionReport;
    
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

        // Split data for weight optimization
        var uniqueRounds = trainingData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var splitIndex = (int)(uniqueRounds.Count * 0.8);
        var trainRounds = uniqueRounds.Take(splitIndex).ToHashSet();
        var valRounds = uniqueRounds.Skip(splitIndex).ToHashSet();
        
        var trainData = trainingData.Where(f => trainRounds.Contains(f.RoundId)).ToList();
        var valData = trainingData.Where(f => valRounds.Contains(f.RoundId)).ToList();

        // Train each base strategy
        foreach (var strategy in _baseStrategies)
        {
            try
            {
                Console.WriteLine($"      Training {strategy.StrategyName}...");
                await strategy.TrainAsync(trainData, interactionReport);
                Console.WriteLine($"         ✅ {strategy.StrategyName} trained");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"         ❌ {strategy.StrategyName} failed: {ex.Message}");
            }
        }

        // Calculate weights based on validation performance
        await CalculateStrategyWeights(valData);
        
        Console.WriteLine($"   ✅ Trained {_baseStrategies.Count} base strategies with optimized weights");
    }

    private async Task CalculateStrategyWeights(List<PirateFeatureRecord> valData)
    {
        Console.WriteLine("      Calculating strategy weights...");
        
        var performances = new Dictionary<string, double>();
        
        foreach (var strategy in _baseStrategies)
        {
            try
            {
                var eval = await strategy.EvaluateAsync(valData);
                performances[strategy.StrategyName] = eval.Auc;
                Console.WriteLine($"         {strategy.StrategyName}: AUC = {eval.Auc:F4}");
            }
            catch
            {
                performances[strategy.StrategyName] = 0.5; // Default to random
            }
        }

        // Convert AUC to weights (higher AUC = higher weight)
        var totalAuc = performances.Values.Sum();
        foreach (var kvp in performances)
        {
            _strategyWeights[kvp.Key] = totalAuc > 0 ? kvp.Value / totalAuc : 1.0 / _baseStrategies.Count;
        }

        Console.WriteLine("      Strategy weights:");
        foreach (var kvp in _strategyWeights.OrderByDescending(k => k.Value))
        {
            Console.WriteLine($"         {kvp.Key}: {kvp.Value:P1}");
        }
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        // Get predictions from all strategies
        var allPredictions = new Dictionary<string, List<PiratePrediction>>();
        
        foreach (var strategy in _baseStrategies)
        {
            try
            {
                var preds = await strategy.PredictAsync(features);
                allPredictions[strategy.StrategyName] = preds;
            }
            catch
            {
                // Skip failed strategies
            }
        }

        if (!allPredictions.Any())
            throw new InvalidOperationException("All strategies failed");

        // Combine predictions with weighted average
        var combinedPredictions = new List<PiratePrediction>();

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var combinedProbs = new double[4];

            foreach (var (strategyName, preds) in allPredictions)
            {
                var roundPreds = preds
                    .Where(p => p.RoundId == roundGroup.Key.RoundId && p.ArenaId == roundGroup.Key.ArenaId)
                    .OrderBy(p => pirates.FindIndex(pr => pr.PirateId == p.PirateId))
                    .ToList();

                if (roundPreds.Count != 4) continue;

                var weight = _strategyWeights.GetValueOrDefault(strategyName, 1.0 / _baseStrategies.Count);

                for (int i = 0; i < 4; i++)
                {
                    combinedProbs[i] += roundPreds[i].WinProbability * weight;
                }
            }

// Normalize to sum to 1
            var total = combinedProbs.Sum();
            if (total > 0)
            {
                for (int i = 0; i < 4; i++)
                {
                    combinedProbs[i] /= total;
                }
            }
            else
            {
                combinedProbs = new[] { 0.25, 0.25, 0.25, 0.25 };
            }

            for (int i = 0; i < 4; i++)
            {
                combinedPredictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)Math.Clamp(combinedProbs[i], 0.01, 0.99),
                    Payout = Math.Max(2, pirates[i].CurrentOdds)
                });
            }
        }

        return combinedPredictions;
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
        // Save weights
        var data = new NormalizedEnsembleModelData
        {
            StrategyWeights = new Dictionary<string, double>(_strategyWeights)
        };

        var json = JsonSerializer.Serialize(data,
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path.Replace(".zip", "_normalized_ensemble.json"), json);

        // Save each base strategy
        for (int i = 0; i < _baseStrategies.Count; i++)
        {
            try
            {
                _baseStrategies[i].SaveModel(path.Replace(".zip", $"_normalized_base{i}.zip"));
            }
            catch
            {
                // Some strategies may not support saving
            }
        }
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_normalized_ensemble.json");
        if (File.Exists(jsonPath))
        {
            var json = File.ReadAllText(jsonPath);
            var data = JsonSerializer.Deserialize<NormalizedEnsembleModelData>(json);
            if (data != null)
            {
                _strategyWeights.Clear();
                foreach (var kvp in data.StrategyWeights)
                {
                    _strategyWeights[kvp.Key] = kvp.Value;
                }
            }
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
                _baseStrategies[i].LoadModel(path.Replace(".zip", $"_normalized_base{i}.zip"));
            }
            catch
            {
                // Some strategies may not have saved models
            }
        }
    }
}

internal class NormalizedEnsembleModelData
{
    public Dictionary<string, double> StrategyWeights { get; set; } = new();
}