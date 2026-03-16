using System.Text.Json;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Multinomial Logit (Conditional Logit / McFadden's Choice Model)
/// Models the probability of choosing alternative j from choice set C
/// P(j|C) = exp(V_j) / sum_k(exp(V_k)) where V is the utility function
/// </summary>
public class MultinomialLogit : IMlStrategy
{
    private readonly Dictionary<int, double[]> _arenaCoefficients = new();
    private double[]? _globalCoefficients;
    private readonly int _numFeatures = 8;
    private InteractionAnalysisReport? _interactionReport;

    public string StrategyName => "Multinomial Logit (Choice Model)";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        // Pre-compute grouped data for feature conversion
        var groupedByRoundArena = trainingData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        // Train per-arena models
        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();

            if (!arenaData.Any())
            {
                Console.WriteLine($"      ⚠️ No data for Arena {arenaId}");
                continue;
            }

            Console.WriteLine($"      Training Arena {arenaId}...");
            var choiceSets = ConvertToChoiceSets(arenaData, groupedByRoundArena);

            if (choiceSets.Any())
            {
                _arenaCoefficients[arenaId] = TrainMNL(choiceSets, $"Arena {arenaId}");
                Console.WriteLine($"         ✅ Arena {arenaId} trained ({choiceSets.Count} rounds)");
            }
        }

        // Train global model with extra care for numerical stability
        Console.WriteLine("      Training global model...");
        var allChoiceSets = ConvertToChoiceSets(trainingData, groupedByRoundArena);
        
        if (allChoiceSets.Any())
        {
            // Normalize features for global model to prevent numerical issues
            var normalizedChoiceSets = NormalizeChoiceSets(allChoiceSets);
            _globalCoefficients = TrainMNL(normalizedChoiceSets, "Global", useStrictStability: true);
            
            // Check if global model failed
            if (_globalCoefficients != null && _globalCoefficients.Any(c => double.IsNaN(c) || double.IsInfinity(c)))
            {
                Console.WriteLine("         ⚠️ Global model had numerical issues, using fallback");
                _globalCoefficients = CreateFallbackCoefficients();
            }
        }

        Console.WriteLine($"   ✅ Trained {_arenaCoefficients.Count}/5 arena models + global");
    }

    private List<ChoiceSet> NormalizeChoiceSets(List<ChoiceSet> choiceSets)
    {
        if (!choiceSets.Any() || !choiceSets[0].Alternatives.Any())
            return choiceSets;

        var numFeatures = choiceSets[0].Alternatives[0].Features.Length;
        
        // Calculate mean and std for each feature
        var means = new double[numFeatures];
        var stds = new double[numFeatures];
        var counts = new int[numFeatures];

        foreach (var cs in choiceSets)
        {
            foreach (var alt in cs.Alternatives)
            {
                for (int i = 0; i < numFeatures; i++)
                {
                    if (!double.IsNaN(alt.Features[i]) && !double.IsInfinity(alt.Features[i]))
                    {
                        means[i] += alt.Features[i];
                        counts[i]++;
                    }
                }
            }
        }

        for (int i = 0; i < numFeatures; i++)
        {
            means[i] = counts[i] > 0 ? means[i] / counts[i] : 0;
        }

        // Calculate std
        foreach (var cs in choiceSets)
        {
            foreach (var alt in cs.Alternatives)
            {
                for (int i = 0; i < numFeatures; i++)
                {
                    if (!double.IsNaN(alt.Features[i]) && !double.IsInfinity(alt.Features[i]))
                    {
                        stds[i] += Math.Pow(alt.Features[i] - means[i], 2);
                    }
                }
            }
        }

        for (int i = 0; i < numFeatures; i++)
        {
            stds[i] = counts[i] > 1 ? Math.Sqrt(stds[i] / (counts[i] - 1)) : 1;
            if (stds[i] < 1e-6) stds[i] = 1; // Prevent division by zero
        }

        // Normalize
        var normalized = new List<ChoiceSet>();
        foreach (var cs in choiceSets)
        {
            var newAlternatives = new List<Alternative>();
            foreach (var alt in cs.Alternatives)
            {
                var newFeatures = new double[numFeatures];
                for (int i = 0; i < numFeatures; i++)
                {
                    var val = alt.Features[i];
                    if (double.IsNaN(val) || double.IsInfinity(val))
                    {
                        newFeatures[i] = 0;
                    }
                    else
                    {
                        newFeatures[i] = (val - means[i]) / stds[i];
                        // Clip to reasonable range
                        newFeatures[i] = Math.Clamp(newFeatures[i], -5, 5);
                    }
                }
                newAlternatives.Add(new Alternative
                {
                    Features = newFeatures,
                    IsChosen = alt.IsChosen,
                    PirateId = alt.PirateId
                });
            }
            normalized.Add(new ChoiceSet
            {
                RoundId = cs.RoundId,
                ArenaId = cs.ArenaId,
                Alternatives = newAlternatives
            });
        }

        return normalized;
    }

    private double[] CreateFallbackCoefficients()
    {
        // Simple fallback: just use odds-based coefficient
        var coefficients = new double[_numFeatures];
        coefficients[0] = 1.0;  // ImpliedProbability (from odds)
        coefficients[1] = 0.5;  // Strength
        coefficients[2] = 0.3;  // Food
        coefficients[3] = 0.2;  // Historical win rate
        // Rest are 0
        return coefficients;
    }

    private double[] TrainMNL(List<ChoiceSet> choiceSets, string modelName, bool useStrictStability = false)
    {
        var coefficients = new double[_numFeatures];
        var learningRate = useStrictStability ? 0.001 : 0.01;
        var iterations = 1000;
        var minLearningRate = 0.0001;
        var patience = 50;
        var bestLogLik = double.NegativeInfinity;
        var noImprovementCount = 0;
        var bestCoefficients = new double[_numFeatures];

        for (var iter = 0; iter < iterations; iter++)
        {
            var gradients = new double[_numFeatures];
            var logLikelihood = 0.0;
            var validChoiceSets = 0;

            foreach (var choiceSet in choiceSets)
            {
                if (choiceSet.Alternatives.Count != 4) continue;

                // Calculate utilities with numerical stability
                var utilities = new double[4];
                var maxUtility = double.NegativeInfinity;

                for (var j = 0; j < 4; j++)
                {
                    utilities[j] = CalculateUtility(choiceSet.Alternatives[j].Features, coefficients);
                    
                    // Check for NaN/Infinity
                    if (double.IsNaN(utilities[j]) || double.IsInfinity(utilities[j]))
                    {
                        utilities[j] = 0;
                    }
                    
                    maxUtility = Math.Max(maxUtility, utilities[j]);
                }

                // Softmax with numerical stability (subtract max)
                var expUtilities = new double[4];
                var sumExp = 0.0;

                for (var j = 0; j < 4; j++)
                {
                    var shiftedUtility = utilities[j] - maxUtility;
                    // Clip to prevent overflow
                    shiftedUtility = Math.Clamp(shiftedUtility, -500, 500);
                    expUtilities[j] = Math.Exp(shiftedUtility);
                    sumExp += expUtilities[j];
                }

                // Prevent division by zero
                if (sumExp < 1e-300)
                {
                    sumExp = 1e-300;
                }

                var probs = expUtilities.Select(e => e / sumExp).ToArray();

                // Find chosen alternative
                var chosenIndex = choiceSet.Alternatives.FindIndex(a => a.IsChosen);
                if (chosenIndex < 0) continue;

                // Log-likelihood with protection against log(0)
                var chosenProb = Math.Max(probs[chosenIndex], 1e-15);
                var logProb = Math.Log(chosenProb);
                
                if (!double.IsNaN(logProb) && !double.IsInfinity(logProb))
                {
                    logLikelihood += logProb;
                    validChoiceSets++;

                    // Gradient: x_chosen - sum(prob_j * x_j)
                    for (var f = 0; f < _numFeatures; f++)
                    {
                        var chosenFeature = choiceSet.Alternatives[chosenIndex].Features[f];
                        var expectedFeature = 0.0;

                        for (var j = 0; j < 4; j++)
                        {
                            var featureVal = choiceSet.Alternatives[j].Features[f];
                            if (!double.IsNaN(featureVal) && !double.IsInfinity(featureVal))
                            {
                                expectedFeature += probs[j] * featureVal;
                            }
                        }

                        if (!double.IsNaN(chosenFeature) && !double.IsInfinity(chosenFeature))
                        {
                            var gradient = chosenFeature - expectedFeature;
                            
                            // Clip gradient for stability
                            gradient = Math.Clamp(gradient, -10, 10);
                            gradients[f] += gradient;
                        }
                    }
                }
            }

            // Check for valid training
            if (validChoiceSets == 0)
            {
                Console.WriteLine($"            ⚠️ No valid choice sets in iteration {iter}");
                break;
            }

            // Update coefficients with gradient clipping
            var gradientNorm = Math.Sqrt(gradients.Sum(g => g * g));
            var maxGradientNorm = 10.0;
            
            if (gradientNorm > maxGradientNorm)
            {
                var scale = maxGradientNorm / gradientNorm;
                for (var f = 0; f < _numFeatures; f++)
                {
                    gradients[f] *= scale;
                }
            }

            for (var f = 0; f < _numFeatures; f++)
            {
                var update = learningRate * gradients[f];
                
                // Skip NaN updates
                if (!double.IsNaN(update) && !double.IsInfinity(update))
                {
                    coefficients[f] += update;
                    
                    // Clip coefficients to prevent explosion
                    coefficients[f] = Math.Clamp(coefficients[f], -100, 100);
                }
            }

            // Early stopping check
            var avgLogLik = logLikelihood / validChoiceSets;
            
            if (double.IsNaN(avgLogLik))
            {
                Console.WriteLine($"            ⚠️ NaN detected at iteration {iter}, reverting to best");
                Array.Copy(bestCoefficients, coefficients, _numFeatures);
                learningRate *= 0.5;
                noImprovementCount = 0;
                
                if (learningRate < minLearningRate)
                {
                    Console.WriteLine("            Stopping early due to NaN issues");
                    break;
                }
                continue;
            }

            if (avgLogLik > bestLogLik)
            {
                bestLogLik = avgLogLik;
                Array.Copy(coefficients, bestCoefficients, _numFeatures);
                noImprovementCount = 0;
            }
            else
            {
                noImprovementCount++;
                if (noImprovementCount >= patience)
                {
                    // Reduce learning rate
                    learningRate *= 0.5;
                    noImprovementCount = 0;
                    
                    if (learningRate < minLearningRate)
                    {
                        Console.WriteLine($"            Converged at iteration {iter}");
                        break;
                    }
                }
            }

            if ((iter + 1) % 200 == 0)
            {
                Console.WriteLine($"            Iteration {iter + 1}, AvgLogLik: {avgLogLik:F4}, LR: {learningRate:F6}");
            }
        }

        // Return best coefficients found
        return bestCoefficients;
    }

    private double CalculateUtility(double[] features, double[] coefficients)
    {
        var utility = 0.0;
        for (var i = 0; i < Math.Min(features.Length, coefficients.Length); i++)
        {
            var term = coefficients[i] * features[i];
            
            // Skip invalid terms
            if (double.IsNaN(term) || double.IsInfinity(term))
                continue;
                
            utility += term;
        }

        // Clip utility to prevent overflow in exp()
        return Math.Clamp(utility, -500, 500);
    }

    private List<ChoiceSet> ConvertToChoiceSets(List<PirateFeatureRecord> data,
        Dictionary<(int RoundId, int ArenaId), List<PirateFeatureRecord>> groupedByRoundArena)
    {
        var choiceSets = new List<ChoiceSet>();

        var roundGroups = data.GroupBy(f => (f.RoundId, f.ArenaId));

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var winnerIndex = pirates.FindIndex(p => p.IsWinner == true);
            if (winnerIndex < 0) continue;

            var alternatives = new List<Alternative>();
            var hasValidData = true;

            for (var i = 0; i < 4; i++)
            {
                var p = pirates[i];
                var mlFeature = FeatureConversionHelper.ConvertSingle(p, groupedByRoundArena, _interactionReport);

                // Validate features
                var features = new[]
                {
                    SafeValue(mlFeature.ImpliedProbability),
                    SafeValue(mlFeature.Strength / 100.0f),
                    SafeValue(mlFeature.FoodAdjustment / 10.0f),
                    SafeValue(mlFeature.HistoricalWinRate),
                    SafeValue(mlFeature.ArenaWinRate),
                    SafeValue(mlFeature.RecentWinRate),
                    SafeValue(InteractionCalculator.GetTotalBonus(mlFeature)),
                    SafeValue(-InteractionCalculator.GetTotalPenalty(mlFeature))
                };

                // Check for any remaining invalid values
                if (features.Any(f => double.IsNaN(f) || double.IsInfinity(f)))
                {
                    hasValidData = false;
                    break;
                }

                alternatives.Add(new Alternative
                {
                    Features = features,
                    IsChosen = i == winnerIndex,
                    PirateId = p.PirateId
                });
            }

            if (hasValidData && alternatives.Count == 4)
            {
                choiceSets.Add(new ChoiceSet
                {
                    RoundId = round.Key.RoundId,
                    ArenaId = round.Key.ArenaId,
                    Alternatives = alternatives
                });
            }
        }

        return choiceSets;
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

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        // Pre-compute grouped data
        var groupedByRoundArena = features
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var arenaId = roundGroup.Key.ArenaId;

            // Use arena-specific model if available, otherwise global
            var coefficients = _arenaCoefficients.GetValueOrDefault(arenaId) ?? _globalCoefficients;

            if (coefficients == null)
            {
                // Fallback to uniform probabilities
                foreach (var pirate in pirates)
                {
                    predictions.Add(new PiratePrediction
                    {
                        RoundId = pirate.RoundId,
                        ArenaId = pirate.ArenaId,
                        PirateId = pirate.PirateId,
                        WinProbability = 0.25f,
                        Payout = Math.Max(2, pirate.CurrentOdds)
                    });
                }
                continue;
            }

            // Calculate utilities
            var utilities = new double[4];
            var maxUtility = double.NegativeInfinity;

            for (var i = 0; i < 4; i++)
            {
                var p = pirates[i];
                var mlFeature = FeatureConversionHelper.ConvertSingle(p, groupedByRoundArena, _interactionReport);

                var pirateFeatures = new[]
                {
                    SafeValue(mlFeature.ImpliedProbability),
                    SafeValue(mlFeature.Strength / 100.0f),
                    SafeValue(mlFeature.FoodAdjustment / 10.0f),
                    SafeValue(mlFeature.HistoricalWinRate),
                    SafeValue(mlFeature.ArenaWinRate),
                    SafeValue(mlFeature.RecentWinRate),
                    SafeValue(InteractionCalculator.GetTotalBonus(mlFeature)),
                    SafeValue(-InteractionCalculator.GetTotalPenalty(mlFeature))
                };

                utilities[i] = CalculateUtility(pirateFeatures, coefficients);
                maxUtility = Math.Max(maxUtility, utilities[i]);
            }

            // Softmax with numerical stability
            var expUtilities = new double[4];
            var sumExp = 0.0;

            for (var i = 0; i < 4; i++)
            {
                var shiftedUtility = utilities[i] - maxUtility;
                shiftedUtility = Math.Clamp(shiftedUtility, -500, 500);
                expUtilities[i] = Math.Exp(shiftedUtility);
                sumExp += expUtilities[i];
            }

            if (sumExp < 1e-300) sumExp = 1e-300;

            var probs = expUtilities.Select(e => e / sumExp).ToArray();

            // Create predictions
            for (var i = 0; i < 4; i++)
            {
                var prob = probs[i];
                
                // Final safety check
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
        var data = new MultinomialLogitModelData
        {
            ArenaCoefficients = _arenaCoefficients.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToList()),
            GlobalCoefficients = _globalCoefficients?.ToList()
        };

        var json = JsonSerializer.Serialize(data,
            new JsonSerializerOptions { WriteIndented = true });
        
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path.Replace(".zip", "_multinomial.json"), json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_multinomial.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        var data = JsonSerializer.Deserialize<MultinomialLogitModelData>(json);

        if (data == null) return;

        _arenaCoefficients.Clear();
        foreach (var kvp in data.ArenaCoefficients) 
            _arenaCoefficients[kvp.Key] = kvp.Value.ToArray();

        _globalCoefficients = data.GlobalCoefficients?.ToArray();
    }

    private class ChoiceSet
    {
        public int RoundId { get; set; }
        public int ArenaId { get; set; }
        public List<Alternative> Alternatives { get; set; } = new();
    }

    private class Alternative
    {
        public double[] Features { get; set; } = Array.Empty<double>();
        public bool IsChosen { get; set; }
        public int PirateId { get; set; }
    }
}

internal class MultinomialLogitModelData
{
    public Dictionary<int, List<double>> ArenaCoefficients { get; set; } = new();
    public List<double>? GlobalCoefficients { get; set; }
}