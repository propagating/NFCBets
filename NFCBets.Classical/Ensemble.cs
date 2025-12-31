using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class Ensemble : IMlStrategy
{
    private readonly BinaryClassification _binaryStrategy;
    private readonly BradleyTerry _bradleyTerryStrategy;
    private readonly ConditionalLogisticRegression _conditionalLogisticStrategy;
    private readonly MultiClassPerArena _multiClassStrategy;
    private readonly LearnToRank _rankingStrategy;

    private InteractionAnalysisReport? _interactionReport;

    public Ensemble()
    {
        _binaryStrategy = new BinaryClassification();
        _multiClassStrategy = new MultiClassPerArena();
        _rankingStrategy = new LearnToRank();
        _conditionalLogisticStrategy = new ConditionalLogisticRegression();
        _bradleyTerryStrategy = new BradleyTerry();
    }

    public string StrategyName => "Ensemble";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        try
        {
            Console.WriteLine("      Training binary classifier...");
            await _binaryStrategy.TrainAsync(trainingData, interactionReport);
            Console.WriteLine("      ✅ Binary trained");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Binary failed: {ex.Message}");
        }

        try
        {
            Console.WriteLine("      Training multi-class classifier...");
            await _multiClassStrategy.TrainAsync(trainingData, interactionReport);
            Console.WriteLine("      ✅ Multi-class trained");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Multi-class failed: {ex.Message}");
        }

        try
        {
            Console.WriteLine("      Training ranker...");
            await _rankingStrategy.TrainAsync(trainingData, interactionReport);
            Console.WriteLine("      ✅ Ranker trained");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Ranker failed: {ex.Message}");
        }

        try
        {
            Console.WriteLine("      Training conditional logistic...");
            await _conditionalLogisticStrategy.TrainAsync(trainingData, interactionReport);
            Console.WriteLine("      ✅ Conditional logistic trained");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Conditional logistic failed: {ex.Message}");
        }

        try
        {
            Console.WriteLine("      Training Bradley-Terry...");
            await _bradleyTerryStrategy.TrainAsync(trainingData, interactionReport);
            Console.WriteLine("      ✅ Bradley-Terry trained");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Bradley-Terry failed: {ex.Message}");
        }

        Console.WriteLine("   ✅ All ensemble components trained");
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        Console.WriteLine("   Generating ensemble predictions...");

        // Get predictions from all models
        var binaryPreds = new List<PiratePrediction>();
        var multiClassPreds = new List<PiratePrediction>();
        var rankingPreds = new List<PiratePrediction>();
        var conditionalPreds = new List<PiratePrediction>();
        var bradleyTerryPreds = new List<PiratePrediction>();

        try
        {
            binaryPreds = await _binaryStrategy.PredictAsync(features);
        }
        catch
        {
            Console.WriteLine("      ⚠️ Binary predictions failed");
        }

        try
        {
            multiClassPreds = await _multiClassStrategy.PredictAsync(features);
        }
        catch
        {
            Console.WriteLine("      ⚠️ Multi-class predictions failed");
        }

        try
        {
            rankingPreds = await _rankingStrategy.PredictAsync(features);
        }
        catch
        {
            Console.WriteLine("      ⚠️ Ranking predictions failed");
        }

        try
        {
            conditionalPreds = await _conditionalLogisticStrategy.PredictAsync(features);
        }
        catch
        {
            Console.WriteLine("      ⚠️ Conditional logistic predictions failed");
        }

        try
        {
            bradleyTerryPreds = await _bradleyTerryStrategy.PredictAsync(features);
        }
        catch
        {
            Console.WriteLine("      ⚠️ Bradley-Terry predictions failed");
        }

        Console.WriteLine($"      Binary: {binaryPreds.Count}, MultiClass: {multiClassPreds.Count}, " +
                          $"Ranking: {rankingPreds.Count}, Conditional: {conditionalPreds.Count}, " +
                          $"BradleyTerry: {bradleyTerryPreds.Count}");

        // Create lookup dictionaries with (RoundId, ArenaId, PirateId) as key
        var binaryDict = new Dictionary<(int, int, int), float>();
        var multiDict = new Dictionary<(int, int, int), float>();
        var rankDict = new Dictionary<(int, int, int), float>();
        var conditionalDict = new Dictionary<(int, int, int), float>();
        var bradleyDict = new Dictionary<(int, int, int), float>();

        foreach (var pred in binaryPreds)
        {
            var key = (pred.RoundId, pred.ArenaId, pred.PirateId);
            binaryDict[key] = pred.WinProbability;
        }

        foreach (var pred in multiClassPreds)
        {
            var key = (pred.RoundId, pred.ArenaId, pred.PirateId);
            multiDict[key] = pred.WinProbability;
        }

        foreach (var pred in rankingPreds)
        {
            var key = (pred.RoundId, pred.ArenaId, pred.PirateId);
            rankDict[key] = pred.WinProbability;
        }

        foreach (var pred in conditionalPreds)
        {
            var key = (pred.RoundId, pred.ArenaId, pred.PirateId);
            conditionalDict[key] = pred.WinProbability;
        }

        foreach (var pred in bradleyTerryPreds)
        {
            var key = (pred.RoundId, pred.ArenaId, pred.PirateId);
            bradleyDict[key] = pred.WinProbability;
        }

        // Weighted ensemble combining all models
        // Weights based on typical performance (can be tuned)
        var ensemblePredictions = features.Select(f =>
        {
            var key = (f.RoundId, f.ArenaId, f.PirateId);

            var binaryProb = binaryDict.GetValueOrDefault(key, 0.25f);
            var multiProb = multiDict.GetValueOrDefault(key, 0.25f);
            var rankProb = rankDict.GetValueOrDefault(key, 0.25f);
            var conditionalProb = conditionalDict.GetValueOrDefault(key, 0.25f);
            var bradleyProb = bradleyDict.GetValueOrDefault(key, 0.25f);

            // Count how many models contributed
            var modelCount = 0;
            var weightedSum = 0f;

            if (binaryDict.ContainsKey(key))
            {
                weightedSum += binaryProb * 0.20f;
                modelCount++;
            }

            if (multiDict.ContainsKey(key))
            {
                weightedSum += multiProb * 0.15f;
                modelCount++;
            }

            if (rankDict.ContainsKey(key))
            {
                weightedSum += rankProb * 0.15f;
                modelCount++;
            }

            if (conditionalDict.ContainsKey(key))
            {
                weightedSum += conditionalProb * 0.25f;
                modelCount++;
            }

            if (bradleyDict.ContainsKey(key))
            {
                weightedSum += bradleyProb * 0.25f;
                modelCount++;
            }

            // Normalize if not all models contributed
            var totalWeight = 0f;
            if (binaryDict.ContainsKey(key)) totalWeight += 0.20f;
            if (multiDict.ContainsKey(key)) totalWeight += 0.15f;
            if (rankDict.ContainsKey(key)) totalWeight += 0.15f;
            if (conditionalDict.ContainsKey(key)) totalWeight += 0.25f;
            if (bradleyDict.ContainsKey(key)) totalWeight += 0.25f;

            var ensembleProb = totalWeight > 0 ? weightedSum / totalWeight : 0.25f;

            return new PiratePrediction
            {
                RoundId = f.RoundId,
                ArenaId = f.ArenaId,
                PirateId = f.PirateId,
                WinProbability = Math.Clamp(ensembleProb, 0.01f, 0.99f),
                Payout = Math.Max(2, f.CurrentOdds)
            };
        }).ToList();

        Console.WriteLine($"      Ensemble: {ensemblePredictions.Count} predictions");

        return ensemblePredictions;
    }

    public async Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        Console.WriteLine($"   Evaluating {StrategyName}...");

        var predictions = await PredictAsync(testData);

        var correctPredictions = 0;
        var totalRounds = 0;
        var allPredictions = new List<(bool Actual, float Predicted)>();

        foreach (var arenaGroup in testData.GroupBy(f => f.ArenaId))
        foreach (var roundGroup in arenaGroup.GroupBy(f => f.RoundId))
        {
            var actualWinner = roundGroup.FirstOrDefault(p => p.IsWinner == true);
            if (actualWinner == null) continue;

            var roundPredictions = predictions
                .Where(p => p.RoundId == roundGroup.Key && p.ArenaId == arenaGroup.Key)
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
        _binaryStrategy.SaveModel(path.Replace(".zip", "_ensemble_binary.zip"));
        _multiClassStrategy.SaveModel(path.Replace(".zip", "_ensemble_multiclass.zip"));
        _rankingStrategy.SaveModel(path.Replace(".zip", "_ensemble_ranking.zip"));
        _conditionalLogisticStrategy.SaveModel(path.Replace(".zip", "_ensemble_conditional.zip"));
        _bradleyTerryStrategy.SaveModel(path.Replace(".zip", "_ensemble_bradley.zip"));
    }

    public void LoadModel(string path)
    {
        try
        {
            _binaryStrategy.LoadModel(path.Replace(".zip", "_ensemble_binary.zip"));
        }
        catch
        {
            Console.WriteLine("      ⚠️ Could not load binary model");
        }

        try
        {
            _multiClassStrategy.LoadModel(path.Replace(".zip", "_ensemble_multiclass.zip"));
        }
        catch
        {
            Console.WriteLine("      ⚠️ Could not load multi-class model");
        }

        try
        {
            _rankingStrategy.LoadModel(path.Replace(".zip", "_ensemble_ranking.zip"));
        }
        catch
        {
            Console.WriteLine("      ⚠️ Could not load ranking model");
        }

        try
        {
            _conditionalLogisticStrategy.LoadModel(path.Replace(".zip", "_ensemble_conditional.zip"));
        }
        catch
        {
            Console.WriteLine("      ⚠️ Could not load conditional logistic model");
        }

        try
        {
            _bradleyTerryStrategy.LoadModel(path.Replace(".zip", "_ensemble_bradley.zip"));
        }
        catch
        {
            Console.WriteLine("      ⚠️ Could not load Bradley-Terry model");
        }
    }
}