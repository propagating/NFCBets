using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Utilities.Models;

namespace NFCBets.Services;

public class FeatureEngineeringService(NfcbetsContext context, ILogger<FeatureEngineeringService> logger)
    : IFeatureEngineeringService
{
    private Dictionary<int, List<RoundResult>>? _allHistoricalResultsCache;
    private Dictionary<int, Pirate>? _pirateCache;
    private Dictionary<int, string>? _pirateNamesCache;

    #region Feature Creation

    /// <summary>
    /// Create features for a specific round (for predictions)
    /// </summary>
    public async Task<List<PirateFeatureRecord>> CreateFeaturesForRoundAsync(int roundId)
    {
        var features = new List<PirateFeatureRecord>();

        // Check total placements first
        var allPlacements = await context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId)
            .ToListAsync();

        if (!allPlacements.Any())
        {
            logger.LogInformation($"   ⚠️ WARNING: No valid placements for round {roundId}!");
            return features;
        }

        // Filter out 1:1 odds
        var placements = allPlacements
            .Where(p => (p.CurrentOdds ?? p.StartingOdds) > 1)
            .ToList();

        if (!placements.Any())
        {
            Console.WriteLine($"   ⚠️ WARNING: No valid placements after filtering for round {roundId}!");
            Console.WriteLine("   All pirates have 1:1 odds - this round has no betting opportunities");
            return features;
        }

        // Get all pirate IDs involved
        var pirateIds = placements
            .Where(p => p.PirateId.HasValue)
            .Select(p => p.PirateId!.Value)
            .Distinct()
            .ToList();

        // Batch load pirates
        _pirateCache = await context.Pirates
            .Where(p => pirateIds.Contains(p.PirateId))
            .ToDictionaryAsync(p => p.PirateId, p => p);

        // Batch load ALL historical results for these pirates
        _allHistoricalResultsCache = (await context.RoundResults
                .Where(rr => pirateIds.Contains(rr.PirateId) &&
                             rr.IsComplete &&
                             rr.RoundId.HasValue &&
                             rr.RoundId < roundId)
                .ToListAsync())
            .GroupBy(rr => rr.PirateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Pre-calculate rivals for each arena
        var rivalsByArena = placements
            .Where(p => p is { ArenaId: not null, PirateId: not null })
            .GroupBy(p => p.ArenaId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.Select(p => p.PirateId!.Value).ToList()
            );

        // Process each placement using cached data (no more DB queries)
        foreach (var placement in placements)
        {
            if (!placement.PirateId.HasValue || !placement.ArenaId.HasValue) continue;

            var rivalsInArena = rivalsByArena.GetValueOrDefault(placement.ArenaId.Value, new List<int>())
                .Where(id => id != placement.PirateId.Value)
                .ToList();

            var feature = BuildFeatureRecordOptimized(
                placement.PirateId.Value,
                placement.ArenaId.Value,
                roundId,
                placement,
                rivalsInArena,
                null // No outcome for prediction
            );

            if (feature != null)
                features.Add(feature);
        }

        // Clear caches
        _pirateCache = null;
        _allHistoricalResultsCache = null;

        return features;
    }

    /// <summary>
    /// Create training data from historical rounds
    /// </summary>
    public async Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 10000)
    {
        Console.WriteLine("📊 Creating training data (excluding 1:1 odds placeholders)...");

        var features = new List<PirateFeatureRecord>();

        // Get all completed rounds
        var completedRounds = await context.RoundResults
            .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
            .Select(rr => rr.RoundId!.Value)
            .Distinct()
            .OrderBy(r => r)
            .Take(maxRounds)
            .ToListAsync();

        Console.WriteLine($"Processing {completedRounds.Count} rounds...");

        // Load ALL pirates
        _pirateCache = await context.Pirates
            .ToDictionaryAsync(p => p.PirateId, p => p);

        // Load ALL historical results
        _allHistoricalResultsCache = (await context.RoundResults
                .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
                .ToListAsync())
            .GroupBy(rr => rr.PirateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Process in batches
        const int batchSize = 200;

        for (var i = 0; i < completedRounds.Count; i += batchSize)
        {
            var batchRounds = completedRounds.Skip(i).Take(batchSize).ToList();

            // EXCLUDE 1:1 odds at query level
            var batchPlacements = await context.RoundPiratePlacements
                .Where(rpp => batchRounds.Contains(rpp.RoundId!.Value) &&
                              (rpp.CurrentOdds ?? rpp.StartingOdds) > 1)
                .ToListAsync();

            var batchResults = await context.RoundResults
                .Where(rr => batchRounds.Contains(rr.RoundId!.Value))
                .ToListAsync();

            // Process each round in batches
            foreach (var roundId in batchRounds)
            {
                var roundPlacements = batchPlacements.Where(p => p.RoundId == roundId).ToList();
                var roundResults = batchResults.Where(r => r.RoundId == roundId).ToList();

                foreach (var placement in roundPlacements)
                {
                    if (!placement.PirateId.HasValue || !placement.ArenaId.HasValue) continue;

                    var result = roundResults.FirstOrDefault(rr =>
                        rr.PirateId == placement.PirateId.Value &&
                        rr.ArenaId == placement.ArenaId.Value);

                    // Get rivals from batch data (no DB query)
                    var rivalsInArena = roundPlacements
                        .Where(rpp => rpp.ArenaId == placement.ArenaId &&
                                      rpp.PirateId != placement.PirateId &&
                                      rpp.PirateId.HasValue)
                        .Select(rpp => rpp.PirateId!.Value)
                        .ToList();

                    // Build features using cached data
                    var feature = BuildFeatureRecordOptimized(
                        placement.PirateId.Value,
                        placement.ArenaId.Value,
                        roundId,
                        placement,
                        rivalsInArena,
                        result?.IsWinner
                    );

                    if (feature != null)
                        features.Add(feature);
                }
            }

            if ((i + batchSize) % 1000 == 0)
                Console.WriteLine(
                    $"   Processed {Math.Min(i + batchSize, completedRounds.Count)}/{completedRounds.Count} rounds...");
        }

        // Clear caches
        _pirateCache = null;
        _allHistoricalResultsCache = null;

        Console.WriteLine($"✅ Generated {features.Count} training features (1:1 odds excluded)");
        return features;
    }

    #endregion

    #region Feature Building

    private PirateFeatureRecord? BuildFeatureRecordOptimized(
        int pirateId,
        int arenaId,
        int roundId,
        RoundPiratePlacement placement,
        List<int> rivalIds,
        bool? isWinner)
    {
        // Use cached pirate data (no DB query)
        if (!_pirateCache!.TryGetValue(pirateId, out var pirate))
            return null;

        // Use cached results (no DB query)
        var historicalResults = _allHistoricalResultsCache!
            .GetValueOrDefault(pirateId, new List<RoundResult>())
            .Where(rr => rr.RoundId < roundId)
            .ToList();

        // Calculate all stats from cached data (all in-memory, no DB queries)
        var historicalStats = GetHistoricalStatsOptimized(historicalResults);
        var arenaWinRate = GetArenaWinRateOptimized(historicalResults, arenaId);
        var recentForm = GetRecentFormOptimized(historicalResults, 10);
        var rivalPerformance = GetRivalPerformanceOptimized(rivalIds, roundId, historicalResults);

        // NORMALIZE ODDS: Treat 1:1 as 2:1 (game minimum)
        var normalizedStartingOdds = Math.Max(2, placement.StartingOdds);
        var normalizedCurrentOdds = Math.Max(2, placement.CurrentOdds ?? placement.StartingOdds);

        return new PirateFeatureRecord
        {
            RoundId = roundId,
            ArenaId = arenaId,
            PirateId = pirateId,
            Position = placement.PirateSeatPosition ?? 0,
            OpeningOdds = normalizedStartingOdds,
            CurrentOdds = normalizedCurrentOdds,
            FoodAdjustment = placement.PirateFoodAdjustment,
            Strength = pirate.Strength ?? 0,
            Weight = pirate.Weight ?? 0,
            HistoricalWinRate = historicalStats.WinRate,
            TotalAppearances = historicalStats.TotalAppearances,
            ArenaWinRate = arenaWinRate,
            RecentWinRate = recentForm,
            WinRateVsCurrentRivals = rivalPerformance.WinRate,
            MatchesVsCurrentRivals = rivalPerformance.TotalMatches,
            AvgRivalStrength = rivalPerformance.AvgRivalStrength,
            IsWinner = isWinner
        };
    }

    #endregion

    #region Statistical Calculations

    private (double WinRate, int TotalAppearances, double AverageOdds) GetHistoricalStatsOptimized(
        List<RoundResult> historicalResults)
    {
        if (!historicalResults.Any())
            return (0, 0, 0);

        var wins = historicalResults.Count(r => r.IsWinner);
        var avgOdds = historicalResults.Average(r => r.EndingOdds ?? 0);

        return ((double)wins / historicalResults.Count, historicalResults.Count, avgOdds);
    }

    private double GetArenaWinRateOptimized(List<RoundResult> historicalResults, int arenaId)
    {
        var arenaResults = historicalResults.Where(r => r.ArenaId == arenaId).ToList();
        if (!arenaResults.Any()) return 0;

        return (double)arenaResults.Count(r => r.IsWinner) / arenaResults.Count;
    }

    private double GetRecentFormOptimized(List<RoundResult> historicalResults, int lastN)
    {
        var recentResults = historicalResults
            .OrderByDescending(r => r.RoundId)
            .Take(lastN)
            .ToList();

        if (!recentResults.Any()) return 0;

        return (double)recentResults.Count(r => r.IsWinner) / recentResults.Count;
    }

    private (double WinRate, int TotalMatches, double AvgRivalStrength) GetRivalPerformanceOptimized(
        List<int> rivalIds,
        int beforeRoundId,
        List<RoundResult> pirateHistoricalResults)
    {
        if (!rivalIds.Any())
            return (0, 0, 0);

        // Get rival results from cache (no DB query)
        var rivalResults = rivalIds
            .Where(id => _allHistoricalResultsCache!.ContainsKey(id))
            .SelectMany(id => _allHistoricalResultsCache![id])
            .Where(rr => rr.RoundId < beforeRoundId)
            .ToList();

        // Create lookup for fast matching
        var rivalRoundArenas = rivalResults
            .Where(rr => rr.RoundId.HasValue)
            .Select(rr => (rr.RoundId!.Value, rr.ArenaId))
            .ToHashSet();

        // Find matchups in memory
        var matchups = pirateHistoricalResults
            .Where(pr => pr.RoundId.HasValue && rivalRoundArenas.Contains((pr.RoundId.Value, pr.ArenaId)))
            .ToList();

        // Get rival strengths from cache (no DB query)
        var rivalStrengths = rivalIds
            .Where(id => _pirateCache!.ContainsKey(id))
            .Select(id => _pirateCache![id].Strength ?? 0)
            .ToList();

        var avgRivalStrength = rivalStrengths.Any() ? rivalStrengths.Average() : 0;
        var winRate = matchups.Any() ? (double)matchups.Count(m => m.IsWinner) / matchups.Count : 0;

        return (winRate, matchups.Count, avgRivalStrength);
    }

    #endregion

    #region Pirate Name Methods

    /// <summary>
    /// Get pirate names for a list of pirate IDs
    /// </summary>
    public async Task<Dictionary<int, string>> GetPirateNamesAsync(IEnumerable<int> pirateIds)
    {
        var ids = pirateIds.Distinct().ToList();

        // Check cache first
        if (_pirateNamesCache != null)
        {
            var cached = ids
                .Where(id => _pirateNamesCache.ContainsKey(id))
                .ToDictionary(id => id, id => _pirateNamesCache[id]);

            if (cached.Count == ids.Count)
                return cached;
        }

        return await context.Pirates
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(
                p => p.Id,
                p => p.PirateName);
    }

    /// <summary>
    /// Get all pirate names (cached for performance)
    /// </summary>
    public async Task<Dictionary<int, string>> GetAllPirateNamesAsync()
    {
        if (_pirateNamesCache != null)
            return _pirateNamesCache;

        _pirateNamesCache = await context.Pirates
            .ToDictionaryAsync(
                p => p.Id,
                p => p.PirateName);

        return _pirateNamesCache;
    }

    /// <summary>
    /// Clear the pirate names cache (call if pirates are updated)
    /// </summary>
    public void ClearPirateNamesCache()
    {
        _pirateNamesCache = null;
    }

    #endregion
}