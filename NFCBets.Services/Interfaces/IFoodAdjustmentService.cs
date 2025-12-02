namespace NFCBets.Services.Interfaces;

public interface IFoodAdjustmentService
{
    Task<int> CalculateFoodAdjustmentAsync(int pirateId, int roundId, int arenaId);
    Task<Dictionary<int, int>> CalculateAllArenaAdjustmentsAsync(int roundId, int arenaId);
}