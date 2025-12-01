using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Repository.Interfaces;
using NFCBets.Repository.Models;

namespace NFCBets.Repository;

public class FoodClubRepository(NfcbetsContext conext) : IFoodClubRepository
{
    private readonly NfcbetsContext _db = conext;


    public async Task<List<Pirate>> GetAllPiratesAsync()
    {
        return await _db.Pirates
            .Include(p => p.FoodCategoryPreferences)
            .Include(p => p.FoodCategoryAllergies)
            .ToListAsync();
    }

    public async Task<Pirate?> GetPirateByIdAsync(int pirateId)
    {
        return await _db.Pirates
            .Include(p => p.FoodCategoryPreferences)
            .Include(p => p.FoodCategoryAllergies)
            .FirstOrDefaultAsync(p => p.PirateId == pirateId);
    }

    public async Task<Food?> GetFoodByIdAsync(int foodId)
    {
        return await _db.Foods
            .Include(f => f.FoodCategoryFoods)
            .FirstOrDefaultAsync(f => f.FoodId == foodId);
    }


    public async Task<List<FoodCategoryPreference>> GetPirateFoodPreferencesAsync(int pirateId)
    {
        return await _db.FoodCategoryPreferences
            .Include(fp => fp.FoodCategory)
            .ThenInclude(fc => fc.FoodCategoryFoods)
            .ThenInclude(fcf => fcf.Food)
            .Where(fp => fp.PirateId == pirateId)
            .ToListAsync();
    }

    public async Task<List<FoodCategoryAllergy>> GetPirateFoodAllergiesAsync(int pirateId)
    {
        return await _db.FoodCategoryAllergies
            .Include(fp => fp.FoodCategory)
            .ThenInclude(fc => fc.FoodCategoryFoods)
            .ThenInclude(fcf => fcf.Food)
            .Where(fp => fp.PirateId == pirateId)
            .ToListAsync();
    }

    public async Task<int> CalculateTotalPirateFoodAdjustmentAsync(int pirateId, List<int> foodIds)
    {
        var foods = await _db.Foods.Where(f => foodIds.Contains(f.FoodId)).ToListAsync();
        var foodCategories = await _db.FoodCategoryFoods
            .Where(fcf => foods.Select(f => f.FoodId).Contains(fcf.Food.FoodId))
            .Select(fcf => fcf.FoodCategoryId)
            .ToListAsync();

        var preferences = await _db.FoodCategoryPreferences
            .Where(fp => fp.PirateId == pirateId && foodCategories.Contains(fp.FoodCategoryId))
            .ToListAsync();

        var allergies = await _db.FoodCategoryAllergies
            .Where(fp => fp.PirateId == pirateId && foodCategories.Contains(fp.FoodCategoryId))
            .ToListAsync();


        return preferences.Count - allergies.Count;
    }

    public async Task<int> CalculatePirateFoodAdjustmentAsync(int pirateId, List<int> foods)
    {
        var baseAdjustment = 0;
        foreach (var foodId in foods)
        {
            var foodCategories = await _db.FoodCategoryFoods
                .Where(fcf => fcf.FoodId == foodId)
                .Select(fcf => fcf.FoodCategoryId)
                .ToListAsync();

            var preferences = await _db.FoodCategoryPreferences
                .Where(fp => fp.PirateId == pirateId && foodCategories.Contains(fp.FoodCategoryId))
                .ToListAsync();

            var allergies = await _db.FoodCategoryAllergies
                .Where(fp => fp.PirateId == pirateId && foodCategories.Contains(fp.FoodCategoryId))
                .ToListAsync();

            baseAdjustment += preferences.Count - allergies.Count;
        }


        return baseAdjustment;
    }

    public async Task SaveRoundData(List<FoodClubRound> rounds)
    {
        foreach (var round in rounds) await SaveSingleRoundData(round);
    }

    public async Task SaveSingleRoundData(FoodClubRound round)
    {
        var existingPlacements = await _db.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == round.Round)
            .ToListAsync();

        var existingFoodCourses = await _db.RoundFoodCourses
            .Where(rfc => rfc.RoundId == round.Round)
            .ToListAsync();

        var existingResults = await _db.RoundResults
            .Where(rr => rr.RoundId == round.Round)
            .ToListAsync();

        var placementLookup = existingPlacements
            .ToDictionary(p => new { p.RoundId, p.ArenaId, p.PirateId }, p => p);

        var foodCourseLookup = existingFoodCourses
            .ToDictionary(f => new { f.RoundId, f.ArenaId, f.FoodId }, f => f);

        var resultLookup = existingResults
            .ToDictionary(r => new { r.RoundId, r.ArenaId, r.PirateId }, r => r);

        for (var i = 0; i < round.Pirates.Count; i++)
        {
            var arenaId = i;
            var pirateIds = round.Pirates[i];
            var foodIds = round.Foods[i];
            var winnerIds = round.Winners;
            var openingOds = round.OpeningOdds[i];
            var currentOds = round.CurrentOdds[i];

            // Handle RoundPiratePlacements
            for (var p = 0; p < pirateIds.Count; p++)
            {
                var pirate = pirateIds[p];
                var placementKey = new
                    { RoundId = (int?)round.Round, ArenaId = (int?)arenaId, PirateId = (int?)pirate };
                var foodIdsForArena = round.Foods[i];
                var adjustment = await CalculatePirateFoodAdjustmentAsync(pirate, foodIdsForArena);
                if (placementLookup.TryGetValue(placementKey, out var existingPlacement))
                {
                    // Update existing placement
                    existingPlacement.PirateSeatPosition = p;
                    existingPlacement.PirateFoodAdjustment = adjustment;
                    existingPlacement.CurrentOdds = currentOds[p];
                }
                else
                {
                    // Add new placement
                    var newPlacement = new RoundPiratePlacement
                    {
                        RoundId = round.Round,
                        ArenaId = arenaId,
                        PirateId = pirate,
                        PirateSeatPosition = p,
                        PirateFoodAdjustment = 0,
                        StartingOdds = openingOds[p],
                        CurrentOdds = currentOds[p]
                    };

                    await _db.RoundPiratePlacements.AddAsync(newPlacement);
                }
            }

            // Handle RoundFoodCourses
            foreach (var food in foodIds)
            {
                var foodCourseKey = new { RoundId = round.Round, ArenaId = arenaId, FoodId = food };

                if (!foodCourseLookup.ContainsKey(foodCourseKey))
                {
                    // Add new food course (these typically don't change, so no update needed)
                    var newFoodCourse = new RoundFoodCourse
                    {
                        RoundId = round.Round,
                        ArenaId = arenaId,
                        FoodId = food
                    };

                    await _db.RoundFoodCourses.AddAsync(newFoodCourse);
                }
            }

            // Handle RoundResults
            for (var p = 0; p < pirateIds.Count; p++)
            {
                var pirate = pirateIds[p];
                var resultKey = new { RoundId = (int?)round.Round, ArenaId = arenaId, PirateId = pirate };

                if (resultLookup.TryGetValue(resultKey, out var existingResult))
                {
                    // Update existing result
                    existingResult.EndingOdds = currentOds[p];
                    existingResult.IsWinner = winnerIds.Contains(pirate);
                    existingResult.IsComplete = round.IsComplete;
                }
                else
                {
                    // Add new result
                    var newResult = new RoundResult
                    {
                        RoundId = round.Round,
                        ArenaId = arenaId,
                        PirateId = pirate,
                        EndingOdds = currentOds[p],
                        IsWinner = winnerIds.Contains(pirate),
                        IsComplete = round.IsComplete
                    };

                    await _db.RoundResults.AddAsync(newResult);
                }
            }
        }

        // Save all changes at once
        await _db.SaveChangesAsync();
    }

    public async Task<bool> RoundExistsAsync(int roundNumber)
    {
        return await _db.RoundResults.Where(r => r.RoundId == roundNumber).AnyAsync();
    }

    public async Task UpdatePirateOdds(int roundDataRound, Change change)
    {
        var oddsToChange = await _db.RoundPiratePlacements
            .Where(x => x.ArenaId == change.Arena && x.RoundId == roundDataRound &&
                        x.PirateSeatPosition == change.Pirate).FirstOrDefaultAsync();
        oddsToChange?.CurrentOdds = change.New;
        await _db.SaveChangesAsync();
    }
}