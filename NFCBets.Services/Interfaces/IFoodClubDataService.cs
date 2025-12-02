namespace NFCBets.Services.Interfaces;

public interface IFoodClubDataService
{
    Task<bool> CollectAndSaveRoundAsync(int roundId);
    Task<List<int>> CollectRangeAsync(int startRound, int endRound);
}