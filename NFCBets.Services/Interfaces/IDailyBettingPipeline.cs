using NFCBets.Services.Enums;
using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IDailyBettingPipeline
{
    Task<DailyBettingRecommendations> GenerateRecommendationsAsync(int roundId, BetOptimizationMethod method);
}