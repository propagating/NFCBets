using System.Text.Json;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Bradley-Terry model for pairwise comparisons
/// Models P(i beats j) = exp(s_i) / (exp(s_i) + exp(s_j))
/// where s_i is the "strength" parameter for competitor i
/// Extended with features: s_i = beta * x_i
/// </summary>
public class BradleyTerry : IMlStrategy
{
    private double[]? _coefficients;
    private readonly int _numFeatures = 10;
    private double[]? _featureMeans;
    private double[]? _featureStds;
    private InteractionAnalysisReport? _interactionReport;

    public string StrategyName => "Bradley-Terry Pairwise Model";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        // Pre-compute grouped data
        var groupedByRoundArena = trainingData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Convert to pairwise comparisons
        var comparisons = ConvertToPairwiseComparisons(trainingData, groupedByRoundArena);

        Console.WriteLine($"      Generated {comparisons.Count} pairwise comparisons...");

        if (!comparisons.Any())
        {
            Console.WriteLine("      ⚠️ No valid comparisons found");
            _coefficients = new double[_numFeatures];
            return;
        }

        // Normalize features first
        NormalizeFeatures(comparisons);

        // Debug: Check feature variance and discriminative power
        PrintFeatureStatistics(comparisons);

        // Train with gradient descent
        _coefficients = TrainBradleyTerry(comparisons);

        // Print final coefficients
        PrintCoefficients();

        Console.WriteLine($"   ✅ {StrategyName} trained");
    }

    private void NormalizeFeatures(List<PairwiseComparison> comparisons)
    {
        if (!comparisons.Any())
            return;

        var numFeatures = comparisons[0].WinnerFeatures.Length;
        _featureMeans = new double[numFeatures];
        _featureStds = new double[numFeatures];

        // Collect all feature values
        var allValues = new List<double>[numFeatures];
        for (int i = 0; i < numFeatures; i++)
        {
            allValues[i] = new List<double>();
        }

        foreach (var comp in comparisons)
        {
            for (int i = 0; i < numFeatures; i++)
            {
                var winnerVal = comp.WinnerFeatures[i];
                var loserVal = comp.LoserFeatures[i];

                if (!double.IsNaN(winnerVal) && !double.IsInfinity(winnerVal))
                    allValues[i].Add(winnerVal);
                if (!double.IsNaN(loserVal) && !double.IsInfinity(loserVal))
                    allValues[i].Add(loserVal);
            }
        }

        // Calculate means and stds
        for (int i = 0; i < numFeatures; i++)
        {
            if (allValues[i].Any())
            {
                _featureMeans[i] = allValues[i].Average();
                var variance = allValues[i].Average(v => Math.Pow(v - _featureMeans[i], 2));
                _featureStds[i] = Math.Sqrt(variance);
                if (_featureStds[i] < 1e-6) _featureStds[i] = 1;
            }
            else
            {
                _featureMeans[i] = 0;
                _featureStds[i] = 1;
            }
        }

        // Apply normalization
        foreach (var comp in comparisons)
        {
            for (int i = 0; i < numFeatures; i++)
            {
                // Normalize winner features
                if (!double.IsNaN(comp.WinnerFeatures[i]) && !double.IsInfinity(comp.WinnerFeatures[i]))
                {
                    comp.WinnerFeatures[i] = (comp.WinnerFeatures[i] - _featureMeans[i]) / _featureStds[i];
                    comp.WinnerFeatures[i] = Math.Clamp(comp.WinnerFeatures[i], -5, 5);
                }
                else
                {
                    comp.WinnerFeatures[i] = 0;
                }

                // Normalize loser features
                if (!double.IsNaN(comp.LoserFeatures[i]) && !double.IsInfinity(comp.LoserFeatures[i]))
                {
                    comp.LoserFeatures[i] = (comp.LoserFeatures[i] - _featureMeans[i]) / _featureStds[i];
                    comp.LoserFeatures[i] = Math.Clamp(comp.LoserFeatures[i], -5, 5);
                }
                else
                {
                    comp.LoserFeatures[i] = 0;
                }
            }
        }
    }

    private void PrintFeatureStatistics(List<PairwiseComparison> comparisons)
    {
        if (!comparisons.Any())
            return;

        var numFeatures = comparisons[0].WinnerFeatures.Length;
        var featureNames = new[]
        {
            "ImpliedProb", "Strength", "Food", "Position", "HistWinRate",
            "ArenaWinRate", "RecentWinRate", "RivalWinRate", "Bonus", "Penalty"
        };

        Console.WriteLine("      Feature statistics (after normalization):");

        for (int i = 0; i < Math.Min(numFeatures, featureNames.Length); i++)
        {
            var winnerVals = comparisons
                .Select(c => c.WinnerFeatures[i])
                .Where(v => !double.IsNaN(v))
                .ToList();

            var loserVals = comparisons
                .Select(c => c.LoserFeatures[i])
                .Where(v => !double.IsNaN(v))
                .ToList();

            if (winnerVals.Any() && loserVals.Any())
            {
                var winnerMean = winnerVals.Average();
                var loserMean = loserVals.Average();
                var diff = winnerMean - loserMean;

                // Only print features with meaningful differences
                Console.WriteLine($"         {featureNames[i],-15}: Winner={winnerMean,7:F3}, Loser={loserMean,7:F3}, Diff={diff,7:+0.000;-0.000}");
            }
        }

        // Print features sorted by discriminative power
        Console.WriteLine("      Most discriminative features:");
        var discriminativePower = new List<(string Name, double Diff)>();

        for (int i = 0; i < Math.Min(numFeatures, featureNames.Length); i++)
        {
            var winnerMean = comparisons.Average(c => c.WinnerFeatures[i]);
            var loserMean = comparisons.Average(c => c.LoserFeatures[i]);
            discriminativePower.Add((featureNames[i], winnerMean - loserMean));
        }

        foreach (var (name, diff) in discriminativePower.OrderByDescending(x => Math.Abs(x.Diff)).Take(5))
        {
            var direction = diff > 0 ? "↑ Winner higher" : "↓ Winner lower";
            Console.WriteLine($"         {name,-15}: {diff:+0.000;-0.000} ({direction})");
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
        
        var sortedCoefs = _coefficients
            .Select((c, i) => (Index: i, Coef: c, Name: i < featureNames.Length ? featureNames[i] : $"Feature{i}"))
            .OrderByDescending(x => Math.Abs(x.Coef))
            .ToList();

        foreach (var (index, coef, name) in sortedCoefs)
        {
            var sign = coef >= 0 ? "+" : "";
            var bar = new string('█', (int)Math.Min(Math.Abs(coef) * 10, 20));
            Console.WriteLine($"         {name,-15}: {sign}{coef:F4} {bar}");
        }
    }

    private double[] TrainBradleyTerry(List<PairwiseComparison> comparisons)
    {
        var numFeatures = comparisons[0].WinnerFeatures.Length;

        // Initialize with small random values instead of zeros
        var random = new Random(42);
        var coefficients = new double[numFeatures];
        for (int i = 0; i < numFeatures; i++)
        {
            coefficients[i] = (random.NextDouble() - 0.5) * 0.1;
        }

        var learningRate = 0.5;  // Start higher for Bradley-Terry
        var minLearningRate = 1e-5;
        var iterations = 500;
        var patience = 30;
        var bestLogLik = double.NegativeInfinity;
        var noImprovementCount = 0;
        var bestCoefficients = (double[])coefficients.Clone();

        // Mini-batch size for stochastic gradient descent
        var batchSize = Math.Min(1000, comparisons.Count);
        var useSGD = comparisons.Count > 5000;

        for (var iter = 0; iter < iterations; iter++)
        {
            var gradients = new double[numFeatures];
            var logLikelihood = 0.0;
            var validComparisons = 0;

            // Optionally use mini-batches for large datasets
            var batch = useSGD
                ? comparisons.OrderBy(_ => random.Next()).Take(batchSize).ToList()
                : comparisons;

            foreach (var comparison in batch)
            {
                // Calculate strength difference: s_winner - s_loser
                var winnerStrength = CalculateStrength(comparison.WinnerFeatures, coefficients);
                var loserStrength = CalculateStrength(comparison.LoserFeatures, coefficients);

                var strengthDiff = winnerStrength - loserStrength;

                // Clip to prevent overflow
                strengthDiff = Math.Clamp(strengthDiff, -500, 500);

                // P(winner beats loser) = sigmoid(s_winner - s_loser) = 1 / (1 + exp(-(s_w - s_l)))
                var prob = 1.0 / (1.0 + Math.Exp(-strengthDiff));

                // Protect against log(0)
                prob = Math.Clamp(prob, 1e-15, 1 - 1e-15);

                // Log-likelihood: log(P(winner beats loser))
                logLikelihood += Math.Log(prob);
                validComparisons++;

                // Gradient: (1 - prob) * (x_winner - x_loser)
                var gradientScale = 1.0 - prob;

                for (var f = 0; f < numFeatures; f++)
                {
                    var featureDiff = comparison.WinnerFeatures[f] - comparison.LoserFeatures[f];
                    
                    if (!double.IsNaN(featureDiff) && !double.IsInfinity(featureDiff))
                    {
                        gradients[f] += gradientScale * featureDiff;
                    }
                }
            }

            if (validComparisons == 0)
            {
                Console.WriteLine("         ⚠️ No valid comparisons in batch");
                break;
            }

            // Average gradients
            for (var f = 0; f < numFeatures; f++)
            {
                gradients[f] /= validComparisons;
            }

            // Calculate gradient norm
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

            // Update coefficients (gradient ascent for log-likelihood)
            for (var f = 0; f < numFeatures; f++)
            {
                var update = learningRate * gradients[f];
                if (!double.IsNaN(update) && !double.IsInfinity(update))
                {
                    coefficients[f] += update;
                    coefficients[f] = Math.Clamp(coefficients[f], -10, 10);
                }
            }

            // Calculate full log-likelihood for monitoring (not just batch)
            var fullLogLik = useSGD ? CalculateFullLogLikelihood(comparisons, coefficients) : logLikelihood;
            var avgLogLik = fullLogLik / comparisons.Count;

            // Track best model
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

            if ((iter + 1) % 50 == 0)
            {
                var accuracy = CalculateTrainingAccuracy(comparisons, coefficients);
                Console.WriteLine($"         Iteration {iter + 1}, AvgLogLik: {avgLogLik:F4}, Accuracy: {accuracy:P1}, GradNorm: {gradientNorm:F4}, LR: {learningRate:F5}");
            }
        }

        // Final accuracy check
        var finalAccuracy = CalculateTrainingAccuracy(comparisons, bestCoefficients);
        Console.WriteLine($"      Final training accuracy: {finalAccuracy:P2}");

        return bestCoefficients;
    }

    private double CalculateFullLogLikelihood(List<PairwiseComparison> comparisons, double[] coefficients)
    {
        var logLik = 0.0;

        foreach (var comparison in comparisons)
        {
            var winnerStrength = CalculateStrength(comparison.WinnerFeatures, coefficients);
            var loserStrength = CalculateStrength(comparison.LoserFeatures, coefficients);
            var strengthDiff = Math.Clamp(winnerStrength - loserStrength, -500, 500);
            var prob = 1.0 / (1.0 + Math.Exp(-strengthDiff));
            prob = Math.Clamp(prob, 1e-15, 1 - 1e-15);
            logLik += Math.Log(prob);
        }

        return logLik;
    }

    private double CalculateTrainingAccuracy(List<PairwiseComparison> comparisons, double[] coefficients)
    {
        var correct = 0;

        foreach (var comparison in comparisons)
        {
            var winnerStrength = CalculateStrength(comparison.WinnerFeatures, coefficients);
            var loserStrength = CalculateStrength(comparison.LoserFeatures, coefficients);

            if (winnerStrength > loserStrength)
                correct++;
        }

        return (double)correct / comparisons.Count;
    }

    private double CalculateStrength(double[] features, double[] coefficients)
    {
        var strength = 0.0;
        for (var i = 0; i < Math.Min(features.Length, coefficients.Length); i++)
        {
            var term = coefficients[i] * features[i];
            if (!double.IsNaN(term) && !double.IsInfinity(term))
            {
                strength += term;
            }
        }
        return Math.Clamp(strength, -500, 500);
    }

    private List<PairwiseComparison> ConvertToPairwiseComparisons(List<PirateFeatureRecord> data,
        Dictionary<(int RoundId, int ArenaId), List<PirateFeatureRecord>> groupedByRoundArena)
    {
        var comparisons = new List<PairwiseComparison>();

        foreach (var roundGroup in data.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.ToList();
            if (pirates.Count != 4) continue;

            var winner = pirates.FirstOrDefault(p => p.IsWinner == true);
            if (winner == null) continue;

            var losers = pirates.Where(p => p.PirateId != winner.PirateId).ToList();

            // Get winner features
            var winnerMlFeature = FeatureConversionHelper.ConvertSingle(winner, groupedByRoundArena, _interactionReport);
            var winnerFeatures = ExtractFeatures(winnerMlFeature);

            // Create pairwise comparison: winner vs each loser
            foreach (var loser in losers)
            {
                var loserMlFeature = FeatureConversionHelper.ConvertSingle(loser, groupedByRoundArena, _interactionReport);
                var loserFeatures = ExtractFeatures(loserMlFeature);

                comparisons.Add(new PairwiseComparison
                {
                    RoundId = roundGroup.Key.RoundId,
                    ArenaId = roundGroup.Key.ArenaId,
                    WinnerId = winner.PirateId,
                    LoserId = loser.PirateId,
                    WinnerFeatures = winnerFeatures,
                    LoserFeatures = loserFeatures
                });
            }
        }

        return comparisons;
    }

    private double[] ExtractFeatures(MlPirateFeature mlFeature)
    {
        return new[]
        {
            SafeValue(mlFeature.ImpliedProbability),
            SafeValue(mlFeature.Strength / 100.0f),
            SafeValue(mlFeature.FoodAdjustment / 10.0f),
            SafeValue((4 - mlFeature.Position) / 3.0f), // Invert so higher = better
            SafeValue(mlFeature.HistoricalWinRate),
            SafeValue(mlFeature.ArenaWinRate),
            SafeValue(mlFeature.RecentWinRate),
            SafeValue(mlFeature.WinRateVsCurrentRivals),
            SafeValue(InteractionCalculator.GetTotalBonus(mlFeature)),
            SafeValue(-InteractionCalculator.GetTotalPenalty(mlFeature)) // Negate so positive = good
        };
    }

    private double SafeValue(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return 0.0;
        return Math.Clamp(value, -100, 100);
    }

    private double SafeValue(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0.0;
        return Math.Clamp(value, -100, 100);
    }

    private double[] NormalizeForPrediction(double[] features)
    {
        if (_featureMeans == null || _featureStds == null)
            return features;

        var normalized = new double[features.Length];
        for (int i = 0; i < features.Length; i++)
        {
            if (i < _featureMeans.Length && i < _featureStds.Length)
            {
                normalized[i] = (features[i] - _featureMeans[i]) / _featureStds[i];
                normalized[i] = Math.Clamp(normalized[i], -5, 5);
            }
            else
            {
                normalized[i] = features[i];
            }
        }
        return normalized;
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

            if (pirates.Count < 2)
            {
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

            // Calculate strength for each pirate
            var strengths = new double[pirates.Count];

            for (var i = 0; i < pirates.Count; i++)
            {
                var pirate = pirates[i];
                var mlFeature = FeatureConversionHelper.ConvertSingle(pirate, groupedByRoundArena, _interactionReport);
                var pirateFeatures = ExtractFeatures(mlFeature);

                // Apply normalization
                pirateFeatures = NormalizeForPrediction(pirateFeatures);

                strengths[i] = CalculateStrength(pirateFeatures, _coefficients);
            }

            // Convert strengths to probabilities using softmax
            var maxStrength = strengths.Max();
            var expStrengths = strengths.Select(s => Math.Exp(Math.Clamp(s - maxStrength, -500, 500))).ToArray();
            var sumExp = expStrengths.Sum();

            if (sumExp < 1e-300) sumExp = 1e-300;

            for (var i = 0; i < pirates.Count; i++)
            {
                var prob = expStrengths[i] / sumExp;

                if (double.IsNaN(prob) || double.IsInfinity(prob))
                    prob = 1.0 / pirates.Count;

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
        var data = new BradleyTerryModelData
        {
            Coefficients = _coefficients?.ToList(),
            FeatureMeans = _featureMeans?.ToList(),
            FeatureStds = _featureStds?.ToList()
        };

        var json = JsonSerializer.Serialize(data,
            new JsonSerializerOptions { WriteIndented = true });

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path.Replace(".zip", "_bradleyterry.json"), json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_bradleyterry.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        var data = JsonSerializer.Deserialize<BradleyTerryModelData>(json);

        if (data == null) return;

        _coefficients = data.Coefficients?.ToArray();
        _featureMeans = data.FeatureMeans?.ToArray();
        _featureStds = data.FeatureStds?.ToArray();
    }

    private class PairwiseComparison
    {
        public int RoundId { get; set; }
        public int ArenaId { get; set; }
        public int WinnerId { get; set; }
        public int LoserId { get; set; }
        public double[] WinnerFeatures { get; set; } = Array.Empty<double>();
        public double[] LoserFeatures { get; set; } = Array.Empty<double>();
    }
}

internal class BradleyTerryModelData
{
    public List<double>? Coefficients { get; set; }
    public List<double>? FeatureMeans { get; set; }
    public List<double>? FeatureStds { get; set; }
}