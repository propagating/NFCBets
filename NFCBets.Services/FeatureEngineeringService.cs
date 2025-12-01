using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NFCBets.EF.Models;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;

namespace NFCBets.Services
{

    public class FeatureEngineeringService : IFeatureEngineeringService
    {
        private readonly NfcbetsContext _context;
        private readonly IFoodAdjustmentService _foodAdjustmentService;
        private readonly ILogger<FeatureEngineeringService> _logger;

        public FeatureEngineeringService(NfcbetsContext context, IFoodAdjustmentService foodAdjustmentService, ILogger<FeatureEngineeringService> logger)
        {
            _context = context;
            _foodAdjustmentService = foodAdjustmentService;
            _logger = logger;
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

                var feature = await BuildFeatureRecordAsync(
                    placement.PirateId.Value,
                    placement.ArenaId.Value,
                    roundId,
                    placement,
                    null // No outcome yet for prediction
                );

                if (feature != null)
                    features.Add(feature);
            }

            return features;
        }

        public async Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 10000)
        {
            var features = new List<PirateFeatureRecord>();

            var completedRounds = await _context.RoundResults
                .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
                .Select(rr => rr.RoundId!.Value)
                .Distinct()
                .OrderBy(r => r)
                .Take(maxRounds)
                .ToListAsync();

            _logger.LogWarning($"Creating training data for {completedRounds.Count} rounds");
            var count = 0;
            foreach (var roundId in completedRounds)
            {
                count++;
                if(count % 100 == 0){_logger.LogWarning($"Completed {count}/{completedRounds.Count} rounds");}
                var roundPlacements = await _context.RoundPiratePlacements
                    .Where(rpp => rpp.RoundId == roundId)
                    .ToListAsync();

                var roundResults = await _context.RoundResults
                    .Where(rr => rr.RoundId == roundId)
                    .ToListAsync();

                foreach (var placement in roundPlacements)
                {
                    if (!placement.PirateId.HasValue || !placement.ArenaId.HasValue) continue;

                    var result = roundResults.FirstOrDefault(rr => 
                        rr.PirateId == placement.PirateId.Value && 
                        rr.ArenaId == placement.ArenaId.Value);

                    var feature = await BuildFeatureRecordAsync(
                        placement.PirateId.Value,
                        placement.ArenaId.Value,
                        roundId,
                        placement,
                        result?.IsWinner
                    );

                    if (feature != null)
                        features.Add(feature);
                }
            }

            return features;
        }

         private async Task<PirateFeatureRecord?> BuildFeatureRecordAsync(
        int pirateId, 
        int arenaId, 
        int roundId, 
        RoundPiratePlacement placement,
        bool? isWinner)
    {
        // Get pirate data
        var pirate = await _context.Pirates.FirstOrDefaultAsync(p => p.PirateId == pirateId);
        if (pirate == null) return null;

        // Get rivals in this arena
        var rivalsInArena = await _context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId && 
                         rpp.ArenaId == arenaId && 
                         rpp.PirateId != pirateId)
            .Select(rpp => rpp.PirateId!.Value)
            .ToListAsync();

        // Calculate features
        var historicalStats = await GetHistoricalStatsAsync(pirateId, arenaId, roundId);
        var recentForm = await GetRecentFormAsync(pirateId, roundId, 10);
        var arenaWinRate = await GetArenaWinRateAsync(pirateId, arenaId, roundId);
        var rivalPerformance = await GetRivalPerformanceAsync(pirateId, rivalsInArena, roundId);

        return new PirateFeatureRecord
        {
            RoundId = roundId,
            ArenaId = arenaId,
            PirateId = pirateId,
            Position = placement.PirateSeatPosition ?? 0,
            StartingOdds = placement.StartingOdds,
            CurrentOdds = placement.CurrentOdds ?? placement.StartingOdds,
            FoodAdjustment = placement.PirateFoodAdjustment,
            
            // Pirate attributes
            Strength = pirate.Strength ?? 0,
            Weight = pirate.Weight ?? 0,
            
            // Historical features
            HistoricalWinRate = historicalStats.WinRate,
            TotalAppearances = historicalStats.TotalAppearances,
            AverageOdds = historicalStats.AverageOdds,
            
            // Arena-specific
            ArenaWinRate = arenaWinRate,
            
            // Recent form
            RecentWinRate = recentForm,
            
            // Rival performance
            WinRateVsCurrentRivals = rivalPerformance.WinRate,
            MatchesVsCurrentRivals = rivalPerformance.TotalMatches,
            AvgRivalStrength = rivalPerformance.AvgRivalStrength,
            
            // Target
            IsWinner = isWinner
        };
    }

    private async Task<(double WinRate, int TotalMatches, double AvgRivalStrength)> GetRivalPerformanceAsync(
        int pirateId, 
        List<int> rivalIds, 
        int beforeRoundId)
    {
        if (!rivalIds.Any())
            return (0, 0, 0);

        // Get all historical rounds where this pirate faced these rivals
        var pirateRounds = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId && 
                        rr.IsComplete && 
                        rr.RoundId < beforeRoundId)
            .Select(rr => new { rr.RoundId, rr.ArenaId, rr.IsWinner })
            .ToListAsync();

        var rivalRounds = await _context.RoundResults
            .Where(rr => rivalIds.Contains(rr.PirateId) && 
                        rr.IsComplete && 
                        rr.RoundId < beforeRoundId)
            .Select(rr => new { rr.RoundId, rr.ArenaId, rr.PirateId })
            .ToListAsync();

        // Find matches where pirate faced these specific rivals
        var matchups = pirateRounds
            .Join(rivalRounds,
                pr => new { pr.RoundId, pr.ArenaId },
                rr => new { rr.RoundId, rr.ArenaId },
                (pr, rr) => new { pr.IsWinner, RivalId = rr.PirateId })
            .ToList();

        // Calculate rival strengths
        var rivalStrengths = await _context.Pirates
            .Where(p => rivalIds.Contains(p.PirateId))
            .Select(p => p.Strength ?? 0)
            .ToListAsync();

        var avgRivalStrength = rivalStrengths.Any() ? rivalStrengths.Average() : 0;
        var winRate = matchups.Any() ? (double)matchups.Count(m => m.IsWinner) / matchups.Count : 0;

        return (winRate, matchups.Count, avgRivalStrength);
    }

        private async Task<(double WinRate, int TotalAppearances, double AverageOdds)> GetHistoricalStatsAsync(int pirateId, int arenaId, int beforeRoundId)
        {
            var results = await _context.RoundResults
                .Where(rr => rr.PirateId == pirateId && 
                            rr.IsComplete && 
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
                            rr.RoundId < beforeRoundId)
                .OrderByDescending(rr => rr.RoundId)
                .Take(lastN)
                .ToListAsync();

            if (!recentResults.Any())
                return 0;

            return (double)recentResults.Count(r => r.IsWinner) / recentResults.Count;
        }
    }
}