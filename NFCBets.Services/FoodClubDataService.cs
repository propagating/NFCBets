using System.Collections.Concurrent;
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
            var roundData = await FetchRoundDataAsync(roundId);
            if (roundData == null)
                return false;

            await SaveRoundDataAsync(roundData);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error collecting round {roundId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    ///     Collects a range of rounds from the API
    /// </summary>
    /// <param name="startRound">First round to collect</param>
    /// <param name="endRound">Last round to collect</param>
    /// <param name="forceCollect">If true, re-collect even if round already exists (for updates)</param>
    /// <param name="maxParallel">Maximum number of parallel operations (default: 5)</param>
    /// <returns>List of successfully collected round IDs</returns>
    public async Task<List<int>> CollectRangeAsync(int startRound, int endRound, bool forceCollect = false,
        int maxParallel = 5)
    {
        var successfulRounds = new List<int>();

        List<int> completedRounds = new();

        if (!forceCollect)
        {
            // Only check for existing rounds if not forcing collection
            completedRounds = await _context.RoundResults
                .Where(x => x.IsComplete && x.RoundId.HasValue)
                .Select(x => x.RoundId!.Value)
                .Distinct()
                .ToListAsync();

            Console.WriteLine($"Found {completedRounds.Count} already collected rounds");
        }
        else
        {
            Console.WriteLine("⚠️ Force collect enabled - will re-collect all rounds in range");
        }

        var roundsToProcess = Enumerable.Range(startRound, endRound - startRound + 1).ToList();
        var roundsToCollect = forceCollect
            ? roundsToProcess
            : roundsToProcess.Where(r => !completedRounds.Contains(r)).ToList();

        if (!roundsToCollect.Any())
        {
            Console.WriteLine("✅ All rounds in range already collected");
            return successfulRounds;
        }

        Console.WriteLine($"Collecting {roundsToCollect.Count} rounds from {startRound} to {endRound}...");

        for (var round = startRound; round <= endRound; round++)
        {
            if (!forceCollect && completedRounds.Contains(round))
            {
                Console.WriteLine($"⏭️  Skipping round {round} (already collected)");
                continue;
            }

            if (forceCollect && completedRounds.Contains(round))
                Console.WriteLine($"🔄 Re-collecting round {round} (force collect enabled)");

            if (await CollectAndSaveRoundAsync(round))
            {
                successfulRounds.Add(round);
                Console.WriteLine($"✅ Collected round {round}");
            }
            else
            {
                Console.WriteLine($"❌ Failed round {round}");
            }

            // Optional: Add small delay to avoid hammering the API
            //await Task.Delay(100);
        }

        Console.WriteLine("\n📊 Collection Summary:");
        Console.WriteLine($"   Attempted: {roundsToCollect.Count} rounds");
        Console.WriteLine($"   Successful: {successfulRounds.Count} rounds");
        Console.WriteLine($"   Failed: {roundsToCollect.Count - successfulRounds.Count} rounds");

        return successfulRounds;
    }

    /// <summary>
    ///     Collects a range of rounds from the API with parallel processing
    /// </summary>
    /// <param name="startRound">First round to collect</param>
    /// <param name="endRound">Last round to collect</param>
    /// <param name="forceCollect">If true, re-collect even if round already exists</param>
    /// <param name="maxParallel">Maximum number of parallel operations (default: 5)</param>
    /// <returns>List of successfully collected round IDs</returns>
    public async Task<List<int>> CollectRangeParallelAsync(
        int startRound,
        int endRound,
        bool forceCollect = false,
        int maxParallel = 5)
    {
        List<int> completedRounds = new();

        if (!forceCollect)
        {
            completedRounds = await _context.RoundResults
                .Where(x => x.IsComplete && x.RoundId.HasValue)
                .Select(x => x.RoundId!.Value)
                .Distinct()
                .ToListAsync();

            Console.WriteLine($"Found {completedRounds.Count} already collected rounds");
        }
        else
        {
            Console.WriteLine("⚠️ Force collect enabled - will re-collect all rounds in range");
        }

        var roundsToProcess = Enumerable.Range(startRound, endRound - startRound + 1).ToList();
        var roundsToCollect = forceCollect
            ? roundsToProcess
            : roundsToProcess.Where(r => !completedRounds.Contains(r)).ToList();

        if (!roundsToCollect.Any())
        {
            Console.WriteLine("✅ All rounds in range already collected");
            return new List<int>();
        }

        Console.WriteLine($"Collecting {roundsToCollect.Count} rounds with {maxParallel} parallel operations...");

        var successfulRounds = new ConcurrentBag<int>();
        var failedRounds = new ConcurrentBag<int>();

        await Parallel.ForEachAsync(
            roundsToCollect,
            new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
            async (round, ct) =>
            {
                var isReCollect = completedRounds.Contains(round);
                var prefix = isReCollect ? "🔄" : "📥";

                if (await CollectAndSaveRoundAsync(round))
                {
                    successfulRounds.Add(round);
                    Console.WriteLine($"{prefix} ✅ Collected round {round}");
                }
                else
                {
                    failedRounds.Add(round);
                    Console.WriteLine($"{prefix} ❌ Failed round {round}");
                }
            });

        var results = successfulRounds.OrderBy(r => r).ToList();

        Console.WriteLine("\n📊 Collection Summary:");
        Console.WriteLine($"   Attempted: {roundsToCollect.Count} rounds");
        Console.WriteLine($"   Successful: {successfulRounds.Count} rounds");
        Console.WriteLine($"   Failed: {failedRounds.Count} rounds");

        if (forceCollect && successfulRounds.Any())
            Console.WriteLine(
                $"   Re-collected: {completedRounds.Intersect(successfulRounds).Count()} existing rounds");

        return results;
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

    /// <summary>
    ///     OPTIMIZED: Batch save all round data with single SaveChanges
    /// </summary>
    private async Task SaveRoundDataAsync(FoodClubRoundData roundData)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Validate odds arrays structure
            ValidateRoundDataStructure(roundData);

            // Batch load ALL existing records for this round upfront
            var existingPlacements = await _context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundData.Round)
                .ToListAsync();

            var existingFoodCourses = await _context.RoundFoodCourses
                .Where(rfc => rfc.RoundId == roundData.Round)
                .ToListAsync();

            var existingResults = await _context.RoundResults
                .Where(rr => rr.RoundId == roundData.Round)
                .ToListAsync();

            // Create lookup dictionaries for O(1) access
            var placementLookup = existingPlacements
                .ToDictionary(p => (p.ArenaId, p.PirateId), p => p);

            var foodCourseLookup = existingFoodCourses
                .ToDictionary(f => (f.ArenaId, f.FoodId), f => f);

            var resultLookup = existingResults
                .ToDictionary(r => (r.ArenaId, r.PirateId), r => r);

            // Prepare lists for bulk insert
            var newPlacements = new List<RoundPiratePlacement>();
            var newFoodCourses = new List<RoundFoodCourse>();
            var newResults = new List<RoundResult>();

            // Process each arena
            for (var arenaIndex = 0; arenaIndex < roundData.Pirates.Count; arenaIndex++)
            {
                // ✅ Use helper method for arena ID conversion
                var arenaId = GetArenaIdFromIndex(arenaIndex);

                var pirateIds = roundData.Pirates[arenaIndex];
                var foodIds = roundData.Foods[arenaIndex];
                var openingOdds = roundData.OpeningOdds[arenaIndex];
                var currentOdds = roundData.CurrentOdds[arenaIndex];

                // Process food courses
                foreach (var foodId in foodIds)
                {
                    var key = (arenaId, foodId);

                    if (!foodCourseLookup.ContainsKey(key))
                        newFoodCourses.Add(new RoundFoodCourse
                        {
                            RoundId = roundData.Round,
                            ArenaId = arenaId,
                            FoodId = foodId
                        });
                }

                // Calculate food adjustments for all pirates in arena at once
                var foodAdjustments = await _foodAdjustmentService
                    .CalculateFoodAdjustmentsBatchAsync(roundData.Round, arenaId, pirateIds);

                // Process pirate placements
                for (var position = 0; position < pirateIds.Count; position++)
                {
                    var pirateId = pirateIds[position];

                    // ✅ Use helper method for odds position conversion
                    var oddsPosition = GetOddsArrayPosition(position);

                    var startingOddsValue = openingOdds[oddsPosition];
                    var currentOddsValue = currentOdds[oddsPosition];

                    // Validate odds are not placeholders
                    if (startingOddsValue == 1 || currentOddsValue == 1)
                    {
                        Console.WriteLine(
                            $"   ⚠️ Warning: Found 1:1 odds at arena {arenaId}, position {position} (oddsPosition {oddsPosition}) for pirate {pirateId}");
                        continue;
                    }

                    var key = (arenaId, pirateId);
                    var foodAdjustment = foodAdjustments[pirateId];

                    if (placementLookup.TryGetValue(key, out var existingPlacement))
                    {
                        // Update existing
                        existingPlacement.StartingOdds = startingOddsValue;
                        existingPlacement.CurrentOdds = currentOddsValue;
                        existingPlacement.PirateFoodAdjustment = foodAdjustment;
                    }
                    else
                    {
                        // Add to batch insert list
                        newPlacements.Add(new RoundPiratePlacement
                        {
                            RoundId = roundData.Round,
                            ArenaId = arenaId,
                            PirateId = pirateId,
                            PirateSeatPosition = position,
                            PirateFoodAdjustment = foodAdjustment,
                            StartingOdds = startingOddsValue,
                            CurrentOdds = currentOddsValue
                        });
                    }
                }

                // Process results (if round is complete)
                //if there are any winners and they are not all 0, then there is a winner for this arena
                if (roundData.Winners?.Any() == true && roundData.Winners?.Any(x => x == 0) == false)
                {
                    // ✅ Use helper method for winner position conversion
                    var winnerPositionFromApi = roundData.Winners[arenaIndex];
                    var winnerPirateIndex = GetPirateIndexFromWinnerPosition(winnerPositionFromApi);
                    var winnerId = roundData.Pirates[arenaIndex][winnerPirateIndex];

                    for (var position = 0; position < pirateIds.Count; position++)
                    {
                        var pirateId = pirateIds[position];

                        // ✅ Use helper method for odds position conversion
                        var oddsPosition = GetOddsArrayPosition(position);
                        var startingOddsValue = openingOdds[oddsPosition];
                        var endingOddsValue = currentOdds[oddsPosition];

                        // Skip if somehow 1:1
                        if (endingOddsValue == 1)
                        {
                            Console.WriteLine(
                                $"   ⚠️ Warning: Found 1:1 ending odds at arena {arenaId} for pirate {pirateId}");
                            continue;
                        }

                        var key = (arenaId, pirateId);
                        var isWinner = winnerId == pirateId;

                        if (resultLookup.TryGetValue(key, out var existingResult))
                        {
                            // Update existing
                            existingResult.EndingOdds = endingOddsValue;
                            existingResult.IsWinner = isWinner;
                            existingResult.IsComplete = true;
                        }
                        else
                        {
                            // Add to batch insert list
                            newResults.Add(new RoundResult
                            {
                                RoundId = roundData.Round,
                                ArenaId = arenaId,
                                PirateId = pirateId,
                                EndingOdds = endingOddsValue,
                                IsWinner = isWinner,
                                IsComplete = true
                            });
                        }
                    }
                }
            }

            // Bulk insert all new records at once
            if (newFoodCourses.Any())
            {
                await _context.RoundFoodCourses.AddRangeAsync(newFoodCourses);
                Console.WriteLine($"   Adding {newFoodCourses.Count} new food courses");
            }

            if (newPlacements.Any())
            {
                await _context.RoundPiratePlacements.AddRangeAsync(newPlacements);
                Console.WriteLine($"   Adding {newPlacements.Count} new placements");
            }

            if (newResults.Any())
            {
                await _context.RoundResults.AddRangeAsync(newResults);
                Console.WriteLine($"   Adding {newResults.Count} new results");
            }

            // Single SaveChanges for entire round
            await _context.SaveChangesAsync();
            await transaction.CommitAsync();

            Console.WriteLine($"   ✅ Round {roundData.Round} saved successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Error saving round {roundData.Round}: {ex.Message}");
            await transaction.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    ///     Validates the complete API response structure for a round
    ///     Checks:
    ///     - Odds array lengths match pirate counts
    ///     - Placeholder (1:1) exists at position 0 in odds arrays
    ///     - No 1:1 odds in actual pirate positions (1-4)
    ///     - Winner positions are valid (1-4, 1-based)
    ///     - Arena and position conversions work correctly
    /// </summary>
    private void ValidateRoundDataStructure(FoodClubRoundData roundData)
    {
        Console.WriteLine($"   Validating round {roundData.Round} data structure...");

        // Validate each arena
        for (var arenaIndex = 0; arenaIndex < roundData.Pirates.Count; arenaIndex++)
        {
            var arenaId = GetArenaIdFromIndex(arenaIndex);

            var pirateCount = roundData.Pirates[arenaIndex].Count;
            var openingOddsCount = roundData.OpeningOdds[arenaIndex].Count;
            var currentOddsCount = roundData.CurrentOdds[arenaIndex].Count;

            // Odds arrays should have pirateCount + 1 (for the 1:1 placeholder)
            var expectedOddsCount = pirateCount + 1;

            if (openingOddsCount != expectedOddsCount)
                Console.WriteLine(
                    $"   ⚠️ Arena {arenaId} (API index {arenaIndex}): {pirateCount} pirates but {openingOddsCount} opening odds (expected {expectedOddsCount})");

            if (currentOddsCount != expectedOddsCount)
                Console.WriteLine(
                    $"   ⚠️ Arena {arenaId} (API index {arenaIndex}): {pirateCount} pirates but {currentOddsCount} current odds (expected {expectedOddsCount})");

            // Validate placeholder at position 0
            if (roundData.OpeningOdds[arenaIndex][0] != 1)
                Console.WriteLine(
                    $"   ⚠️ Arena {arenaId}: opening odds[0] = {roundData.OpeningOdds[arenaIndex][0]}, expected 1 (placeholder)");

            if (roundData.CurrentOdds[arenaIndex][0] != 1)
                Console.WriteLine(
                    $"   ⚠️ Arena {arenaId}: current odds[0] = {roundData.CurrentOdds[arenaIndex][0]}, expected 1 (placeholder)");

            // Validate no 1:1 odds in actual pirate positions (1-4)
            for (var position = 0; position < pirateCount; position++)
            {
                var oddsPosition = GetOddsArrayPosition(position);

                if (roundData.OpeningOdds[arenaIndex][oddsPosition] == 1)
                    Console.WriteLine(
                        $"   ⚠️ Arena {arenaId}, pirate position {position}: opening odds = 1 (unexpected - should be ≥2)");

                if (roundData.CurrentOdds[arenaIndex][oddsPosition] == 1)
                    Console.WriteLine(
                        $"   ⚠️ Arena {arenaId}, pirate position {position}: current odds = 1 (unexpected - should be ≥2)");
            }
        }

        // Validate winners array if round is complete
        if (roundData.Winners?.Any() == true)
        {
            if (roundData.Winners?.All(w => w == 0) == true)
            {
                Console.WriteLine(
                    $"   ⚠️ Round {roundData.Round}: Winners array contains all zeros, indicating the round is not yet complete. Skipping winner validation.");
                return;
            }

            for (var arenaIndex = 0; arenaIndex < roundData.Winners.Count; arenaIndex++)
            {
                var arenaId = GetArenaIdFromIndex(arenaIndex);
                var winnerPosition = roundData.Winners[arenaIndex];
                var pirateCount = roundData.Pirates[arenaIndex].Count;

                // Winner position should be 1-based (1, 2, 3, or 4)
                if (winnerPosition < 1 || winnerPosition > pirateCount)
                    Console.WriteLine(
                        $"   ⚠️ Arena {arenaId}: winner position = {winnerPosition}, expected 1-{pirateCount}");

                // Verify conversion to array index works correctly
                var pirateIndex = GetPirateIndexFromWinnerPosition(winnerPosition);
                if (pirateIndex < 0 || pirateIndex >= pirateCount)
                    Console.WriteLine(
                        $"   ⚠️ Arena {arenaId}: winner position {winnerPosition} converts to invalid pirate index {pirateIndex}");
            }
        }

        Console.WriteLine($"   ✅ Round {roundData.Round} structure validation complete");
    }

    #region API to Database Conversion Helper Methods

    /// <summary>
    ///     Converts API arena index (0-based) to database arena ID (1-based)
    ///     API:  0, 1, 2, 3, 4
    ///     DB:   1, 2, 3, 4, 5
    /// </summary>
    /// <param name="arenaIndex">0-based arena index from API</param>
    /// <returns>1-based arena ID for database</returns>
    private int GetArenaIdFromIndex(int arenaIndex)
    {
        return arenaIndex + 1;
    }

    /// <summary>
    ///     Gets the correct odds array position for a pirate.
    ///     API odds arrays have a 1:1 placeholder at position 0, so we offset by 1.
    ///     Example:
    ///     API odds:  [1, 13, 6, 2, 3]  (5 values, 1:1 is placeholder)
    ///     Pirates:   [9, 11, 2, 3]      (4 pirates)
    ///     Pirate at position 0 → odds at index 1 (13:1)
    ///     Pirate at position 1 → odds at index 2 (6:1)
    ///     Pirate at position 2 → odds at index 3 (2:1)
    ///     Pirate at position 3 → odds at index 4 (3:1)
    /// </summary>
    /// <param name="piratePosition">0-based position in the pirates array (0-3)</param>
    /// <returns>Index to use in the odds array (1-4)</returns>
    private int GetOddsArrayPosition(int piratePosition)
    {
        return piratePosition + 1;
    }

    /// <summary>
    ///     Converts API winner position (1-based) to pirate array index (0-based)
    ///     API winners: 1, 2, 3, 4 (position of winning pirate)
    ///     Array index: 0, 1, 2, 3
    /// </summary>
    /// <param name="winnerPosition">1-based winner position from API</param>
    /// <returns>0-based index for pirates array</returns>
    private int GetPirateIndexFromWinnerPosition(int winnerPosition)
    {
        return winnerPosition - 1;
    }

    #endregion
}