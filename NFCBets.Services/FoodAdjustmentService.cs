using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;

namespace NFCBets.Services
{
    public interface IFoodAdjustmentService
    {
        Task<int> CalculateFoodAdjustmentAsync(int pirateId, int roundId, int arenaId);
        Task<Dictionary<int, int>> CalculateAllArenaAdjustmentsAsync(int roundId, int arenaId);
    }

    public class FoodAdjustmentService : IFoodAdjustmentService
    {
        private readonly NfcbetsContext _context;

        public FoodAdjustmentService(NfcbetsContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Calculate food adjustment for a specific pirate in a specific arena/round.
        /// Returns -3 to +3 based on (preferences count - allergies count) for food categories served.
        /// </summary>
        public async Task<int> CalculateFoodAdjustmentAsync(int pirateId, int roundId, int arenaId)
        {
            // Get food categories served in this arena for this round
            var servedFoodCategories = await GetServedFoodCategoriesAsync(roundId, arenaId);
            
            if (!servedFoodCategories.Any())
                return 0;

            // Count pirate's preferences for served categories
            var preferences = await _context.FoodCategoryPreferences
                .Where(fcp => fcp.PirateId == pirateId && 
                             servedFoodCategories.Contains(fcp.FoodCategoryId))
                .CountAsync();

            // Count pirate's allergies for served categories  
            var allergies = await _context.FoodCategoryAllergies
                .Where(fca => fca.PirateId == pirateId && 
                             servedFoodCategories.Contains(fca.FoodCategoryId))
                .CountAsync();

            // Calculate adjustment: preferences add positive, allergies subtract
            var adjustment = preferences - allergies;

            // Clamp to range [-3, +3]
            return adjustment;
        }

        /// <summary>
        /// Calculate food adjustments for all pirates in a specific arena/round.
        /// </summary>
        public async Task<Dictionary<int, int>> CalculateAllArenaAdjustmentsAsync(int roundId, int arenaId)
        {
            var adjustments = new Dictionary<int, int>();

            // Get all pirates in this arena for this round
            var pirateIds = await _context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundId && rpp.ArenaId == arenaId)
                .Select(rpp => rpp.PirateId!.Value)
                .ToListAsync();

            // Get served food categories
            var servedFoodCategories = await GetServedFoodCategoriesAsync(roundId, arenaId);
            
            if (!servedFoodCategories.Any())
            {
                // No food served, all pirates get 0
                return pirateIds.ToDictionary(id => id, id => 0);
            }

            // Get all preferences for these pirates and categories
            var preferences = await _context.FoodCategoryPreferences
                .Where(fcp => pirateIds.Contains(fcp.PirateId) && 
                             servedFoodCategories.Contains(fcp.FoodCategoryId))
                .GroupBy(fcp => fcp.PirateId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Get all allergies for these pirates and categories
            var allergies = await _context.FoodCategoryAllergies
                .Where(fca => pirateIds.Contains(fca.PirateId) && 
                             servedFoodCategories.Contains(fca.FoodCategoryId))
                .GroupBy(fca => fca.PirateId)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            // Calculate adjustments
            foreach (var pirateId in pirateIds)
            {
                var prefCount = preferences.GetValueOrDefault(pirateId, 0);
                var allergyCount = allergies.GetValueOrDefault(pirateId, 0);
                var adjustment = prefCount - allergyCount;
                
                adjustments[pirateId] = adjustment;
            }

            return adjustments;
        }

        /// <summary>
        /// Get all food category IDs served in a specific arena for a round.
        /// </summary>
        private async Task<List<int>> GetServedFoodCategoriesAsync(int roundId, int arenaId)
        {
            var foods = await _context.RoundFoodCourses
                .Where(rfc => rfc.RoundId == roundId && rfc.ArenaId == arenaId)
                .Select(x => x.FoodId)
                .ToListAsync();

            return await _context.FoodCategoryFoods
                .Where(fcf => foods.Contains(fcf.FoodId))
                .Select(fcf => fcf.FoodCategoryId)
                .Distinct().ToListAsync();
        }
    }
}