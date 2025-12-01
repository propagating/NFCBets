namespace NFCBets.Services;

public interface IDailyBettingPipeline
{
    Task<DailyBettingRecommendations> GenerateRecommendationsAsync(int roundId);
}