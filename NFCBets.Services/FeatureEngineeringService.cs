using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services;

public class FeatureEngineeringService : IFeatureEngineeringService
{
    private readonly NfcbetsContext _context;
    private readonly IFoodAdjustmentService _foodAdjustmentService;

    public FeatureEngineeringService(NfcbetsContext context, IFoodAdjustmentService foodAdjustmentService)
    {
        _context = context;
        _foodAdjustmentService = foodAdjustmentService;
    }

    public async Task<List<PirateFeatureRecord>> CreateFeaturesForRoundAsync(int roundId)
    {
        var features = new List<PirateFeatureRecord>();

        var placements = await _context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId)
            .Include(rpp => rpp.Pirate)
            .Include(rpp => rpp.Arena)
            .ToListAsync();

        foreach (var placement in placements)
        {
            if (!placement.PirateId.HasValue || !placement.ArenaId.HasValue) continue;

            var rivalsInArena = await _context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundId &&
                              rpp.ArenaId == placement.ArenaId &&
                              rpp.PirateId != placement.PirateId &&
                              rpp.PirateId.HasValue)
                .Select(rpp => rpp.PirateId!.Value)
                .ToListAsync();

            var feature = await BuildFeatureRecordAsync(
                placement.PirateId.Value,
                placement.ArenaId.Value,
                roundId,
                placement,
                rivalsInArena,
                null
            );

            if (feature != null)
                features.Add(feature);
        }

        return features;
    }

    public async Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 4000)
    {
        Console.WriteLine("📊 Creating training data (sequential processing)...");

        var features = new List<PirateFeatureRecord>();

        // Get all completed rounds SEQUENTIALLY
        var completedRounds = await _context.RoundResults
            .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
            .Select(rr => rr.RoundId!.Value)
            .Distinct()
            .OrderBy(r => r)
            .Take(maxRounds)
            .ToListAsync();

        Console.WriteLine($"Processing {completedRounds.Count} rounds...");

        var processedCount = 0;

        // Process each round SEQUENTIALLY
        foreach (var roundId in completedRounds)
        {
            var roundPlacements = await _context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundId)
                .Include(rpp => rpp.Pirate)
                .ToListAsync();

            var roundResults = await _context.RoundResults
                .Where(rr => rr.RoundId == roundId)
                .ToListAsync();

            // Process each placement SEQUENTIALLY
            foreach (var placement in roundPlacements)
            {
                if (!placement.PirateId.HasValue || !placement.ArenaId.HasValue) continue;

                var result = roundResults.FirstOrDefault(rr =>
                    rr.PirateId == placement.PirateId.Value &&
                    rr.ArenaId == placement.ArenaId.Value);

                var rivalsInArena = roundPlacements
                    .Where(rpp => rpp.ArenaId == placement.ArenaId &&
                                  rpp.PirateId != placement.PirateId &&
                                  rpp.PirateId.HasValue)
                    .Select(rpp => rpp.PirateId!.Value)
                    .ToList();

                var feature = await BuildFeatureRecordAsync(
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

            processedCount++;
            if (processedCount % 100 == 0)
                Console.WriteLine($"   Processed {processedCount}/{completedRounds.Count} rounds...");
        }

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
}