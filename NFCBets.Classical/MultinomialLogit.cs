using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Multinomial Logit Model (McFadden's Choice Model)
/// Theoretically optimal for "choose 1 from N" problems
/// P(choose i) = exp(V_i) / sum(exp(V_j)) where V is utility function
/// </summary>
public class MultinomialLogit : IMlStrategy
{
    public string StrategyName => "Multinomial Logit (Choice Model)";
    
    private readonly Dictionary<int, double[]> _arenaWeights = new();
    private double[]? _globalWeights;
    
    private readonly double _learningRate = 0.01;
    private readonly int _maxIterations = 1000;
    private readonly double _regularization = 0.001;
    private readonly int _numFeatures = 18;
    private InteractionAnalysisReport? _interactionReport;

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;
        
        Console.WriteLine($"   Training {StrategyName}...");

        // Train per-arena models
        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = trainingData.Where(f => f.ArenaId == arenaId).ToList();
            if (!arenaData.Any()) continue;

            Console.WriteLine($"      Training Arena {arenaId}...");
            
            var choiceSets = CreateChoiceSets(arenaData);
            
            if (choiceSets.Count < 50)
            {
                Console.WriteLine($"         ⚠️ Insufficient data for Arena {arenaId} ({choiceSets.Count} rounds)");
                continue;
            }

            _arenaWeights[arenaId] = TrainMultinomialLogit(choiceSets);
            Console.WriteLine($"         ✅ Arena {arenaId} trained ({choiceSets.Count} rounds)");
        }

        // Train global model as fallback
        Console.WriteLine($"      Training global model...");
        var allChoiceSets = CreateChoiceSets(trainingData);
        _globalWeights = TrainMultinomialLogit(allChoiceSets);
        
        Console.WriteLine($"   ✅ Trained {_arenaWeights.Count}/5 arena models + global");
    }

    private List<MultinomialChoiceSet> CreateChoiceSets(List<PirateFeatureRecord> data)
    {
        var choiceSets = new List<MultinomialChoiceSet>();
        
        var roundGroups = data.GroupBy(f => f.RoundId);

        foreach (var round in roundGroups)
        {
            var pirates = round.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var winnerIndex = pirates.FindIndex(p => p.IsWinner == true);
            if (winnerIndex < 0) continue;

            var alternatives = new List<double[]>();
            for (int i = 0; i < 4; i++)
            {
                alternatives.Add(ExtractUtilityFeatures(pirates[i], pirates, i));
            }

            choiceSets.Add(new MultinomialChoiceSet
            {
                Alternatives = alternatives,
                ChosenIndex = winnerIndex,
                RoundId = round.Key,
                PirateIds = pirates.Select(p => p.PirateId).ToList()
            });
        }

        return choiceSets;
    }

    private double[] ExtractUtilityFeatures(PirateFeatureRecord pirate, List<PirateFeatureRecord> allPirates, int position)
    {
        var avgStrength = allPirates.Average(p => p.Strength);
        var avgOdds = allPirates.Average(p => p.CurrentOdds);
        var maxStrength = allPirates.Max(p => p.Strength);
        var minOdds = allPirates.Min(p => p.CurrentOdds);

        // Calculate ranks
        var oddsRank = allPirates.OrderBy(p => p.CurrentOdds).ToList().IndexOf(pirate);
        var strengthRank = allPirates.OrderByDescending(p => p.Strength).ToList().IndexOf(pirate);

        // Interaction effects
        var mlFeature = new MlPirateFeature();
        InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirate, _interactionReport);
        
        var penalty = mlFeature.Penalty_FoodPosition + mlFeature.Penalty_FoodFavorite + 
                      mlFeature.Penalty_StrengthPosition + mlFeature.Penalty_StrengthWeakRivals +
                      mlFeature.Penalty_FavoriteInexperienced + mlFeature.Penalty_LowStrengthFavorite;
        var bonus = mlFeature.Bonus_UndervaluedStrong + mlFeature.Bonus_HotStreakBeatsRivals +
                    mlFeature.Bonus_ArenaSpecialistModerateOdds + mlFeature.Bonus_FoodPosition3;

        return new double[]
        {
            // Absolute features
            pirate.Strength / 100.0,
            Math.Log(Math.Max(2, pirate.CurrentOdds)),
            pirate.FoodAdjustment / 10.0,
            pirate.HistoricalWinRate,
            pirate.ArenaWinRate,
            pirate.RecentWinRate,
            pirate.WinRateVsCurrentRivals,
            
            // Relative features (vs competition)
            (pirate.Strength - avgStrength) / 30.0,
            (avgOdds - pirate.CurrentOdds) / Math.Max(1, avgOdds),
            pirate.Strength == maxStrength ? 1.0 : 0.0,  // Is strongest
            pirate.CurrentOdds == minOdds ? 1.0 : 0.0,   // Is favorite
            
            // Rank features
            (3 - oddsRank) / 3.0,
            (3 - strengthRank) / 3.0,
            
            // Position feature (position-specific effects)
            position == 0 ? 1.0 : 0.0,  // Position 0 indicator
            position == 2 ? 1.0 : 0.0,  // Position 2 indicator (often advantageous)
            position == 3 ? 1.0 : 0.0,  // Position 3 indicator
            
            // Interaction effects
            -penalty,
            bonus
        };
    }

    private double[] TrainMultinomialLogit(List<MultinomialChoiceSet> choiceSets)
    {
        var weights = new double[_numFeatures];
        var random = new Random(42);
        
        // Initialize with small random values
        for (int i = 0; i < _numFeatures; i++)
        {
            weights[i] = (random.NextDouble() - 0.5) * 0.1;
        }

        // Adam optimizer parameters
        var m = new double[_numFeatures];  // First moment
        var v = new double[_numFeatures];  // Second moment
        var beta1 = 0.9;
        var beta2 = 0.999;
        var epsilon = 1e-8;

        for (int iter = 0; iter < _maxIterations; iter++)
        {
            var gradient = new double[_numFeatures];
            var totalLogLikelihood = 0.0;

            foreach (var choiceSet in choiceSets)
            {
                // Calculate utilities
                var utilities = new double[4];
                for (int j = 0; j < 4; j++)
                {
                    utilities[j] = DotProduct(weights, choiceSet.Alternatives[j]);
                }

                // Softmax probabilities
                var probs = Softmax(utilities);
                
                totalLogLikelihood += Math.Log(Math.Max(1e-15, probs[choiceSet.ChosenIndex]));

                // Gradient: x_chosen - E[x]
                for (int f = 0; f < _numFeatures; f++)
                {
                    var expectedFeature = 0.0;
                    for (int j = 0; j < 4; j++)
                    {
                        expectedFeature += probs[j] * choiceSet.Alternatives[j][f];
                    }
                    gradient[f] += choiceSet.Alternatives[choiceSet.ChosenIndex][f] - expectedFeature;
                }
            }

            // Adam update
            for (int f = 0; f < _numFeatures; f++)
            {
                var g = gradient[f] / choiceSets.Count - _regularization * weights[f];
                
                m[f] = beta1 * m[f] + (1 - beta1) * g;
                v[f] = beta2 * v[f] + (1 - beta2) * g * g;
                
                var mHat = m[f] / (1 - Math.Pow(beta1, iter + 1));
                var vHat = v[f] / (1 - Math.Pow(beta2, iter + 1));
                
                weights[f] += _learningRate * mHat / (Math.Sqrt(vHat) + epsilon);
            }

            if (iter > 0 && iter % 200 == 0)
            {
                var avgLL = totalLogLikelihood / choiceSets.Count;
                Console.WriteLine($"            Iteration {iter}, AvgLogLik: {avgLL:F4}");
            }
        }

        return weights;
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var arenaId = pirates[0].ArenaId;
            var weights = _arenaWeights.GetValueOrDefault(arenaId, _globalWeights);
            
            if (weights == null) continue;

            var utilities = new double[4];
            for (int i = 0; i < 4; i++)
            {
                var featureVector = ExtractUtilityFeatures(pirates[i], pirates, i);
                utilities[i] = DotProduct(weights, featureVector);
            }

            var probs = Softmax(utilities);

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
        var max = utilities.Max();
        var exps = utilities.Select(u => Math.Exp(u - max)).ToArray();
        var sum = exps.Sum();
        return exps.Select(e => e / sum).ToArray();
    }

    private double DotProduct(double[] a, double[] b)
    {
        double sum = 0;
        var len = Math.Min(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            sum += a[i] * b[i];
        }
        return sum;
    }

    public void SaveModel(string path)
    {
        var data = new MultinomialLogitModelData
        {
            GlobalWeights = _globalWeights?.ToList() ?? new List<double>(),
            ArenaWeights = _arenaWeights.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToList())
        };

        var json = System.Text.Json.JsonSerializer.Serialize(data,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path.Replace(".zip", "_multinomial_logit.json"), json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_multinomial_logit.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        var data = System.Text.Json.JsonSerializer.Deserialize<MultinomialLogitModelData>(json);
        
        if (data == null) return;

        _globalWeights = data.GlobalWeights.ToArray();
        _arenaWeights.Clear();
        foreach (var kvp in data.ArenaWeights)
        {
            _arenaWeights[kvp.Key] = kvp.Value.ToArray();
        }
    }
}

internal class MultinomialChoiceSet
{
    public List<double[]> Alternatives { get; set; } = new();
    public int ChosenIndex { get; set; }
    public int RoundId { get; set; }
    public List<int> PirateIds { get; set; } = new();
}

internal class MultinomialLogitModelData
{
    public List<double> GlobalWeights { get; set; } = new();
    public Dictionary<int, List<double>> ArenaWeights { get; set; } = new();
}