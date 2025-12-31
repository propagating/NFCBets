using System.Text.Json;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical;

public class BradleyTerry : IMlStrategy
{
    private readonly Dictionary<int, double> _arenaModifiers = new();
    private readonly double _defaultStrength = 1.0;

    private readonly double _learningRate = 0.1;
    private readonly int _maxIterations = 500;

    private readonly Dictionary<int, double> _pirateStrengths = new();
    private readonly Dictionary<int, double> _positionModifiers = new();
    private InteractionAnalysisReport? _interactionReport;
    public string StrategyName => "Bradley-Terry Competition Model";

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData,
        InteractionAnalysisReport interactionReport = null)
    {
        _interactionReport = interactionReport;

        Console.WriteLine($"   Training {StrategyName}...");

        if (_interactionReport != null) Console.WriteLine("      Applying interaction controls");

        var allPirateIds = trainingData.Select(f => f.PirateId).Distinct();
        foreach (var pirateId in allPirateIds) _pirateStrengths[pirateId] = _defaultStrength;

        for (var i = 0; i < 4; i++) _positionModifiers[i] = 0;

        for (var i = 1; i <= 5; i++) _arenaModifiers[i] = 0;

        var competitions = trainingData
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .Where(g => g.Count() == 4 && g.Any(p => p.IsWinner == true))
            .ToList();

        Console.WriteLine($"      Training on {competitions.Count} competitions...");

        for (var iter = 0; iter < _maxIterations; iter++)
        {
            var totalLogLikelihood = 0.0;

            foreach (var competition in competitions)
            {
                var pirates = competition.OrderBy(p => p.Position).ToList();
                var winnerIdx = pirates.FindIndex(p => p.IsWinner == true);
                var winnerId = pirates[winnerIdx].PirateId;
                var arenaId = pirates[0].ArenaId;

                var strengths = new double[4];
                for (var i = 0; i < 4; i++)
                {
                    var baseStrength = _pirateStrengths[pirates[i].PirateId];
                    var positionMod = _positionModifiers[i];
                    var arenaMod = _arenaModifiers.GetValueOrDefault(arenaId, 0);
                    var foodMod = pirates[i].FoodAdjustment * 0.05;

                    // Apply interaction adjustments
                    var mlFeature = new MlPirateFeature();
                    InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirates[i], _interactionReport);
                    var interactionAdj = InteractionCalculator.CalculateNetInteractionAdjustment(mlFeature);

                    strengths[i] = baseStrength * Math.Exp(positionMod + arenaMod + foodMod + interactionAdj);
                }

                var totalStrength = strengths.Sum();
                var probs = strengths.Select(s => s / totalStrength).ToArray();

                totalLogLikelihood += Math.Log(Math.Max(1e-15, probs[winnerIdx]));

                var winnerGradient = 1 - probs[winnerIdx];
                _pirateStrengths[winnerId] *= Math.Exp(_learningRate * winnerGradient);

                for (var i = 0; i < 4; i++)
                    if (i != winnerIdx)
                    {
                        var loserGradient = -probs[i];
                        _pirateStrengths[pirates[i].PirateId] *= Math.Exp(_learningRate * loserGradient * 0.5);
                    }

                for (var i = 0; i < 4; i++)
                {
                    var gradient = (i == winnerIdx ? 1 : 0) - probs[i];
                    _positionModifiers[i] += _learningRate * gradient * 0.1;
                }
            }

            var avgStrength = _pirateStrengths.Values.Average();
            foreach (var key in _pirateStrengths.Keys.ToList()) _pirateStrengths[key] /= avgStrength;

            if (iter > 0 && iter % 100 == 0)
                Console.WriteLine(
                    $"         Iteration {iter}, AvgLogLik: {totalLogLikelihood / competitions.Count:F4}");
        }

        Console.WriteLine($"   ✅ Trained {_pirateStrengths.Count} pirate strengths");
        Console.WriteLine(
            $"      Position effects: {string.Join(", ", _positionModifiers.Values.Select(v => $"{v:+0.00;-0.00}"))}");
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
            for (var i = 0; i < 4; i++)
            {
                var baseStrength = _pirateStrengths.GetValueOrDefault(pirates[i].PirateId, _defaultStrength);
                var positionMod = _positionModifiers.GetValueOrDefault(i, 0);
                var arenaMod = _arenaModifiers.GetValueOrDefault(arenaId, 0);
                var foodMod = pirates[i].FoodAdjustment * 0.05;

                var oddsPrior = 1.0 / Math.Max(2, pirates[i].CurrentOdds);

                // Apply interaction adjustments
                var mlFeature = new MlPirateFeature();
                InteractionCalculator.ApplyInteractionFeatures(mlFeature, pirates[i], _interactionReport);
                var interactionAdj = InteractionCalculator.CalculateNetInteractionAdjustment(mlFeature);

                strengths[i] = baseStrength * Math.Exp(positionMod + arenaMod + foodMod + interactionAdj) *
                               (1 + oddsPrior);
            }

            var totalStrength = strengths.Sum();

            for (var i = 0; i < 4; i++)
                predictions.Add(new PiratePrediction
                {
                    RoundId = pirates[i].RoundId,
                    ArenaId = pirates[i].ArenaId,
                    PirateId = pirates[i].PirateId,
                    WinProbability = (float)(strengths[i] / totalStrength),
                    Payout = Math.Max(2, pirates[i].CurrentOdds)
                });
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

    public void SaveModel(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var data = new BradleyTerryModelData
        {
            PirateStrengths = new Dictionary<int, double>(_pirateStrengths),
            PositionModifiers = new Dictionary<int, double>(_positionModifiers),
            ArenaModifiers = new Dictionary<int, double>(_arenaModifiers)
        };

        var options = new JsonSerializerOptions { WriteIndented = true };
        var json = JsonSerializer.Serialize(data, options);
        var jsonPath = path.Replace(".zip", "_bradley_terry.json");
        File.WriteAllText(jsonPath, json);
    }

    public void LoadModel(string path)
    {
        var jsonPath = path.Replace(".zip", "_bradley_terry.json");

        if (!File.Exists(jsonPath)) return;

        var json = File.ReadAllText(jsonPath);
        var data = JsonSerializer.Deserialize<BradleyTerryModelData>(json);

        if (data == null) return;

        _pirateStrengths.Clear();
        foreach (var kvp in data.PirateStrengths)
            _pirateStrengths[kvp.Key] = kvp.Value;

        _positionModifiers.Clear();
        foreach (var kvp in data.PositionModifiers)
            _positionModifiers[kvp.Key] = kvp.Value;

        _arenaModifiers.Clear();
        foreach (var kvp in data.ArenaModifiers)
            _arenaModifiers[kvp.Key] = kvp.Value;
    }
}