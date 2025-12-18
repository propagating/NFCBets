namespace NFCBets.Services.Interfaces;

public interface IFoodClubDataService
{
    Task<bool> CollectAndSaveRoundAsync(int roundId);
    Task<List<int>> CollectRangeAsync(int startRound, int endRound, bool forceCollect = false, int maxParallel = 5);
    Task<List<int>> CollectRangeParallelAsync(int startRound, int endRound, bool forceCollect = false, int maxParallel = 5);
}