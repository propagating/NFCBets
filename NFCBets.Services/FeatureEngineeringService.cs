using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;

namespace NFCBets.Services
{
    public interface IFeatureEngineeringService
    {
        Task<List<PirateFeatureRecord>> CreateFeaturesForRoundAsync(int roundId);
        Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 4000);
    }

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

        public async Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 4000)
        {
            var features = new List<PirateFeatureRecord>();

            var completedRounds = await _context.RoundResults
                .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
                .Select(rr => rr.RoundId!.Value)
                .Distinct()
                .OrderBy(r => r)
                .Take(maxRounds)
                .ToListAsync();

            foreach (var roundId in completedRounds)
            {
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
            var historicalStats = await GetHistoricalStatsAsync(pirateId, arenaId, roundId);
            var recentForm = await GetRecentFormAsync(pirateId, roundId, 10);
            var arenaWinRate = await GetArenaWinRateAsync(pirateId, arenaId, roundId);

            return new PirateFeatureRecord
            {
                RoundId = roundId,
                ArenaId = arenaId,
                PirateId = pirateId,
                Position = placement.PirateSeatPosition ?? 0,
                StartingOdds = placement.StartingOdds,
                CurrentOdds = placement.CurrentOdds ?? placement.StartingOdds,
                FoodAdjustment = placement.PirateFoodAdjustment,
                
                // Historical features
                HistoricalWinRate = historicalStats.WinRate,
                TotalAppearances = historicalStats.TotalAppearances,
                AverageOdds = historicalStats.AverageOdds,
                
                // Arena-specific
                ArenaWinRate = arenaWinRate,
                
                // Recent form
                RecentWinRate = recentForm,
                
                // Target
                IsWinner = isWinner
            };
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

    public class PirateFeatureRecord
    {
        public int RoundId { get; set; }
        public int ArenaId { get; set; }
        public int PirateId { get; set; }
        public int Position { get; set; }
        public int StartingOdds { get; set; }
        public int CurrentOdds { get; set; }
        public int FoodAdjustment { get; set; }
        public double HistoricalWinRate { get; set; }
        public int TotalAppearances { get; set; }
        public double AverageOdds { get; set; }
        public double ArenaWinRate { get; set; }
        public double RecentWinRate { get; set; }
        public bool? IsWinner { get; set; }
    }
}