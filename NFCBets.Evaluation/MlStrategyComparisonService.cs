using NFCBets.Causal;
using NFCBets.Classical.Interfaces;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Utilities.Models;
using System.Diagnostics;
using NFCBets.Classical;
using NFCBets.Evaluation.Enums;

namespace NFCBets.Evaluation;

public class MlStrategyComparisonService : IMlStrategyComparisonService
{
    private readonly IFeatureEngineeringService _featureService;
    private readonly IBacktestService _backtestService;
    private Dictionary<string, IMlStrategy> _strategies = new();

    public MlStrategyComparisonService(
        IFeatureEngineeringService featureService,
        IBacktestService backtestService)
    {
        _featureService = featureService;
        _backtestService = backtestService;
        RegisterStrategies();
    }

    private void RegisterStrategies()
    {
        Console.WriteLine("📋 Registering ML strategies for comparison...");
        
        _strategies = new Dictionary<string, IMlStrategy>();
        
        // Original strategies
        TryRegister("LogisticRegression", () => new LogisticRegression());
        TryRegister("Binary", () => new BinaryClassification());
        TryRegister("ConditionalLogistic", () => new ConditionalLogisticRegression());
        TryRegister("BradleyTerry", () => new BradleyTerry());
        TryRegister("Pairwise", () => new PairwiseComparison());
        TryRegister("MultiClass", () => new MultiClassPerArena());
        TryRegister("Softmax", () => new SoftmaxPerArena());
        TryRegister("Ranking", () => new LearnToRank());
        TryRegister("MultiOutput", () => new MultiOutput());
        
        // New improved strategies
        TryRegister("MultiClassPairwise", () => new MultiClassPairwise());
        TryRegister("PlackettLuce", () => new PlackettLuce());
        TryRegister("MultinomialLogit", () => new MultinomialLogit());
        TryRegister("NormalizedEnsemble", () => new NormalizedEnsemble());
        TryRegister("StackingEnsemble", () => new StackingEnsemble());
        
        Console.WriteLine($"\n   Total strategies registered: {_strategies.Count}\n");
    }

    private void TryRegister(string name, Func<IMlStrategy> factory)
    {
        try
        {
            _strategies[name] = factory();
            Console.WriteLine($"   ✅ {_strategies[name].StrategyName} registered");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ {name} failed: {ex.Message}");
        }
    }

    public async Task<MlStrategyComparisonReport> CompareAllStrategiesAsync(
        InteractionAnalysisReport? interactionReport = null,
        bool includeBacktest = true,
        BacktestConfiguration? backtestConfig = null)
    {
        var overallStopwatch = Stopwatch.StartNew();
        
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🏆 ML STRATEGY COMPARISON");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var report = new MlStrategyComparisonReport
        {
            ComparisonDate = DateTime.UtcNow,
            TotalStrategiesTested = _strategies.Count
        };

        // Load and prepare data
        Console.WriteLine("📊 Loading training data...");
        var allData = await _featureService.CreateTrainingDataAsync(4000);
        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();
        
        Console.WriteLine($"   Loaded {validData.Count} valid records");

        // Run interaction analysis if not provided
        if (interactionReport == null)
        {
            Console.WriteLine("\n🔬 Running interaction analysis...");
            var analyzer = new InteractionEffectAnalyzer();
            interactionReport = await analyzer.AnalyzeAllInteractionsAsync(validData);
        }

        report.AntagonisticInteractionsFound = interactionReport.AntagonisticInteractions.Count;
        report.SynergisticInteractionsFound = interactionReport.SynergisticInteractions.Count;

        // Split data by rounds (time-based split)
        var uniqueRounds = validData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var splitIndex = (int)(uniqueRounds.Count * 0.8);
        
        var trainRounds = uniqueRounds.Take(splitIndex).ToHashSet();
        var testRounds = uniqueRounds.Skip(splitIndex).ToHashSet();
        
        var trainData = validData.Where(f => trainRounds.Contains(f.RoundId)).ToList();
        var testData = validData.Where(f => testRounds.Contains(f.RoundId)).ToList();

        report.TrainingRecords = trainData.Count;
        report.TestRecords = testData.Count;
        report.TrainingRounds = trainRounds.Count;
        report.TestRounds = testRounds.Count;

        Console.WriteLine($"   Training: {trainData.Count} records ({trainRounds.Count} rounds)");
        Console.WriteLine($"   Testing:  {testData.Count} records ({testRounds.Count} rounds)");

        // Phase 1: Statistical Evaluation
        Console.WriteLine("\n" + new string('═', 60));
        Console.WriteLine("📊 PHASE 1: STATISTICAL EVALUATION");
        Console.WriteLine(new string('═', 60) + "\n");

        var results = new List<MlStrategyResult>();
        int completed = 0;
        int total = _strategies.Count;

        foreach (var (name, strategy) in _strategies)
        {
            completed++;
            Console.WriteLine($"🎯 [{completed}/{total}] Testing {strategy.StrategyName}...");
            
            var result = new MlStrategyResult
            {
                StrategyName = strategy.StrategyName
            };
            
            try
            {
                var stopwatch = Stopwatch.StartNew();
                
                await strategy.TrainAsync(trainData, interactionReport);
                
                stopwatch.Stop();
                result.TrainingTime = stopwatch.Elapsed;
                
                var evalReport = await strategy.EvaluateAsync(testData);
                
                result.Auc = evalReport.AUC;
                result.Accuracy = evalReport.Accuracy;
                result.LogLoss = evalReport.LogLoss;
                result.F1Score = evalReport.F1Score;
                result.Precision = evalReport.Precision;
                result.Recall = evalReport.Recall;
                
                Console.WriteLine($"   AUC: {result.Auc:F4} | Accuracy: {result.Accuracy:P1} | LogLoss: {result.LogLoss:F4} | Time: {result.TrainingTime.TotalSeconds:F1}s");
            }
            catch (Exception ex)
            {
                result.ErrorMessage = ex.Message;
                Console.WriteLine($"   ❌ FAILED: {ex.Message}");
            }
            
            results.Add(result);
        }

        // Sort and rank statistical results
        var sortedResults = results
            .Where(r => r.ErrorMessage == null)
            .OrderByDescending(r => r.Auc)
            .ToList();

        for (int i = 0; i < sortedResults.Count; i++)
        {
            sortedResults[i].Rank = i + 1;
            sortedResults[i].IsRecommended = (i == 0);
        }

        var failedResults = results.Where(r => r.ErrorMessage != null).ToList();
        foreach (var failed in failedResults)
        {
            failed.Rank = sortedResults.Count + 1;
        }

        report.Results = sortedResults.Concat(failedResults).ToList();
        report.SuccessfulStrategies = sortedResults.Count;

        if (sortedResults.Any())
        {
            var best = sortedResults.First();
            report.RecommendedStrategy = best.StrategyName;
            report.BestAuc = best.Auc;
            report.BestAccuracy = best.Accuracy;
            report.BestLogLoss = best.LogLoss;
        }

        DisplayStatisticalSummary(report);

        // Phase 2: Backtest Evaluation
        if (includeBacktest)
        {
            Console.WriteLine("\n" + new string('═', 60));
            Console.WriteLine("💰 PHASE 2: BACKTEST EVALUATION");
            Console.WriteLine(new string('═', 60) + "\n");

            backtestConfig ??= new BacktestConfiguration
            {
                StartingBankroll = 10000m,
                RoundsToSimulate = Math.Min(1000, testRounds.Count),
                BettingStrategy = BettingStrategyTypeEnum.QuarterKelly,
                MinEdgeRequired = 0.05m,
                MaxBetPercentage = 0.10m,
                IncludeDetailedHistory = false
            };

            report.BacktestIncluded = true;
            report.BacktestConfig = backtestConfig;

            // Re-register strategies for fresh backtest (they need to be retrained)
            RegisterStrategies();

            var backtestResults = await _backtestService.CompareStrategiesBacktestAsync(
                _strategies, 
                validData, 
                backtestConfig);

            report.BacktestResults = backtestResults;

            // Merge backtest results into strategy results
            foreach (var backtestResult in backtestResults)
            {
                var strategyResult = report.Results.FirstOrDefault(r => r.StrategyName == backtestResult.StrategyName);
                if (strategyResult != null)
                {
                    strategyResult.BacktestROI = backtestResult.ROI;
                    strategyResult.BacktestWinRate = backtestResult.WinRate;
                    strategyResult.BacktestProfit = backtestResult.TotalProfit;
                    strategyResult.BacktestMaxDrawdown = backtestResult.MaxDrawdownPercentage;
                    strategyResult.BacktestSharpeRatio = backtestResult.SharpeRatio;
                }
            }

            // Rank by backtest ROI
            var sortedByBacktest = report.Results
                .Where(r => r.BacktestROI.HasValue)
                .OrderByDescending(r => r.BacktestROI)
                .ToList();

            for (int i = 0; i < sortedByBacktest.Count; i++)
            {
                sortedByBacktest[i].BacktestRank = i + 1;
            }

            if (backtestResults.Any())
            {
                var bestBacktest = backtestResults.OrderByDescending(r => r.ROI).First();
                report.BestBacktestStrategy = bestBacktest.StrategyName;
                report.BestBacktestROI = bestBacktest.ROI;
            }
        }

        overallStopwatch.Stop();
        report.TotalComparisonTime = overallStopwatch.Elapsed;

        // Final Summary
        DisplayFinalSummary(report);
        SaveComparisonReport(report);

        return report;
    }

    private void DisplayStatisticalSummary(MlStrategyComparisonReport report)
    {
        Console.WriteLine("\n" + new string('─', 80));
        Console.WriteLine("📊 STATISTICAL EVALUATION RESULTS");
        Console.WriteLine(new string('─', 80) + "\n");

        Console.WriteLine($"{"Rank",-5} {"Strategy",-40} {"AUC",-10} {"Accuracy",-10} {"LogLoss",-10} {"Time",-8}");
        Console.WriteLine(new string('─', 85));

        foreach (var result in report.Results.Where(r => r.ErrorMessage == null).OrderBy(r => r.Rank))
        {
            var medal = result.Rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => "  "
            };

            Console.WriteLine($"{medal}{result.Rank,-3} {result.StrategyName,-40} {result.Auc:F4,-10} {result.Accuracy:P1,-10} {result.LogLoss:F4,-10} {result.TrainingTime.TotalSeconds:F1}s");
        }

        // Show failed
        var failed = report.Results.Where(r => r.ErrorMessage != null).ToList();
        if (failed.Any())
        {
            Console.WriteLine();
            foreach (var result in failed)
            {
                Console.WriteLine($"  ❌ {result.StrategyName,-40} FAILED: {result.ErrorMessage}");
            }
        }

        Console.WriteLine(new string('─', 85));
        Console.WriteLine($"\n🏆 Best Statistical Model: {report.RecommendedStrategy} (AUC: {report.BestAuc:F4})");
    }

    private void DisplayFinalSummary(MlStrategyComparisonReport report)
    {
        Console.WriteLine("\n" + new string('═', 80));
        Console.WriteLine("🎯 FINAL RECOMMENDATIONS");
        Console.WriteLine(new string('═', 80) + "\n");

        Console.WriteLine($"📊 Statistical Analysis:");
        Console.WriteLine($"   Best Model: {report.RecommendedStrategy}");
        Console.WriteLine($"   AUC: {report.BestAuc:F4} | Accuracy: {report.BestAccuracy:P2}");

        if (report.BacktestIncluded && report.BacktestResults.Any())
        {
            Console.WriteLine($"\n💰 Backtest Analysis:");
            Console.WriteLine($"   Best Model: {report.BestBacktestStrategy}");
            Console.WriteLine($"   ROI: {report.BestBacktestROI:P2}");

            // Compare statistical vs backtest winners
            if (report.RecommendedStrategy != report.BestBacktestStrategy)
            {
                Console.WriteLine($"\n⚠️ DIVERGENCE DETECTED:");
                Console.WriteLine($"   Statistical winner ({report.RecommendedStrategy}) differs from backtest winner ({report.BestBacktestStrategy})");
                Console.WriteLine($"   Consider using {report.BestBacktestStrategy} for actual betting.");
            }
            else
            {
                Console.WriteLine($"\n✅ CONSISTENT RESULTS:");
                Console.WriteLine($"   Both statistical and backtest analysis recommend {report.RecommendedStrategy}");
            }

            // Find balanced recommendation
            var balancedResults = report.Results
                .Where(r => r.BacktestROI.HasValue && r.BacktestROI > 0 && r.ErrorMessage == null)
                .Select(r => new
                {
                    Result = r,
                    Score = (r.Auc * 0.3) + ((double)r.BacktestROI!.Value * 0.4) + ((double)r.BacktestSharpeRatio!.Value * 0.1 / 10) + (1 - (double)r.BacktestMaxDrawdown!.Value) * 0.2
                })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (balancedResults != null)
            {
                Console.WriteLine($"\n🎯 BALANCED RECOMMENDATION (considering all factors):");
                Console.WriteLine($"   {balancedResults.Result.StrategyName}");
                Console.WriteLine($"   AUC: {balancedResults.Result.Auc:F4} | ROI: {balancedResults.Result.BacktestROI:P2} | Sharpe: {balancedResults.Result.BacktestSharpeRatio:F2}");
            }
        }

        Console.WriteLine($"\n⏱️ Total comparison time: {report.TotalComparisonTime.TotalSeconds:F1}s");
    }

    private void SaveComparisonReport(MlStrategyComparisonReport report)
    {
        try
        {
            Directory.CreateDirectory("Reports");
            var fileName = Path.Combine("Reports", $"ml_comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions 
            { 
                WriteIndented = true 
            });
            File.WriteAllText(fileName, json);
            Console.WriteLine($"\n📄 Full comparison report saved to {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ Could not save report: {ex.Message}");
        }
    }
}