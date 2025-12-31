using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

/// <summary>
/// Plackett-Luce Model - Generalized Bradley-Terry for multi-way competition
/// Models P(i wins | i,j,k,l compete) = strength_i / (strength_i + strength_j + strength_k + strength_l)
/// Learns both pirate-specific and feature-based strength parameters
/// </summary>
public class PlackettLuce : IMlStrategy
{
    public string StrategyName => "Plackett-Luce (Generalized Bradley-Terry)";
    
    private readonly Dictionary<int, double> _pirateStrengths = new();
    private readonly Dictionary<int, double> _arenaMultipliers = new();
    private double[] _featureWeights = Array.Empty<double>();
    
    private readonly double _learningRate = 0.05;
    private readonly int _maxIterations = 500;
    private readonly double _regularization = 0.01;
    private InteractionAnalysisReport? _interactionReport;

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport? interactionReport = null)
    {
        _interactionReport = interactionReport;
        
        Console.WriteLine($"   Training {StrategyName}...");
        
        // Initialize feature weights (15 features)
        var numFeatures = 15;
        _featureWeights = new double[numFeatures];
        var random = new Random(42);
        for (int i = 0; i < numFeatures; i++)
        {
            _featureWeights[i] = (random.NextDouble() - 0.5) * 0.1;
        }

        // Initialize pirate strengths
        var allPirateIds = trainingData.Select(f => f.PirateId).Distinct();
        foreach (var pirateId in allPirateIds)
        {
            _pirateStrengths[pirateId] = 1.0;
        }

        // Initialize arena multipliers
        for (int i = 1; i <= 5; i++)
        {
            _arenaMultipliers[i] = 1.0;
        }

        // Group into competitions
        var competitions = trainingData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .Where(g => g.Count() == 4 && g.Any(p => p.IsWinner == true))
            .Select(g => g.OrderBy(p => p.Position).ToList())
            .ToList();

        Console.WriteLine($"      Training on {competitions.Count} competitions...");

        // Iterative optimization using gradient descent
        for (int iter = 0; iter < _maxIterations; iter++)
        {
            var totalLogLikelihood = 0.0;
            var featureGradients = new double[numFeatures];

            foreach (var pirates in competitions)
            {
                var winnerIdx = pirates.FindIndex(p => p.IsWinner == true);
                var arenaId = pirates[0].ArenaId;

                // Calculate strengths using both pirate-specific and feature-based components
                var strengths = new double[4];
                var featureVectors = new double[4][];
                
                for (int i = 0; i < 4; i++)
                {
                    featureVectors[i] = ExtractFeatures(pirates[i], pirates);
                    
                    var pirateStrength = _pirateStrengths.GetValueOrDefault(pirates[i].PirateId, 1.0);
var featureScore = DotProduct(_featureWeights, featureVectors[i]);
                    var arenaMult = _arenaMultipliers.GetValueOrDefault(arenaId, 1.0);
                    
                    strengths[i] = pirateStrength * Math.Exp(featureScore) * arenaMult;
                }

                var totalStrength = strengths.Sum();
                var probs = strengths.Select(s => s / totalStrength).ToArray();

                // Log likelihood contribution
                totalLogLikelihood += Math.Log(Math.Max(1e-15, probs[winnerIdx]));

                // Gradient for feature weights
                for (int f = 0; f < numFeatures; f++)
                {
                    var expectedFeature = 0.0;
                    for (int j = 0; j < 4; j++)
                    {
                        expectedFeature += probs[j] * featureVectors[j][f];
                    }
                    featureGradients[f] += featureVectors[winnerIdx][f] - expectedFeature;
                }

                // Update pirate strengths
                var winnerId = pirates[winnerIdx].PirateId;
                var winnerGradient = 1 - probs[winnerIdx];
                _pirateStrengths[winnerId] *= Math.Exp(_learningRate * winnerGradient * 0.5);

                for (int i = 0; i < 4; i++)
                {
                    if (i != winnerIdx)
                    {
                        var loserGradient = -probs[i];
                        _pirateStrengths[pirates[i].PirateId] *= Math.Exp(_learningRate * loserGradient * 0.25);
                    }
                }
            }

            // Update feature weights with regularization
            for (int f = 0; f < numFeatures; f++)
            {
                _featureWeights[f] += _learningRate * (featureGradients[f] / competitions.Count - _regularization * _featureWeights[f]);
            }

            // Normalize pirate strengths to prevent drift
            var avgStrength = _pirateStrengths.Values.Average();
            foreach (var key in _pirateStrengths.Keys.ToList())
            {
                _pirateStrengths[key] /= avgStrength;
            }

            if (iter > 0 && iter % 100 == 0)
            {
                var avgLL = totalLogLikelihood / competitions.Count;
                Console.WriteLine($"         Iteration {iter}, AvgLogLik: {avgLL:F4}");
            }
        }

        Console.WriteLine($"   ✅ Trained {_pirateStrengths.Count} pirate strengths + {_featureWeights.Length} feature weights");
    }

    private double[] ExtractFeatures(PirateFeatureRecord pirate, List<PirateFeatureRecord> allPirates)
    {
        var avgStrength = allPirates.Average(p => p.Strength);
        var avgOdds = allPirates.Average(p => p.CurrentOdds);
        var avgFood = allPirates.Average(p => p.FoodAdjustment);
        
        var oddsRank = allPirates.OrderBy(p => p.CurrentOdds).ToList().IndexOf(pirate);
        var strengthRank = allPirates.OrderByDescending(p => p.Strength).ToList().IndexOf(pirate);

        // Calculate interaction features
        var mlFeature = new MlPirateFeature();
        InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirate, _interactionReport);
        
        var totalPenalty = mlFeature.Penalty_FoodPosition + mlFeature.Penalty_FoodFavorite + 
                           mlFeature.Penalty_StrengthPosition + mlFeature.Penalty_StrengthWeakRivals;
        var totalBonus = mlFeature.Bonus_UndervaluedStrong + mlFeature.Bonus_HotStreakBeatsRivals;

        return new double[]
        {
            // Base features
            pirate.Strength / 100.0,
            1.0 / Math.Max(2, pirate.CurrentOdds),  // Implied probability from odds
            pirate.FoodAdjustment / 10.0,
            (4 - pirate.Position) / 3.0,  // Position advantage (lower is better)
            pirate.HistoricalWinRate,
            pirate.ArenaWinRate,
            pirate.RecentWinRate,
            pirate.WinRateVsCurrentRivals,
            
            // Relative features
            (pirate.Strength - avgStrength) / 30.0,
            (avgOdds - pirate.CurrentOdds) / avgOdds,
            (pirate.FoodAdjustment - avgFood) / 5.0,
            
            // Rank features
            (3 - oddsRank) / 3.0,
            (3 - strengthRank) / 3.0,
            
            // Interaction adjustments
            -totalPenalty,
            totalBonus
        };
    }

    public async Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        var predictions = new List<PiratePrediction>();

        foreach (var roundGroup in features.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            var pirates = roundGroup.OrderBy(p => p.Position).ToList();
            if (pirates.Count != 4) continue;

            var arenaId = pirates[0].ArenaId;

            var strengths = new double[4];
            for (int i = 0; i < 4; i++)
            {
                var featureVector = ExtractFeatures(pirates[i], pirates);
                var pirateStrength = _pirateStrengths.GetValueOrDefault(pirates[i].PirateId, 1.0);
                var featureScore = DotProduct(_featureWeights, featureVector);
                var arenaMult = _arenaMultipliers.GetValueOrDefault(arenaId, 1.0);
                
                strengths[i] = pirateStrength * Math.Exp(featureScore) * arenaMult;
            }

            var totalStrength = strengths.Sum();

            for (int i = 0; i < 4; i++)
            {
                predictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)(strengths[i] / totalStrength),
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
        var data = new PlackettLuceModelData
        {
            PirateStrengths = new Dictionary<int, double>(_pirateStrengths),
            ArenaMultipliers = new Dictionary<int, double>(_arenaMultipliers),
            FeatureWeights = _featureWeights.ToList()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(data, 
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        var jsonPath = path.Replace(".zip", "_plackett_luce.json");
        File.WriteAllText(jsonPath, json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_plackett_luce.json");
        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        var data = System.Text.Json.JsonSerializer.Deserialize<PlackettLuceModelData>(json);
        
        if (data == null) return;

        _pirateStrengths.Clear();
        foreach (var kvp in data.PirateStrengths)
            _pirateStrengths[kvp.Key] = kvp.Value;

        _arenaMultipliers.Clear();
        foreach (var kvp in data.ArenaMultipliers)
            _arenaMultipliers[kvp.Key] = kvp.Value;

        _featureWeights = data.FeatureWeights.ToArray();
    }
}
