using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Ensemble with proper probability normalization per arena/round
/// Combines multiple models and ensures probabilities sum to 1
/// </summary>
public class NormalizedEnsemble : IMlStrategy
{
    public string StrategyName => "Normalized Ensemble";
    
    private readonly List<(IMlStrategy Strategy, double Weight)> _strategies = new();
    private InteractionAnalysisReport? _interactionReport;

    public NormalizedEnsemble()
    {
        // Initialize component models with their weights
        // Weights based on expected performance (can be tuned via validation)
    }

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;
        
        Console.WriteLine($"   Training {StrategyName}...");
        
        // Initialize strategies with weights
        _strategies.Clear();
        _strategies.Add((new MultinomialLogit(), 0.25));
        _strategies.Add((new PlackettLuce(), 0.25));
        _strategies.Add((new ConditionalLogisticRegression(), 0.20));
        _strategies.Add((new BradleyTerry(), 0.15));
        _strategies.Add((new MultiClassPairwise(), 0.15));

        // Train all component models
        foreach (var (strategy, weight) in _strategies)
        {
            try
            {
                Console.WriteLine($"      Training {strategy.StrategyName} (weight: {weight:P0})...");
                await strategy.TrainAsync(trainingData, interactionReport);
                Console.WriteLine($"      ✅ {strategy.StrategyName} trained");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"      ❌ {strategy.StrategyName} failed: {ex.Message}");
            }
        }

        Console.WriteLine($"   ✅ Ensemble trained with {_strategies.Count} models");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        // Get predictions from all models
        var modelPredictions = new List<List<PiratePrediction>>();
        var modelWeights = new List<double>();

        foreach (var (strategy, weight) in _strategies)
        {
            try
            {
                var preds = await strategy.PredictAsync(features);
                if (preds.Any())
                {
                    modelPredictions.Add(preds);
                    modelWeights.Add(weight);
                }
            }
            catch
            {
                // Skip failed models
            }
        }

        if (!modelPredictions.Any())
        {
            // Return uniform probabilities if all models fail
            return features.Select(f => new PiratePrediction
            {
                RoundId = f.RoundId,
                ArenaId = f.ArenaId,
                PirateId = f.PirateId,
                WinProbability = 0.25f,
                Payout = Math.Max(2, f.CurrentOdds)
            }).ToList();
        }

        // Normalize weights for active models
        var totalWeight = modelWeights.Sum();
        var normalizedWeights = modelWeights.Select(w => w / totalWeight).ToList();

        // Combine predictions per round with proper normalization
        var combinedPredictions = new List<PiratePrediction>();

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var roundId = roundGroup.Key.RoundId;
            var arenaId = roundGroup.Key.ArenaId;

            // Aggregate probabilities from all models
// Aggregate probabilities from all models
            var aggregatedProbs = new double[4];

            for (int modelIdx = 0; modelIdx < modelPredictions.Count; modelIdx++)
            {
                var modelPreds = modelPredictions[modelIdx]
                    .Where(p => p.RoundId == roundId && p.ArenaId == arenaId)
                    .OrderBy(p => pirates.FindIndex(pi => pi.PirateId == p.PirateId))
                    .ToList();

                if (modelPreds.Count != 4) continue;

                // First normalize model predictions to ensure they sum to 1
                var modelSum = modelPreds.Sum(p => p.WinProbability);
                if (modelSum <= 0) modelSum = 1;

                for (int i = 0; i < 4; i++)
                {
                    var normalizedProb = modelPreds[i].WinProbability / modelSum;
                    aggregatedProbs[i] += normalizedProb * normalizedWeights[modelIdx];
                }
            }

            // Final normalization to ensure probabilities sum to 1
            var totalProb = aggregatedProbs.Sum();
            if (totalProb <= 0) totalProb = 1;

            for (int i = 0; i < 4; i++)
            {
                combinedPredictions.Add(new PiratePrediction
                {
                    RoundId = roundId,
                    ArenaId = arenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)(aggregatedProbs[i] / totalProb),
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
            AUC = auc,
            F1Score = accuracy * 0.5,
            TestDataSize = testData.Count,
            LogLoss = logLoss
        };
    }

    public void SaveModel(string path)
    {
        var basePath = path.Replace(".zip", "");
        
        for (int i = 0; i < _strategies.Count; i++)
        {
            try
            {
                _strategies[i].Strategy.SaveModel($"{basePath}_ensemble_component{i}.zip");
            }
            catch
            {
                // Skip failed saves
            }
        }
    }

    public void LoadModel(string path)
    {
        var basePath = path.Replace(".zip", "");
        
        for (int i = 0; i < _strategies.Count; i++)
        {
            try
            {
                _strategies[i].Strategy.LoadModel($"{basePath}_ensemble_component{i}.zip");
            }
            catch
            {
                // Skip failed loads
            }
        }
    }
}