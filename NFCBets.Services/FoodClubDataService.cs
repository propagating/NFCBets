using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class FoodClubDataService : IFoodClubDataService
{
    private readonly NfcbetsContext _context;
    private readonly IFoodAdjustmentService _foodAdjustmentService;
    private readonly HttpClient _httpClient;

    public FoodClubDataService(HttpClient httpClient, NfcbetsContext context,
        IFoodAdjustmentService foodAdjustmentService)
    {
        _httpClient = httpClient;
        _context = context;
        _foodAdjustmentService = foodAdjustmentService;
    }

    public async Task<bool> CollectAndSaveRoundAsync(int roundId)
    {
        try
        {
            // Fetch round data from API
            var roundData = await FetchRoundDataAsync(roundId);
            if (roundData == null)
                return false;

            // Save the data
            await SaveRoundDataAsync(roundData);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting round {roundId}: {ex.Message}");
            return false;
        }
    }

    public async Task<List<int>> CollectRangeAsync(int startRound, int endRound)
    {
        var successfulRounds = new List<int>();

        for (var round = startRound; round <= endRound; round++)
        {
            if (_context.RoundResults.Where(x => x.IsComplete).Select(x => x.RoundId).Contains(round))
            {
                Console.WriteLine($"Skipping round {round} as it's already been collected");
                continue;
            }

            if (await CollectAndSaveRoundAsync(round))
            {
                successfulRounds.Add(round);
                Console.WriteLine($"✅ Collected round {round}");
            }
            else
            {
                Console.WriteLine($"❌ Failed round {round}");
            }
        }

        return successfulRounds;
    }

    private async Task<FoodClubRoundData?> FetchRoundDataAsync(int roundId)
    {
        var url = $"https://cdn.neofood.club/rounds/{roundId}.json";

        try
        {
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<FoodClubRoundData>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch
        {
            return null;
        }
    }

    private async Task SaveRoundDataAsync(FoodClubRoundData roundData)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            for (var arenaIndex = 0; arenaIndex < roundData.Pirates.Count; arenaIndex++)
            {
                var pirateIds = roundData.Pirates[arenaIndex];
                var foodIds = roundData.Foods[arenaIndex];
                var openingOdds = roundData.OpeningOdds[arenaIndex];
                var currentOdds = roundData.CurrentOdds[arenaIndex];
                var arenaId = arenaIndex + 1;

                // Save food courses for this arena
                await SaveFoodCoursesAsync(roundData.Round, arenaId, foodIds);

                // Save pirate placements with food adjustments
                await SavePiratePlacementsAsync(roundData.Round, arenaId, pirateIds, openingOdds, currentOdds);

                await _context.SaveChangesAsync();


                // Save results if round is complete
                if (roundData.Winners?.Any() == true)
                {
                    var winnerPosition = roundData.Winners[arenaIndex];
                    var winnerId = roundData.Pirates[arenaIndex][winnerPosition - 1];
                    await SaveRoundResultsAsync(roundData.Round, arenaId, pirateIds, currentOdds, winnerId);
                }
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            await transaction.RollbackAsync();
            throw;
        }
    }

    private async Task SaveFoodCoursesAsync(int roundId, int arenaId, List<int> foodIds)
    {
        foreach (var foodId in foodIds)
        {
            var existing = await _context.RoundFoodCourses
                .FirstOrDefaultAsync(rfc => rfc.RoundId == roundId &&
                                            rfc.ArenaId == arenaId &&
                                            rfc.FoodId == foodId);

            if (existing == null)
                _context.RoundFoodCourses.Add(new RoundFoodCourse
                {
                    RoundId = roundId,
                    ArenaId = arenaId,
                    FoodId = foodId
                });
        }
    }

    private async Task SavePiratePlacementsAsync(int roundId, int arenaId, List<int> pirateIds, List<int> openingOdds,
        List<int> currentOdds)
    {
        for (var position = 0; position < pirateIds.Count; position++)
        {
            var pirateId = pirateIds[position];

            var existing = await _context.RoundPiratePlacements
                .FirstOrDefaultAsync(rpp => rpp.RoundId == roundId &&
                                            rpp.ArenaId == arenaId &&
                                            rpp.PirateId == pirateId);

            // Calculate food adjustment for this pirate
            var foodAdjustment = await _foodAdjustmentService.CalculateFoodAdjustmentAsync(pirateId, roundId, arenaId);

            if (existing == null)
            {
                _context.RoundPiratePlacements.Add(new RoundPiratePlacement
                {
                    RoundId = roundId,
                    ArenaId = arenaId,
                    PirateId = pirateId,
                    PirateSeatPosition = position,
                    PirateFoodAdjustment = foodAdjustment,
                    StartingOdds = openingOdds[position],
                    CurrentOdds = currentOdds[position]
                });
            }
            else
            {
                // Update existing (don't change StartingOdds)
                existing.CurrentOdds = currentOdds[position];
                existing.PirateFoodAdjustment = foodAdjustment;
            }
        }
    }

    private async Task SaveRoundResultsAsync(int roundId, int arenaId, List<int> pirateIds, List<int> endingOdds,
        int winnerId)
    {
        for (var position = 0; position < pirateIds.Count; position++)
        {
            var pirateId = pirateIds[position];
            var isWinner = winnerId == pirateId;

            var existing = await _context.RoundResults
                .FirstOrDefaultAsync(rr => rr.RoundId == roundId &&
                                           rr.ArenaId == arenaId &&
                                           rr.PirateId == pirateId);

            if (existing == null)
            {
                _context.RoundResults.Add(new RoundResult
                {
                    RoundId = roundId,
                    ArenaId = arenaId,
                    PirateId = pirateId,
                    EndingOdds = endingOdds[position],
                    IsWinner = isWinner,
                    IsComplete = true
                });
            }
            else
            {
                existing.EndingOdds = endingOdds[position];
                existing.IsWinner = isWinner;
                existing.IsComplete = true;
            }
        }
    }
}

// Data model for the API response