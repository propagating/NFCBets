using NFCBets.Services.Enums;
using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IBettingPerformanceEvaluator
{
    Task<BettingPerformanceReport> BacktestBettingStrategyAsync(int startRound, int endRound,
        BetOptimizationMethodEnum methodEnum);

    Task<List<int>> FindRoundsWithMultipleWinnersAsync(int startRound, int endRound);
}