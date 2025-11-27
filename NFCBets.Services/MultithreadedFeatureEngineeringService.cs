using System.Collections.Concurrent;
using System.Threading;
using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;

namespace NFCBets.Services
{
    public class MultithreadedFeatureEngineeringService : IFeatureEngineeringService
    {
        private readonly IDbContextFactory<NfcbetsContext> _contextFactory;
        private readonly SemaphoreSlim _processingLimiter;

        public MultithreadedFeatureEngineeringService(IDbContextFactory<NfcbetsContext> contextFactory)
        {
            _contextFactory = contextFactory;
            _processingLimiter = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
        }

        public async Task<List<PirateFeatureRecord>> CreateTrainingFeaturesAsync(DateTime? startDate = null, int? maxRounds = null)
        {
            Console.WriteLine("🔧 Starting multithreaded feature engineering...");
            
            // Get all completed results to process
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.RoundResults
                .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
                .OrderBy(rr => rr.RoundId);
            

            var allResults = await query.Take(maxRounds ?? int.MaxValue).ToListAsync();

            Console.WriteLine($"📊 Processing {allResults.Count} results across multiple threads...");

            // Group by round for processing
            var roundGroups = allResults
                .GroupBy(rr => rr.RoundId!.Value)
                .OrderBy(g => g.Key)
                .ToList();

            var allFeatures = new ConcurrentBag<PirateFeatureRecord>();
            var completed = 0;
            var startTime = DateTime.Now;

            // Process rounds in parallel
            var tasks = roundGroups.Select(async roundGroup =>
            {
                await _processingLimiter.WaitAsync();
                try
                {
                    var roundFeatures = await ProcessRoundFeaturesAsync(roundGroup.Key, roundGroup.ToList());
                    
                    foreach (var feature in roundFeatures)
                    {
                        allFeatures.Add(feature);
                    }
                    
                    var completedCount = Interlocked.Increment(ref completed);
                    
                    if (completedCount % 100 == 0)
                    {
                        var elapsed = DateTime.Now - startTime;
                        var rate = completedCount / elapsed.TotalSeconds;
                        Console.WriteLine($"📈 Processed {completedCount}/{roundGroups.Count} rounds ({rate:F1} rounds/sec)");
                    }
                }
                finally
                {
                    _processingLimiter.Release();
                }
            });

            await Task.WhenAll(tasks);

            var totalFeatures = allFeatures.ToList();
            var totalTime = DateTime.Now - startTime;
            
            Console.WriteLine($"✅ Feature engineering complete: {totalFeatures.Count} features in {totalTime.TotalSeconds:F1}s");
            
            return totalFeatures;
        }

        public async Task<List<PirateFeatureRecord>> CreatePredictionFeaturesAsync(int roundId)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var placements = await context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundId && rpp.PirateId.HasValue)
                .ToListAsync();

            var tasks = placements.Select(async placement =>
            {
                return await CreateFeatureForPirateAsync(
                    placement.PirateId!.Value,
                    roundId,
                    placement.ArenaId!.Value,
                    null // Unknown outcome for prediction
                );
            });

            var features = await Task.WhenAll(tasks);
            return features.Where(f => f != null).Cast<PirateFeatureRecord>().ToList();
        }

        private async Task<List<PirateFeatureRecord>> ProcessRoundFeaturesAsync(int roundId, List<RoundResult> roundResults)
        {
            var features = new List<PirateFeatureRecord>();

            // Process all pirates in this round concurrently
            var tasks = roundResults.Select(async result =>
            {
                return await CreateFeatureForPirateAsync(
                    result.PirateId,
                    roundId,
                    result.ArenaId,
                    result.IsWinner
                );
            });

            var roundFeatures = await Task.WhenAll(tasks);
            return roundFeatures.Where(f => f != null).Cast<PirateFeatureRecord>().ToList();
        }

        private async Task<PirateFeatureRecord?> CreateFeatureForPirateAsync(int pirateId, int roundId, int arenaId, bool? actualOutcome)
        {
            // Use a fresh context for each thread
            using var context = _contextFactory.CreateDbContext();
            
            var placement = await context.RoundPiratePlacements
                .FirstOrDefaultAsync(rpp => rpp.RoundId == roundId && 
                                           rpp.ArenaId == arenaId && 
                                           rpp.PirateId == pirateId);

            if (placement == null) return null;

            // Calculate features in parallel
            var historicalTask = CalculateHistoricalFeaturesAsync(context, pirateId, roundId);
            var arenaTask = CalculateArenaSpecificFeaturesAsync(context, pirateId, arenaId, roundId);
            var competitiveTask = CalculateCompetitiveFeaturesAsync(context, pirateId, roundId, arenaId);

            var oddsFeatures = CalculateOddsFeatures(placement);

            // Wait for all async calculations to complete
            var historicalFeatures = await historicalTask;
            var arenaFeatures = await arenaTask;
            var competitiveFeatures = await competitiveTask;

            return new PirateFeatureRecord
            {
                // Basic identifiers
                RoundId = roundId,
                ArenaId = arenaId,
                PirateId = pirateId,
                Position = placement.PirateSeatPosition ?? 0,

                // Odds features
                StartingOdds = placement.StartingOdds,
                CurrentOdds = placement.CurrentOdds ?? placement.StartingOdds,
                OddsMovement = oddsFeatures.OddsMovement,
                OddsRank = oddsFeatures.OddsRank,
                ImpliedProbability = oddsFeatures.ImpliedProbability,

                // Food adjustment
                FoodAdjustment = placement.PirateFoodAdjustment,

                // Historical performance
                OverallWinRate = historicalFeatures.OverallWinRate,
                RecentWinRate = historicalFeatures.RecentWinRate,
                WinStreak = historicalFeatures.WinStreak,
                TotalAppearances = historicalFeatures.TotalAppearances,

                // Arena-specific features
                ArenaWinRate = arenaFeatures.ArenaWinRate,
                ArenaAppearances = arenaFeatures.ArenaAppearances,
                PositionWinRate = arenaFeatures.PositionWinRate,

                // Odds-based historical features
                AverageStartingOdds = historicalFeatures.AverageStartingOdds,
                AverageWinningOdds = historicalFeatures.AverageWinningOdds,
                WinRateAtCurrentOdds = historicalFeatures.WinRateAtCurrentOdds,

                // Food-related features
                AverageFoodAdjustment = historicalFeatures.AverageFoodAdjustment,
                WinRateWithPositiveFoodAdjustment = historicalFeatures.WinRateWithPositiveFoodAdjustment,
                WinRateWithNegativeFoodAdjustment = historicalFeatures.WinRateWithNegativeFoodAdjustment,

                // Competitive features
                HeadToHeadWinRate = competitiveFeatures.HeadToHeadWinRate,
                HeadToHeadAppearances = competitiveFeatures.HeadToHeadAppearances,
                AverageOpponentWinRate = competitiveFeatures.AverageOpponentWinRate,
                StrongestOpponentWinRate = competitiveFeatures.StrongestOpponentWinRate,
                FieldStrengthRank = competitiveFeatures.FieldStrengthRank,
                IsFavorite = competitiveFeatures.IsFavorite,
                IsUnderdog = competitiveFeatures.IsUnderdog,
                OddsAdvantageVsField = competitiveFeatures.OddsAdvantageVsField,
                NumberOfStrongerOpponents = competitiveFeatures.NumberOfStrongerOpponents,
                NumberOfWeakerOpponents = competitiveFeatures.NumberOfWeakerOpponents,

                // Target variable
                Won = actualOutcome
            };
        }

        // Updated helper methods to use the provided context (for thread safety)
        private async Task<HistoricalFeatures> CalculateHistoricalFeaturesAsync(NfcbetsContext context, int pirateId, int currentRoundId)
        {
            var historicalResults = await context.RoundResults
                .Where(rr => rr.PirateId == pirateId && 
                            rr.IsComplete && 
                            rr.RoundId.HasValue &&
                            rr.RoundId.Value < currentRoundId)
                .OrderByDescending(rr => rr.RoundId)
                .ToListAsync();

            var historicalPlacements = await context.RoundPiratePlacements
                .Where(rpp => rpp.PirateId == pirateId && 
                             rpp.RoundId.HasValue &&
                             rpp.RoundId.Value < currentRoundId)
                .OrderByDescending(rpp => rpp.RoundId)
                .ToListAsync();

            if (!historicalResults.Any())
                return new HistoricalFeatures();

            var recentResults = historicalResults.Take(20).ToList();
            var wins = historicalResults.Where(r => r.IsWinner).ToList();
            var recentWins = recentResults.Where(r => r.IsWinner).ToList();

            var positiveFoodRoundIds = historicalPlacements
                .Where(p => p.PirateFoodAdjustment > 0 && p.RoundId.HasValue)
                .Select(p => p.RoundId!.Value)
                .ToHashSet();

            var negativeFoodRoundIds = historicalPlacements
                .Where(p => p.PirateFoodAdjustment < 0 && p.RoundId.HasValue)
                .Select(p => p.RoundId!.Value)
                .ToHashSet();

            var positiveFoodResults = historicalResults
                .Where(r => r.RoundId.HasValue && positiveFoodRoundIds.Contains(r.RoundId.Value))
                .ToList();
            
            var negativeFoodResults = historicalResults
                .Where(r => r.RoundId.HasValue && negativeFoodRoundIds.Contains(r.RoundId.Value))
                .ToList();

            return new HistoricalFeatures
            {
                OverallWinRate = (double)wins.Count / historicalResults.Count,
                RecentWinRate = recentResults.Any() ? (double)recentWins.Count / recentResults.Count : 0,
                WinStreak = CalculateCurrentWinStreak(historicalResults),
                TotalAppearances = historicalResults.Count,
                AverageStartingOdds = historicalPlacements.Any() ? historicalPlacements.Average(p => p.StartingOdds) : 0,
                AverageWinningOdds = wins.Any() ? wins.Where(w => w.EndingOdds.HasValue).Average(w => w.EndingOdds!.Value) : 0,
                WinRateAtCurrentOdds = historicalResults.Any() ? (double)wins.Count / historicalResults.Count : 0,
                AverageFoodAdjustment = historicalPlacements.Any() ? historicalPlacements.Average(p => p.PirateFoodAdjustment) : 0,
                WinRateWithPositiveFoodAdjustment = positiveFoodResults.Any() ? (double)positiveFoodResults.Count(r => r.IsWinner) / positiveFoodResults.Count : 0,
                WinRateWithNegativeFoodAdjustment = negativeFoodResults.Any() ? (double)negativeFoodResults.Count(r => r.IsWinner) / negativeFoodResults.Count : 0
            };
        }

        // Similar updates for other calculation methods...
        private async Task<ArenaFeatures> CalculateArenaSpecificFeaturesAsync(NfcbetsContext context, int pirateId, int arenaId, int currentRoundId)
        {
            // Run arena queries in parallel
            var arenaResultsTask = context.RoundResults
                .Where(rr => rr.PirateId == pirateId && 
                             rr.ArenaId == arenaId && 
                             rr.IsComplete && 
                             rr.RoundId.HasValue &&
                             rr.RoundId.Value < currentRoundId)
                .ToListAsync();

            var arenaPlacementsTask = context.RoundPiratePlacements
                .Where(rpp => rpp.PirateId == pirateId && 
                              rpp.ArenaId == arenaId && 
                              rpp.RoundId.HasValue &&
                              rpp.RoundId.Value < currentRoundId)
                .ToListAsync();

            var currentPositionTask = context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == currentRoundId && 
                              rpp.ArenaId == arenaId && 
                              rpp.PirateId == pirateId)
                .Select(rpp => rpp.PirateSeatPosition ?? 0)
                .FirstOrDefaultAsync();

            // Wait for all queries to complete
            var arenaResults = await arenaResultsTask;
            var arenaPlacements = await arenaPlacementsTask;
            var currentPosition = await currentPositionTask;

            // Process position-specific data
            var samePositionRoundIds = arenaPlacements
                .Where(p => p.PirateSeatPosition == currentPosition && p.RoundId.HasValue)
                .Select(p => p.RoundId!.Value)
                .ToHashSet();

            var positionResults = arenaResults
                .Where(r => r.RoundId.HasValue && samePositionRoundIds.Contains(r.RoundId.Value))
                .ToList();

            return new ArenaFeatures
            {
                ArenaWinRate = arenaResults.Any() ? (double)arenaResults.Count(r => r.IsWinner) / arenaResults.Count : 0,
                ArenaAppearances = arenaResults.Count,
                PositionWinRate = positionResults.Any() ? (double)positionResults.Count(r => r.IsWinner) / positionResults.Count : 0
            };
        }
        
        private async Task<CompetitiveFeatures> CalculateCompetitiveFeaturesAsync(NfcbetsContext context, int pirateId, int roundId, int arenaId)
        {
            // Get arena pirates data
            var arenaPirates = await context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == roundId && 
                             rpp.ArenaId == arenaId && 
                             rpp.PirateId.HasValue)
                .Select(rpp => new ArenaPirateData
                { 
                    PirateId = rpp.PirateId!.Value, 
                    Position = rpp.PirateSeatPosition ?? 0,
                    Odds = rpp.CurrentOdds ?? rpp.StartingOdds 
                })
                .ToListAsync();

            var opponentIds = arenaPirates.Where(p => p.PirateId != pirateId).Select(p => p.PirateId).ToList();
            var currentPirate = arenaPirates.FirstOrDefault(p => p.PirateId == pirateId);

            if (currentPirate == null || !opponentIds.Any())
            {
                return new CompetitiveFeatures();
            }

            // Run competitive analysis in parallel
            var headToHeadTask = CalculateHeadToHeadStatsAsync(context, pirateId, opponentIds, roundId);
            var fieldStrengthTask = CalculateArenaFieldStrengthAsync(context, pirateId, arenaPirates.Select(p => p.PirateId).ToList(), roundId);
            
            // Competitive position can be calculated immediately (no DB access)
            var competitivePosition = CalculateCompetitivePosition(currentPirate, arenaPirates);

            // Wait for async calculations
            var headToHeadStats = await headToHeadTask;
            var fieldStrengthStats = await fieldStrengthTask;

            return new CompetitiveFeatures
            {
                HeadToHeadWinRate = headToHeadStats.OverallWinRate,
                HeadToHeadAppearances = headToHeadStats.TotalMatchups,
                WinsAgainstStrongestOpponent = headToHeadStats.WinsVsStrongest,
                WinsAgainstWeakestOpponent = headToHeadStats.WinsVsWeakest,
                AverageOpponentWinRate = fieldStrengthStats.AverageOpponentWinRate,
                StrongestOpponentWinRate = fieldStrengthStats.StrongestOpponentWinRate,
                WeakestOpponentWinRate = fieldStrengthStats.WeakestOpponentWinRate,
                FieldStrengthRank = fieldStrengthStats.PirateRankInField,
                IsFavorite = competitivePosition.IsFavorite,
                IsUnderdog = competitivePosition.IsUnderdog,
                OddsAdvantageVsField = competitivePosition.OddsAdvantageVsField,
                NumberOfStrongerOpponents = competitivePosition.StrongerOpponents,
                NumberOfWeakerOpponents = competitivePosition.WeakerOpponents
            };
        }
        
        private async Task<HeadToHeadStats> CalculateHeadToHeadStatsAsync(NfcbetsContext context, int pirateId, List<int> opponentIds, int currentRoundId)
        {
            // Get all pirate's historical results in one query
            var pirateHistoricalResults = await context.RoundResults
                .Where(rr => rr.PirateId == pirateId && 
                            rr.IsComplete && 
                            rr.RoundId.HasValue &&
                            rr.RoundId < currentRoundId)
                .Select(rr => new { rr.RoundId, rr.ArenaId, rr.IsWinner })
                .ToListAsync();

            // Get opponent data in parallel
            var opponentTasks = opponentIds.Select(async opponentId =>
            {
                var opponentRounds = await context.RoundResults
                    .Where(rr => rr.PirateId == opponentId && 
                                rr.IsComplete && 
                                rr.RoundId.HasValue &&
                                rr.RoundId < currentRoundId)
                    .Select(rr => new { rr.RoundId, rr.ArenaId, rr.IsWinner })
                    .ToListAsync();

                // Calculate win rate for this opponent
                var winRate = opponentRounds.Any() ? 
                    (double)opponentRounds.Count(r => r.IsWinner) / opponentRounds.Count : 0;

                return new { OpponentId = opponentId, Rounds = opponentRounds, WinRate = winRate };
            }).ToArray();

            var opponentData = await Task.WhenAll(opponentTasks);

            // Find matchups (rounds where both pirates were in the same arena)
            var matchupRounds = new HashSet<(int RoundId, int ArenaId)>();
            
            foreach (var opponent in opponentData)
            {
                var opponentRoundArenas = opponent.Rounds.Select(r => (r.RoundId!.Value, r.ArenaId)).ToHashSet();
                
                foreach (var pirateResult in pirateHistoricalResults)
                {
                    if (opponentRoundArenas.Contains((pirateResult.RoundId!.Value, pirateResult.ArenaId)))
                    {
                        matchupRounds.Add((pirateResult.RoundId!.Value, pirateResult.ArenaId));
                    }
                }
            }

            // Calculate stats from matchups
            var relevantResults = pirateHistoricalResults
                .Where(r => matchupRounds.Contains((r.RoundId!.Value, r.ArenaId)))
                .ToList();

            // Get strongest/weakest opponents
            var strongestOpponent = opponentData.OrderByDescending(o => o.WinRate).FirstOrDefault();
            var weakestOpponent = opponentData.OrderBy(o => o.WinRate).FirstOrDefault();

            // Calculate wins vs strongest/weakest (parallel)
            var winsVsStrongestTask = strongestOpponent != null ? 
                CalculateWinsAgainstSpecificOpponentAsync(context, pirateId, strongestOpponent.OpponentId, currentRoundId) : 
                Task.FromResult(0);
                
            var winsVsWeakestTask = weakestOpponent != null ? 
                CalculateWinsAgainstSpecificOpponentAsync(context, pirateId, weakestOpponent.OpponentId, currentRoundId) : 
                Task.FromResult(0);

            var winsVsStrongest = await winsVsStrongestTask;
            var winsVsWeakest = await winsVsWeakestTask;

            return new HeadToHeadStats
            {
                OverallWinRate = relevantResults.Any() ? (double)relevantResults.Count(r => r.IsWinner) / relevantResults.Count : 0,
                TotalMatchups = relevantResults.Count,
                WinsVsStrongest = winsVsStrongest,
                WinsVsWeakest = winsVsWeakest
            };
        }
        
        private async Task<FieldStrengthStats> CalculateArenaFieldStrengthAsync(NfcbetsContext context, int pirateId, List<int> allPirateIds, int currentRoundId)
        {
            // Get win rate data for all pirates in parallel
            var pirateStatsTasks = allPirateIds.Select(async id =>
            {
                var results = await context.RoundResults
                    .Where(rr => rr.PirateId == id && 
                                 rr.IsComplete && 
                                 rr.RoundId.HasValue &&
                                 rr.RoundId.Value < currentRoundId)
                    .ToListAsync();

                var winRate = results.Any() ? (double)results.Count(r => r.IsWinner) / results.Count : 0;
        
                return new { PirateId = id, WinRate = winRate, TotalGames = results.Count };
            }).ToArray();

            var allPirateStats = await Task.WhenAll(pirateStatsTasks);
    
            var currentPirateStats = allPirateStats.First(p => p.PirateId == pirateId);
            var opponentStats = allPirateStats.Where(p => p.PirateId != pirateId).ToList();

            // Calculate field metrics
            var opponentWinRates = opponentStats.Select(o => o.WinRate).ToList();
            var allWinRates = allPirateStats.Select(p => p.WinRate).OrderByDescending(wr => wr).ToList();
            var pirateRank = allWinRates.IndexOf(currentPirateStats.WinRate) + 1;

            return new FieldStrengthStats
            {
                AverageOpponentWinRate = opponentWinRates.Any() ? opponentWinRates.Average() : 0,
                StrongestOpponentWinRate = opponentWinRates.Any() ? opponentWinRates.Max() : 0,
                WeakestOpponentWinRate = opponentWinRates.Any() ? opponentWinRates.Min() : 0,
                PirateRankInField = pirateRank
            };
        }
        
        private async Task<int> CalculateWinsAgainstSpecificOpponentAsync(NfcbetsContext context, int pirateId, int opponentId, int currentRoundId)
        {
            // Get both pirates' round data in parallel
            var pirateRoundsTask = context.RoundResults
                .Where(rr => rr.PirateId == pirateId && 
                             rr.IsComplete && 
                             rr.RoundId.HasValue &&
                             rr.RoundId < currentRoundId)
                .Select(rr => new { rr.RoundId, rr.ArenaId, rr.IsWinner })
                .ToListAsync();

            var opponentRoundsTask = context.RoundResults
                .Where(rr => rr.PirateId == opponentId && 
                             rr.IsComplete && 
                             rr.RoundId.HasValue &&
                             rr.RoundId < currentRoundId)
                .Select(rr => new { rr.RoundId, rr.ArenaId })
                .ToListAsync();

            var pirateRounds = await pirateRoundsTask;
            var opponentRounds = await opponentRoundsTask;

            // Create lookup for opponent rounds for fast intersection
            var opponentRoundSet = opponentRounds
                .Select(or => (or.RoundId!.Value, or.ArenaId))
                .ToHashSet();

            // Find direct matchups
            var directMatchups = pirateRounds
                .Where(pr => opponentRoundSet.Contains((pr.RoundId!.Value, pr.ArenaId)))
                .ToList();

            return directMatchups.Count(m => m.IsWinner);
        }
        
        /// <summary>
        /// Calculate competitive position based on odds (no DB access - can be synchronous)
        /// </summary>
        private CompetitivePositionStats CalculateCompetitivePosition(ArenaPirateData currentPirate, List<ArenaPirateData> allPirates)
        {
            var allOdds = allPirates.Select(p => p.Odds).OrderBy(o => o).ToList();
            var currentOdds = currentPirate.Odds;
        
            var isFavorite = currentOdds == allOdds.First();
            var isUnderdog = currentOdds == allOdds.Last();
        
            var averageFieldOdds = allOdds.Average();
            var oddsAdvantage = averageFieldOdds - currentOdds; // Positive means better odds than average
        
            var strongerOpponents = allOdds.Count(odds => odds < currentOdds);
            var weakerOpponents = allOdds.Count(odds => odds > currentOdds);

            return new CompetitivePositionStats
            {
                IsFavorite = isFavorite,
                IsUnderdog = isUnderdog,
                OddsAdvantageVsField = oddsAdvantage,
                StrongerOpponents = strongerOpponents,
                WeakerOpponents = weakerOpponents
            };
        }

        /// <summary>
        /// Calculate current win streak (no DB access needed - processes in-memory data)
        /// </summary>
        private int CalculateCurrentWinStreak(List<RoundResult> historicalResults)
        {
            int streak = 0;
        
            // Results should be ordered by RoundId descending (most recent first)
            foreach (var result in historicalResults.OrderByDescending(r => r.RoundId))
            {
                if (result.IsWinner)
                    streak++;
                else
                    break;
            }
        
            return streak;
        }

        /// <summary>
        /// Calculate odds features (no DB access - synchronous)
        /// </summary>
        private OddsFeatures CalculateOddsFeatures(RoundPiratePlacement placement)
        {
            var startingOdds = placement.StartingOdds;
            var currentOdds = placement.CurrentOdds ?? startingOdds;
        
            return new OddsFeatures
            {
                OddsMovement = startingOdds != 0 ? (double)(currentOdds - startingOdds) / startingOdds : 0,
                OddsRank = placement.PirateSeatPosition ?? 0 + 1, // Simplified - should be calculated vs other arena pirates
                ImpliedProbability = currentOdds != 0 ? 1.0 / currentOdds : 0
            };
        }
         /// <summary>
    /// Process all pirates in a specific round to generate their features (PUBLIC for BatchProcessingService)
    /// </summary>
    public async Task<List<PirateFeatureRecord>> ProcessRoundFeaturesOptimizedAsync(int roundId, List<RoundResult> roundResults)
    {
        // Create a fresh context for this round
        using var context = _contextFactory.CreateDbContext();
        
        // Pre-load data that will be needed for this round
        await PreloadRoundDataAsync(context, roundId, roundResults);

        // Process all pirates in this round concurrently
        var featureTasks = roundResults.Select(async result =>
        {
            return await CreateFeatureForPirateOptimizedAsync(context,
                result.PirateId, 
                roundId, 
                result.ArenaId, 
                result.IsWinner);
        }).ToArray();

        var features = await Task.WhenAll(featureTasks);
        return features.Where(f => f != null).Cast<PirateFeatureRecord>().ToList();
    }

    /// <summary>
    /// Pre-load commonly needed data for a round to reduce individual queries (PUBLIC for external access)
    /// </summary>
    public async Task PreloadRoundDataAsync(NfcbetsContext context, int roundId, List<RoundResult> roundResults)
    {
        var pirateIds = roundResults.Select(r => r.PirateId).Distinct().ToList();
        var arenaIds = roundResults.Select(r => r.ArenaId).Distinct().ToList();
        
        // Pre-load placement data for all pirates in this round
        await context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId && pirateIds.Contains(rpp.PirateId!.Value))
            .LoadAsync(); // LoadAsync caches the data in the context

        // Pre-load recent historical data for these pirates (speeds up subsequent queries)
        await context.RoundResults
            .Where(rr => pirateIds.Contains(rr.PirateId) && 
                        rr.IsComplete && 
                        rr.RoundId.HasValue &&
                        rr.RoundId.Value < roundId &&
                        rr.RoundId.Value >= roundId - 100) // Last 100 rounds only
            .LoadAsync();

        // Pre-load arena placements for competitive analysis
        await context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId && arenaIds.Contains(rpp.ArenaId!.Value))
            .LoadAsync();
    }

    /// <summary>
    /// Create features for a single pirate using pre-loaded context data (PUBLIC)
    /// </summary>
    public async Task<PirateFeatureRecord?> CreateFeatureForPirateOptimizedAsync(NfcbetsContext context, int pirateId, int roundId, int arenaId, bool? actualOutcome)
    {
        // Get placement data (should be cached from pre-loading)
        var placement = await context.RoundPiratePlacements
            .FirstOrDefaultAsync(rpp => rpp.RoundId == roundId && 
                                       rpp.ArenaId == arenaId && 
                                       rpp.PirateId == pirateId);

        if (placement == null) return null;

        // Calculate all features in parallel using the context with pre-loaded data
        var historicalTask = CalculateHistoricalFeaturesAsync(context, pirateId, roundId);
        var arenaTask = CalculateArenaSpecificFeaturesAsync(context, pirateId, arenaId, roundId);
        var competitiveTask = CalculateCompetitiveFeaturesAsync(context, pirateId, roundId, arenaId);

        // Odds features don't need async processing
        var oddsFeatures = CalculateOddsFeatures(placement);

        // Wait for all async calculations to complete
        var historicalFeatures = await historicalTask;
        var arenaFeatures = await arenaTask;
        var competitiveFeatures = await competitiveTask;

        return new PirateFeatureRecord
        {
            // Basic identifiers
            RoundId = roundId,
            ArenaId = arenaId,
            PirateId = pirateId,
            Position = placement.PirateSeatPosition ?? 0,

            // Odds features
            StartingOdds = placement.StartingOdds,
            CurrentOdds = placement.CurrentOdds ?? placement.StartingOdds,
            OddsMovement = oddsFeatures.OddsMovement,
            OddsRank = oddsFeatures.OddsRank,
            ImpliedProbability = oddsFeatures.ImpliedProbability,

            // Food adjustment
            FoodAdjustment = placement.PirateFoodAdjustment,

            // Historical performance
            OverallWinRate = historicalFeatures.OverallWinRate,
            RecentWinRate = historicalFeatures.RecentWinRate,
            WinStreak = historicalFeatures.WinStreak,
            TotalAppearances = historicalFeatures.TotalAppearances,

            // Arena-specific features
            ArenaWinRate = arenaFeatures.ArenaWinRate,
            ArenaAppearances = arenaFeatures.ArenaAppearances,
            PositionWinRate = arenaFeatures.PositionWinRate,

            // Odds-based historical features
            AverageStartingOdds = historicalFeatures.AverageStartingOdds,
            AverageWinningOdds = historicalFeatures.AverageWinningOdds,
            WinRateAtCurrentOdds = historicalFeatures.WinRateAtCurrentOdds,

            // Food-related features
            AverageFoodAdjustment = historicalFeatures.AverageFoodAdjustment,
            WinRateWithPositiveFoodAdjustment = historicalFeatures.WinRateWithPositiveFoodAdjustment,
            WinRateWithNegativeFoodAdjustment = historicalFeatures.WinRateWithNegativeFoodAdjustment,

            // Competitive features
            HeadToHeadWinRate = competitiveFeatures.HeadToHeadWinRate,
            HeadToHeadAppearances = competitiveFeatures.HeadToHeadAppearances,
            AverageOpponentWinRate = competitiveFeatures.AverageOpponentWinRate,
            StrongestOpponentWinRate = competitiveFeatures.StrongestOpponentWinRate,
            FieldStrengthRank = competitiveFeatures.FieldStrengthRank,
            IsFavorite = competitiveFeatures.IsFavorite,
            IsUnderdog = competitiveFeatures.IsUnderdog,
            OddsAdvantageVsField = competitiveFeatures.OddsAdvantageVsField,
            NumberOfStrongerOpponents = competitiveFeatures.NumberOfStrongerOpponents,
            NumberOfWeakerOpponents = competitiveFeatures.NumberOfWeakerOpponents,

            // Target variable
            Won = actualOutcome
        };
    }
        
    }
}