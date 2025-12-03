using NFCBets.Utilities.Models;

namespace NFCBets.Utilities;

public static class MathUtilities
{
    public static double CalculateStandardDeviation(IEnumerable<double> values)
    {
        var valueList = values.ToList();
        if (valueList.Count < 2) return 0;

        var mean = valueList.Average();
        var variance = valueList.Sum(v => Math.Pow(v - mean, 2)) / valueList.Count;
        return Math.Sqrt(variance);
    }

    public static double CalculateMedian(IEnumerable<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (!sorted.Any()) return 0;

        var middle = sorted.Count / 2;
        return sorted.Count % 2 == 0
            ? (sorted[middle - 1] + sorted[middle]) / 2
            : sorted[middle];
    }

    public static double CalculatePercentile(IEnumerable<double> values, double percentile)
    {
        var sorted = values.OrderBy(v => v).ToList();
        if (!sorted.Any()) return 0;

        var index = (int)Math.Ceiling(sorted.Count * percentile) - 1;
        index = Math.Max(0, Math.Min(index, sorted.Count - 1));

        return sorted[index];
    }


    public static double CalculateConsistencyScore(List<BetSeriesResult> results, List<double> returns)
    {
        var winRate = results.Count(r => r.NetProfit > 0) / (double)results.Count;
        var returnStability = 1.0 / (1.0 + CalculateStandardDeviation(returns));
        var noExtremeLosses = results.All(r => r.ROI > -0.5) ? 1.0 : 0.5;

        return winRate * 0.5 + returnStability * 0.3 + noExtremeLosses * 0.2;
    }

    public static double CalculateRiskAdjustedScore(double avgReturn, double sharpe, double consistency,
        double profitFactor)
    {
        // Weighted combination of metrics
        return avgReturn * 0.3 +
               sharpe * 0.3 +
               consistency * 0.2 +
               Math.Min(profitFactor / 5.0, 1.0) * 0.2;
    }

    public static (double WinStreak, double LossStreak) CalculateStreaks(List<BetSeriesResult> results)
    {
        int currentWinStreak = 0, maxWinStreak = 0;
        int currentLossStreak = 0, maxLossStreak = 0;

        foreach (var result in results)
            if (result.NetProfit > 0)
            {
                currentWinStreak++;
                maxWinStreak = Math.Max(maxWinStreak, currentWinStreak);
                currentLossStreak = 0;
            }
            else if (result.NetProfit < 0)
            {
                currentLossStreak++;
                maxLossStreak = Math.Max(maxLossStreak, currentLossStreak);
                currentWinStreak = 0;
            }

        return (maxWinStreak, maxLossStreak);
    }


    public static double CalculateSortinoRatio(List<double> returns, double riskFreeRate = 0.02)
    {
        var avgReturn = returns.Average();
        var downsideReturns = returns.Where(r => r < riskFreeRate).ToList();

        if (!downsideReturns.Any()) return avgReturn > riskFreeRate ? 999 : 0;

        var downsideDeviation =
            Math.Sqrt(downsideReturns.Sum(r => Math.Pow(r - riskFreeRate, 2)) / downsideReturns.Count);
        return downsideDeviation > 0 ? (avgReturn - riskFreeRate) / downsideDeviation : 0;
    }

    public static double CalculateSharpeRatio(List<double> returns)
    {
        if (!returns.Any()) return 0;

        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Count);

        return stdDev > 0 ? avgReturn / stdDev : 0;
    }

    public static double CalculateMaxDrawdown(List<BetSeriesResult> results)
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
}