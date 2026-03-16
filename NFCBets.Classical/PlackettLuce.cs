using System.Text.Json;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Plackett-Luce ranking model
/// Models full ranking of all pirates, not just the winner
/// P(ranking) = product of P(next best | remaining)
/// </summary>
public class PlackettLuce : IMlStrategy
{
    private double[]? _coefficients;
    private readonly int _numFeatures = 10;
    private double[]? _featureMeans;
    private double[]? _featureStds;
    private InteractionAnalysisReport? _interactionReport;

    public string StrategyName => "Plackett-Luce Ranking Model";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        // Pre-compute grouped data
        var groupedByRoundArena = trainingData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Convert to ranking format (we'll use position as rank proxy, winner is rank 1)
        var rankings = ConvertToRankings(trainingData, groupedByRoundArena);

        Console.WriteLine($"      Training on {rankings.Count} competitions...");

        if (!rankings.Any())
        {
            Console.WriteLine("      ⚠️ No valid rankings found");
            _coefficients = new double[_numFeatures];
            return;
        }

        // Normalize features first
        NormalizeFeatures(rankings);

        // Debug: Check feature variance
        PrintFeatureStatistics(rankings);

        // Train with gradient descent
        _coefficients = TrainPlackettLuce(rankings);

        // Print final coefficients
        PrintCoefficients();

        Console.WriteLine($"   ✅ {StrategyName} trained");
    }

    private void NormalizeFeatures(List<RankingInstance> rankings)
    {
        if (!rankings.Any() || !rankings[0].Items.Any())
            return;

        var numFeatures = rankings[0].Items[0].Features.Length;
        _featureMeans = new double[numFeatures];
        _featureStds = new double[numFeatures];

        // Calculate means
        var counts = new int[numFeatures];
        foreach (var ranking in rankings)
        {
            foreach (var item in ranking.Items)
            {
                for (int i = 0; i < numFeatures; i++)
                {
                    var val = item.Features[i];
                    if (!double.IsNaN(val) && !double.IsInfinity(val))
                    {
                        _featureMeans[i] += val;
                        counts[i]++;
                    }
                }
            }
        }

        for (int i = 0; i < numFeatures; i++)
        {
            _featureMeans[i] = counts[i] > 0 ? _featureMeans[i] / counts[i] : 0;
        }

        // Calculate standard deviations
        foreach (var ranking in rankings)
        {
            foreach (var item in ranking.Items)
            {
                for (int i = 0; i < numFeatures; i++)
                {
                    var val = item.Features[i];
                    if (!double.IsNaN(val) && !double.IsInfinity(val))
                    {
                        _featureStds[i] += Math.Pow(val - _featureMeans[i], 2);
                    }
                }
            }
        }

        for (int i = 0; i < numFeatures; i++)
        {
            _featureStds[i] = counts[i] > 1 ? Math.Sqrt(_featureStds[i] / (counts[i] - 1)) : 1;
            if (_featureStds[i] < 1e-6) _featureStds[i] = 1; // Prevent division by zero
        }

        // Apply normalization
        foreach (var ranking in rankings)
        {
            foreach (var item in ranking.Items)
            {
                for (int i = 0; i < numFeatures; i++)
                {
                    var val = item.Features[i];
                    if (!double.IsNaN(val) && !double.IsInfinity(val))
                    {
                        item.Features[i] = (val - _featureMeans[i]) / _featureStds[i];
                        item.Features[i] = Math.Clamp(item.Features[i], -5, 5);
                    }
                    else
                    {
                        item.Features[i] = 0;
                    }
                }
            }
        }
    }

    private void PrintFeatureStatistics(List<RankingInstance> rankings)
    {
        if (!rankings.Any() || !rankings[0].Items.Any())
            return;

        var numFeatures = rankings[0].Items[0].Features.Length;
        var featureNames = new[] 
        { 
            "ImpliedProb", "Strength", "Food", "Position", "HistWinRate",
            "ArenaWinRate", "RecentWinRate", "RivalWinRate", "Bonus", "Penalty"
        };

        Console.WriteLine("      Feature statistics (after normalization):");
        
        for (int i = 0; i < Math.Min(numFeatures, featureNames.Length); i++)
        {
            var values = rankings
                .SelectMany(r => r.Items)
                .Select(item => item.Features[i])
                .Where(v => !double.IsNaN(v))
                .ToList();

            if (values.Any())
            {
                var min = values.Min();
                var max = values.Max();
                var mean = values.Average();
                var variance = values.Average(v => Math.Pow(v - mean, 2));

                Console.WriteLine($"         {featureNames[i],-15}: Mean={mean:F3}, Var={variance:F3}, Range=[{min:F2}, {max:F2}]");
            }
        }

        // Check winner vs loser feature differences
        Console.WriteLine("      Winner vs Loser feature differences:");
        for (int i = 0; i < Math.Min(numFeatures, featureNames.Length); i++)
        {
            var winnerVals = rankings
                .SelectMany(r => r.Items.Where(item => item.Rank == 1))
                .Select(item => item.Features[i])
                .Where(v => !double.IsNaN(v))
                .ToList();

            var loserVals = rankings
                .SelectMany(r => r.Items.Where(item => item.Rank > 1))
                .Select(item => item.Features[i])
                .Where(v => !double.IsNaN(v))
                .ToList();

            if (winnerVals.Any() && loserVals.Any())
            {
                var winnerMean = winnerVals.Average();
                var loserMean = loserVals.Average();
                var diff = winnerMean - loserMean;

                if (Math.Abs(diff) > 0.1)
                {
                    Console.WriteLine($"         {featureNames[i],-15}: Winner={winnerMean:F3}, Loser={loserMean:F3}, Diff={diff:+0.000;-0.000}");
                }
            }
        }
    }

    private void PrintCoefficients()
    {
        if (_coefficients == null) return;

        var featureNames = new[] 
        { 
            "ImpliedProb", "Strength", "Food", "Position", "HistWinRate",
            "ArenaWinRate", "RecentWinRate", "RivalWinRate", "Bonus", "Penalty"
        };

        Console.WriteLine("      Learned coefficients:");
        for (int i = 0; i < Math.Min(_coefficients.Length, featureNames.Length); i++)
        {
            var coef = _coefficients[i];
            var sign = coef >= 0 ? "+" : "";
            Console.WriteLine($"         {featureNames[i],-15}: {sign}{coef:F4}");
        }
    }

    private double[] TrainPlackettLuce(List<RankingInstance> rankings)
    {
        var numFeatures = rankings[0].Items[0].Features.Length;
        
        // Initialize with small random values instead of zeros
        var random = new Random(42);
        var coefficients = new double[numFeatures];
        for (int i = 0; i < numFeatures; i++)
        {
            coefficients[i] = (random.NextDouble() - 0.5) * 0.1;
        }

        var learningRate = 0.1;  // Start higher
        var minLearningRate = 1e-5;
        var iterations = 500;
        var patience = 30;
        var bestLogLik = double.NegativeInfinity;
        var noImprovementCount = 0;
        var bestCoefficients = (double[])coefficients.Clone();

        for (var iter = 0; iter < iterations; iter++)
        {
            var gradients = new double[numFeatures];
            var logLikelihood = 0.0;
            var validRankings = 0;

            foreach (var ranking in rankings)
            {
                var items = ranking.Items.OrderBy(i => i.Rank).ToList();
                if (items.Count < 2) continue;

                // Plackett-Luce: product over ranks
                // P(ranking) = prod_i [ exp(v_i) / sum_{j>=i} exp(v_j) ]
                for (var rank = 0; rank < items.Count - 1; rank++)
                {
                    var chosenItem = items[rank];
                    var remainingItems = items.Skip(rank).ToList();

                    // Calculate utilities for remaining items
                    var utilities = new double[remainingItems.Count];
                    var maxUtility = double.NegativeInfinity;

                    for (var j = 0; j < remainingItems.Count; j++)
                    {
                        utilities[j] = CalculateUtility(remainingItems[j].Features, coefficients);
                        maxUtility = Math.Max(maxUtility, utilities[j]);
                    }

                    // Softmax with stability
                    var expUtilities = new double[remainingItems.Count];
                    var sumExp = 0.0;

                    for (var j = 0; j < remainingItems.Count; j++)
                    {
                        var shifted = utilities[j] - maxUtility;
                        shifted = Math.Clamp(shifted, -500, 500);
                        expUtilities[j] = Math.Exp(shifted);
                        sumExp += expUtilities[j];
                    }

                    if (sumExp < 1e-300) sumExp = 1e-300;

                    // Log-likelihood contribution
                    var chosenProb = expUtilities[0] / sumExp; // chosen is first in remaining
                    logLikelihood += Math.Log(Math.Max(chosenProb, 1e-15));

                    // Gradient: feature of chosen - expected feature
                    for (var f = 0; f < numFeatures; f++)
                    {
                        var chosenFeature = chosenItem.Features[f];
                        var expectedFeature = 0.0;

                        for (var j = 0; j < remainingItems.Count; j++)
                        {
                            var prob = expUtilities[j] / sumExp;
                            expectedFeature += prob * remainingItems[j].Features[f];
                        }

                        var grad = chosenFeature - expectedFeature;
                        if (!double.IsNaN(grad) && !double.IsInfinity(grad))
                        {
                            gradients[f] += grad;
                        }
                    }
                }

                validRankings++;
            }

            if (validRankings == 0)
            {
                Console.WriteLine("         ⚠️ No valid rankings");
                break;
            }

            // Average gradients
            for (var f = 0; f < numFeatures; f++)
            {
                gradients[f] /= validRankings;
            }

            // Check gradient magnitude
            var gradientNorm = Math.Sqrt(gradients.Sum(g => g * g));

            // Gradient clipping
            var maxGradNorm = 5.0;
            if (gradientNorm > maxGradNorm)
            {
                var scale = maxGradNorm / gradientNorm;
                for (var f = 0; f < numFeatures; f++)
                {
                    gradients[f] *= scale;
                }
                gradientNorm = maxGradNorm;
            }

            // Update coefficients
            for (var f = 0; f < numFeatures; f++)
            {
                var update = learningRate * gradients[f];
                if (!double.IsNaN(update) && !double.IsInfinity(update))
                {
                    coefficients[f] += update;
                    coefficients[f] = Math.Clamp(coefficients[f], -10, 10);
                }
            }

            var avgLogLik = logLikelihood / validRankings;

            // Early stopping
            if (avgLogLik > bestLogLik + 1e-6)
            {
                bestLogLik = avgLogLik;
                bestCoefficients = (double[])coefficients.Clone();
                noImprovementCount = 0;
            }
            else
            {
                noImprovementCount++;
                if (noImprovementCount >= patience)
                {
                    learningRate *= 0.5;
                    noImprovementCount = 0;

                    if (learningRate < minLearningRate)
                    {
                        Console.WriteLine($"         Converged at iteration {iter + 1}");
                        break;
                    }
                }
            }

            if ((iter + 1) % 100 == 0)
            {
                Console.WriteLine($"         Iteration {iter + 1}, AvgLogLik: {avgLogLik:F4}, GradNorm: {gradientNorm:F4}, LR: {learningRate:F5}");
            }
        }

        return bestCoefficients;
    }

    private double CalculateUtility(double[] features, double[] coefficients)
    {
        var utility = 0.0;
        for (var i = 0; i < Math.Min(features.Length, coefficients.Length); i++)
        {
            var term = coefficients[i] * features[i];
            if (!double.IsNaN(term) && !double.IsInfinity(term))
            {
                utility += term;
            }
        }
        return Math.Clamp(utility, -500, 500);
    }

    private List<RankingInstance> ConvertToRankings(List<PirateFeatureRecord> data,
        Dictionary<(int RoundId, int ArenaId), List<PirateFeatureRecord>> groupedByRoundArena)
    {
        var rankings = new List<RankingInstance>();

        foreach (var roundGroup in data.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.ToList();
            if (pirates.Count != 4) continue;

            // Find winner
            var winner = pirates.FirstOrDefault(p => p.IsWinner == true);
            if (winner == null) continue;

            var items = new List<RankedItem>();

            foreach (var pirate in pirates)
            {
                var mlFeature = FeatureConversionHelper.ConvertSingle(pirate, groupedByRoundArena, _interactionReport);

                // Winner gets rank 1, others get rank 2 (we only know 1st place)
                var rank = pirate.PirateId == winner.PirateId ? 1 : 2;

                var features = new double[]
                {
                    mlFeature.ImpliedProbability,
                    mlFeature.Strength / 100.0f,
                    mlFeature.FoodAdjustment / 10.0f,
                    (4 - mlFeature.Position) / 3.0f, // Invert so higher = better
                    mlFeature.HistoricalWinRate,
                    mlFeature.ArenaWinRate,
                    mlFeature.RecentWinRate,
                    mlFeature.WinRateVsCurrentRivals,
                    InteractionCalculator.GetTotalBonus(mlFeature),
                    -InteractionCalculator.GetTotalPenalty(mlFeature) // Negate so positive = good
                };

                items.Add(new RankedItem
                {
                    PirateId = pirate.PirateId,
                    Features = features,
                    Rank = rank
                });
            }

            rankings.Add(new RankingInstance
            {
                RoundId = roundGroup.Key.RoundId,
                ArenaId = roundGroup.Key.ArenaId,
                Items = items
            });
        }

        return rankings;
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        if (_coefficients == null)
            throw new InvalidOperationException("Model not trained");

        var predictions = new List<PiratePrediction>();

        var groupedByRoundArena = features
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.ToList();
            if (pirates.Count != 4)
            {
                // Fallback for incomplete rounds
                foreach (var p in pirates)
                {
                    predictions.Add(new PiratePrediction
                    {
                        RoundId = p.RoundId,
                        ArenaId = p.ArenaId,
                        PirateId = p.PirateId,
                        WinProbability = 1f / pirates.Count,
                        Payout = Math.Max(2, p.CurrentOdds)
                    });
                }
                continue;
            }

            // Calculate utilities
            var utilities = new double[4];
            var maxUtility = double.NegativeInfinity;

            for (var i = 0; i < 4; i++)
            {
                var pirate = pirates[i];
                var mlFeature = FeatureConversionHelper.ConvertSingle(pirate, groupedByRoundArena, _interactionReport);

                var pirateFeatures = new double[]
                {
                    mlFeature.ImpliedProbability,
                    mlFeature.Strength / 100.0f,
                    mlFeature.FoodAdjustment / 10.0f,
                    (4 - mlFeature.Position) / 3.0f,
                    mlFeature.HistoricalWinRate,
                    mlFeature.ArenaWinRate,
                    mlFeature.RecentWinRate,
                    mlFeature.WinRateVsCurrentRivals,
                    InteractionCalculator.GetTotalBonus(mlFeature),
                    -InteractionCalculator.GetTotalPenalty(mlFeature)
                };

                // Apply normalization if we have it
                if (_featureMeans != null && _featureStds != null)
                {
                    for (int f = 0; f < pirateFeatures.Length; f++)
                    {
                        pirateFeatures[f] = (pirateFeatures[f] - _featureMeans[f]) / _featureStds[f];
                        pirateFeatures[f] = Math.Clamp(pirateFeatures[f], -5, 5);
                    }
                }

                utilities[i] = CalculateUtility(pirateFeatures, _coefficients);
                maxUtility = Math.Max(maxUtility, utilities[i]);
            }

            // Softmax
            var expUtilities = new double[4];
            var sumExp = 0.0;

            for (var i = 0; i < 4; i++)
            {
                var shifted = utilities[i] - maxUtility;
                shifted = Math.Clamp(shifted, -500, 500);
                expUtilities[i] = Math.Exp(shifted);
                sumExp += expUtilities[i];
            }

            if (sumExp < 1e-300) sumExp = 1e-300;

            // Create predictions
            for (var i = 0; i < 4; i++)
            {
                var prob = expUtilities[i] / sumExp;
                if (double.IsNaN(prob) || double.IsInfinity(prob))
                    prob = 0.25;

                predictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)Math.Clamp(prob, 0.01, 0.99),
                    Payout = Math.Max(2, pirates[i].CurrentOdds)
                });
            }
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
        var data = new PlackettLuceModelData
        {
            Coefficients = _coefficients?.ToList(),
            FeatureMeans = _featureMeans?.ToList(),
            FeatureStds = _featureStds?.ToList()
        };

        var json = JsonSerializer.Serialize(data,
            new JsonSerializerOptions { WriteIndented = true });

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path.Replace(".zip", "_plackettluce.json"), json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_plackettluce.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        var data = JsonSerializer.Deserialize<PlackettLuceModelData>(json);

        if (data == null) return;

        _coefficients = data.Coefficients?.ToArray();
        _featureMeans = data.FeatureMeans?.ToArray();
        _featureStds = data.FeatureStds?.ToArray();
    }

    private class RankingInstance
    {
        public int RoundId { get; set; }
        public int ArenaId { get; set; }
        public List<RankedItem> Items { get; set; } = new();
    }

    private class RankedItem
    {
        public int PirateId { get; set; }
        public double[] Features { get; set; } = Array.Empty<double>();
        public int Rank { get; set; }
    }
}

internal class PlackettLuceModelData
{
    public List<double>? Coefficients { get; set; }
    public List<double>? FeatureMeans { get; set; }
    public List<double>? FeatureStds { get; set; }
}