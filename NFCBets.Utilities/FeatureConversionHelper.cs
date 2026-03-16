using NFCBets.Utilities.Models;

namespace NFCBets.Utilities;

/// <summary>
/// Centralized feature conversion helper for all ML models
/// </summary>
public static class FeatureConversionHelper
{
    /// <summary>
    /// Convert PirateFeatureRecords to MlPirateFeatures with full feature population
    /// </summary>
    public static List<MlPirateFeature> ConvertToMlFormat(
        List<PirateFeatureRecord> features,
        InteractionAnalysisReport? interactionReport = null)
    {
        if (!features.Any())
            return new List<MlPirateFeature>();

        // Group by round and arena to calculate relative features
        var groupedByRoundArena = features
            .GroupBy(f => (f.RoundId, f.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

        return features.Select(f => ConvertSingle(f, groupedByRoundArena, interactionReport)).ToList();
    }

    /// <summary>
    /// Convert a single PirateFeatureRecord to MlPirateFeature
    /// </summary>
    public static MlPirateFeature ConvertSingle(
        PirateFeatureRecord f,
        Dictionary<(int RoundId, int ArenaId), List<PirateFeatureRecord>> groupedByRoundArena,
        InteractionAnalysisReport? interactionReport = null)
    {
        var arenaContext = groupedByRoundArena.GetValueOrDefault((f.RoundId, f.ArenaId)) 
                           ?? new List<PirateFeatureRecord> { f };

        var mlFeature = new MlPirateFeature
        {
            // Identifiers
            RoundId = f.RoundId,
            PirateId = f.PirateId,

            // Core features
            Position = f.Position,
            ArenaId = f.ArenaId,
            CurrentOdds = Math.Max(2, f.CurrentOdds),
            OpeningOdds = f.OpeningOdds > 0 ? f.OpeningOdds : f.CurrentOdds,
            FoodAdjustment = f.FoodAdjustment,
            Strength = f.Strength,
            Weight = f.Weight,

            // Historical performance
            HistoricalWinRate = (float)f.HistoricalWinRate,
            TotalAppearances = f.TotalAppearances,
            ArenaWinRate = (float)f.ArenaWinRate,
            RecentWinRate = (float)f.RecentWinRate,
            WinRateVsCurrentRivals = (float)f.WinRateVsCurrentRivals,
            MatchesVsCurrentRivals = f.MatchesVsCurrentRivals,
            AvgRivalStrength = (float)f.AvgRivalStrength,

            // Label
            Won = f.IsWinner ?? false
        };

        // Apply derived features (calculates relative features from arena context)
        InteractionCalculator.ApplyDerivedFeatures(mlFeature, f, arenaContext);

        // Apply interaction features
        InteractionCalculator.ApplyInteractionFeatures(mlFeature, f, interactionReport);

        return mlFeature;
    }

    /// <summary>
    /// Convert for a single round (creates temporary grouping)
    /// </summary>
    public static List<MlPirateFeature> ConvertRound(
        List<PirateFeatureRecord> roundFeatures,
        InteractionAnalysisReport? interactionReport = null)
    {
        var grouped = new Dictionary<(int RoundId, int ArenaId), List<PirateFeatureRecord>>();
        
        foreach (var group in roundFeatures.GroupBy(f => (f.RoundId, f.ArenaId)))
        {
            grouped[group.Key] = group.ToList();
        }

        return roundFeatures.Select(f => ConvertSingle(f, grouped, interactionReport)).ToList();
    }
}