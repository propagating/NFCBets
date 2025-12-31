using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NFCBets.Causal.Interfaces;
using NFCBets.Causal.Models;
using NFCBets.EF.Models;
using NFCBets.Utilities;

namespace NFCBets.Causal;

public class CausalInferenceService : ICausalInferenceService
{
    private readonly NfcbetsContext _context;

    public CausalInferenceService(NfcbetsContext context)
    {
        _context = context;
    }

    public async Task<ComprehensiveCausalReport> AnalyzeAllTreatmentEffectsAsync()
    {
        Console.WriteLine("🧬 Comprehensive Causal Analysis");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var report = new ComprehensiveCausalReport();

        // ✅ Load all data ONCE at entry point
        var allData = await LoadCausalDataAsync();
        Console.WriteLine($"📊 Loaded {allData.Count} observations for causal analysis\n");

        // 1. Food Adjustment Effect
        Console.WriteLine("1️⃣ Analyzing Food Adjustment Effect...");
        report.FoodAdjustmentEffect = await EstimateFoodAdjustmentEffectAsync(allData);
        DisplayEffect(report.FoodAdjustmentEffect);

        // ========== SEAT POSITION - SIMPLIFIED TO 2 TESTS ==========
        Console.WriteLine("\n2️⃣ SEAT POSITION ANALYSIS (2 Tests)");
        Console.WriteLine("═══════════════════════════════════════════════════");

        // Test 1: Does seat position matter at all? (Joint test)
        Console.WriteLine("\n   Test 1: Overall Seat Position Effect (Joint Test)...");
        report.OverallSeatPositionJointTest = await TestOverallSeatPositionEffectAsync(allData);
        Console.WriteLine($"      p-value: {report.OverallSeatPositionJointTest.PValue:F4} " +
                          $"{(report.OverallSeatPositionJointTest.IsSignificant ? "✅ Significant" : "⚠️ Not Significant")}");

        if (report.OverallSeatPositionJointTest.IsSignificant)
        {
            Console.WriteLine("      → Seat position DOES matter - analyzing individual positions...\n");

            // Test 2: Which positions are best/worst?
            Console.WriteLine("   Test 2: Individual Seat Position Effects...");
            report.EachSeatVsOthersEffects = await EstimateEachSeatVsOthersEffectAsync(allData);

            foreach (var (position, effect) in report.EachSeatVsOthersEffects.OrderBy(kv => kv.Key))
            {
                var significance = effect.IsSignificant ? "✅" : "⚠️";
                Console.WriteLine(
                    $"      Position {position}: {effect.AverageTreatmentEffect:+0.0%;-0.0%} {significance}");
            }

            // Identify best position
            var bestPosition = report.EachSeatVsOthersEffects
                .OrderByDescending(kv => kv.Value.AverageTreatmentEffect)
                .First();
            Console.WriteLine(
                $"\n      🏆 Best position: {bestPosition.Key} ({bestPosition.Value.AverageTreatmentEffect:+0.0%;-0.0%})");
        }
        else
        {
            Console.WriteLine("      → Seat position does NOT matter significantly");
            Console.WriteLine("      → Skipping individual position analysis");
        }

        // ========== ARENA - 2 TESTS ==========
        Console.WriteLine("\n3️⃣ ARENA PLACEMENT ANALYSIS (2 Tests)");
        Console.WriteLine("═══════════════════════════════════════════════════");

// Test 1: Joint test for arena
        Console.WriteLine("\n   Test 1: Overall Arena Effect (Joint Test)...");
        report.OverallArenaJointTest = await TestOverallArenaEffectAsync(allData);
        Console.WriteLine($"      p-value: {report.OverallArenaJointTest.PValue:F4} " +
                          $"{(report.OverallArenaJointTest.IsSignificant ? "✅ Significant" : "⚠️ Not Significant")}");

        if (report.OverallArenaJointTest.IsSignificant)
        {
            Console.WriteLine("      → Arena placement DOES matter - analyzing individual arenas...\n");

            // Test 2: Individual arena effects
            Console.WriteLine("   Test 2: Individual Arena Effects...");
            report.IndividualArenaEffects = await EstimateIndividualArenaEffectsAsync(allData);

            // ✅ Display individual effects with full details
            foreach (var (arenaId, effect) in report.IndividualArenaEffects.OrderBy(kv => kv.Key))
            {
                Console.WriteLine($"\n      Arena {arenaId}:");
                DisplayEffect(effect);
            }

            // Identify best arena
            var significantArenas = report.IndividualArenaEffects
                .Where(kv => kv.Value.IsSignificant)
                .OrderByDescending(kv => kv.Value.AverageTreatmentEffect)
                .ToList();

            if (significantArenas.Any())
            {
                var bestArena = significantArenas.First();
                Console.WriteLine(
                    $"\n      🏆 Best arena: {bestArena.Key} ({bestArena.Value.AverageTreatmentEffect:+0.0%;-0.0%})");
            }
            else
            {
                Console.WriteLine(
                    "\n      ⚠️ No individual arenas show significant effects (limited same-pirate data)");
            }
        }
        else
        {
            Console.WriteLine("      → Arena placement does NOT matter significantly");
            Console.WriteLine("      → Skipping individual arena analysis");
        }

        // 4. Rival Strength Effect
        Console.WriteLine("\n4️⃣ Analyzing Rival Strength Effect...");
        report.RivalStrengthEffect = await EstimateRivalStrengthEffectAsync(allData);
        DisplayEffect(report.RivalStrengthEffect);

        // 5. Odds Effect + Diagnostic
        Console.WriteLine("\n5️⃣ Analyzing Odds/Favorite Status Effect...");
        report.OddsEffect = await EstimateOddsEffectAsync(allData);
        DisplayEffect(report.OddsEffect);

        Console.WriteLine("\n5️⃣b Running Comprehensive Odds Diagnostic...");
        report.OddsDiagnostic = await DiagnoseOddsPatternAsync(allData);

        // 6. Interaction Effects
        Console.WriteLine("\n6️⃣ Analyzing Interaction Effects...");
        report.InteractionEffects = await AnalyzeInteractionEffectsAsync(allData);
        DisplayInteractionEffects(report.InteractionEffects);

        // 7. Generate key findings
        Console.WriteLine("\n7️⃣ Generating Key Findings...");
        GenerateKeyFindings(report);

        SaveCausalReport(report);
        return report;
    }

    private record struct CausalMatchCandidate(CausalDataPoint Data, double[] Covariates);

    #region Core Treatment Effect Estimation

    public async Task<CausalEffectReport> EstimateFoodAdjustmentEffectAsync(List<CausalDataPoint>? data = null)
    {
        // ✅ Use passed data if available, only load if null
        data ??= await LoadCausalDataAsync();

        // Treatment: Positive food adjustment (≥1) vs neutral/negative (≤0)
        var treated = data.Where(d => d.FoodAdjustment >= 1).ToList();
        var control = data.Where(d => d.FoodAdjustment <= 0).ToList();

        // Match on confounders: odds, position, strength
        var matches = MatchOnCovariates(treated, control,
            d => new[]
            {
                1.0 / Math.Max(2, d.CurrentOdds),
                d.Position / 4.0,
                d.Strength / 100.0
            });

        var ate = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
        var variance = MathUtilities.CalculateVariance(matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
        var standardError = Math.Sqrt(variance / matches.Count);
        var tStat = ate / standardError;

        return new CausalEffectReport
        {
            TreatmentName = "Positive Food Adjustment (≥1 vs ≤0)",
            AverageTreatmentEffect = ate,
            StandardError = standardError,
            TStatistic = tStat,
            PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
            TreatmentGroupSize = treated.Count,
            ControlGroupSize = control.Count,
            MatchedPairs = matches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
        };
    }

    public async Task<CausalEffectReport> EstimateRivalStrengthEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        // ✅ FIX: Calculate rival strengths in memory, no N+1 queries
        var dataWithRivalStrength = new List<(CausalDataPoint Point, double AvgRivalStrength)>();

        // Group by round and arena to calculate rival strengths efficiently
        var arenaGroups = data.GroupBy(d => (d.RoundId, d.ArenaId));

        foreach (var group in arenaGroups)
        {
            var piratesInArena = group.ToList();

            foreach (var point in piratesInArena)
            {
                // Calculate average rival strength from same arena (excluding self)
                var rivals = piratesInArena.Where(p => p.PirateId != point.PirateId).ToList();
                var avgRivalStrength = rivals.Any() ? rivals.Average(r => r.Strength) : 0;

                dataWithRivalStrength.Add((point, avgRivalStrength));
            }
        }

        // Treatment: Facing strong rivals (above median) vs weak rivals (below median)
        var medianRivalStrength = dataWithRivalStrength
            .Select(d => d.AvgRivalStrength)
            .OrderBy(s => s)
            .ElementAt(dataWithRivalStrength.Count / 2);

        var strongRivals = dataWithRivalStrength.Where(d => d.AvgRivalStrength >= medianRivalStrength).ToList();
        var weakRivals = dataWithRivalStrength.Where(d => d.AvgRivalStrength < medianRivalStrength).ToList();

        // Match on pirate characteristics
        var matches = MatchOnCovariates(
            strongRivals.Select(d => d.Point).ToList(),
            weakRivals.Select(d => d.Point).ToList(),
            d => new[]
            {
                d.Strength / 100.0,
                d.FoodAdjustment / 3.0,
                1.0 / Math.Max(2, d.CurrentOdds)
            });

        var ate = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
        var standardError =
            MathUtilities.CalculateStandardError(matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
        var tStat = ate / standardError;

        return new CausalEffectReport
        {
            TreatmentName = "Facing Strong Rivals (Above Median Strength)",
            AverageTreatmentEffect = ate,
            StandardError = standardError,
            TStatistic = tStat,
            PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
            TreatmentGroupSize = strongRivals.Count,
            ControlGroupSize = weakRivals.Count,
            MatchedPairs = matches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
        };
    }

    public async Task<CausalEffectReport> EstimateOddsEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        // Treatment: Being the favorite (odds ≤2) vs non-favorite (odds > 2, but < 13)
        var cleanData = data.Where(d => d.CurrentOdds > 1 && d.CurrentOdds < 13).ToList();

        var favorites = cleanData.Where(d => d.CurrentOdds == 2).ToList();
        var nonFavorites = cleanData.Where(d => d.CurrentOdds > 2).ToList();

        // Match on pirate quality indicators (not odds-related)
        var matches = MatchOnCovariates(favorites, nonFavorites,
            d => new[]
            {
                d.Strength / 100.0,
                d.Weight / 100.0,
                d.FoodAdjustment / 3.0,
                d.Position / 3.0
            });

        var ate = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
        var standardError =
            MathUtilities.CalculateStandardError(matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
        var tStat = ate / standardError;

        // Calculate dose-response
        var doseResponse = CalculateOddsDoseResponse(cleanData);

        return new CausalEffectReport
        {
            TreatmentName = "Being Favorite (Odds 2:1 vs >2:1, excluding clamped)",
            AverageTreatmentEffect = ate,
            StandardError = standardError,
            TStatistic = tStat,
            PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
            TreatmentGroupSize = favorites.Count,
            ControlGroupSize = nonFavorites.Count,
            MatchedPairs = matches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError),
            DoseResponse = doseResponse
        };
    }

    #endregion

    #region Seat Position Analysis (2 Tests)

    public async Task<CausalEffectReport> TestOverallSeatPositionEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Performing permutation test for overall seat position effect...");

        var outcomes = data.Select(d => d.IsWinner ? 1.0 : 0.0).ToArray();
        var positions = data.Select(d => d.Position).ToArray();

        var actualRates = new double[4];
        var counts = new int[4];
        for (var i = 0; i < outcomes.Length; i++)
        {
            actualRates[positions[i]] += outcomes[i];
            counts[positions[i]]++;
        }

        for (var i = 0; i < 4; i++) actualRates[i] /= Math.Max(1, counts[i]);
        var actualVariance = MathUtilities.CalculateVariance(actualRates);

        var random = new Random(42);
        var permutationVariances = new double[1000];

        for (var i = 0; i < 1000; i++)
        {
            random.Shuffle(positions);

            var pRates = new double[4];
            var pCounts = new int[4];
            for (var j = 0; j < outcomes.Length; j++)
            {
                pRates[positions[j]] += outcomes[j];
                pCounts[positions[j]]++;
            }

            for (var k = 0; k < 4; k++) pRates[k] /= Math.Max(1, pCounts[k]);

            permutationVariances[i] = MathUtilities.CalculateVariance(pRates);
        }

        // P-value calculation
        var pValue = permutationVariances.Count(pv => pv >= actualVariance) / 1000.0;


        var avgEffect = Math.Sqrt(actualVariance);
        var standardError = Math.Sqrt(MathUtilities.CalculateVariance(permutationVariances));

        return new CausalEffectReport
        {
            TreatmentName = "Overall Seat Position Effect (Joint Permutation Test)",
            AverageTreatmentEffect = avgEffect,
            StandardError = standardError,
            TStatistic = avgEffect / standardError,
            PValue = pValue,
            TreatmentGroupSize = data.Count,
            ControlGroupSize = data.Count,
            MatchedPairs = 1000,
            IsSignificant = pValue < 0.05,
            ConfidenceInterval = (0, avgEffect + 1.96 * standardError)
        };
    }

    public async Task<Dictionary<int, CausalEffectReport>> EstimateEachSeatVsOthersEffectAsync(
        List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Analyzing each seat position vs pooled others...");

        var seatEffects = new Dictionary<int, CausalEffectReport>();

        var candidates = data.Select(d => new CausalMatchCandidate(
            d,
            new[] { d.Strength / 100.0, d.FoodAdjustment / 3.0, 1.0 / Math.Max(2, d.CurrentOdds) }
        )).ToList();

        for (var position = 0; position < 4; position++)
        {
            var treated = candidates.Where(c => c.Data.Position == position).ToList();
            var control = candidates.Where(c => c.Data.Position != position).ToList();

            var matches = MatchOptimized(treated, control, 0.15);

            if (!matches.Any())
            {
                Console.WriteLine($"      Position {position}: No matches found (skipping)");
                continue;
            }

            var ate = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
            var standardError = MathUtilities.CalculateStandardError(
                matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
            var tStat = ate / standardError;

            seatEffects[position] = new CausalEffectReport
            {
                TreatmentName = $"Position {position} vs Pooled Others",
                AverageTreatmentEffect = ate,
                StandardError = standardError,
                TStatistic = tStat,
                PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
                TreatmentGroupSize = treated.Count,
                ControlGroupSize = control.Count,
                MatchedPairs = matches.Count,
                IsSignificant = Math.Abs(tStat) > 1.96,
                ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
            };

            Console.WriteLine(
                $"      Position {position} vs All Others: {ate:+0.0%;-0.0%} effect ({matches.Count} matches)");
        }

        return seatEffects;
    }

    #endregion

    #region Arena Analysis (2 Tests)

    // ========== ARENA - 2 TESTS ==========

// Test 1: Joint test - Do arenas differentially affect pirate performance?
    public async Task<CausalEffectReport> TestOverallArenaEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Testing if arena placement affects pirate-specific win rates...");

        // Find pirates who appear in multiple arenas (required for within-pirate comparison)
        var pirateArenas = new Dictionary<int, HashSet<int>>();
        foreach (var point in data)
        {
            if (!pirateArenas.ContainsKey(point.PirateId))
                pirateArenas[point.PirateId] = new HashSet<int>();
            pirateArenas[point.PirateId].Add(point.ArenaId);
        }

        var piratesInMultipleArenas = pirateArenas
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .ToList();

        Console.WriteLine($"      Found {piratesInMultipleArenas.Count} pirates appearing in multiple arenas");

        if (piratesInMultipleArenas.Count < 10)
        {
            Console.WriteLine("      ⚠️ Insufficient pirates in multiple arenas for joint test");
            return new CausalEffectReport
            {
                TreatmentName = "Overall Arena Assignment Effect (Joint Test)",
                AverageTreatmentEffect = 0,
                PValue = 1.0,
                IsSignificant = false,
                TreatmentGroupSize = 0,
                ControlGroupSize = 0,
                MatchedPairs = 0
            };
        }

        // For each pirate in multiple arenas, calculate variance in their win rates across arenas
        var pirateVariances = new List<double>();

        foreach (var pirateId in piratesInMultipleArenas)
        {
            var pirateData = data.Where(d => d.PirateId == pirateId).ToList();
            var pirateWinRatesByArena = pirateData
                .GroupBy(d => d.ArenaId)
                .Select(g => g.Average(d => d.IsWinner ? 1.0 : 0.0))
                .ToList();

            // Only calculate variance if pirate appeared in 2+ arenas with multiple observations
            if (pirateWinRatesByArena.Count >= 2)
            {
                var variance = MathUtilities.CalculateVariance(pirateWinRatesByArena);
                pirateVariances.Add(variance);
            }
        }

        if (!pirateVariances.Any())
        {
            Console.WriteLine("      ⚠️ No pirates with sufficient arena comparisons");
            return new CausalEffectReport
            {
                TreatmentName = "Overall Arena Assignment Effect (Joint Test)",
                AverageTreatmentEffect = 0,
                PValue = 1.0,
                IsSignificant = false
            };
        }

        // Average variance across all pirates (actual effect)
        var actualAvgVariance = pirateVariances.Average();

        Console.WriteLine($"      Average within-pirate variance across arenas: {actualAvgVariance:F6}");

        // Permutation test: shuffle arena assignments within each pirate
        var countGreaterOrEqual = 0;
        var random = new Random(42);

        for (var perm = 0; perm < 1000; perm++)
        {
            var permutedVariances = new List<double>();

            foreach (var pirateId in piratesInMultipleArenas)
            {
                var pirateData = data.Where(d => d.PirateId == pirateId).ToList();
                if (pirateData.Count < 2) continue;

                // Shuffle arenas within this pirate's data
                var arenas = pirateData.Select(d => d.ArenaId).ToArray();
                for (var j = arenas.Length - 1; j > 0; j--)
                {
                    var k = random.Next(j + 1);
                    (arenas[j], arenas[k]) = (arenas[k], arenas[j]);
                }

                // Calculate permuted variance
                var permutedByArena = new Dictionary<int, List<double>>();
                for (var j = 0; j < arenas.Length; j++)
                {
                    var arena = arenas[j];
                    if (!permutedByArena.ContainsKey(arena))
                        permutedByArena[arena] = new List<double>();
                    permutedByArena[arena].Add(pirateData[j].IsWinner ? 1.0 : 0.0);
                }

                if (permutedByArena.Count >= 2)
                {
                    var permutedWinRates = permutedByArena.Values.Select(list => list.Average()).ToList();
                    permutedVariances.Add(MathUtilities.CalculateVariance(permutedWinRates));
                }
            }

            if (permutedVariances.Any())
            {
                var permutedAvgVariance = permutedVariances.Average();
                if (permutedAvgVariance >= actualAvgVariance)
                    countGreaterOrEqual++;
            }
        }

        var pValue = countGreaterOrEqual / 1000.0;

        Console.WriteLine($"      Permutations with variance ≥ actual: {countGreaterOrEqual}/1000");
        Console.WriteLine($"      p-value: {pValue:F4}");

        var avgEffect = Math.Sqrt(actualAvgVariance);
        var standardError = Math.Sqrt(actualAvgVariance / piratesInMultipleArenas.Count);

        return new CausalEffectReport
        {
            TreatmentName = "Overall Arena Assignment Effect (Within-Pirate Variance Test)",
            AverageTreatmentEffect = avgEffect,
            StandardError = standardError,
            TStatistic = avgEffect / standardError,
            PValue = pValue,
            TreatmentGroupSize = piratesInMultipleArenas.Count,
            ControlGroupSize = piratesInMultipleArenas.Count,
            MatchedPairs = 1000,
            IsSignificant = pValue < 0.05,
            ConfidenceInterval = (0, avgEffect + 1.96 * standardError)
        };
    }

// Test 2: Individual arena effects (same-pirate comparison)
    public async Task<Dictionary<int, CausalEffectReport>> EstimateIndividualArenaEffectsAsync(
        List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Analyzing individual arena effects (same-pirate comparisons)...");

        var arenaEffects = new Dictionary<int, CausalEffectReport>();

        for (var arenaId = 1; arenaId <= 5; arenaId++)
        {
            var effect = await EstimateArenaEffectAsync(data, arenaId);
            arenaEffects[arenaId] = effect;

            Console.WriteLine($"      Arena {arenaId}: {effect.AverageTreatmentEffect:+0.0%;-0.0%} " +
                              $"({effect.MatchedPairs} matched pairs) " +
                              $"{(effect.IsSignificant ? "✅" : "⚠️")}");
        }

        return arenaEffects;
    }

    private async Task<CausalEffectReport> EstimateArenaEffectAsync(List<CausalDataPoint> data, int targetArenaId)
    {
        // Build lookup dictionaries upfront for O(1) access
        var pirateArenas = new Dictionary<int, HashSet<int>>();
        foreach (var point in data)
        {
            if (!pirateArenas.ContainsKey(point.PirateId))
                pirateArenas[point.PirateId] = new HashSet<int>();
            pirateArenas[point.PirateId].Add(point.ArenaId);
        }

        var piratesInMultipleArenas = pirateArenas
            .Where(kv => kv.Value.Count > 1)
            .Select(kv => kv.Key)
            .ToHashSet();

        var targetArenaData = data
            .Where(d => d.ArenaId == targetArenaId && piratesInMultipleArenas.Contains(d.PirateId))
            .ToList();

        var otherArenasLookup = data
            .Where(d => d.ArenaId != targetArenaId && piratesInMultipleArenas.Contains(d.PirateId))
            .GroupBy(d => d.PirateId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var matches = new List<MatchedPair>();

        foreach (var target in targetArenaData)
        {
            if (!otherArenasLookup.TryGetValue(target.PirateId, out var candidateMatches))
                continue;

            var samePirateOtherArena = candidateMatches
                .Where(d => Math.Abs(d.FoodAdjustment - target.FoodAdjustment) <= 1 &&
                            Math.Abs(d.Position - target.Position) <= 1)
                .OrderBy(d => Math.Abs(d.CurrentOdds - target.CurrentOdds))
                .FirstOrDefault();

            if (samePirateOtherArena != null)
                matches.Add(new MatchedPair
                {
                    TreatedOutcome = target.IsWinner ? 1.0 : 0.0,
                    ControlOutcome = samePirateOtherArena.IsWinner ? 1.0 : 0.0,
                    PropensityScore = 0.5
                });
        }

        var ate = matches.Any() ? matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average() : 0;
        var standardError = matches.Any()
            ? MathUtilities.CalculateStandardError(matches.Select(m => m.TreatedOutcome - m.ControlOutcome))
            : 0;
        var tStat = standardError > 0 ? ate / standardError : 0;

        return new CausalEffectReport
        {
            TreatmentName = $"Arena {targetArenaId} Placement",
            AverageTreatmentEffect = ate,
            StandardError = standardError,
            TStatistic = tStat,
            PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
            TreatmentGroupSize = targetArenaData.Count,
            ControlGroupSize = otherArenasLookup.Values.Sum(list => list.Count),
            MatchedPairs = matches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96 && matches.Count > 30,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
        };
    }

    #endregion


    #region Helper Methods (continued)

    private async Task<List<CausalDataPoint>> LoadCausalDataAsync(bool redistributeClamped = true)
    {
        var roundResults = await _context.RoundResults
            .Where(rr => rr.IsComplete && rr.RoundId.HasValue)
            .Select(rr => new
            {
                rr.RoundId,
                rr.ArenaId,
                rr.PirateId,
                rr.IsWinner
            })
            .ToListAsync();

        var roundPlacements = await _context.RoundPiratePlacements
            .Where(rpp => rpp.RoundId.HasValue &&
                          rpp.ArenaId.HasValue &&
                          rpp.PirateId.HasValue &&
                          (rpp.CurrentOdds ?? rpp.StartingOdds) > 1) // Exclude 1:1 placeholders
            .Select(rpp => new
            {
                RoundId = rpp.RoundId!.Value,
                ArenaId = rpp.ArenaId!.Value,
                PirateId = rpp.PirateId!.Value,
                rpp.PirateFoodAdjustment,
                CurrentOdds = rpp.CurrentOdds ?? rpp.StartingOdds,
                Position = rpp.PirateSeatPosition ?? 0
            })
            .ToListAsync();

        var pirates = await _context.Pirates
            .Select(p => new
            {
                p.PirateId,
                Strength = p.Strength ?? 0,
                Weight = p.Weight ?? 0
            })
            .ToListAsync();

        var placementLookup = roundPlacements.ToDictionary(
            rpp => (rpp.RoundId, rpp.ArenaId, rpp.PirateId),
            rpp => rpp
        );

        var pirateLookup = pirates.ToDictionary(p => p.PirateId, p => p);

        var causalData = new List<CausalDataPoint>();

        foreach (var result in roundResults)
        {
            var key = (result.RoundId!.Value, result.ArenaId, result.PirateId);

            if (placementLookup.TryGetValue(key, out var placement) &&
                pirateLookup.TryGetValue(result.PirateId, out var pirate))
                causalData.Add(new CausalDataPoint
                {
                    RoundId = result.RoundId.Value,
                    ArenaId = result.ArenaId,
                    PirateId = result.PirateId,
                    IsWinner = result.IsWinner,
                    FoodAdjustment = placement.PirateFoodAdjustment,
                    CurrentOdds = placement.CurrentOdds,
                    Position = placement.Position,
                    Strength = pirate.Strength,
                    Weight = pirate.Weight
                });
        }

        Console.WriteLine($"   Loaded {causalData.Count} causal data points (1:1 odds excluded)");

        if (redistributeClamped) causalData = SplitClamped13IntoSubBuckets(causalData);

        return causalData;
    }

    private List<CausalDataPoint> SplitClamped13IntoSubBuckets(List<CausalDataPoint> data)
    {
        var result = new List<CausalDataPoint>();

        var clamped13 = data.Where(d => d.CurrentOdds == 13).ToList();

        if (!clamped13.Any())
            return data;

        var qualityScores = clamped13.Select(d => new
        {
            DataPoint = d,
            Quality = d.Strength / 100.0 * 0.4 +
                      (d.FoodAdjustment + 3) / 6.0 * 0.3 +
                      (4 - d.Position) / 4.0 * 0.3
        }).OrderByDescending(x => x.Quality).ToList();

        var count = qualityScores.Count;
        var q1 = count / 4;
        var q2 = count / 2;
        var q3 = count * 3 / 4;

        for (var i = 0; i < qualityScores.Count; i++)
        {
            var item = qualityScores[i];
            int estimatedOdds;

            if (i < q1) estimatedOdds = 13;
            else if (i < q2) estimatedOdds = 15;
            else if (i < q3) estimatedOdds = 18;
            else estimatedOdds = 23;

            result.Add(new CausalDataPoint
            {
                RoundId = item.DataPoint.RoundId,
                ArenaId = item.DataPoint.ArenaId,
                PirateId = item.DataPoint.PirateId,
                IsWinner = item.DataPoint.IsWinner,
                FoodAdjustment = item.DataPoint.FoodAdjustment,
                CurrentOdds = estimatedOdds,
                Position = item.DataPoint.Position,
                Strength = item.DataPoint.Strength,
                Weight = item.DataPoint.Weight
            });
        }

        result.AddRange(data.Where(d => d.CurrentOdds != 13));

        Console.WriteLine($"   Split {clamped13.Count} clamped 13:1 odds into estimated true odds (13-23:1)");

        return result;
    }

    private Dictionary<int, double> CalculateOddsDoseResponse(List<CausalDataPoint> data)
    {
        var doseResponse = new Dictionary<int, double>();
        var oddsLevels = new[] { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        foreach (var oddsLevel in oddsLevels)
        {
            var atOdds = data.Where(d => d.CurrentOdds == oddsLevel).ToList();
            if (atOdds.Count < 10) continue;

            var winRate = atOdds.Average(d => d.IsWinner ? 1.0 : 0.0);
            doseResponse[oddsLevel] = winRate;
        }

        return doseResponse;
    }

    private async Task<Dictionary<string, InteractionEffect>> AnalyzeInteractionEffectsAsync(List<CausalDataPoint> data)
    {
        var interactions = new Dictionary<string, InteractionEffect>();

        // 1. Food Adjustment × Position
        interactions["FoodAdj_x_Position"] = AnalyzeInteraction(data,
            d => d.FoodAdjustment >= 1,
            d => d.Position <= 1,
            "Positive Food × Front Position");

        // 2. Food Adjustment × Being Favorite
        interactions["FoodAdj_x_Favorite"] = AnalyzeInteraction(data,
            d => d.FoodAdjustment >= 1,
            d => d.CurrentOdds <= 2,
            "Positive Food × Favorite Status");

        // 3. High Strength × Position
        var medianStrength = data.Select(d => d.Strength).OrderBy(s => s).ElementAt(data.Count / 2);
        interactions["Strength_x_Position"] = AnalyzeInteraction(data,
            d => d.Strength >= medianStrength,
            d => d.Position <= 1,
            "High Strength × Front Position");

        return interactions;
    }

    private InteractionEffect AnalyzeInteraction(
        List<CausalDataPoint> data,
        Func<CausalDataPoint, bool> treatment1,
        Func<CausalDataPoint, bool> treatment2,
        string name)
    {
        var bothGroup = data.Where(d => treatment1(d) && treatment2(d)).ToList();
        var t1OnlyGroup = data.Where(d => treatment1(d) && !treatment2(d)).ToList();
        var t2OnlyGroup = data.Where(d => !treatment1(d) && treatment2(d)).ToList();
        var neitherGroup = data.Where(d => !treatment1(d) && !treatment2(d)).ToList();

        var both = bothGroup.Any() ? bothGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;
        var t1Only = t1OnlyGroup.Any() ? t1OnlyGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;
        var t2Only = t2OnlyGroup.Any() ? t2OnlyGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;
        var neither = neitherGroup.Any() ? neitherGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;

        var interactionEffect = both - t1Only - (t2Only - neither);

        if (bothGroup.Count < 10 || t1OnlyGroup.Count < 10 || t2OnlyGroup.Count < 10 || neitherGroup.Count < 10)
        {
            Console.WriteLine($"   ⚠️ Warning: {name} has small sample sizes:");
            Console.WriteLine(
                $"      Both: {bothGroup.Count}, T1 only: {t1OnlyGroup.Count}, T2 only: {t2OnlyGroup.Count}, Neither: {neitherGroup.Count}");
        }

        return new InteractionEffect
        {
            Name = name,
            InteractionStrength = interactionEffect,
            BothTreatments = both,
            Treatment1Only = t1Only,
            Treatment2Only = t2Only,
            Neither = neither,
            IsSynergistic = interactionEffect > 0.02 && bothGroup.Count >= 10,
            IsAntagonistic = interactionEffect < -0.02 && bothGroup.Count >= 10
        };
    }

    private List<MatchedPair> MatchOptimized(
        List<CausalMatchCandidate> treatment,
        List<CausalMatchCandidate> control,
        double maxDistance)
    {
        var matches = new List<MatchedPair>();

        // PERFORMANCE: Cap search pool to maintain O(N) execution time for massive datasets
        // 2000 is a standard statistical sample size for stable matching
        var pool = control.Count > 2000
            ? control.OrderBy(_ => Random.Shared.Next()).Take(2000).ToList()
            : control;

        foreach (var t in treatment)
        {
            var bestDist = double.MaxValue;
            CausalMatchCandidate? bestMatch = null;

            foreach (var c in pool)
            {
                var dist = MathUtilities.EuclideanDistance(t.Covariates, c.Covariates);
                if (dist < bestDist && dist < maxDistance)
                {
                    bestDist = dist;
                    bestMatch = c;
                }
            }

            if (bestMatch.HasValue)
                matches.Add(new MatchedPair
                {
                    TreatedOutcome = t.Data.IsWinner ? 1.0 : 0.0,
                    ControlOutcome = bestMatch.Value.Data.IsWinner ? 1.0 : 0.0,
                    Distance = bestDist
                });
        }

        return matches;
    }


    private List<MatchedPair> MatchOnCovariates(
        List<CausalDataPoint> treatment,
        List<CausalDataPoint> control,
        Func<CausalDataPoint, double[]> getCovariates,
        double maxDistance = 0.2)
    {
        var matches = new List<MatchedPair>();

        // ✅ OPTIMIZATION: Adaptive sampling based on control size
        // If control is massive, sample intelligently for 100x speedup
        List<CausalDataPoint> controlSample;
        var sampleSize = 3000; // Statistically sufficient for matching

        if (control.Count > sampleSize * 2)
        {
            // Stratified sampling: ensure we get representation across strength/odds
            var controlByStrength = control
                .OrderBy(c => c.Strength)
                .Select((c, idx) => new { Data = c, Stratum = idx / (control.Count / 10) })
                .GroupBy(x => x.Stratum)
                .SelectMany(g => g.OrderBy(_ => Guid.NewGuid()).Take(sampleSize / 10))
                .Select(x => x.Data)
                .ToList();

            controlSample = controlByStrength;
            Console.WriteLine(
                $"      Matching optimization: sampled {controlSample.Count} from {control.Count} controls (stratified)");
        }
        else
        {
            controlSample = control;
        }

        // ✅ OPTIMIZATION: Pre-compute all control covariates (avoid recomputing in loop)
        var controlCovariates = controlSample
            .Select(c => new { Control = c, Covariates = getCovariates(c) })
            .ToList();

        foreach (var treated in treatment)
        {
            var treatedCovariates = getCovariates(treated);

            var bestMatch = controlCovariates
                .Select(cc => new
                {
                    cc.Control,
                    Distance = MathUtilities.EuclideanDistance(treatedCovariates, cc.Covariates)
                })
                .Where(x => x.Distance < maxDistance)
                .OrderBy(x => x.Distance)
                .FirstOrDefault();

            if (bestMatch != null)
                matches.Add(new MatchedPair
                {
                    TreatedOutcome = treated.IsWinner ? 1.0 : 0.0,
                    ControlOutcome = bestMatch.Control.IsWinner ? 1.0 : 0.0,
                    PropensityScore = 1.0 - bestMatch.Distance,
                    Distance = bestMatch.Distance
                });
        }

        return matches;
    }

    private void DisplayEffect(CausalEffectReport effect)
    {
        var significance = effect.IsSignificant ? "✅ Significant" : "⚠️ Not Significant";
        var direction = effect.AverageTreatmentEffect > 0 ? "increases" : "decreases";

        Console.WriteLine($"   {effect.TreatmentName}:");
        Console.WriteLine($"      Effect: {effect.AverageTreatmentEffect:+0.0%;-0.0%} {direction} win probability");
        Console.WriteLine($"      {significance} (p={effect.PValue:F3}, t={effect.TStatistic:F2})");
        Console.WriteLine(
            $"      95% CI: [{effect.ConfidenceInterval.Lower:+0.0%;-0.0%}, {effect.ConfidenceInterval.Upper:+0.0%;-0.0%}]");
        Console.WriteLine(
            $"      Sample: {effect.TreatmentGroupSize} treated, {effect.ControlGroupSize} control, {effect.MatchedPairs} matched pairs");

        if (effect.DoseResponse != null && effect.DoseResponse.Any())
        {
            Console.WriteLine("      Dose-Response:");
            foreach (var (dose, response) in effect.DoseResponse.OrderBy(kv => kv.Key))
                Console.WriteLine($"         Odds {dose}:1 → {response:P2} win rate");
        }

        if (effect.PositionEffects != null && effect.PositionEffects.Any())
        {
            Console.WriteLine("      By Position:");
            foreach (var (position, posEffect) in effect.PositionEffects.OrderBy(kv => kv.Key))
                Console.WriteLine($"         Position {position}: {posEffect:+0.0%;-0.0%} effect");
        }
    }

    private void DisplayInteractionEffects(Dictionary<string, InteractionEffect> interactions)
    {
        foreach (var (key, interaction) in interactions)
        {
            Console.WriteLine($"   {interaction.Name}:");
            Console.WriteLine($"      Interaction Strength: {interaction.InteractionStrength:+0.0%;-0.0%}");

            if (interaction.IsSynergistic)
                Console.WriteLine("      🔵 Synergistic: Combining treatments is better than sum of parts");
            else if (interaction.IsAntagonistic)
                Console.WriteLine("      🔴 Antagonistic: Treatments interfere with each other");
            else
                Console.WriteLine("      ⚪ Additive: Effects are independent");

            Console.WriteLine("      Win Rates:");
            Console.WriteLine($"         Both treatments: {interaction.BothTreatments:P2}");
            Console.WriteLine($"         Treatment 1 only: {interaction.Treatment1Only:P2}");
            Console.WriteLine($"         Treatment 2 only: {interaction.Treatment2Only:P2}");
            Console.WriteLine($"         Neither: {interaction.Neither:P2}");
        }
    }

    private void GenerateKeyFindings(ComprehensiveCausalReport report)
    {
        report.KeyFindings.Clear();
        report.Recommendations.Clear();

        // Food adjustment findings
        if (report.FoodAdjustmentEffect.IsSignificant)
        {
            report.KeyFindings.Add(
                $"Food adjustment has {report.FoodAdjustmentEffect.AverageTreatmentEffect:+0.0%;-0.0%} causal effect");
            if (Math.Abs(report.FoodAdjustmentEffect.AverageTreatmentEffect) > 0.05)
                report.Recommendations.Add("Strongly prioritize food adjustments in betting strategy");
        }

        // Seat position findings
        if (report.OverallSeatPositionJointTest?.IsSignificant == true)
        {
            report.KeyFindings.Add("Seat position has significant causal impact");

            if (report.EachSeatVsOthersEffects.Any())
            {
                var bestPos = report.EachSeatVsOthersEffects.OrderByDescending(kv => kv.Value.AverageTreatmentEffect)
                    .First();
                var worstPos = report.EachSeatVsOthersEffects.OrderBy(kv => kv.Value.AverageTreatmentEffect).First();

                report.KeyFindings.Add(
                    $"Position {bestPos.Key} shows strongest advantage ({bestPos.Value.AverageTreatmentEffect:+0.0%;-0.0%})");
                report.Recommendations.Add($"Weight Position {bestPos.Key} heavily in predictions");
            }
        }
        else
        {
            report.KeyFindings.Add("Seat position shows no significant causal effect");
        }

        // Arena findings
        if (report.OverallArenaJointTest?.IsSignificant == true)
        {
            report.KeyFindings.Add("Arena placement has significant causal impact");

            var significantArenas = report.IndividualArenaEffects
                .Where(kv => kv.Value.IsSignificant)
                .OrderByDescending(kv => Math.Abs(kv.Value.AverageTreatmentEffect))
                .ToList();

            if (significantArenas.Any())
            {
                var best = significantArenas.First();
                report.Recommendations.Add(
                    $"Consider arena-specific adjustments (Arena {best.Key} shows {best.Value.AverageTreatmentEffect:+0.0%;-0.0%} effect)");
            }
        }
        else
        {
            report.KeyFindings.Add("Arena placement shows no significant causal effect");
        }

        // Rival strength
        if (report.RivalStrengthEffect.IsSignificant &&
            Math.Abs(report.RivalStrengthEffect.AverageTreatmentEffect) > 0.03)
        {
            report.KeyFindings.Add(
                $"Strong rivals {(report.RivalStrengthEffect.AverageTreatmentEffect < 0 ? "reduce" : "increase")} win probability by {Math.Abs(report.RivalStrengthEffect.AverageTreatmentEffect):0.0%}");
            report.Recommendations.Add("Include detailed rival analysis in predictions");
        }

        // Odds diagnostic
        if (report.OddsDiagnostic?.IsPatternInverted == true)
        {
            report.KeyFindings.Add("⚠️ WARNING: Odds pattern appears inverted in data");
            report.Recommendations.Add("URGENT: Investigate odds data quality");
        }

        // Interactions
        var strongSynergies = report.InteractionEffects
            .Where(kv => kv.Value.IsSynergistic && Math.Abs(kv.Value.InteractionStrength) > 0.03)
            .ToList();

        if (strongSynergies.Any())
        {
            report.KeyFindings.Add($"Found {strongSynergies.Count} strong synergistic combinations");
            foreach (var (key, effect) in strongSynergies)
                report.Recommendations.Add($"Prioritize {effect.Name} combinations");
        }

        Console.WriteLine("\n📋 KEY FINDINGS:");
        foreach (var finding in report.KeyFindings) Console.WriteLine($"   • {finding}");

        Console.WriteLine("\n💡 RECOMMENDATIONS:");
        foreach (var rec in report.Recommendations) Console.WriteLine($"   → {rec}");
    }

    private void SaveCausalReport(ComprehensiveCausalReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"causal_analysis_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"\n📄 Comprehensive causal analysis report saved to {fileName}");
    }

    public async Task<OddsDiagnosticReport> DiagnoseOddsPatternAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 ODDS PATTERN DIAGNOSTIC");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine("ℹ️  Note: 1:1 odds excluded (no-bet placeholders)");
        Console.WriteLine("ℹ️  Note: 13:1 odds redistributed to estimated true odds\n");

        var oddsBuckets = data.GroupBy(d => d.CurrentOdds)
            .OrderBy(g => g.Key)
            .Select(g => new OddsBucket
            {
                Odds = g.Key,
                Count = g.Count(),
                Wins = g.Count(d => d.IsWinner),
                WinRate = g.Average(d => d.IsWinner ? 1.0 : 0.0),
                ImpliedProbability = 1.0 / (g.Key + 1.0),
                AvgStrength = g.Average(d => d.Strength),
                AvgFoodAdjustment = g.Average(d => d.FoodAdjustment),
                AvgPosition = g.Average(d => d.Position)
            })
            .ToList();

        Console.WriteLine("Odds Analysis:");
        Console.WriteLine("────────────────────────────────────────────────────────────────────");
        Console.WriteLine($"{"Odds",-8} {"Count",-8} {"Wins",-8} {"Win%",-10} {"Expected%",-12} {"Diff",-10}");
        Console.WriteLine("────────────────────────────────────────────────────────────────────");

        foreach (var bucket in oddsBuckets)
        {
            var diff = bucket.WinRate - bucket.ImpliedProbability;
            var warning = bucket.Odds >= 13 && bucket.Odds <= 25 ? " (redistributed)" : "";

            Console.WriteLine($"{bucket.Odds}:1      {bucket.Count,-8} {bucket.Wins,-8} " +
                              $"{bucket.WinRate,-10:P2} {bucket.ImpliedProbability,-12:P2} " +
                              $"{diff,-10:+0.0%;-0.0%}{warning}");
        }

        var oddsValues = data.Select(d => (double)d.CurrentOdds).ToList();
        var outcomes = data.Select(d => d.IsWinner ? 1.0 : 0.0).ToList();
        var correlation = MathUtilities.CalculateCorrelation(oddsValues, outcomes);

        Console.WriteLine($"\n📊 Correlation: {correlation:F4}");
        Console.WriteLine(correlation > 0
            ? "   ⚠️  Positive = HIGHER odds = MORE wins (INVERTED!)"
            : "   ✅ Negative = higher odds = fewer wins (expected)");

        return new OddsDiagnosticReport
        {
            OddsBuckets = oddsBuckets,
            CorrelationWithWinning = correlation,
            IsPatternInverted = correlation > 0,
            TotalObservations = data.Count
        };
    }

    #endregion
}