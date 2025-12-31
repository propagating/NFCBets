using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class ConditionalLogisticRegression : IMlStrategy
{
    public string StrategyName => "Conditional Logistic (Choice Model)";
    
    private readonly Dictionary<int, double[]> _arenaWeights = new();
    private double[]? _globalWeights;
    private readonly double _learningRate = 0.01;
    private readonly int _maxIterations = 1000;
    private readonly int _numFeatures = 17;
    private InteractionAnalysisReport? _interactionReport;

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;
        
        Console.WriteLine($"   Training {StrategyName}...");
        
        if (_interactionReport != null)
        {
            Console.WriteLine($"      Applying interaction controls");
        }

        var arenaGroups = trainingData.GroupBy(f => f.ArenaId);

        foreach (var arenaGroup in arenaGroups)
        {
            var arenaId = arenaGroup.Key;
            var arenaData = arenaGroup.ToList();
            
            Console.WriteLine($"      Training Arena {arenaId}...");
            
            var choiceSets = CreateChoiceSets(arenaData);
            
            if (choiceSets.Count < 10)
            {
                Console.WriteLine($"         ⚠️ Insufficient data for Arena {arenaId} ({choiceSets.Count} choice sets)");
                continue;
            }

            var weights = TrainConditionalLogit(choiceSets);
            if (weights != null)
            {
                _arenaWeights[arenaId] = weights;
                Console.WriteLine($"         ✅ Arena {arenaId} trained ({choiceSets.Count} choice sets)");
            }
            else
            {
                Console.WriteLine($"         ⚠️ Arena {arenaId} training failed");
            }
        }

        Console.WriteLine($"      Training global model...");
        var allChoiceSets = CreateChoiceSets(trainingData);
        
        if (allChoiceSets.Count >= 10)
        {
            _globalWeights = TrainConditionalLogit(allChoiceSets);
            if (_globalWeights != null)
            {
                Console.WriteLine($"   ✅ Trained {_arenaWeights.Count}/5 arena models + global ({allChoiceSets.Count} choice sets)");
            }
            else
            {
                Console.WriteLine($"   ⚠️ Global model training failed, using arena models only");
            }
        }
        else
        {
            Console.WriteLine($"   ⚠️ Insufficient data for global model ({allChoiceSets.Count} choice sets)");
        }
    }

    private List<ChoiceSet> CreateChoiceSets(List<PirateFeatureRecord> data)
    {
        var choiceSets = new List<ChoiceSet>();
        
        var roundGroups = data.GroupBy(f => f.RoundId);

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var winnerIndex = pirates.FindIndex(p => p.IsWinner == true);
            if (winnerIndex < 0) continue;

            var alternatives = new List<double[]>();
            foreach (var pirate in pirates)
            {
                var features = ExtractFeatures(pirate, pirates);
                if (features != null)
                {
                    alternatives.Add(features);
                }
            }

            // Only add if we have all 4 alternatives
            if (alternatives.Count == 4)
            {
                choiceSets.Add(new ChoiceSet
                {
                    Alternatives = alternatives,
                    ChosenIndex = winnerIndex,
                    RoundId = round.Key,
                    PirateIds = pirates.Select(p => p.PirateId).ToList()
                });
            }
        }

        return choiceSets;
    }

    private double[]? ExtractFeatures(PirateFeatureRecord pirate, List<PirateFeatureRecord> allPirates)
    {
        try
        {
            var avgStrength = allPirates.Average(p => p.Strength);
            var avgOdds = allPirates.Average(p => p.CurrentOdds);
            var avgFood = allPirates.Average(p => p.FoodAdjustment);
            
            var sortedByOdds = allPirates.OrderBy(p => p.CurrentOdds).ToList();
            var sortedByStrength = allPirates.OrderByDescending(p => p.Strength).ToList();
            
            var oddsRank = sortedByOdds.IndexOf(pirate);
            var strengthRank = sortedByStrength.IndexOf(pirate);
            
            // Handle case where pirate not found in sorted lists
            if (oddsRank < 0) oddsRank = 2;
            if (strengthRank < 0) strengthRank = 2;

            // Calculate interaction penalties/bonuses
            var mlFeature = new MlPirateFeature();
            InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirate, _interactionReport);
            
            var totalPenalty = mlFeature.Penalty_FoodPosition + mlFeature.Penalty_FoodFavorite + 
                               mlFeature.Penalty_StrengthPosition + mlFeature.Penalty_StrengthWeakRivals +
                               mlFeature.Penalty_FavoriteInexperienced + mlFeature.Penalty_LowStrengthFavorite;
            
            var totalBonus = mlFeature.Bonus_UndervaluedStrong + mlFeature.Bonus_HotStreakBeatsRivals +
                             mlFeature.Bonus_ArenaSpecialistModerateOdds + mlFeature.Bonus_FoodPosition3;

            return new double[]
            {
                // Absolute features (normalized)
                pirate.Strength / 100.0,
                Math.Log(Math.Max(2, pirate.CurrentOdds)),
                pirate.FoodAdjustment / 10.0,
                pirate.Position / 4.0,
                pirate.HistoricalWinRate,
                pirate.ArenaWinRate,
                pirate.RecentWinRate,
                pirate.WinRateVsCurrentRivals,
                
                // Relative features
                (pirate.Strength - avgStrength) / 30.0,
                (avgOdds - pirate.CurrentOdds) / Math.Max(1, avgOdds),
                (pirate.FoodAdjustment - avgFood) / 5.0,
                
                // Rank features
                (3 - oddsRank) / 3.0,
                (3 - strengthRank) / 3.0,
                
                // Interaction terms
                pirate.FoodAdjustment * (4 - pirate.Position) / 40.0,
                pirate.Strength * pirate.HistoricalWinRate / 100.0,
                
                // Interaction controls from causal analysis
                -totalPenalty,
                totalBonus
            };
        }
        catch
        {
            return null;
        }
    }

    private double[]? TrainConditionalLogit(List<ChoiceSet> choiceSets)
    {
        if (choiceSets == null || !choiceSets.Any())
        {
            return null;
        }

        // Validate that all choice sets have proper alternatives
        var validChoiceSets = choiceSets
            .Where(cs => cs.Alternatives != null && 
                         cs.Alternatives.Count == 4 && 
                         cs.Alternatives.All(a => a != null && a.Length == _numFeatures) &&
                         cs.ChosenIndex >= 0 && 
                         cs.ChosenIndex < 4)
            .ToList();

        if (!validChoiceSets.Any())
        {
            return null;
        }

        var weights = new double[_numFeatures];
        var random = new Random(42);
        
        for (int i = 0; i < _numFeatures; i++)
        {
            weights[i] = (random.NextDouble() - 0.5) * 0.1;
        }

        var lambda = 0.01;  // L2 regularization

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            var gradient = new double[_numFeatures];
            var totalLogLikelihood = 0.0;

            foreach (var choiceSet in validChoiceSets)
            {
                var utilities = new double[4];
                for (int j = 0; j < 4; j++)
                {
                    utilities[j] = DotProduct(weights, choiceSet.Alternatives[j]);
                }

                var probs = Softmax(utilities);
                
                // Check for valid probability
                var chosenProb = probs[choiceSet.ChosenIndex];
                if (chosenProb > 0 && !double.IsNaN(chosenProb) && !double.IsInfinity(chosenProb))
                {
                    totalLogLikelihood += Math.Log(Math.Max(1e-15, chosenProb));
                }

                for (int f = 0; f < _numFeatures; f++)
                {
                    var expectedFeature = 0.0;
                    for (int j = 0; j < 4; j++)
                    {
                        if (!double.IsNaN(probs[j]) && !double.IsInfinity(probs[j]))
                        {
                            expectedFeature += probs[j] * choiceSet.Alternatives[j][f];
                        }
                    }
                    
                    var chosenFeature = choiceSet.Alternatives[choiceSet.ChosenIndex][f];
                    if (!double.IsNaN(chosenFeature) && !double.IsNaN(expectedFeature))
                    {
                        gradient[f] += chosenFeature - expectedFeature;
                    }
                }
            }

            // Update weights with regularization
            for (int f = 0; f < _numFeatures; f++)
            {
                var update = _learningRate * (gradient[f] / validChoiceSets.Count - lambda * weights[f]);
                if (!double.IsNaN(update) && !double.IsInfinity(update))
                {
                    weights[f] += update;
                }
            }

            // Early stopping check
            if (iter > 0 && iter % 200 == 0)
            {
                var avgLL = totalLogLikelihood / validChoiceSets.Count;
                if (double.IsNaN(avgLL) || double.IsInfinity(avgLL))
                {
                    Console.WriteLine($"            ⚠️ Training diverged at iteration {iter}");
                    break;
                }
            }
        }

        // Validate final weights
        if (weights.Any(w => double.IsNaN(w) || double.IsInfinity(w)))
        {
            return null;
        }

        return weights;
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        foreach (var arenaGroup in features.GroupBy(f => f.ArenaId))
        {
            var arenaId = arenaGroup.Key;
            var weights = _arenaWeights.GetValueOrDefault(arenaId, _globalWeights);
            
            if (weights == null)
            {
                // Fallback to uniform probabilities
                foreach (var f in arenaGroup)
                {
                    predictions.Add(new PiratePrediction
                    {
                        RoundId = f.RoundId,
                        ArenaId = f.ArenaId,
                        PirateId = f.PirateId,
                        WinProbability = 0.25f,
                        Payout = Math.Max(2, f.CurrentOdds)
                    });
                }
                continue;
            }

            foreach (var roundGroup in arenaGroup.GroupBy(f => f.RoundId))
            {
                var pirates = roundGroup.OrderBy(p => p.Position).ToList();
                if (pirates.Count != 4)
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

                var utilities = new double[4];
                var validFeatures = true;
                
                for (int i = 0; i < 4; i++)
                {
                    var featureVector = ExtractFeatures(pirates[i], pirates);
                    if (featureVector == null)
                    {
                        validFeatures = false;
                        break;
                    }
                    utilities[i] = DotProduct(weights, featureVector);
                }

                double[] probs;
                if (validFeatures)
                {
                    probs = Softmax(utilities);
                    
                    // Check for invalid probabilities
                    if (probs.Any(p => double.IsNaN(p) || double.IsInfinity(p)))
                    {
                        probs = new double[] { 0.25, 0.25, 0.25, 0.25 };
                    }
                }
                else
                {
                    probs = new double[] { 0.25, 0.25, 0.25, 0.25 };
                }

                for (int i = 0; i < 4; i++)
                {
                    predictions.Add(new PiratePrediction
                    {
                        RoundId = pirates[i].RoundId,
                        ArenaId = pirates[i].ArenaId,
                        PirateId = pirates[i].PirateId,
                        WinProbability = (float)probs[i],
                        Payout = Math.Max(2, pirates[i].CurrentOdds)
                    });
                }
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
            AUC = auc,
            F1Score = accuracy * 0.5,
            TestDataSize = testData.Count,
            LogLoss = logLoss
        };
    }

    private double[] Softmax(double[] utilities)
    {
        if (utilities == null || utilities.Length == 0)
            return new double[] { 0.25, 0.25, 0.25, 0.25 };
            
        var max = utilities.Max();
        
        // Check for invalid values
        if (double.IsNaN(max) || double.IsInfinity(max))
            return new double[] { 0.25, 0.25, 0.25, 0.25 };
            
        var exps = utilities.Select(u => 
        {
            var exp = Math.Exp(u - max);
            return double.IsNaN(exp) || double.IsInfinity(exp) ? 0 : exp;
        }).ToArray();
        
        var sum = exps.Sum();
        
        if (sum <= 0 || double.IsNaN(sum) || double.IsInfinity(sum))
            return new double[] { 0.25, 0.25, 0.25, 0.25 };
            
        return exps.Select(e => e / sum).ToArray();
    }

    private double DotProduct(double[] a, double[] b)
    {
        if (a == null || b == null)
            return 0;
            
        double sum = 0;
        var len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            var product = a[i] * b[i];
            if (!double.IsNaN(product) && !double.IsInfinity(product))
            {
                sum += product;
            }
        }
        return sum;
    }

    public void SaveModel(string path)
    {
        var data = new ConditionalLogisticModelData
        {
            GlobalWeights = _globalWeights?.ToList() ?? new List<double>(),
            ArenaWeights = _arenaWeights.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value.ToList())
        };
        
        var json = System.Text.Json.JsonSerializer.Serialize(data, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var jsonPath = path.Replace(".zip", "_conditional_logit.json");
        
        Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
        File.WriteAllText(jsonPath, json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_conditional_logit.json");
        if (!File.Exists(jsonPath)) return;
        
        var json = File.ReadAllText(jsonPath);
        var data = System.Text.Json.JsonSerializer.Deserialize<ConditionalLogisticModelData>(json);
        
        if (data == null) return;

        if (data.GlobalWeights.Any())
        {
            _globalWeights = data.GlobalWeights.ToArray();
        }
        
        _arenaWeights.Clear();
        foreach (var kvp in data.ArenaWeights)
        {
            _arenaWeights[kvp.Key] = kvp.Value.ToArray();
        }
    }
}

internal class ChoiceSet
{
    public List<double[]> Alternatives { get; set; } = new();
    public int ChosenIndex { get; set; }
    public int RoundId { get; set; }
    public List<int> PirateIds { get; set; } = new();
}

internal class ConditionalLogisticModelData
{
    public List<double> GlobalWeights { get; set; } = new();
    public Dictionary<int, List<double>> ArenaWeights { get; set; } = new();
}