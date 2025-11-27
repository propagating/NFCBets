using Microsoft.EntityFrameworkCore;
using NFCBets.EF.Models;

namespace NFCBets.Services;

public interface IFeatureEngineeringService
{
    Task<List<PirateFeatureRecord>> CreateTrainingFeaturesAsync(DateTime? startDate = null, int? maxRounds = null);
    Task<List<PirateFeatureRecord>> CreatePredictionFeaturesAsync(int roundId);
    
    // Add these public methods for the batch processor to use
    Task<List<PirateFeatureRecord>> ProcessRoundFeaturesOptimizedAsync(int roundId, List<RoundResult> roundResults);
    Task<PirateFeatureRecord?> CreateFeatureForPirateOptimizedAsync(NfcbetsContext context, int pirateId, int roundId, int arenaId, bool? actualOutcome);


}

public class FeatureEngineeringService : IFeatureEngineeringService
{
    private readonly NfcbetsContext _context;
    // Add this method to FeatureEngineeringService to find the exact source of leakage
public async Task<DetailedLeakageReport> DiagnoseDetailedLeakageAsync(int testRoundId, int testPirateId)
{
    Console.WriteLine($"🔬 Detailed leakage analysis for Round {testRoundId}, Pirate {testPirateId}");
    
    var report = new DetailedLeakageReport
    {
        TestRoundId = testRoundId,
        TestPirateId = testPirateId
    };
    
    // Check historical features calculation
  
    var historicalResults = await _context.RoundResults
        .Where(rr => rr.PirateId == testPirateId && 
                    rr.IsComplete && 
                    rr.RoundId < testRoundId)
        .OrderBy(rr => rr.RoundId)
        .Select(rr => new { rr.RoundId, rr.IsWinner })
        .ToListAsync();
    
    report.HistoricalRounds = historicalResults.Select(r => r.RoundId!.Value).ToList();

    
    // Check arena-specific features

    var arenaResults = await _context.RoundResults
        .Where(rr => rr.PirateId == testPirateId && 
                    rr.ArenaId == 0 && // Test arena 0
                    rr.IsComplete && 
                    rr.RoundId < testRoundId)
        .OrderBy(rr => rr.RoundId)
        .Select(rr => rr.RoundId!.Value)
        .ToListAsync();
    
    report.ArenaHistoricalRounds = arenaResults;

    
    // Check competitive features - this might be the culprit

    var opponentData = await _context.RoundResults
        .Where(rr => rr.RoundId == testRoundId && rr.ArenaId == 0)
        .Select(rr => new { rr.PirateId, rr.IsWinner })
        .ToListAsync();
    
    report.CurrentRoundOpponents = opponentData.Select(o => o.PirateId).ToList();

    
    // Check if we're accidentally using current round results for opponent analysis
    foreach (var opponentId in report.CurrentRoundOpponents.Take(2))
    {
        var opponentHistory = await _context.RoundResults
            .Where(rr => rr.PirateId == opponentId && rr.IsComplete)
            .OrderBy(rr => rr.RoundId)
            .Select(rr => rr.RoundId!.Value)
            .ToListAsync();
            

        
        if (opponentHistory.Contains(testRoundId))
        {

            report.LeakageSource = $"Opponent analysis including current round {testRoundId}";
        }
    }
    
    return report;
}

public void ValidateFeatures(List<PirateFeatureRecord> features)
    {

        foreach (var feature in features.Take(5)) // Check first 5
        {

            // Red flags:
            if (feature.OverallWinRate > 0.9 || feature.RecentWinRate > 0.9)
            {
                Console.WriteLine("  🚨 SUSPICIOUS: Win rate too high!");
            }
        
            if (feature.TotalAppearances < 5)
            {
                Console.WriteLine("  ⚠️  Warning: Very few historical appearances");
            }
        }
    }
    public FeatureEngineeringService(NfcbetsContext context)
    {
        _context = context;
    }

    /// <summary>
    ///     Create training features from completed rounds
    /// </summary>
    public async Task<List<PirateFeatureRecord>> CreateTrainingFeaturesAsync(DateTime? startDate = null,
        int? maxRounds = null)
    {
        // Get completed rounds for training
        var query = _context.RoundResults
            .Include(rr => rr.Pirate)
            .Include(rr => rr.Arena)
            .Where(rr => rr.IsComplete && rr.RoundId.HasValue);

        if (startDate.HasValue)
            query = query.Where(rr => rr.RoundId >= GetRoundIdFromDate(startDate.Value));

        var completedResults = await query
            .OrderBy(rr => rr.RoundId)
            .Take(maxRounds ?? int.MaxValue)
            .ToListAsync();

        var features = new List<PirateFeatureRecord>();

        foreach (var result in completedResults)
        {
            if (!result.RoundId.HasValue) continue;

            var feature = await CreateFeatureForPirateAsync(
                result.PirateId,
                result.RoundId.Value,
                result.ArenaId,
                result.IsWinner // We know the outcome for training
            );

            if (feature != null)
                features.Add(feature);
        }

        return features;
    }

    /// <summary>
    ///     Create prediction features for current/upcoming round
    /// </summary>
    public async Task<List<PirateFeatureRecord>> CreatePredictionFeaturesAsync(int roundId)
    {
        var features = new List<PirateFeatureRecord>();

        // Get all pirate placements for this round
        var placements = await _context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId == roundId && rpp.PirateId.HasValue)
            .ToListAsync();

        foreach (var placement in placements)
        {
            var feature = await CreateFeatureForPirateAsync(
                placement.PirateId!.Value,
                roundId,
                placement.ArenaId!.Value,
                null // Unknown outcome for prediction
            );

            if (feature != null)
                features.Add(feature);
        }

        return features;
    }

    public Task<List<PirateFeatureRecord>> ProcessRoundFeaturesOptimizedAsync(int roundId, List<RoundResult> roundResults)
    {
        throw new NotImplementedException();
    }

    public Task<PirateFeatureRecord?> CreateFeatureForPirateOptimizedAsync(NfcbetsContext context, int pirateId, int roundId, int arenaId,
        bool? actualOutcome)
    {
        throw new NotImplementedException();
    }

    // Update CreateFeatureForPirateAsync with detailed logging
private async Task<PirateFeatureRecord?> CreateFeatureForPirateAsync(int pirateId, int roundId, int arenaId, bool? actualOutcome)
{
    
    // Get pirate placement data for this round
    var placement = await _context.RoundPiratePlacements
        .FirstOrDefaultAsync(rpp => rpp.RoundId == roundId && 
                                   rpp.ArenaId == arenaId && 
                                   rpp.PirateId == pirateId);

    if (placement == null) 
    {
        Console.WriteLine($"   ❌ No placement found");
        return null;
    }


    // Calculate historical features (data before this round) - ADD LOGGING
    var historicalFeatures = await CalculateHistoricalFeaturesAsync(pirateId, roundId);

    // Calculate arena-specific features - ADD LOGGING  
    var arenaFeatures = await CalculateArenaSpecificFeaturesAsync(pirateId, arenaId, roundId);

    // Calculate competitive features - ADD LOGGING
    var competitiveFeatures = await CalculateCompetitiveFeaturesAsync(pirateId, roundId, arenaId);

    var oddsFeatures = CalculateOddsFeatures(placement);

    var feature = new PirateFeatureRecord
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

        // Target variable (null for prediction)
        Won = actualOutcome
    };

    // 🚨 CRITICAL CHECK: Look for suspiciously perfect correlations
    if (actualOutcome.HasValue)
    {
        Console.WriteLine($"   🎯 Target: Won = {actualOutcome.Value}");
        
        // Check for perfect correlations (signs of leakage)
        if (historicalFeatures.OverallWinRate > 0.9 && actualOutcome.Value)
        {
            Console.WriteLine($"   🚨 SUSPICIOUS: Perfect win rate {historicalFeatures.OverallWinRate:F3} correlates with win!");
        }
        
        if (competitiveFeatures.HeadToHeadWinRate > 0.9 && actualOutcome.Value)
        {
            Console.WriteLine($"   🚨 SUSPICIOUS: Perfect H2H rate {competitiveFeatures.HeadToHeadWinRate:F3} correlates with win!");
        }
    }

    return feature;
}

// CORRECTED: CalculateHistoricalFeaturesAsync method
private async Task<HistoricalFeatures> CalculateHistoricalFeaturesAsync(int pirateId, int currentRoundId)
{
    // ✅ FIXED: Ensure we NEVER use data from currentRoundId or later
    var historicalResults = await _context.RoundResults
        .Where(rr => rr.PirateId == pirateId && 
                    rr.IsComplete && 
                    rr.RoundId.HasValue &&
                    rr.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Strict less-than
        .OrderByDescending(rr => rr.RoundId)
        .ToListAsync();

    var historicalPlacements = await _context.RoundPiratePlacements
        .Where(rpp => rpp.PirateId == pirateId && 
                     rpp.RoundId.HasValue &&
                     rpp.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Strict less-than
        .OrderByDescending(rpp => rpp.RoundId)
        .ToListAsync();

    Console.WriteLine($"   Pirate {pirateId}: Using {historicalResults.Count} historical results (max round: {(historicalResults.Any() ? historicalResults.Max(r => r.RoundId!.Value) : 0)})");

    if (!historicalResults.Any())
    {
        return new HistoricalFeatures(); // Return defaults for new pirate
    }

    var recentResults = historicalResults.Take(20).ToList();
    var wins = historicalResults.Where(r => r.IsWinner).ToList();
    var recentWins = recentResults.Where(r => r.IsWinner).ToList();

    // Get placements with positive food adjustment - FIXED
    var positiveFoodRoundIds = historicalPlacements
        .Where(p => p.PirateFoodAdjustment > 0 && p.RoundId.HasValue)
        .Select(p => p.RoundId!.Value)
        .ToHashSet();

    // Get placements with negative food adjustment - FIXED
    var negativeFoodRoundIds = historicalPlacements
        .Where(p => p.PirateFoodAdjustment < 0 && p.RoundId.HasValue)
        .Select(p => p.RoundId!.Value)
        .ToHashSet();

    // Calculate win rates for food adjustments - FIXED
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

// CORRECTED: CalculateArenaSpecificFeaturesAsync method
private async Task<ArenaFeatures> CalculateArenaSpecificFeaturesAsync(int pirateId, int arenaId, int currentRoundId)
{
    // ✅ FIXED: Strict historical data only
    var arenaResults = await _context.RoundResults
        .Where(rr => rr.PirateId == pirateId && 
                    rr.ArenaId == arenaId && 
                    rr.IsComplete && 
                    rr.RoundId.HasValue &&
                    rr.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Strict less-than
        .ToListAsync();

    var arenaPlacements = await _context.RoundPiratePlacements
        .Where(rpp => rpp.PirateId == pirateId && 
                     rpp.ArenaId == arenaId && 
                     rpp.RoundId.HasValue &&
                     rpp.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Strict less-than
        .ToListAsync();


    // Get current position from CURRENT round (this is OK - it's not outcome data)
    var currentPosition = await _context.RoundPiratePlacements
        .Where(rpp => rpp.RoundId == currentRoundId && 
                     rpp.ArenaId == arenaId && 
                     rpp.PirateId == pirateId)
        .Select(rpp => rpp.PirateSeatPosition ?? 0)
        .FirstOrDefaultAsync();

    // Get historical rounds where pirate was in same position
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

    private OddsFeatures CalculateOddsFeatures(RoundPiratePlacement placement)
    {
        var startingOdds = placement.StartingOdds;
        var currentOdds = placement.CurrentOdds ?? startingOdds;

        return new OddsFeatures
        {
            OddsMovement = startingOdds != 0 ? (double)(currentOdds - startingOdds) / startingOdds : 0,
            OddsRank = placement.PirateSeatPosition ?? 0 + 1, // This should be calculated properly based on arena odds
            ImpliedProbability = currentOdds != 0 ? 1.0 / currentOdds : 0
        };
    }

    // Helper methods
    private int CalculateCurrentWinStreak(List<RoundResult> results)
    {
        var streak = 0;
        foreach (var result in results.OrderByDescending(r => r.RoundId))
            if (result.IsWinner)
                streak++;
            else
                break;
        return streak;
    }

    private double CalculateWinRateAtOddsRange(List<RoundResult> results, List<RoundPiratePlacement> placements)
    {
        // Simplified - in practice you'd want to match results with placements and group by odds ranges
        return results.Any() ? (double)results.Count(r => r.IsWinner) / results.Count : 0;
    }

    private double CalculateWinRateByFoodAdjustment(List<RoundResult> results, List<RoundPiratePlacement> placements,
        bool positive)
    {
        // Filter placements by food adjustment
        var relevantPlacements = positive
            ? placements.Where(p => p.PirateFoodAdjustment > 0)
            : placements.Where(p => p.PirateFoodAdjustment < 0);

        if (!relevantPlacements.Any()) return 0;

        // Get the round IDs where the pirate had the relevant food adjustment
        var relevantRoundIds = relevantPlacements
            .Where(p => p.RoundId.HasValue)
            .Select(p => p.RoundId!.Value)
            .ToHashSet(); // Using HashSet for faster lookup

        // Filter results to only those rounds
        var matchedResults = results
            .Where(r => r.RoundId.HasValue && relevantRoundIds.Contains(r.RoundId.Value))
            .ToList();

        return matchedResults.Any() ? (double)matchedResults.Count(r => r.IsWinner) / matchedResults.Count : 0;
    }

    private int GetRoundIdFromDate(DateTime date)
    {
        // This would need to be implemented based on your round numbering system
        // For now, return a reasonable default
        return 8000;
    }

   // CORRECTED: CalculateCompetitiveFeaturesAsync - remove current round from opponent analysis
private async Task<CompetitiveFeatures> CalculateCompetitiveFeaturesAsync(int pirateId, int roundId, int arenaId)
{
    Console.WriteLine($"      ⚔️ Competitive analysis for round {roundId}, arena {arenaId}");
    
    // Get all pirates in this arena for this round
    var arenaPirates = await _context.RoundPiratePlacements
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

    Console.WriteLine($"      ⚔️ Opponents: [{string.Join(", ", opponentIds)}]");

    if (currentPirate == null || !opponentIds.Any())
    {
        return new CompetitiveFeatures();
    }

    // 🔥 CRITICAL FIX: Pass roundId to ensure no future data is used
    var headToHeadStats = await CalculateHeadToHeadStatsAsync(pirateId, opponentIds, roundId);
    var fieldStrengthStats = await CalculateArenaFieldStrengthAsync(pirateId, arenaPirates.Select(p => p.PirateId).ToList(), roundId);
    var competitivePosition = CalculateCompetitivePosition(currentPirate, arenaPirates);

    Console.WriteLine($"      ⚔️ H2H: {headToHeadStats.TotalMatchups} matchups, {headToHeadStats.OverallWinRate:F3} win rate");

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


    /// <summary>
    ///     Calculate head-to-head statistics against specific opponents
    /// </summary>
    private async Task<HeadToHeadStats> CalculateHeadToHeadStatsAsync(int pirateId, List<int> opponentIds,
        int currentRoundId)
    {
        // Find historical rounds where this pirate faced these specific opponents
        var historicalMatchups = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId &&
                         rr.IsComplete &&
                         rr.RoundId < currentRoundId)
            .ToListAsync();

        var matchupRounds = new List<int>();

        // For each historical result, check if any of the current opponents were also in that arena
        foreach (var result in historicalMatchups)
        {
            var opponentsInThatRound = await _context.RoundResults
                .Where(rr => rr.RoundId == result.RoundId &&
                             rr.ArenaId == result.ArenaId &&
                             rr.PirateId != pirateId &&
                             opponentIds.Contains(rr.PirateId))
                .CountAsync();

            if (opponentsInThatRound > 0) matchupRounds.Add(result.RoundId!.Value);
        }

        var relevantResults = historicalMatchups
            .Where(r => r.RoundId.HasValue && matchupRounds.Contains(r.RoundId.Value))
            .ToList();

        // Get opponent win rates to identify strongest/weakest
        var opponentWinRates = new Dictionary<int, double>();
        foreach (var opponentId in opponentIds)
        {
            var opponentWins = await _context.RoundResults
                .Where(rr => rr.PirateId == opponentId && rr.IsComplete && rr.RoundId < currentRoundId)
                .CountAsync(rr => rr.IsWinner);

            var opponentTotal = await _context.RoundResults
                .Where(rr => rr.PirateId == opponentId && rr.IsComplete && rr.RoundId < currentRoundId)
                .CountAsync();

            opponentWinRates[opponentId] = opponentTotal > 0 ? (double)opponentWins / opponentTotal : 0;
        }

        var strongestOpponent = opponentWinRates.OrderByDescending(kvp => kvp.Value).FirstOrDefault();
        var weakestOpponent = opponentWinRates.OrderBy(kvp => kvp.Value).FirstOrDefault();

        // Calculate wins against strongest/weakest opponents
        var winsVsStrongest =
            await CalculateWinsAgainstSpecificOpponentAsync(pirateId, strongestOpponent.Key, currentRoundId);
        var winsVsWeakest =
            await CalculateWinsAgainstSpecificOpponentAsync(pirateId, weakestOpponent.Key, currentRoundId);

        return new HeadToHeadStats
        {
            OverallWinRate = relevantResults.Any()
                ? (double)relevantResults.Count(r => r.IsWinner) / relevantResults.Count
                : 0,
            TotalMatchups = relevantResults.Count,
            WinsVsStrongest = winsVsStrongest,
            WinsVsWeakest = winsVsWeakest
        };
    }

    /// <summary>
    ///     Calculate field strength statistics for the current arena composition
    /// </summary>
// CORRECTED: CalculateArenaFieldStrengthAsync - ensure no current round data
private async Task<FieldStrengthStats> CalculateArenaFieldStrengthAsync(int pirateId, List<int> allPirateIds, int currentRoundId)
{
    
    var opponentIds = allPirateIds.Where(id => id != pirateId).ToList();
    var opponentWinRates = new List<double>();

    // Calculate win rate for each opponent - EXCLUDING current round
    foreach (var opponentId in opponentIds)
    {
        var opponentWins = await _context.RoundResults
            .Where(rr => rr.PirateId == opponentId && 
                        rr.IsComplete && 
                        rr.RoundId.HasValue &&
                        rr.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Exclude current round
            .CountAsync(rr => rr.IsWinner);
        
        var opponentTotal = await _context.RoundResults
            .Where(rr => rr.PirateId == opponentId && 
                        rr.IsComplete && 
                        rr.RoundId.HasValue &&
                        rr.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Exclude current round
            .CountAsync();

        var winRate = opponentTotal > 0 ? (double)opponentWins / opponentTotal : 0;
        opponentWinRates.Add(winRate);
        
    }

    // Calculate current pirate's win rate for ranking - EXCLUDING current round
    var pirateWins = await _context.RoundResults
        .Where(rr => rr.PirateId == pirateId && 
                    rr.IsComplete && 
                    rr.RoundId.HasValue &&
                    rr.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Exclude current round
        .CountAsync(rr => rr.IsWinner);
    
    var pirateTotal = await _context.RoundResults
        .Where(rr => rr.PirateId == pirateId && 
                    rr.IsComplete && 
                    rr.RoundId.HasValue &&
                    rr.RoundId.Value < currentRoundId) // 🔥 CRITICAL: Exclude current round
        .CountAsync();

    var pirateWinRate = pirateTotal > 0 ? (double)pirateWins / pirateTotal : 0;


    // Calculate rank in field
    var allWinRates = opponentWinRates.Concat(new[] { pirateWinRate }).OrderByDescending(wr => wr).ToList();
    var pirateRank = allWinRates.IndexOf(pirateWinRate) + 1;

    return new FieldStrengthStats
    {
        AverageOpponentWinRate = opponentWinRates.Any() ? opponentWinRates.Average() : 0,
        StrongestOpponentWinRate = opponentWinRates.Any() ? opponentWinRates.Max() : 0,
        WeakestOpponentWinRate = opponentWinRates.Any() ? opponentWinRates.Min() : 0,
        PirateRankInField = pirateRank
    };
}

    /// <summary>
    ///     Calculate competitive position based on odds
    /// </summary>
    private CompetitivePositionStats CalculateCompetitivePosition(ArenaPirateData currentPirate,
        List<ArenaPirateData> allPirates)
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
    ///     Helper method to calculate wins against a specific opponent
    /// </summary>
    private async Task<int> CalculateWinsAgainstSpecificOpponentAsync(int pirateId, int opponentId, int currentRoundId)
    {
        // Find rounds where both pirates were in the same arena
        var pirateRounds = await _context.RoundResults
            .Where(rr => rr.PirateId == pirateId && rr.IsComplete && rr.RoundId < currentRoundId)
            .Select(rr => new { rr.RoundId, rr.ArenaId, rr.IsWinner })
            .ToListAsync();

        var opponentRounds =  await _context.RoundResults
            .Where(rr => rr.PirateId == opponentId && rr.IsComplete && rr.RoundId < currentRoundId)
            .Select(rr => new { rr.RoundId, rr.ArenaId })
            .ToHashSetAsync();

        var directMatchups = pirateRounds
            .Where(pr => opponentRounds.Contains(new { pr.RoundId, pr.ArenaId }))
            .ToList();

        return directMatchups.Count(m => m.IsWinner);
    }
    
    public async Task<FeatureLeakageReport> DiagnoseFeatureLeakageAsync(int testRoundId)
    {
    
        // Check what historical data we're using
        var historicalResults = await _context.RoundResults
            .Where(rr => rr.PirateId == 1 && // Test with pirate 1
                         rr.IsComplete && 
                         rr.RoundId < testRoundId) // Should NOT include testRoundId
            .OrderBy(rr => rr.RoundId)
            .ToListAsync();
        
        var suspiciousResults = await _context.RoundResults
            .Where(rr => rr.PirateId == 1 && 
                         rr.IsComplete && 
                         rr.RoundId >= testRoundId) // This would be leakage!
            .ToListAsync();
    
        return new FeatureLeakageReport
        {
            TestRoundId = testRoundId,
            HistoricalRoundsUsed = historicalResults.Select(r => r.RoundId!.Value).ToList(),
            LeakedRounds = suspiciousResults.Select(r => r.RoundId!.Value).ToList(),
            MaxHistoricalRound = historicalResults.Any() ? historicalResults.Max(r => r.RoundId!.Value) : 0
        };
    }

}

public class DetailedLeakageReport
{
    public int TestRoundId { get; set; }
    public int TestPirateId { get; set; }
    public List<int> HistoricalRounds { get; set; } = new();
    public List<int> ArenaHistoricalRounds { get; set; } = new();
    public List<int> CurrentRoundOpponents { get; set; } = new();
    public string LeakageSource { get; set; } = "";
}

// Data models for features
public class FeatureLeakageReport
{
    public int TestRoundId { get; set; }
    public List<int> HistoricalRoundsUsed { get; set; } = new();
    public List<int> LeakedRounds { get; set; } = new();
    public int MaxHistoricalRound { get; set; }
}
public class ArenaPirateData
{
    public int PirateId { get; set; }
    public int Position { get; set; }
    public int Odds { get; set; }
}

public class PirateFeatureRecord
{
    // Identifiers
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public int Position { get; set; }

    // Odds features
    public int StartingOdds { get; set; }
    public int CurrentOdds { get; set; }
    public double OddsMovement { get; set; }
    public int OddsRank { get; set; }
    public double ImpliedProbability { get; set; }

    // Food adjustment
    public int FoodAdjustment { get; set; }

    // Historical performance
    public double OverallWinRate { get; set; }
    public double RecentWinRate { get; set; }
    public int WinStreak { get; set; }
    public int TotalAppearances { get; set; }

    // Arena-specific
    public double ArenaWinRate { get; set; }
    public int ArenaAppearances { get; set; }
    public double PositionWinRate { get; set; }

    // Odds-based historical
    public double AverageStartingOdds { get; set; }
    public double AverageWinningOdds { get; set; }
    public double WinRateAtCurrentOdds { get; set; }

    // Food-related
    public double AverageFoodAdjustment { get; set; }
    public double WinRateWithPositiveFoodAdjustment { get; set; }
    public double WinRateWithNegativeFoodAdjustment { get; set; }

    // Target variable (null for predictions)
    public bool? Won { get; set; }

    public double HeadToHeadWinRate { get; set; }
    public int HeadToHeadAppearances { get; set; }
    public double AverageOpponentWinRate { get; set; }
    public double StrongestOpponentWinRate { get; set; }
    public int FieldStrengthRank { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsUnderdog { get; set; }
    public double OddsAdvantageVsField { get; set; }
    public int NumberOfStrongerOpponents { get; set; }
    public int NumberOfWeakerOpponents { get; set; }
}

public class HistoricalFeatures
{
    public double OverallWinRate { get; set; }
    public double RecentWinRate { get; set; }
    public int WinStreak { get; set; }
    public int TotalAppearances { get; set; }
    public double AverageStartingOdds { get; set; }
    public double AverageWinningOdds { get; set; }
    public double WinRateAtCurrentOdds { get; set; }
    public double AverageFoodAdjustment { get; set; }
    public double WinRateWithPositiveFoodAdjustment { get; set; }
    public double WinRateWithNegativeFoodAdjustment { get; set; }
}

public class ArenaFeatures
{
    public double ArenaWinRate { get; set; }
    public int ArenaAppearances { get; set; }
    public double PositionWinRate { get; set; }
}

public class OddsFeatures
{
    public double OddsMovement { get; set; }
    public int OddsRank { get; set; }
    public double ImpliedProbability { get; set; }
}

// Data models for the new features
public class CompetitiveFeatures
{
    // Head-to-head features
    public double HeadToHeadWinRate { get; set; }
    public int HeadToHeadAppearances { get; set; }
    public int WinsAgainstStrongestOpponent { get; set; }
    public int WinsAgainstWeakestOpponent { get; set; }

    // Field strength features
    public double AverageOpponentWinRate { get; set; }
    public double StrongestOpponentWinRate { get; set; }
    public double WeakestOpponentWinRate { get; set; }
    public int FieldStrengthRank { get; set; }

    // Competitive position
    public bool IsFavorite { get; set; }
    public bool IsUnderdog { get; set; }
    public double OddsAdvantageVsField { get; set; }
    public int NumberOfStrongerOpponents { get; set; }
    public int NumberOfWeakerOpponents { get; set; }
}

public class HeadToHeadStats
{
    public double OverallWinRate { get; set; }
    public int TotalMatchups { get; set; }
    public int WinsVsStrongest { get; set; }
    public int WinsVsWeakest { get; set; }
}

public class FieldStrengthStats
{
    public double AverageOpponentWinRate { get; set; }
    public double StrongestOpponentWinRate { get; set; }
    public double WeakestOpponentWinRate { get; set; }
    public int PirateRankInField { get; set; }
}

public class CompetitivePositionStats
{
    public bool IsFavorite { get; set; }
    public bool IsUnderdog { get; set; }
    public double OddsAdvantageVsField { get; set; }
    public int StrongerOpponents { get; set; }
    public int WeakerOpponents { get; set; }
}