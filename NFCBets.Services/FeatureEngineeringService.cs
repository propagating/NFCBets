using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class FeatureEngineeringService : IFeatureEngineeringService
{
    private readonly NfcbetsContext _context;
    private readonly IFoodAdjustmentService _foodAdjustmentService;
    private Dictionary<int, Pirate>? _pirateCache;
    private Dictionary<int, List<RoundResult>>? _allHistoricalResultsCache;

    public FeatureEngineeringService(NfcbetsContext context, IFoodAdjustmentService foodAdjustmentService)
    {
        _context = context;
        _foodAdjustmentService = foodAdjustmentService;
    }

/// OPTIMIZED: CreateFeaturesForRoundAsync with caching
public async Task<List<PirateFeatureRecord>> CreateFeaturesForRoundAsync(int roundId)
{
    var features = new List<PirateFeatureRecord>();

    // OPTIMIZATION 1: Single query for all placements
    var placements = await _context.RoundPiratePlacements
        .Where(rpp => rpp.RoundId == roundId)
        .ToListAsync();

    if (!placements.Any()) return features;

    // OPTIMIZATION 2: Get all pirate IDs involved
    var pirateIds = placements
        .Where(p => p.PirateId.HasValue)
        .Select(p => p.PirateId!.Value)
        .Distinct()
        .ToList();

    // OPTIMIZATION 3: Batch load pirates
    _pirateCache = await _context.Pirates
        .Where(p => pirateIds.Contains(p.PirateId))
        .ToDictionaryAsync(p => p.PirateId, p => p);

    // OPTIMIZATION 4: Batch load ALL historical results for these pirates
    _allHistoricalResultsCache = (await _context.RoundResults
        .Where(rr => pirateIds.Contains(rr.PirateId) && 
                    rr.IsComplete && 
                    rr.RoundId.HasValue &&
                    rr.RoundId < roundId)
        .ToListAsync())
        .GroupBy(rr => rr.PirateId)
        .ToDictionary(g => g.Key, g => g.ToList());

    // OPTIMIZATION 5: Pre-calculate rivals for each arena
    var rivalsByArena = placements
        .Where(p => p.ArenaId.HasValue && p.PirateId.HasValue)
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

      /// OPTIMIZED: CreateTrainingDataAsync
    public async Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 4000)
    {
        Console.WriteLine("📊 Creating training data with optimized batch loading...");
        
        var features = new List<PirateFeatureRecord>();

        // Get all completed rounds
        var completedRounds = await _context.RoundResults
            .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
            .Select(rr => rr.RoundId!.Value)
            .Distinct()
            .OrderBy(r => r)
            .Take(maxRounds)
            .ToListAsync();

        Console.WriteLine($"Processing {completedRounds.Count} rounds...");

        // OPTIMIZATION 1: Load ALL pirates once
        _pirateCache = await _context.Pirates
            .ToDictionaryAsync(p => p.PirateId, p => p);

        // OPTIMIZATION 2: Load ALL historical results once
        _allHistoricalResultsCache = (await _context.RoundResults
            .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
            .ToListAsync())
            .GroupBy(rr => rr.PirateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        // OPTIMIZATION 3: Process in batches
        const int batchSize = 200;
        
        for (int i = 0; i < completedRounds.Count; i += batchSize)
        {
            var batchRounds = completedRounds.Skip(i).Take(batchSize).ToList();
            
            // Load all placements and results for batch
            var batchPlacements = await _context.RoundPiratePlacements
                .Where(rpp => batchRounds.Contains(rpp.RoundId!.Value))
                .ToListAsync();

            var batchResults = await _context.RoundResults
                .Where(rr => batchRounds.Contains(rr.RoundId!.Value))
                .ToListAsync();

            // Process each round in batch
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
            {
                Console.WriteLine($"   Processed {Math.Min(i + batchSize, completedRounds.Count)}/{completedRounds.Count} rounds...");
            }
        }

        // Clear caches
        _pirateCache = null;
        _allHistoricalResultsCache = null;

        Console.WriteLine($"✅ Generated {features.Count} training features");
        return features;
    }

    private async Task<PirateFeatureRecord?> BuildFeatureRecordAsync(
        int pirateId,
        int arenaId,
        int roundId,
        RoundPiratePlacement placement,
        List<int> rivalIds,
        bool? isWinner)
    {
        // Get pirate data
        var pirate = await _context.Pirates.FirstOrDefaultAsync(p => p.PirateId == pirateId);
        if (pirate == null) return null;

        // Calculate all features SEQUENTIALLY
        var historicalStats = await GetHistoricalStatsAsync(pirateId, arenaId, roundId);
        var arenaWinRate = await GetArenaWinRateAsync(pirateId, arenaId, roundId);
        var recentWinRate = await GetRecentFormAsync(pirateId, roundId);
        var rivalPerformance = await GetRivalPerformanceAsync(pirateId, rivalIds, roundId);

        return new PirateFeatureRecord
        {
            RoundId = roundId,
            ArenaId = arenaId,
            PirateId = pirateId,
            Position = placement.PirateSeatPosition ?? 0,
            StartingOdds = placement.StartingOdds,
            CurrentOdds = placement.CurrentOdds ?? placement.StartingOdds,
            FoodAdjustment = placement.PirateFoodAdjustment,
            Strength = pirate.Strength ?? 0,
            Weight = pirate.Weight ?? 0,
            HistoricalWinRate = historicalStats.WinRate,
            TotalAppearances = historicalStats.TotalAppearances,
            AverageOdds = historicalStats.AverageOdds,
            ArenaWinRate = arenaWinRate,
            RecentWinRate = recentWinRate,
            WinRateVsCurrentRivals = rivalPerformance.WinRate,
            MatchesVsCurrentRivals = rivalPerformance.TotalMatches,
            AvgRivalStrength = rivalPerformance.AvgRivalStrength,
            IsWinner = isWinner
        };
    }
   
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
        var rivalPerformance = GetRivalPerformanceOptimized(pirateId, rivalIds, roundId, historicalResults);

        return new PirateFeatureRecord
        {
            RoundId = roundId,
            ArenaId = arenaId,
            PirateId = pirateId,
            Position = placement.PirateSeatPosition ?? 0,
            StartingOdds = placement.StartingOdds,
            CurrentOdds = placement.CurrentOdds ?? placement.StartingOdds,
            FoodAdjustment = placement.PirateFoodAdjustment,
            Strength = pirate.Strength ?? 0,
            Weight = pirate.Weight ?? 0,
            HistoricalWinRate = historicalStats.WinRate,
            TotalAppearances = historicalStats.TotalAppearances,
            AverageOdds = historicalStats.AverageOdds,
            ArenaWinRate = arenaWinRate,
            RecentWinRate = recentForm,
            WinRateVsCurrentRivals = rivalPerformance.WinRate,
            MatchesVsCurrentRivals = rivalPerformance.TotalMatches,
            AvgRivalStrength = rivalPerformance.AvgRivalStrength,
            IsWinner = isWinner
        };
    }
    private async Task<(double WinRate, int TotalAppearances, double AverageOdds)> GetHistoricalStatsAsync(int pirateId,
        int arenaId, int beforeRoundId)
    {
        var results = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId &&
                         rr.IsComplete &&
                         rr.RoundId.HasValue &&
                         rr.RoundId < beforeRoundId)
            .ToListAsync();

        if (!results.Any())
            return (0, 0, 0);

        var wins = results.Count(r => r.IsWinner);
        var avgOdds = results.Average(r => r.EndingOdds ?? 0);

        return ((double)wins / results.Count, results.Count, avgOdds);
    }
    
    private (double WinRate, int TotalAppearances, double AverageOdds) GetHistoricalStatsOptimized(List<RoundResult> historicalResults)
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

    private async Task<double> GetArenaWinRateAsync(int pirateId, int arenaId, int beforeRoundId)
    {
        var arenaResults = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId &&
                         rr.ArenaId == arenaId &&
                         rr.IsComplete &&
                         rr.RoundId.HasValue &&
                         rr.RoundId < beforeRoundId)
            .ToListAsync();

        if (!arenaResults.Any())
            return 0;

        return (double)arenaResults.Count(r => r.IsWinner) / arenaResults.Count;
    }

    private async Task<double> GetRecentFormAsync(int pirateId, int beforeRoundId, int lastN = 10)
    {
        var recentResults = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId &&
                         rr.IsComplete &&
                         rr.RoundId.HasValue &&
                         rr.RoundId < beforeRoundId)
            .OrderByDescending(rr => rr.RoundId)
            .Take(lastN)
            .ToListAsync();

        if (!recentResults.Any())
            return 0;

        return (double)recentResults.Count(r => r.IsWinner) / recentResults.Count;
    }

    private async Task<(double WinRate, int TotalMatches, double AvgRivalStrength)> GetRivalPerformanceAsync(
        int pirateId,
        List<int> rivalIds,
        int beforeRoundId)
    {
        if (!rivalIds.Any())
            return (0, 0, 0);

        var pirateResults = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId &&
                         rr.IsComplete &&
                         rr.RoundId.HasValue &&
                         rr.RoundId < beforeRoundId)
            .Select(rr => new { rr.RoundId, rr.ArenaId, rr.IsWinner })
            .ToListAsync();

        var rivalResults = await _context.RoundResults
            .Where(rr => rivalIds.Contains(rr.PirateId) &&
                         rr.IsComplete &&
                         rr.RoundId.HasValue &&
                         rr.RoundId < beforeRoundId)
            .Select(rr => new { rr.RoundId, rr.ArenaId, rr.PirateId })
            .ToListAsync();

        var rivalRoundSet = rivalResults
            .Select(rr => (rr.RoundId!.Value, rr.ArenaId))
            .ToHashSet();

        var matchups = pirateResults
            .Where(pr => rivalRoundSet.Contains((pr.RoundId!.Value, pr.ArenaId)))
            .ToList();

        var rivalStrengths = await _context.Pirates
            .Where(p => rivalIds.Contains(p.PirateId))
            .Select(p => p.Strength ?? 0)
            .ToListAsync();

        var avgRivalStrength = rivalStrengths.Any() ? rivalStrengths.Average() : 0;
        var winRate = matchups.Any() ? (double)matchups.Count(m => m.IsWinner) / matchups.Count : 0;

        return (winRate, matchups.Count, avgRivalStrength);
    }
    

    /// OPTIMIZED: GetRivalPerformanceAsync - Now fully in-memory
    private (double WinRate, int TotalMatches, double AvgRivalStrength) GetRivalPerformanceOptimized(
        int pirateId,
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
}