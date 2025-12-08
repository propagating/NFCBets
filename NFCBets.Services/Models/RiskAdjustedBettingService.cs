using NFCBets.Utilities.Models;

namespace NFCBets.Services.Models;

public class RiskAdjustedBettingService
{
    private const double UNIT_BET_SIZE = 4000; // Account age * 2
    private const double RISK_FREE_RATE = 0.02; // 2% baseline return

    // Method 1: Kelly Criterion Adjusted EV
    public double CalculateKellyAdjustedEV(Bet bet, double bankroll = UNIT_BET_SIZE)
    {
        var p = bet.CombinedWinProbability;
        var b = bet.TotalPayout - 1; // Net odds

        if (p <= 0 || b <= 0) return double.MinValue;

        // Kelly fraction: f = (bp - q) / b where q = 1-p
        var kellyFraction = (b * p - (1 - p)) / b;

        // Don't bet if Kelly is negative
        if (kellyFraction <= 0) return double.MinValue;

        // Fractional Kelly (more conservative - use 1/4 Kelly)
        var fractionalKelly = kellyFraction * 0.25;

        // Risk-adjusted EV = EV * Kelly Fraction * Bankroll
        return bet.ExpectedValue * fractionalKelly * bankroll;
    }

    // Method 2: Sharpe Ratio for Bet Series
    public double CalculateSharpeRatio(List<BetSeriesResult> historicalResults)
    {
        if (!historicalResults.Any()) return 0;

        var returns = historicalResults.Select(r => r.ROI).ToList();
        var avgReturn = returns.Average();
        var stdDev = Math.Sqrt(returns.Sum(r => Math.Pow(r - avgReturn, 2)) / returns.Count);

        return stdDev > 0 ? (avgReturn - RISK_FREE_RATE) / stdDev : 0;
    }

    // Method 3: Consistency-Weighted EV
    public double CalculateConsistencyWeightedEV(Bet bet, double winRateWeight = 0.5)
    {
        var baseEV = bet.ExpectedValue;
        var winProbability = bet.CombinedWinProbability;

        // Penalize low-probability bets even if they have positive EV
        var consistencyFactor = Math.Pow(winProbability, winRateWeight);

        return baseEV * consistencyFactor;
    }

    // Method 4: Risk-Adjusted Return (EV / Variance)
    public double CalculateRiskAdjustedReturn(Bet bet)
    {
        var p = bet.CombinedWinProbability;
        var payout = bet.TotalPayout;

        // Expected value
        var ev = p * payout - 1;

        // Variance of this bet
        var variance = p * Math.Pow(payout - ev - 1, 2) + (1 - p) * Math.Pow(-ev - 1, 2);
        var stdDev = Math.Sqrt(variance);

        // Return per unit of risk
        return stdDev > 0 ? ev / stdDev : ev;
    }

    // Method 5: Cost-Adjusted EV with Fixed Bet Size
    public double CalculateCostAdjustedEV(Bet bet, double unitBetSize = UNIT_BET_SIZE)
    {
        var p = bet.CombinedWinProbability;
        var payout = bet.TotalPayout;

        // Actual profit if bet wins
        var profitIfWin = payout * unitBetSize - unitBetSize;

        // Expected profit
        var expectedProfit = p * profitIfWin - (1 - p) * unitBetSize;

        // Return as percentage of bankroll
        return expectedProfit / unitBetSize;
    }

    // Method 6: Downside Risk (Sortino Ratio)
    public double CalculateSortinoRatio(List<BetSeriesResult> historicalResults)
    {
        if (!historicalResults.Any()) return 0;

        var returns = historicalResults.Select(r => r.ROI).ToList();
        var avgReturn = returns.Average();

        // Only penalize downside volatility
        var downsideReturns = returns.Where(r => r < RISK_FREE_RATE).ToList();
        var downsideDeviation = downsideReturns.Any()
            ? Math.Sqrt(downsideReturns.Sum(r => Math.Pow(r - RISK_FREE_RATE, 2)) / downsideReturns.Count)
            : 0.001; // Small number to avoid division by zero

        return (avgReturn - RISK_FREE_RATE) / downsideDeviation;
    }
}