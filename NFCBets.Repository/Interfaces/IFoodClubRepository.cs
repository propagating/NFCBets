using NFCBets.EF.Models;
using NFCBets.Repository.Models;

namespace NFCBets.Repository.Interfaces;

public interface IFoodClubRepository
{
    Task<List<Pirate>> GetAllPiratesAsync();
    Task<Pirate?> GetPirateByIdAsync(int pirateId);
    Task<List<FoodCategoryPreference>> GetPirateFoodPreferencesAsync(int pirateId);
    Task<List<FoodCategoryAllergy>> GetPirateFoodAllergiesAsync(int pirateId);
    Task<int> CalculateTotalPirateFoodAdjustmentAsync(int pirateId, List<int> foods);
    Task<int> CalculatePirateFoodAdjustmentAsync(int pirateId, List<int> foodId);
    Task<Food?> GetFoodByIdAsync(int foodId);
    Task SaveRoundData(List<FoodClubRound> rounds);
    Task SaveSingleRoundData(FoodClubRound round);
    Task<bool> RoundExistsAsync(int roundNumber);
    Task UpdatePirateOdds(int roundDataRound, Change change);
}