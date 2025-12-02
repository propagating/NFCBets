using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public interface IBettingPerformanceEvaluator
{
    Task<BettingPerformanceReport> BacktestBettingStrategyAsync(int startRound, int endRound);
    Task<List<int>> FindRoundsWithMultipleWinnersAsync(int startRound, int endRound);
}

public class BettingPerformanceEvaluator : IBettingPerformanceEvaluator
{
    private readonly IFeatureEngineeringService _featureService;
    private readonly IMlModelService _mlService;
    private readonly IBettingStrategyService _bettingService;
    private readonly NfcbetsContext _context;

    public BettingPerformanceEvaluator(
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
    
    public async Task<BettingPerformanceReport> BacktestBettingStrategyAsync(int startRound, int endRound)
    {
        Console.WriteLine($"🔄 Backtesting betting strategy from round {startRound} to {endRound}...");

        // First, check for data quality issues
        await CheckDataQualityAsync(startRound, endRound);

        var report = new BettingPerformanceReport
        {
            StartRound = startRound,
            EndRound = endRound
        };

        var dailyResults = new List<DailyBettingResult>();

        for (int roundId = startRound; roundId <= endRound; roundId++)
        {
            // Generate predictions for this round
            var features = await _featureService.CreateFeaturesForRoundAsync(roundId);
            if (!features.Any()) continue;

            var predictions = await _mlService.PredictAsync(features);
            var betSeries = _bettingService.GenerateBetSeries(predictions);

            // Get actual winners - FIXED: Handle duplicates
            var winnerResults = await _context.RoundResults
                .Where(rr => rr.RoundId == roundId && rr.IsWinner)
                .ToListAsync();

            // Group by arena and take first winner per arena
            var actualWinners = winnerResults
                .GroupBy(rr => rr.ArenaId)
                .ToDictionary(g => g.Key, g => g.First().PirateId);

            // Evaluate each series
            foreach (var series in betSeries)
            {
                var seriesResult = EvaluateBetSeries(series, actualWinners);
                dailyResults.Add(new DailyBettingResult
                {
                    RoundId = roundId,
                    SeriesName = series.Name,
                    Result = seriesResult
                });
            }

            if ((roundId - startRound + 1) % 50 == 0)
            {
                Console.WriteLine($"   Processed {roundId - startRound + 1}/{endRound - startRound + 1} rounds...");
            }
        }

        // Aggregate results
        report.StrategyResults = dailyResults
            .GroupBy(dr => dr.SeriesName)
            .Select(g => CalculateStrategyMetrics(g.Key, g.Select(dr => dr.Result).ToList()))
            .ToList();

        DisplayBacktestReport(report);
        return report;
    }

    private async Task CheckDataQualityAsync(int startRound, int endRound)
    {
        Console.WriteLine("🔍 Checking data quality...");

        var roundsWithMultipleWinners = await FindRoundsWithMultipleWinnersAsync(startRound, endRound);

        if (roundsWithMultipleWinners.Any())
        {
            Console.WriteLine($"⚠️ Warning: Found {roundsWithMultipleWinners.Count} rounds with data quality issues");
            Console.WriteLine($"   These rounds will use the first winner per arena.");
        }
        else
        {
            Console.WriteLine($"✅ Data quality check passed - no duplicate winners found");
        }
    }

    public async Task<List<int>> FindRoundsWithMultipleWinnersAsync(int startRound, int endRound)
    {
        // Pull all winners to client side first
        var allWinners = await _context.RoundResults
            .Where(rr => rr.IsWinner && 
                         rr.RoundId.HasValue && 
                         rr.RoundId >= startRound && 
                         rr.RoundId <= endRound)
            .Select(rr => new { rr.RoundId, rr.ArenaId, rr.PirateId })
            .ToListAsync();

        // Do the grouping in memory
        var roundsWithIssues = allWinners
            .GroupBy(rr => new { rr.RoundId, rr.ArenaId })
            .Where(g => g.Count() > 1)
            .Select(g => g.Key.RoundId!.Value)
            .Distinct()
            .ToList();

        if (roundsWithIssues.Any())
        {
            Console.WriteLine($"\n⚠️ Data Quality Issues - {roundsWithIssues.Count} rounds with multiple winners per arena:");
        
            foreach (var roundId in roundsWithIssues.Take(10))
            {
                var duplicates = allWinners
                    .Where(w => w.RoundId == roundId)
                    .GroupBy(w => w.ArenaId)
                    .Where(g => g.Count() > 1)
                    .ToList();

                foreach (var dup in duplicates)
                {
                    var pirateIds = string.Join(", ", dup.Select(d => d.PirateId));
                    Console.WriteLine($"   Round {roundId}, Arena {dup.Key}: Pirates {pirateIds} all marked as winners");
                }
            }

            if (roundsWithIssues.Count > 10)
            {
                Console.WriteLine($"   ... and {roundsWithIssues.Count - 10} more rounds with issues");
            }
        }
        else
        {
            Console.WriteLine("✅ No duplicate winners found");
        }

        return roundsWithIssues;
    }
    private BetSeriesResult EvaluateBetSeries(BetSeries series, Dictionary<int, int> actualWinners)
    {
        var result = new BetSeriesResult
        {
            TotalBets = series.Bets.Count,
            BetCost = series.Bets.Count
        };

        foreach (var bet in series.Bets)
        {
            // Check if ALL pirates in this bet won their arenas
            var allWon = bet.Pirates.All(p => 
                actualWinners.TryGetValue(p.ArenaId, out var winner) && winner == p.PirateId);

            if (allWon)
            {
                result.WinningBets++;
                result.TotalWinnings += bet.TotalPayout;
            }
        }

        result.NetProfit = result.TotalWinnings - result.BetCost;
        result.ROI = result.BetCost > 0 ? result.NetProfit / result.BetCost : 0;

        return result;
    }

    private StrategyMetrics CalculateStrategyMetrics(string strategyName, List<BetSeriesResult> dailyResults)
    {
        var totalCost = dailyResults.Sum(r => r.BetCost);
        var totalWinnings = dailyResults.Sum(r => r.TotalWinnings);
        var totalNetProfit = totalWinnings - totalCost;

        var dailyROIs = dailyResults.Select(r => r.ROI).ToList();
        var winningDays = dailyResults.Count(r => r.NetProfit > 0);

        return new StrategyMetrics
        {
            StrategyName = strategyName,
            TotalDays = dailyResults.Count,
            TotalBets = dailyResults.Sum(r => r.TotalBets),
            TotalWinningBets = dailyResults.Sum(r => r.WinningBets),
            HitRate = dailyResults.Sum(r => r.TotalBets) > 0 ? 
                dailyResults.Sum(r => r.WinningBets) / (double)dailyResults.Sum(r => r.TotalBets) : 0,
            
            TotalCost = totalCost,
            TotalWinnings = totalWinnings,
            NetProfit = totalNetProfit,
            ROI = totalCost > 0 ? totalNetProfit / totalCost : 0,
            
            WinningDays = winningDays,
            WinningDaysPercentage = winningDays / (double)dailyResults.Count,
            
            AverageDailyROI = dailyROIs.Average(),
            BestDayROI = dailyROIs.Max(),
            WorstDayROI = dailyROIs.Min(),
            
            SharpeRatio = CalculateSharpeRatio(dailyROIs),
            MaxDrawdown = CalculateMaxDrawdown(dailyResults)
        };
    }

    private double CalculateSharpeRatio(List<double> returns)
    {
        if (!returns.Any()) return 0;

        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Count);

        return stdDev > 0 ? avgReturn / stdDev : 0;
    }

    private double CalculateMaxDrawdown(List<BetSeriesResult> results)
    {
        var cumulativeProfit = 0.0;
        var peak = 0.0;
        var maxDrawdown = 0.0;

        foreach (var result in results)
        {
            cumulativeProfit += result.NetProfit;
            peak = Math.Max(peak, cumulativeProfit);
            var drawdown = peak - cumulativeProfit;
            maxDrawdown = Math.Max(maxDrawdown, drawdown);
        }

        return maxDrawdown;
    }

    private void DisplayBacktestReport(BettingPerformanceReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("💰 BETTING STRATEGY BACKTEST RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"Period: Round {report.StartRound} to {report.EndRound}\n");

        foreach (var strategy in report.StrategyResults.OrderByDescending(s => s.ROI))
        {
            Console.WriteLine($"🎯 {strategy.StrategyName.ToUpper()}");
            Console.WriteLine($"   Days Tested:       {strategy.TotalDays}");
            Console.WriteLine($"   Total Bets:        {strategy.TotalBets}");
            Console.WriteLine($"   Winning Bets:      {strategy.TotalWinningBets} ({strategy.HitRate:P2} hit rate)");
            Console.WriteLine($"   ───────────────────────────────────");
            Console.WriteLine($"   Total Cost:        {strategy.TotalCost:N0} units");
            Console.WriteLine($"   Total Winnings:    {strategy.TotalWinnings:N0} units");
            Console.WriteLine($"   Net Profit:        {strategy.NetProfit:+N0;-N0} units");
            Console.WriteLine($"   ROI:               {strategy.ROI:+P2;-P2}");
            Console.WriteLine($"   ───────────────────────────────────");
            Console.WriteLine($"   Winning Days:      {strategy.WinningDays}/{strategy.TotalDays} ({strategy.WinningDaysPercentage:P2})");
            Console.WriteLine($"   Average Daily ROI: {strategy.AverageDailyROI:+P2;-P2}");
            Console.WriteLine($"   Best Day:          {strategy.BestDayROI:+P2;-P2}");
            Console.WriteLine($"   Worst Day:         {strategy.WorstDayROI:+P2;-P2}");
            Console.WriteLine($"   Sharpe Ratio:      {strategy.SharpeRatio:F2}");
            Console.WriteLine($"   Max Drawdown:      {strategy.MaxDrawdown:N0} units");
            Console.WriteLine();
        }

        // Overall recommendation
        var bestStrategy = report.StrategyResults.OrderByDescending(s => s.SharpeRatio).First();
        Console.WriteLine($"🏆 BEST RISK-ADJUSTED STRATEGY: {bestStrategy.StrategyName}");
        Console.WriteLine($"   (Highest Sharpe Ratio: {bestStrategy.SharpeRatio:F2})");
    }
}

// Result classes




