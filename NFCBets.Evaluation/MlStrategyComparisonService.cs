using System.Text.Json;
using NFCBets.Classical;
using NFCBets.Classical.Interfaces;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Evaluation;

public class MlStrategyComparisonService : IMlStrategyComparisonService
{
    private readonly IFeatureEngineeringService _featureService;
    private readonly Dictionary<string, IMlStrategy> _strategies;

    public MlStrategyComparisonService(IFeatureEngineeringService featureService)
    {
        _featureService = featureService;
        
        // Register all strategies to compare
        _strategies = new Dictionary<string, IMlStrategy>
        {
            { "Binary", new BinaryClassification() },
            { "MultiClass", new MultiClassPerArena() },
            { "Softmax", new SoftmaxPerArena() },
            { "Ranking", new LearnToRank() },
            { "Ensemble", new EnsembleStrategy() }
        };
    }

    public async Task<MlStrategyComparisonReport> CompareAllStrategiesAsync()
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🏆 ML STRATEGY COMPARISON");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var report = new MlStrategyComparisonReport
        {
            ComparisonDate = DateTime.UtcNow
        };

        // Load and split data
        var trainingData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = trainingData.Where(f => f.IsWinner.HasValue).ToList();

        var uniqueRounds = validData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var roundSplitIndex = (int)(uniqueRounds.Count * 0.8);
        
        var trainRoundIds = uniqueRounds.Take(roundSplitIndex).ToHashSet();
        var testRoundIds = uniqueRounds.Skip(roundSplitIndex).ToHashSet();
        
        var trainData = validData.Where(f => trainRoundIds.Contains(f.RoundId)).ToList();
        var testData = validData.Where(f => testRoundIds.Contains(f.RoundId)).ToList();

        Console.WriteLine($"Training: {trainData.Count} records from {trainRoundIds.Count} rounds");
        Console.WriteLine($"Testing:  {testData.Count} records from {testRoundIds.Count} rounds\n");

        // Test each strategy
        foreach (var (name, strategy) in _strategies)
        {
            Console.WriteLine($"🔄 Testing {strategy.StrategyName}...");
            
            try
            {
                await strategy.TrainAsync(trainData);
                var evaluation = await strategy.EvaluateAsync(testData);
                
                report.StrategyResults[name] = new MlStrategyResult
                {
                    StrategyName = strategy.StrategyName,
                    AUC = evaluation.AUC,
                    Accuracy = evaluation.Accuracy,
                    F1Score = evaluation.F1Score,
                    LogLoss = evaluation.LogLoss,
                    TrainingTime = 0 // TODO: measure
                };
                
                Console.WriteLine($"   AUC: {evaluation.AUC:F4}");
                Console.WriteLine($"   Accuracy: {evaluation.Accuracy:P2}");
                Console.WriteLine($"   F1 Score: {evaluation.F1Score:F4}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed: {ex.Message}\n");
            }
        }

        // Determine best strategy
        if (report.StrategyResults.Any())
        {
            report.BestByAUC = report.StrategyResults.OrderByDescending(kv => kv.Value.AUC).First().Key;
            report.BestByAccuracy = report.StrategyResults.OrderByDescending(kv => kv.Value.Accuracy).First().Key;
            report.BestByF1 = report.StrategyResults.OrderByDescending(kv => kv.Value.F1Score).First().Key;
        }

        DisplayComparisonReport(report);
        SaveComparisonReport(report);

        return report;
    }

    private void DisplayComparisonReport(MlStrategyComparisonReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 ML STRATEGY COMPARISON RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var sorted = report.StrategyResults.OrderByDescending(kv => kv.Value.AUC);

        foreach (var (name, result) in sorted)
        {
            Console.WriteLine($"🎯 {result.StrategyName}");
            Console.WriteLine($"   AUC:       {result.AUC:F4}");
            Console.WriteLine($"   Accuracy:  {result.Accuracy:P2}");
            Console.WriteLine($"   F1 Score:  {result.F1Score:F4}");
            Console.WriteLine($"   Log Loss:  {result.LogLoss:F4}");
            Console.WriteLine();
        }

        Console.WriteLine("🏆 RANKINGS:");
        Console.WriteLine($"   Best by AUC:      {report.BestByAUC}");
        Console.WriteLine($"   Best by Accuracy: {report.BestByAccuracy}");
        Console.WriteLine($"   Best by F1:       {report.BestByF1}");
    }

    private void SaveComparisonReport(MlStrategyComparisonReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"ml_strategy_comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);
        Console.WriteLine($"\n📄 ML strategy comparison saved to {fileName}");
    }
}