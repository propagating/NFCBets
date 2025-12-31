using NFCBets.Classical.Models;

namespace NFCBets.Classical;

// <summary>
/// Naive betting strategy that uses only opening odds to estimate probabilities
/// This serves as a baseline to compare against ML-based strategies
/// 
/// Algorithm:
/// 1. Computes min/max probability bounds from odds
/// 2. Uses constraint that probabilities sum to 1 per arena
/// 3. Iteratively refines to satisfy all constraints
/// 4. Special handling for clamped odds (13:1 and 2:1)
/// </summary>
public class NaiveOddsBasedStrategy
{
    public List<PirateProbability> ComputePirateProbabilities(List<PirateOdds> pirateOdds)
    {
        // Group by arena
        var arenas = pirateOdds.GroupBy(p => p.ArenaId).OrderBy(g => g.Key).ToList();
        var results = new List<PirateProbability>();

        foreach (var arena in arenas)
        {
            var piratesInArena = arena.OrderBy(p => p.Position).ToList();
            var probabilities = ComputeArenaProbabilities(piratesInArena);
            results.AddRange(probabilities);
        }

        return results;
    }

    private List<PirateProbability> ComputeArenaProbabilities(List<PirateOdds> pirates)
    {
        var arenaId = pirates.First().ArenaId;

        // Initialize min, max, and standard probabilities
        var minProbs = new double[4];
        var maxProbs = new double[4];
        var stdProbs = new double[4];

        // Step 1: Compute initial min/max bounds from odds
        for (var i = 0; i < pirates.Count; i++)
        {
            var odds = pirates[i].Odds;

            if (odds == 13)
            {
                // Clamped maximum - true odds unknown
                minProbs[i] = 0;
                maxProbs[i] = 1.0 / 13.0;
            }
            else if (odds == 2)
            {
                // Clamped minimum - could be favorite
                minProbs[i] = 1.0 / 3.0;
                maxProbs[i] = 1.0;
            }
            else
            {
                // Normal odds: probability between 1/(odds+1) and 1/odds
                minProbs[i] = 1.0 / (odds + 1);
                maxProbs[i] = 1.0 / odds;
            }
        }

        // Step 2: Tighten bounds using sum-to-1 constraint
        var totalMin = minProbs.Sum();
        var totalMax = maxProbs.Sum();

        for (var i = 0; i < 4; i++)
        {
            // New min: max(current_min, 1 - sum_of_other_maxs)
            var newMin = Math.Max(minProbs[i], 1 + maxProbs[i] - totalMax);

            // New max: min(current_max, 1 - sum_of_other_mins)
            var newMax = Math.Min(maxProbs[i], 1 + minProbs[i] - totalMin);

            minProbs[i] = newMin;
            maxProbs[i] = newMax;

            // Initial standard probability
            if (pirates[i].Odds == 13)
                stdProbs[i] = 1.0 / 20.0; // Conservative for clamped longshots
            else
                stdProbs[i] = (minProbs[i] + maxProbs[i]) / 2.0;
        }

        // Step 3: Iteratively adjust to sum to exactly 1
        for (var targetOdds = 2; targetOdds <= 13; targetOdds++)
        {
            var totalStd = stdProbs.Sum();
            if (Math.Abs(totalStd - 1.0) < 0.0001)
                break; // Already sums to 1

            var countSmaller = pirates.Count(p => p.Odds <= targetOdds);
            if (countSmaller == 0)
                continue;

            // Calculate adjustment needed
            double stdToMin = 0;
            for (var i = 0; i < 4; i++)
                if (pirates[i].Odds <= targetOdds)
                    stdToMin += stdProbs[i] - minProbs[i];

            var smallestRange = double.MaxValue;
            for (var i = 0; i < 4; i++)
                if (pirates[i].Odds <= targetOdds)
                    smallestRange = Math.Min(smallestRange, maxProbs[i] - minProbs[i]);

            // Check if we can adjust to sum to 1
            if (totalStd - stdToMin <= 1.0 &&
                stdToMin + 1.0 - totalStd <= smallestRange * countSmaller)
            {
                var remainingGap = (stdToMin + 1.0 - totalStd) / countSmaller;

                for (var i = 0; i < 4; i++)
                    if (pirates[i].Odds <= targetOdds)
                        stdProbs[i] = minProbs[i] + remainingGap;

                break;
            }
        }

        // Return probabilities
        var probabilities = new List<PirateProbability>();
        for (var i = 0; i < pirates.Count; i++)
            probabilities.Add(new PirateProbability
            {
                RoundId = pirates[i].RoundId,
                ArenaId = pirates[i].ArenaId,
                PirateId = pirates[i].PirateId,
                Position = pirates[i].Position,
                Odds = pirates[i].Odds,
                Probability = stdProbs[i]
            });

        return probabilities;
    }
}