using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NFCBets.Classical.Models;
using NFCBets.EF.Models;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Utilities;

namespace NFCBets.Evaluation;

public class BettingStrategyComparisonService : IBettingStrategyComparisonService
{
    private readonly IBettingStrategyService _bettingService;
    private readonly NfcbetsContext _context;
    private readonly IFeatureEngineeringService _featureService;
    private readonly IMlModelService _mlService;
    private readonly NaiveBettingStrategyService _naiveStrategy; // ✅ Add this

    public BettingStrategyComparisonService(
        IFeatureEngineeringService featureService,
        IMlModelService mlService,
        IBettingStrategyService bettingService,
        NfcbetsContext context)
    {
        _featureService = featureService;
        _mlService = mlService;
        _bettingService = bettingService;
        _context = context;
        _naiveStrategy = new NaiveBettingStrategyService(bettingService); // ✅ Pass injected service
    }


public async Task<StrategyComparisonReport> CompareOptimizationMethodsAsync(int startRound, int endRound, bool includeNaiveBaseline = true)
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
        Console.WriteLine($"\n🔄 Testing NAIVE BASELINE (Odds-Only)...");
        var naiveResults = await BacktestNaiveStrategyAsync(startRound, endRound);
        
        // Store as a separate entry
        comparisonReport.NaiveBaselineResults = naiveResults;

        Console.WriteLine($"   ROI: {naiveResults.OverallROI:+0.00%;-0.00%;0.00%}");
        Console.WriteLine($"   Sharpe: {naiveResults.SharpeRatio:F2}");
    }

    // Rank methods
    comparisonReport.BestByROI = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.OverallROI).First().Key;
    comparisonReport.BestBySharpe = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.SharpeRatio).First().Key;
    comparisonReport.BestByConsistency = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.WinningDaysPercentage).First().Key;
    comparisonReport.BestByProfitFactor = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.ProfitFactor).First().Key;

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
            {
                Console.WriteLine($"   Processed {currentCount}/{roundCount} {methodEnum} rounds...");
                
            }
            
            var features = await _featureService.CreateFeaturesForRoundAsync(roundId);
            if (!features.Any()) continue;

            
            var predictions = await _mlService.PredictAsync(features, false);
            var betSeries = _bettingService.GenerateBetSeriesParallel(predictions, methodEnum);

            var actualWinners = await _context.RoundResults
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
            var pirateOdds = await _context.RoundPiratePlacements
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
        var actualWinners = await _context.RoundResults
            .Where(rr => rr.RoundId == roundId && rr.IsWinner)
            .GroupBy(rr => rr.ArenaId)
            .ToDictionaryAsync(g => g.Key, g => g.First().PirateId);

        // Evaluate
        foreach (var series in betSeries)
        {
            var winningBets = 0;
            double winnings = 0.0;

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
        string improvement = "";
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
        
        Console.WriteLine($"\n💡 ML Advantage: {improvement:+0.00%;-0.00%;0.00%} ROI improvement over naive odds-only strategy");
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