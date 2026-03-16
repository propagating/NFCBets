using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NFCBets.Classical;
using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.EF.Models;
using NFCBets.Evaluation.Enums;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation;

public class BettingStrategyComparisonService(
    IFeatureEngineeringService featureService,
    IMlModelService mlService,
    IBettingStrategyService bettingService,
    IBacktestService backtestService,
    NfcbetsContext context)
    : IBettingStrategyComparisonService
{
    private readonly NaiveBettingStrategyService _naiveStrategy = new(bettingService);


    public async Task<StrategyComparisonReport> CompareOptimizationMethodsAsync(int startRound, int endRound,
        bool includeNaiveBaseline = true)
    {
        Console.WriteLine("📊 Comparing Bet Optimization Strategies");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var methods = new[]
        {
            BetOptimizationMethodEnum.RawEV,
            BetOptimizationMethodEnum.Kelly,
            BetOptimizationMethodEnum.ConsistencyWeighted,
            BetOptimizationMethodEnum.RiskAdjusted,
            BetOptimizationMethodEnum.CostAdjusted
        };

        var comparisonReport = new StrategyComparisonReport
        {
            StartRound = startRound,
            EndRound = endRound,
            TotalRounds = endRound - startRound + 1
        };

        // Test ML-based methods
        foreach (var method in methods)
        {
            Console.WriteLine($"🔄 Testing {method}...");
            var methodResults = await BacktestOptimizationMethodAsync(startRound, endRound, method);
            comparisonReport.MethodResults[method] = methodResults;

            Console.WriteLine($"   ROI: {methodResults.OverallROI:+0.00%;-0.00%;0.00%}");
            Console.WriteLine($"   Sharpe: {methodResults.SharpeRatio:F2}");
        }

        // ✅ NEW: Test naive baseline
        if (includeNaiveBaseline)
        {
            Console.WriteLine("\n🔄 Testing NAIVE BASELINE (Odds-Only)...");
            var naiveResults = await BacktestNaiveStrategyAsync(startRound, endRound);

            // Store as a separate entry
            comparisonReport.NaiveBaselineResults = naiveResults;

            Console.WriteLine($"   ROI: {naiveResults.OverallROI:+0.00%;-0.00%;0.00%}");
            Console.WriteLine($"   Sharpe: {naiveResults.SharpeRatio:F2}");
        }

        // Rank methods
        comparisonReport.BestByROI =
            comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.OverallROI).First().Key;
        comparisonReport.BestBySharpe =
            comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.SharpeRatio).First().Key;
        comparisonReport.BestByConsistency = comparisonReport.MethodResults
            .OrderByDescending(kv => kv.Value.WinningDaysPercentage).First().Key;
        comparisonReport.BestByProfitFactor =
            comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.ProfitFactor).First().Key;

        DisplayComparisonReportWithBaseline(comparisonReport);

        return comparisonReport;
    }

    private async Task<OptimizationMethodResults> BacktestOptimizationMethodAsync(int startRound, int endRound,
        BetOptimizationMethodEnum methodEnum)
    {
        var dailyResults = new List<DailyMethodResult>();

        double totalWinnings;
        var roundCount = endRound - startRound + 1;
        var currentCount = 0;
        for (var roundId = startRound; roundId <= endRound; roundId++)
        {
            ++currentCount;
            if (currentCount % 100 == 0)
                Console.WriteLine($"   Processed {currentCount}/{roundCount} {methodEnum} rounds...");

            var features = await featureService.CreateFeaturesForRoundAsync(roundId);
            if (!features.Any()) continue;


            var predictions = await mlService.PredictAsync(features, false);
            var betSeries = bettingService.GenerateBetSeriesParallel(predictions, methodEnum);

            var actualWinners = await context.RoundResults
                .Where(rr => rr.RoundId == roundId && rr.IsWinner)
                .GroupBy(rr => rr.ArenaId)
                .ToDictionaryAsync(g => g.Key, g => g.First().PirateId);

            // Evaluate all strategies for this method
            foreach (var series in betSeries)
            {
                var winningBets = 0;
                totalWinnings = 0.0;

                foreach (var bet in series.Bets)
                {
                    var allWon = bet.Pirates.All(p =>
                        actualWinners.TryGetValue(p.ArenaId, out var winner) && winner == p.PirateId);

                    if (allWon)
                    {
                        winningBets++;
                        totalWinnings += bet.TotalPayout;
                    }
                }

                dailyResults.Add(new DailyMethodResult
                {
                    RoundId = roundId,
                    StrategyName = series.Name,
                    TotalBets = series.Bets.Count,
                    WinningBets = winningBets,
                    NetProfit = totalWinnings - series.Bets.Count
                });
            }
        }

        // Aggregate results
        var totalCost = dailyResults.Sum(r => r.TotalBets);
        totalWinnings = dailyResults.Sum(r => r.WinningBets > 0 ? r.NetProfit + r.TotalBets : 0);
        var netProfit = totalWinnings - totalCost;

        var dailyROIs = dailyResults
            .GroupBy(r => r.RoundId)
            .Select(g => g.Sum(r => r.NetProfit) / g.Sum(r => r.TotalBets))
            .ToList();

        return new OptimizationMethodResults
        {
            MethodEnum = methodEnum,
            OverallROI = totalCost > 0 ? netProfit / totalCost : 0,
            SharpeRatio = MathUtilities.CalculateSharpeRatio(dailyROIs),
            SortinoRatio = MathUtilities.CalculateSortinoRatio(dailyROIs),
            WinningDays = dailyROIs.Count(roi => roi > 0),
            WinningDaysPercentage = dailyROIs.Count > 0 ? dailyROIs.Count(roi => roi > 0) / (double)dailyROIs.Count : 0,
            MaxDrawdown = CalculateMaxDrawdown(dailyResults),
            ProfitFactor = CalculateProfitFactor(dailyResults),
            AverageDailyROI = dailyROIs.Average(),
            MedianDailyROI = MathUtilities.CalculateMedian(dailyROIs)
        };
    }

    private async Task<OptimizationMethodResults> BacktestNaiveStrategyAsync(int startRound, int endRound)
    {
        var dailyResults = new List<DailyMethodResult>();

        for (var roundId = startRound; roundId <= endRound; roundId++)
        {
            // Get opening odds for this round
            var pirateOdds = await context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundId &&
                              rpp.StartingOdds > 1)
                .Select(rpp => new PirateOdds
                {
                    RoundId = roundId,
                    ArenaId = rpp.ArenaId!.Value,
                    PirateId = rpp.PirateId!.Value,
                    Position = rpp.PirateSeatPosition ?? 0,
                    Odds = rpp.StartingOdds
                })
                .ToListAsync();

            if (!pirateOdds.Any()) continue;

            // ✅ Use injected naive strategy
            var betSeries = _naiveStrategy.GenerateNaiveBetSeries(pirateOdds);

            // Get actual winners
            var actualWinners = await context.RoundResults
                .Where(rr => rr.RoundId == roundId && rr.IsWinner)
                .GroupBy(rr => rr.ArenaId)
                .ToDictionaryAsync(g => g.Key, g => g.First().PirateId);

            // Evaluate
            foreach (var series in betSeries)
            {
                var winningBets = 0;
                var winnings = 0.0;

                foreach (var bet in series.Bets)
                {
                    var allWon = bet.Pirates.All(p =>
                        actualWinners.TryGetValue(p.ArenaId, out var winner) && winner == p.PirateId);

                    if (allWon)
                    {
                        winningBets++;
                        winnings += bet.TotalPayout;
                    }
                }

                dailyResults.Add(new DailyMethodResult
                {
                    RoundId = roundId,
                    StrategyName = series.Name,
                    TotalBets = series.Bets.Count,
                    WinningBets = winningBets,
                    NetProfit = winnings - series.Bets.Count
                });
            }

            if ((roundId - startRound + 1) % 50 == 0)
                Console.WriteLine($"   Processed {roundId - startRound + 1}/{endRound - startRound + 1} rounds...");
        }

        // Aggregate (use same calculation as ML methods)
        var totalCost = dailyResults.Sum(r => r.TotalBets);
        var totalWinnings = dailyResults.Where(r => r.WinningBets > 0).Sum(r => r.NetProfit + r.TotalBets);
        var netProfit = totalWinnings - totalCost;

        var dailyROIs = dailyResults
            .GroupBy(r => r.RoundId)
            .Select(g => g.Sum(r => r.NetProfit) / g.Sum(r => r.TotalBets))
            .ToList();

        return new OptimizationMethodResults
        {
            MethodEnum = BetOptimizationMethodEnum.RawEV, // Use as placeholder
            OverallROI = totalCost > 0 ? netProfit / totalCost : 0,
            SharpeRatio = MathUtilities.CalculateSharpeRatio(dailyROIs),
            SortinoRatio = MathUtilities.CalculateSortinoRatio(dailyROIs),
            WinningDays = dailyROIs.Count(roi => roi > 0),
            WinningDaysPercentage = dailyROIs.Count > 0 ? dailyROIs.Count(roi => roi > 0) / (double)dailyROIs.Count : 0,
            MaxDrawdown = CalculateMaxDrawdown(dailyResults),
            ProfitFactor = CalculateProfitFactor(dailyResults),
            AverageDailyROI = dailyROIs.Average(),
            MedianDailyROI = MathUtilities.CalculateMedian(dailyROIs)
        };
    }

    private void DisplayComparisonReport(StrategyComparisonReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 BET OPTIMIZATION STRATEGY COMPARISON");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine($"Period: Rounds {report.StartRound}-{report.EndRound} ({report.TotalRounds} rounds)\n");

        Console.WriteLine("🏆 RANKINGS:\n");
        Console.WriteLine($"   Best by ROI:         {report.BestByROI}");
        Console.WriteLine($"   Best by Sharpe:      {report.BestBySharpe}");
        Console.WriteLine($"   Best by Consistency: {report.BestByConsistency}");
        Console.WriteLine($"   Best by Profit Factor: {report.BestByProfitFactor}");

        Console.WriteLine("\n📈 DETAILED COMPARISON:\n");

        var sortedByScore = report.MethodResults
            .Select(kv => new
            {
                Method = kv.Key,
                Results = kv.Value,
                Score = kv.Value.SharpeRatio * 0.4 + kv.Value.OverallROI * 100 * 0.3 +
                        kv.Value.WinningDaysPercentage * 0.3
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        foreach (var item in sortedByScore)
        {
            var r = item.Results;
            Console.WriteLine($"🎯 {item.Method}");
            Console.WriteLine($"      ROI:              {r.OverallROI:+0.00%;-0.00%;0.00%}");
            Console.WriteLine($"      Sharpe Ratio:     {r.SharpeRatio:F2}");
            Console.WriteLine($"      Sortino Ratio:    {r.SortinoRatio:F2}");
            Console.WriteLine($"      Winning Days:     {r.WinningDaysPercentage:P2}");
            Console.WriteLine($"      Profit Factor:    {r.ProfitFactor:F2}");
            Console.WriteLine($"      Max Drawdown:     {r.MaxDrawdown:P2}");
            Console.WriteLine($"      Composite Score:  {item.Score:F2} ⭐");
            Console.WriteLine();
        }

        Console.WriteLine($"🎖️ RECOMMENDED: {sortedByScore.First().Method}");
    }

    private void DisplayComparisonReportWithBaseline(StrategyComparisonReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 BET OPTIMIZATION STRATEGY COMPARISON");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine($"Period: Rounds {report.StartRound}-{report.EndRound} ({report.TotalRounds} rounds)\n");

        // Display naive baseline first
        if (report.NaiveBaselineResults != null)
        {
            Console.WriteLine("📍 NAIVE BASELINE (Odds-Only, No ML):");
            var r = report.NaiveBaselineResults;
            Console.WriteLine($"      ROI:              {r.OverallROI:+0.00%;-0.00%;0.00%}");
            Console.WriteLine($"      Sharpe Ratio:     {r.SharpeRatio:F2}");
            Console.WriteLine($"      Winning Days:     {r.WinningDaysPercentage:P2}");
            Console.WriteLine($"      Max Drawdown:     {r.MaxDrawdown:P2}");
            Console.WriteLine();
        }

        Console.WriteLine("🏆 ML-BASED STRATEGIES:\n");

        var sortedByScore = report.MethodResults
            .Select(kv => new
            {
                Method = kv.Key,
                Results = kv.Value,
                Score = kv.Value.SharpeRatio * 0.4 + kv.Value.OverallROI * 100 * 0.3 +
                        kv.Value.WinningDaysPercentage * 0.3
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        foreach (var item in sortedByScore)
        {
            var r = item.Results;

            // Calculate improvement over naive baseline
            var improvement = "";
            if (report.NaiveBaselineResults != null)
            {
                var roiDiff = r.OverallROI - report.NaiveBaselineResults.OverallROI;
                improvement = $" (vs baseline: {roiDiff:+0.00%;-0.00%;0.00%})";
            }

            Console.WriteLine($"🎯 {item.Method}");
            Console.WriteLine($"      ROI:              {r.OverallROI:+0.00%;-0.00%;0.00%}{improvement}");
            Console.WriteLine($"      Sharpe Ratio:     {r.SharpeRatio:F2}");
            Console.WriteLine($"      Sortino Ratio:    {r.SortinoRatio:F2}");
            Console.WriteLine($"      Winning Days:     {r.WinningDaysPercentage:P2}");
            Console.WriteLine($"      Profit Factor:    {r.ProfitFactor:F2}");
            Console.WriteLine($"      Max Drawdown:     {r.MaxDrawdown:P2}");
            Console.WriteLine($"      Composite Score:  {item.Score:F2} ⭐");
            Console.WriteLine();
        }

        Console.WriteLine($"🎖️ RECOMMENDED: {sortedByScore.First().Method}");

        // Show value of ML over naive
        if (report.NaiveBaselineResults != null)
        {
            var best = sortedByScore.First().Results;
            var improvement = best.OverallROI - report.NaiveBaselineResults.OverallROI;

            Console.WriteLine(
                $"\n💡 ML Advantage: {improvement:+0.00%;-0.00%;0.00%} ROI improvement over naive odds-only strategy");
        }
    }

    private void SaveComparisonReport(StrategyComparisonReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"strategy_comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"\n📄 Strategy comparison saved to {fileName}");
    }

    
        /// <summary>
    /// Compare different betting strategies for a single ML model
    /// </summary>
    public async Task<List<BettingStrategyComparisonResult>> CompareBettingStrategiesForMlModelAsync(
        IMlStrategy mlStrategy,
        List<PirateFeatureRecord> historicalData,
        decimal startingBankroll = 10000m,
        int rounds = 1000)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine($"💰 BETTING STRATEGY COMPARISON: {mlStrategy.StrategyName}");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var results = new List<BettingStrategyComparisonResult>();

        var strategies = new[]
        {
            BettingStrategyTypeEnum.Flat,
            BettingStrategyTypeEnum.QuarterKelly,
            BettingStrategyTypeEnum.HalfKelly,
            BettingStrategyTypeEnum.Kelly,
            BettingStrategyTypeEnum.ValueBetting,
            BettingStrategyTypeEnum.Proportional
        };

        foreach (var bettingStrategy in strategies)
        {
            Console.WriteLine($"📊 Testing {bettingStrategy}...");

            var config = new BacktestConfiguration
            {
                StartingBankroll = startingBankroll,
                RoundsToSimulate = rounds,
                BettingStrategy = bettingStrategy,
                MinEdgeRequired = 0.05m,
                MaxBetPercentage = bettingStrategy == BettingStrategyTypeEnum.Kelly ? 0.25m : 0.10m,
                IncludeDetailedHistory = false,
                SaveBankrollSnapshots = false
            };

            try
            {
                var strategyInstance = CreateMlStrategyInstance(mlStrategy.StrategyName);
                var backtest = await backtestService.RunBacktestAsync(strategyInstance, historicalData, config);

                results.Add(new BettingStrategyComparisonResult
                {
                    BettingStrategy = bettingStrategy,
                    MlStrategyName = mlStrategy.StrategyName,
                    ROI = backtest.ROI,
                    TotalProfit = backtest.TotalProfit,
                    WinRate = backtest.WinRate,
                    MaxDrawdown = backtest.MaxDrawdownPercentage,
                    SharpeRatio = backtest.SharpeRatio,
                    TotalBets = backtest.TotalBetsPlaced,
                    FinalBankroll = backtest.FinalBankroll
                });

                var indicator = backtest.TotalProfit >= 0 ? "✅" : "❌";
                Console.WriteLine($"   {indicator} ROI: {backtest.ROI:P2} | Profit: ${backtest.TotalProfit:N0} | MaxDD: {backtest.MaxDrawdownPercentage:P1}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Failed: {ex.Message}");
            }
        }

        DisplayBettingStrategyComparison(results, mlStrategy.StrategyName);

        return results;
    }

    /// <summary>
    /// Compare all ML models with all betting strategies (full matrix)
    /// </summary>
    public async Task<List<BettingStrategyComparisonResult>> CompareAllMlModelsWithBettingStrategiesAsync(
        List<PirateFeatureRecord> historicalData,
        decimal startingBankroll = 10000m,
        int rounds = 1000)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("💰 FULL ML MODEL × BETTING STRATEGY COMPARISON");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var allResults = new List<BettingStrategyComparisonResult>();

        var mlStrategies = GetAvailableMlStrategies();
        var bettingStrategies = new[]
        {
            BettingStrategyTypeEnum.QuarterKelly,
            BettingStrategyTypeEnum.HalfKelly,
            BettingStrategyTypeEnum.ValueBetting
        };

        int total = mlStrategies.Count * bettingStrategies.Length;
        int current = 0;

        foreach (var mlStrategy in mlStrategies)
        {
            foreach (var bettingStrategy in bettingStrategies)
            {
                current++;
                Console.WriteLine($"[{current}/{total}] {mlStrategy.StrategyName} + {bettingStrategy}...");

                var config = new BacktestConfiguration
                {
                    StartingBankroll = startingBankroll,
                    RoundsToSimulate = rounds,
                    BettingStrategy = bettingStrategy,
                    MinEdgeRequired = 0.05m,
                    MaxBetPercentage = 0.10m,
                    IncludeDetailedHistory = false,
                    SaveBankrollSnapshots = false
                };

                try
                {
                    var backtest = await backtestService.RunBacktestAsync(mlStrategy, historicalData, config);

                    allResults.Add(new BettingStrategyComparisonResult
                    {
                        BettingStrategy = bettingStrategy,
                        MlStrategyName = mlStrategy.StrategyName,
                        ROI = backtest.ROI,
                        TotalProfit = backtest.TotalProfit,
                        WinRate = backtest.WinRate,
                        MaxDrawdown = backtest.MaxDrawdownPercentage,
                        SharpeRatio = backtest.SharpeRatio,
                        TotalBets = backtest.TotalBetsPlaced,
                        FinalBankroll = backtest.FinalBankroll
                    });

                    var indicator = backtest.TotalProfit >= 0 ? "✅" : "❌";
                    Console.WriteLine($"   {indicator} ROI: {backtest.ROI:P1} | Sharpe: {backtest.SharpeRatio:F2}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ Failed: {ex.Message}");
                }
            }
        }

        DisplayFullMatrixComparison(allResults);
        
        return allResults;
    }

    private List<IMlStrategy> GetAvailableMlStrategies()
    {
        var strategies = new List<IMlStrategy>();

        TryAddStrategy(strategies, () => new MultinomialLogit());
        TryAddStrategy(strategies, () => new ConditionalLogisticRegression());
        TryAddStrategy(strategies, () => new PlackettLuce());
        TryAddStrategy(strategies, () => new BradleyTerry());
        TryAddStrategy(strategies, () => new BinaryClassification());
        TryAddStrategy(strategies, () => new MultiClassPerArena());
        TryAddStrategy(strategies, () => new NormalizedEnsemble());

        return strategies;
    }

    private void TryAddStrategy(List<IMlStrategy> strategies, Func<IMlStrategy> factory)
    {
        try
        {
            strategies.Add(factory());
        }
        catch
        {
            // Skip failed strategies
        }
    }

    private IMlStrategy CreateMlStrategyInstance(string strategyName)
    {
        return strategyName switch
        {
            "Conditional Logistic (Choice Model)" => new ConditionalLogisticRegression(),
            "Bradley-Terry Competition Model" => new BradleyTerry(),
            "Plackett-Luce (Generalized Bradley-Terry)" => new PlackettLuce(),
            "Multinomial Logit (Choice Model)" => new MultinomialLogit(),
            "Binary Classification (LightGBM)" => new BinaryClassification(),
            "Logistic Regression" => new LogisticRegression(),
            "Multi-Class Per Arena" => new MultiClassPerArena(),
            "Multi-Class with Pairwise Features" => new MultiClassPairwise(),
            "Stacking Ensemble (Meta-Learner)" => new StackingEnsemble(),
            "Normalized Ensemble" => new NormalizedEnsemble(),
            "Softmax Per Arena" => new SoftmaxPerArena(),
            "Learn to Rank" => new LearnToRank(),
            "Pairwise Comparison" => new PairwiseComparison(),
            _ => throw new ArgumentException($"Unknown strategy: {strategyName}")
        };
    }

    private void DisplayBettingStrategyComparison(List<BettingStrategyComparisonResult> results, string mlStrategyName)
    {
        Console.WriteLine("\n" + new string('─', 95));
        Console.WriteLine($"📊 BETTING STRATEGY COMPARISON FOR: {mlStrategyName}");
        Console.WriteLine(new string('─', 95) + "\n");

        var sorted = results.OrderByDescending(r => r.ROI).ToList();

        Console.WriteLine($"{"Rank",-5} {"Betting Strategy",-20} {"ROI",-12} {"Profit",-14} {"Win Rate",-10} {"MaxDD",-10} {"Sharpe",-10} {"Bets",-6}");
        Console.WriteLine(new string('─', 95));

        for (int i = 0; i < sorted.Count; i++)
        {
            var r = sorted[i];
            var medal = i switch
            {
                0 => "🥇",
                1 => "🥈",
                2 => "🥉",
                _ => "  "
            };

            var profitStr = r.TotalProfit >= 0 ? $"${r.TotalProfit:N0}" : $"-${Math.Abs(r.TotalProfit):N0}";
            var roiStr = r.ROI >= 0 ? $"+{r.ROI:P1}" : $"{r.ROI:P1}";

            Console.WriteLine($"{medal}{i + 1,-3} {r.BettingStrategy,-20} {roiStr,-12} {profitStr,-14} {r.WinRate:P1,-10} {r.MaxDrawdown:P1,-10} {r.SharpeRatio:F2,-10} {r.TotalBets,-6}");
        }

        Console.WriteLine(new string('─', 95));

        // Recommendations
        var best = sorted.FirstOrDefault();
        var safest = sorted.Where(r => r.ROI > 0).OrderBy(r => r.MaxDrawdown).FirstOrDefault();
        var bestSharpe = sorted.OrderByDescending(r => r.SharpeRatio).FirstOrDefault();

        if (best != null)
        {
            Console.WriteLine("\n🎯 RECOMMENDATIONS:");
            Console.WriteLine($"   💰 Best ROI:           {best.BettingStrategy} ({best.ROI:P2})");
            
            if (safest != null)
            {
                Console.WriteLine($"   🛡️ Safest Profitable:  {safest.BettingStrategy} (MaxDD: {safest.MaxDrawdown:P1})");
            }
            
            if (bestSharpe != null)
            {
                Console.WriteLine($"   📊 Best Risk-Adjusted: {bestSharpe.BettingStrategy} (Sharpe: {bestSharpe.SharpeRatio:F2})");
            }
        }

        // Risk warning for full Kelly
        var kellyResult = results.FirstOrDefault(r => r.BettingStrategy == BettingStrategyTypeEnum.Kelly);
        if (kellyResult != null && kellyResult.MaxDrawdown > 0.3m)
        {
            Console.WriteLine($"\n⚠️ WARNING: Full Kelly shows {kellyResult.MaxDrawdown:P0} max drawdown - consider fractional Kelly");
        }
    }

    private void DisplayFullMatrixComparison(List<BettingStrategyComparisonResult> results)
    {
        Console.WriteLine("\n" + new string('═', 110));
        Console.WriteLine("📊 FULL ML MODEL × BETTING STRATEGY MATRIX");
        Console.WriteLine(new string('═', 110) + "\n");

        // Group by ML strategy and find best betting strategy for each
        var byMlStrategy = results
            .GroupBy(r => r.MlStrategyName)
            .Select(g => new
            {
                MlStrategy = g.Key,
                BestResult = g.OrderByDescending(r => r.ROI).First(),
                AllResults = g.ToList()
            })
            .OrderByDescending(x => x.BestResult.ROI)
            .ToList();

        Console.WriteLine($"{"Rank",-5} {"ML Strategy",-40} {"Best Betting",-15} {"ROI",-10} {"Profit",-12} {"Sharpe",-8}");
        Console.WriteLine(new string('─', 100));

        for (int i = 0; i < byMlStrategy.Count; i++)
        {
            var x = byMlStrategy[i];
            var medal = i switch
            {
                0 => "🥇",
                1 => "🥈",
                2 => "🥉",
                _ => "  "
            };

            var profitStr = x.BestResult.TotalProfit >= 0 ? $"${x.BestResult.TotalProfit:N0}" : $"-${Math.Abs(x.BestResult.TotalProfit):N0}";

            Console.WriteLine($"{medal}{i + 1,-3} {x.MlStrategy,-40} {x.BestResult.BettingStrategy,-15} {x.BestResult.ROI:P1,-10} {profitStr,-12} {x.BestResult.SharpeRatio:F2}");
        }

        Console.WriteLine(new string('─', 100));

        // Overall best combination
        var overallBest = results.OrderByDescending(r => r.ROI).First();
        Console.WriteLine("\n🏆 BEST COMBINATION:");
        Console.WriteLine($"   ML Model: {overallBest.MlStrategyName}");
        Console.WriteLine($"   Betting Strategy: {overallBest.BettingStrategy}");
        Console.WriteLine($"   ROI: {overallBest.ROI:P2} | Profit: ${overallBest.TotalProfit:N2} | Sharpe: {overallBest.SharpeRatio:F2}");
    }

    
    // Helper methods


    private double CalculateMaxDrawdown(List<DailyMethodResult> results)
    {
        var cumulative = 0.0;
        var peak = 0.0;
        var maxDrawdown = 0.0;

        foreach (var result in results.OrderBy(r => r.RoundId))
        {
            cumulative += result.NetProfit / result.TotalBets;
            peak = Math.Max(peak, cumulative);
            var drawdown = peak - cumulative;
            maxDrawdown = Math.Max(maxDrawdown, drawdown);
        }

        return maxDrawdown;
    }

    private double CalculateProfitFactor(List<DailyMethodResult> results)
    {
        var grossProfit = results.Where(r => r.NetProfit > 0).Sum(r => r.NetProfit);
        var grossLoss = Math.Abs(results.Where(r => r.NetProfit < 0).Sum(r => r.NetProfit));
        return grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.MaxValue : 0;
    }
}