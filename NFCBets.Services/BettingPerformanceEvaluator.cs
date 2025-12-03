using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets.Services;

public class BettingPerformanceEvaluator : IBettingPerformanceEvaluator
{
    private const double UNIT_BET_SIZE = 4000; // Account age * 2
    private const double RISK_FREE_RATE = 0.02; // 2% baseline return
    private readonly IBettingStrategyService _bettingService;
    private readonly NfcbetsContext _context;
    private readonly IFeatureEngineeringService _featureService;
    private readonly IMlModelService _mlService;


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

    public async Task<BettingPerformanceReport> BacktestBettingStrategyAsync(int startRound, int endRound,
        BetOptimizationMethod method = BetOptimizationMethod.ConsistencyWeighted)
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

        for (var roundId = startRound; roundId <= endRound; roundId++)
        {
            // Generate predictions for this round
            var features = await _featureService.CreateFeaturesForRoundAsync(roundId);
            if (!features.Any()) continue;

            var predictions = await _mlService.PredictAsync(features);
            var betSeries = _bettingService.GenerateBetSeriesParallel(predictions, method);

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
                Console.WriteLine($"   Processed {roundId - startRound + 1}/{endRound - startRound + 1} rounds...");
        }

        // Aggregate results
        report.StrategyResults = dailyResults
            .GroupBy(dr => dr.SeriesName)
            .Select(g => CalculateStrategyMetrics(g.Key, g.Select(dr => dr.Result).ToList()))
            .ToList();

        DisplayBacktestReport(report);
        return report;
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
            Console.WriteLine(
                $"\n⚠️ Data Quality Issues - {roundsWithIssues.Count} rounds with multiple winners per arena:");

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
                    Console.WriteLine(
                        $"   Round {roundId}, Arena {dup.Key}: Pirates {pirateIds} all marked as winners");
                }
            }

            if (roundsWithIssues.Count > 10)
                Console.WriteLine($"   ... and {roundsWithIssues.Count - 10} more rounds with issues");
        }
        else
        {
            Console.WriteLine("✅ No duplicate winners found");
        }

        return roundsWithIssues;
    }

    private async Task CheckDataQualityAsync(int startRound, int endRound)
    {
        Console.WriteLine("🔍 Checking data quality...");

        var roundsWithMultipleWinners = await FindRoundsWithMultipleWinnersAsync(startRound, endRound);

        if (roundsWithMultipleWinners.Any())
        {
            Console.WriteLine($"⚠️ Warning: Found {roundsWithMultipleWinners.Count} rounds with data quality issues");
            Console.WriteLine("   These rounds will use the first winner per arena.");
        }
        else
        {
            Console.WriteLine("✅ Data quality check passed - no duplicate winners found");
        }
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
        var totalCost = dailyResults.Sum(r => r.BetCost) * UNIT_BET_SIZE;
        var totalWinnings = dailyResults.Sum(r => r.TotalWinnings) * UNIT_BET_SIZE;
        var totalNetProfit = totalWinnings - totalCost;

        var dailyROIs = dailyResults.Select(r => r.ROI).ToList();
        var winningDays = dailyResults.Count(r => r.NetProfit > 0);
        var losingDays = dailyResults.Count(r => r.NetProfit < 0);

        // Calculate streaks
        var (winStreak, lossStreak) = MathUtilities.CalculateStreaks(dailyResults);

        // Calculate profit factor
        var grossProfit = dailyResults.Where(r => r.NetProfit > 0).Sum(r => r.NetProfit * UNIT_BET_SIZE);
        var grossLoss = Math.Abs(dailyResults.Where(r => r.NetProfit < 0).Sum(r => r.NetProfit * UNIT_BET_SIZE));
        var profitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? double.MaxValue : 0;

        // Risk metrics
        var avgReturn = dailyROIs.Average();
        var stdDev = MathUtilities.CalculateStandardDeviation(dailyROIs);
        var sharpeRatio = stdDev > 0 ? (avgReturn - RISK_FREE_RATE) / stdDev : 0;
        var sortinoRatio = MathUtilities.CalculateSortinoRatio(dailyROIs);

        // Consistency score (0-1, higher is better)
        var consistencyScore = MathUtilities.CalculateConsistencyScore(dailyResults, dailyROIs);

        // Risk-adjusted score combining multiple factors
        var riskAdjustedScore =
            MathUtilities.CalculateRiskAdjustedScore(avgReturn, sharpeRatio, consistencyScore, profitFactor);

        return new StrategyMetrics
        {
            StrategyName = strategyName,
            TotalDays = dailyResults.Count,
            TotalBets = dailyResults.Sum(r => r.TotalBets),
            TotalWinningBets = dailyResults.Sum(r => r.WinningBets),
            HitRate = dailyResults.Sum(r => r.TotalBets) > 0
                ? dailyResults.Sum(r => r.WinningBets) / (double)dailyResults.Sum(r => r.TotalBets)
                : 0,

            TotalCost = totalCost,
            TotalWinnings = totalWinnings,
            NetProfit = totalNetProfit,
            ROI = totalCost > 0 ? totalNetProfit / totalCost : 0,

            WinningDays = winningDays,
            WinningDaysPercentage = winningDays / (double)dailyResults.Count,

            AverageDailyROI = avgReturn,
            MedianDailyROI = MathUtilities.CalculateMedian(dailyROIs),
            BestDayROI = dailyROIs.Max(),
            WorstDayROI = dailyROIs.Min(),

            SharpeRatio = sharpeRatio,
            SortinoRatio = sortinoRatio,
            MaxDrawdown = MathUtilities.CalculateMaxDrawdown(dailyResults) * UNIT_BET_SIZE,
            VolatilityStdDev = stdDev,

            WinStreakMax = winStreak,
            LossStreakMax = lossStreak,
            ProfitFactor = profitFactor,

            ConsistencyScore = consistencyScore,
            RiskAdjustedScore = riskAdjustedScore
        };
    }


    private void DisplayBacktestReport(BettingPerformanceReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("💰 BETTING STRATEGY BACKTEST RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════");
        Console.WriteLine($"Period: Round {report.StartRound} to {report.EndRound}");
        Console.WriteLine($"Bet Unit Size: {UNIT_BET_SIZE:N0} NP\n");

        // Sort by risk-adjusted score
        foreach (var strategy in report.StrategyResults.OrderByDescending(s => s.RiskAdjustedScore))
        {
            Console.WriteLine($"🎯 {strategy.StrategyName.ToUpper()}");
            Console.WriteLine("   Returns:");
            Console.WriteLine($"      Net Profit:     {strategy.NetProfit:+N0;-N0} NP");
            Console.WriteLine($"      ROI:            {strategy.ROI:+P2;-P2}");
            Console.WriteLine($"      Hit Rate:       {strategy.HitRate:P2}");
            Console.WriteLine("   Consistency:");
            Console.WriteLine(
                $"      Winning Days:   {strategy.WinningDays}/{strategy.TotalDays} ({strategy.WinningDaysPercentage:P2})");
            Console.WriteLine($"      Median ROI:     {strategy.MedianDailyROI:+P2;-P2}");
            Console.WriteLine($"      Win Streak:     {strategy.WinStreakMax} days");
            Console.WriteLine($"      Loss Streak:    {strategy.LossStreakMax} days");
            Console.WriteLine("   Risk Metrics:");
            Console.WriteLine($"      Sharpe Ratio:   {strategy.SharpeRatio:F2}");
            Console.WriteLine($"      Sortino Ratio:  {strategy.SortinoRatio:F2}");
            Console.WriteLine($"      Volatility:     {strategy.VolatilityStdDev:P2}");
            Console.WriteLine($"      Max Drawdown:   {strategy.MaxDrawdown:N0} NP");
            Console.WriteLine($"      Profit Factor:  {strategy.ProfitFactor:F2}");
            Console.WriteLine("   Scores:");
            Console.WriteLine($"      Consistency:    {strategy.ConsistencyScore:F3} ⭐");
            Console.WriteLine($"      Risk-Adjusted:  {strategy.RiskAdjustedScore:F3} 🏆");
            Console.WriteLine();
        }

        var bestStrategy = report.StrategyResults.OrderByDescending(s => s.RiskAdjustedScore).First();
        Console.WriteLine($"🏆 RECOMMENDED STRATEGY: {bestStrategy.StrategyName}");
        Console.WriteLine($"   Risk-Adjusted Score: {bestStrategy.RiskAdjustedScore:F3}");
        Console.WriteLine($"   Sharpe Ratio: {bestStrategy.SharpeRatio:F2}");
        Console.WriteLine($"   ROI: {bestStrategy.ROI:+P2;-P2}");
    }
}

// Result classes