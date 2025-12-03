using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;

namespace NFCBets.Evaluation;

public class BettingStrategyComparisonService : IBettingStrategyComparisonService
{
    private readonly IFeatureEngineeringService _featureService;
    private readonly IMlModelService _mlService;
    private readonly IBettingStrategyService _bettingService;
    private readonly NfcbetsContext _context;

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
    }

    public async Task<StrategyComparisonReport> CompareOptimizationMethodsAsync(int startRound, int endRound)
    {
        Console.WriteLine("📊 Comparing Bet Optimization Strategies");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var methods = new[]
        {
            BetOptimizationMethod.RawEV,
            BetOptimizationMethod.Kelly,
            BetOptimizationMethod.ConsistencyWeighted,
            BetOptimizationMethod.RiskAdjusted,
            BetOptimizationMethod.CostAdjusted
        };

        var comparisonReport = new StrategyComparisonReport
        {
            StartRound = startRound,
            EndRound = endRound,
            TotalRounds = endRound - startRound + 1
        };

        foreach (var method in methods)
        {
            Console.WriteLine($"🔄 Testing {method}...");
            
            var methodResults = await BacktestOptimizationMethodAsync(startRound, endRound, method);
            comparisonReport.MethodResults[method] = methodResults;

            Console.WriteLine($"   ROI: {methodResults.OverallROI:+P2;-P2}");
            Console.WriteLine($"   Sharpe: {methodResults.SharpeRatio:F2}");
            Console.WriteLine($"   Winning Days: {methodResults.WinningDaysPercentage:P2}");
        }

        // Rank methods by different criteria
        comparisonReport.BestByROI = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.OverallROI).First().Key;
        comparisonReport.BestBySharpe = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.SharpeRatio).First().Key;
        comparisonReport.BestByConsistency = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.WinningDaysPercentage).First().Key;
        comparisonReport.BestByProfitFactor = comparisonReport.MethodResults.OrderByDescending(kv => kv.Value.ProfitFactor).First().Key;

        DisplayComparisonReport(comparisonReport);
        SaveComparisonReport(comparisonReport);

        return comparisonReport;
    }

    private async Task<OptimizationMethodResults> BacktestOptimizationMethodAsync(int startRound, int endRound, BetOptimizationMethod method)
    {
        var dailyResults = new List<DailyMethodResult>();

        double totalWinnings;
        for (int roundId = startRound; roundId <= endRound; roundId++)
        {
            var features = await _featureService.CreateFeaturesForRoundAsync(roundId);
            if (!features.Any()) continue;

            var predictions = await _mlService.PredictAsync(features);
            var betSeries = _bettingService.GenerateBetSeries(predictions, method);

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
            Method = method,
            OverallROI = totalCost > 0 ? netProfit / totalCost : 0,
            SharpeRatio = CalculateSharpe(dailyROIs),
            SortinoRatio = CalculateSortino(dailyROIs),
            WinningDays = dailyROIs.Count(roi => roi > 0),
            WinningDaysPercentage = dailyROIs.Count > 0 ? dailyROIs.Count(roi => roi > 0) / (double)dailyROIs.Count : 0,
            MaxDrawdown = CalculateMaxDrawdown(dailyResults),
            ProfitFactor = CalculateProfitFactor(dailyResults),
            AverageDailyROI = dailyROIs.Average(),
            MedianDailyROI = CalculateMedian(dailyROIs)
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
                Score = (kv.Value.SharpeRatio * 0.4) + (kv.Value.OverallROI * 100 * 0.3) + (kv.Value.WinningDaysPercentage * 0.3)
            })
            .OrderByDescending(x => x.Score)
            .ToList();

        foreach (var item in sortedByScore)
        {
            var r = item.Results;
            Console.WriteLine($"🎯 {item.Method}");
            Console.WriteLine($"      ROI:              {r.OverallROI:+P2;-P2}");
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

    private void SaveComparisonReport(StrategyComparisonReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"strategy_comparison_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        
        var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);
        
        Console.WriteLine($"\n📄 Strategy comparison saved to {fileName}");
    }

    // Helper methods
    private double CalculateSharpe(List<double> returns)
    {
        if (!returns.Any()) return 0;
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Count);
        return stdDev > 0 ? avgReturn / stdDev : 0;
    }

    private double CalculateSortino(List<double> returns)
    {
        if (!returns.Any()) return 0;
        var avgReturn = returns.Average();
        var downsideReturns = returns.Where(r => r < 0).ToList();
        if (!downsideReturns.Any()) return avgReturn > 0 ? 999 : 0;
        
        var downsideDeviation = Math.Sqrt(downsideReturns.Sum(r => Math.Pow(r, 2)) / downsideReturns.Count);
        return downsideDeviation > 0 ? avgReturn / downsideDeviation : 0;
    }

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

    private double CalculateMedian(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (!sorted.Any()) return 0;
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[middle - 1] + sorted[middle]) / 2 : sorted[middle];
    }
}