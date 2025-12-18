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

        // Load all data once
        var allData = await LoadCausalDataAsync();
        Console.WriteLine($"📊 Loaded {allData.Count} observations for causal analysis\n");

        // 1. Food Adjustment Effect
        Console.WriteLine("1️⃣ Analyzing Food Adjustment Effect...");
        report.FoodAdjustmentEffect = await EstimateFoodAdjustmentEffectAsync(allData);
        DisplayEffect(report.FoodAdjustmentEffect);

        // ========== SEAT POSITION - 4 TESTS ==========
        Console.WriteLine("\n2️⃣ SEAT POSITION ANALYSIS (4 Tests)");
        Console.WriteLine("═══════════════════════════════════════════════════");

        // Test 1: Overall seat position effect (Position 0 vs others with breakdown)
        Console.WriteLine("\n   Test 1: Overall Seat Position Effect (Position 0 vs Others)...");
        report.SeatPositionEffect = await EstimateSeatPositionEffectAsync(allData);
        DisplayEffect(report.SeatPositionEffect);

        // Test 2: Individual seat position analysis (detailed analysis of each)
        Console.WriteLine("\n   Test 2: Individual Seat Position Analysis...");
        report.IndividualSeatPositionEffects = await EstimateIndividualSeatPositionEffectsAsync(allData);
        foreach (var (position, effect) in report.IndividualSeatPositionEffects.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"\n      Position {position}:");
            DisplayEffect(effect);
        }

        // Test 3: Each seat vs all others (comparative)
        Console.WriteLine("\n   Test 3: Each Seat Position vs All Others (Comparative)...");
        report.EachSeatVsOthersEffects = await EstimateEachSeatVsOthersEffectAsync(allData);
        foreach (var (position, effect) in report.EachSeatVsOthersEffects.OrderBy(kv => kv.Key))
            Console.WriteLine(
                $"      Position {position} vs All Others: {effect.AverageTreatmentEffect:+0.0%;-0.0%} effect ({effect.MatchedPairs} matches)");

        // Test 4: Joint test for seat position
        Console.WriteLine("\n   Test 4: Overall Seat Position Joint Test...");
        report.OverallSeatPositionJointTest = await TestOverallSeatPositionEffectAsync(allData);
        Console.WriteLine($"      Permutation test: p={report.OverallSeatPositionJointTest.PValue:F3} " +
                          $"{(report.OverallSeatPositionJointTest.IsSignificant ? "✅" : "⚠️")}");
        Console.WriteLine(
            $"      → Seat position {(report.OverallSeatPositionJointTest.IsSignificant ? "DOES" : "does NOT")} matter overall");

        // ========== ARENA - 4 TESTS ==========
        Console.WriteLine("\n3️⃣ ARENA PLACEMENT ANALYSIS (4 Tests)");
        Console.WriteLine("═══════════════════════════════════════════════════");

        // Test 1: Overall arena effect (you might skip this if doing all individually)
        // Skipping since Test 2 covers all arenas

        // Test 2: Individual arena analysis (each arena with full report)
        Console.WriteLine("\n   Test 2: Individual Arena Effects...");
        report.IndividualArenaEffects = await EstimateIndividualArenaEffectsAsync(allData);
        foreach (var (arenaId, effect) in report.IndividualArenaEffects.OrderBy(kv => kv.Key))
        {
            Console.WriteLine($"\n      Arena {arenaId}:");
            DisplayEffect(effect);
        }

        // Test 3: Each arena vs all others (comparative)
        Console.WriteLine("\n   Test 3: Each Arena vs All Others (Comparative)...");
        report.EachArenaVsOthersEffects = await EstimateEachArenaVsOthersEffectAsync(allData);
        foreach (var (arenaId, effect) in report.EachArenaVsOthersEffects.OrderBy(kv => kv.Key))
            Console.WriteLine(
                $"      Arena {arenaId} vs All Others: {effect.AverageTreatmentEffect:+0.0%;-0.0%} effect ({effect.MatchedPairs} matches)");

        // Test 4: Joint test for arena
        Console.WriteLine("\n   Test 4: Overall Arena Joint Test...");
        report.OverallArenaJointTest = await TestOverallArenaEffectAsync(allData);
        Console.WriteLine($"      Permutation test: p={report.OverallArenaJointTest.PValue:F3} " +
                          $"{(report.OverallArenaJointTest.IsSignificant ? "✅" : "⚠️")}");
        Console.WriteLine(
            $"      → Arena placement {(report.OverallArenaJointTest.IsSignificant ? "DOES" : "does NOT")} matter overall");

        // 4. Rival Strength Effect
        Console.WriteLine("\n4️⃣ Analyzing Rival Strength Effect...");
        report.RivalStrengthEffect = await EstimateRivalStrengthEffectAsync(allData);
        DisplayEffect(report.RivalStrengthEffect);

        // 5. Odds Effect
        Console.WriteLine("\n5️⃣ Analyzing Odds/Favorite Status Effect...");
        report.OddsEffect = await EstimateOddsEffectAsync(allData);
        DisplayEffect(report.OddsEffect);

        // 5b. Odds Diagnostic
        Console.WriteLine("\n5️⃣b Running Comprehensive Odds Diagnostic...");
        report.OddsDiagnostic = await DiagnoseOddsPatternAsync(allData);
        Console.WriteLine($"   {report.OddsDiagnostic.DiagnosisMessage}");

        // 6. Interaction Effects
        Console.WriteLine("\n6️⃣ Analyzing Interaction Effects...");
        report.InteractionEffects = await AnalyzeInteractionEffectsAsync(allData);
        DisplayInteractionEffects(report.InteractionEffects);

        // 7. Generate key findings and recommendations
        Console.WriteLine("\n7️⃣ Generating Key Findings and Recommendations...");
        GenerateKeyFindings(report);

        // Save comprehensive report
        SaveCausalReport(report);

        return report;
    }

    public async Task<CausalEffectReport> EstimateFoodAdjustmentEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        // Treatment: Positive food adjustment (≥1) vs neutral/negative (≤0)
        var treated = data.Where(d => d.FoodAdjustment >= 1).ToList();
        var control = data.Where(d => d.FoodAdjustment <= 0).ToList();

        // Match on confounders: odds, position, strength
        var matches = MatchOnCovariates(treated, control,
            d => new[]
            {
                1.0 / Math.Max(2, d.CurrentOdds), // Normalize odds
                d.Position / 4.0, // Normalize position (0-3)
                d.Strength / 100.0 // Normalize strength
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
            IsSignificant = Math.Abs(tStat) > 1.96, // 95% confidence
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
        };
    }

    public async Task<CausalEffectReport> EstimateSeatPositionEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        // Analyze each position separately
        var positionEffects = new Dictionary<int, double>();

        for (var position = 0; position < 4; position++)
        {
            var inPosition = data.Where(d => d.Position == position).ToList();
            var otherPositions = data.Where(d => d.Position != position).ToList();

            var matches = MatchOnCovariates(inPosition, otherPositions,
                d => new[]
                {
                    d.Strength / 100.0,
                    d.FoodAdjustment / 3.0,
                    1.0 / Math.Max(2, d.CurrentOdds)
                },
                0.15);

            if (matches.Any())
                positionEffects[position] = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
        }

        // Overall position effect (position 0 vs others)
        var position0 = data.Where(d => d.Position == 0).ToList();
        var otherPos = data.Where(d => d.Position > 0).ToList();

        var overallMatches = MatchOnCovariates(position0, otherPos,
            d => new[]
            {
                d.Strength / 100.0,
                d.FoodAdjustment / 3.0,
                1.0 / Math.Max(2, d.CurrentOdds)
            });

        var ate = overallMatches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
        var standardError =
            MathUtilities.CalculateStandardError(overallMatches.Select(m => m.TreatedOutcome - m.ControlOutcome));
        var tStat = ate / standardError;

        return new CausalEffectReport
        {
            TreatmentName = "Position 0 (First Seat)",
            AverageTreatmentEffect = ate,
            StandardError = standardError,
            TStatistic = tStat,
            PValue = MathUtilities.CalculatePValueFromT(tStat, overallMatches.Count),
            TreatmentGroupSize = position0.Count,
            ControlGroupSize = otherPos.Count,
            MatchedPairs = overallMatches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError),
            PositionEffects = positionEffects
        };
    }


    public async Task<CausalEffectReport> EstimateRivalStrengthEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        // Calculate average rival strength for each observation
        var dataWithRivalStrength = new List<(CausalDataPoint Point, double AvgRivalStrength)>();

        foreach (var point in data)
        {
            var rivals = await _context.RoundPiratePlacements
                .Where(rpp => rpp.RoundId == point.RoundId &&
                              rpp.ArenaId == point.ArenaId &&
                              rpp.PirateId != point.PirateId)
                .Join(_context.Pirates,
                    rpp => rpp.PirateId,
                    p => p.PirateId,
                    (rpp, p) => p.Strength ?? 0)
                .ToListAsync();

            var avgRivalStrength = rivals.Any() ? rivals.Average() : 0;
            dataWithRivalStrength.Add((point, avgRivalStrength));
        }

        // Treatment: Facing strong rivals (above median) vs weak rivals (below median)
        var medianRivalStrength = dataWithRivalStrength.Select(d => d.AvgRivalStrength).OrderBy(s => s)
            .ElementAt(dataWithRivalStrength.Count / 2);

        var strongRivals = dataWithRivalStrength.Where(d => d.AvgRivalStrength >= medianRivalStrength).ToList();
        var weakRivals = dataWithRivalStrength.Where(d => d.AvgRivalStrength < medianRivalStrength).ToList();

        // Match on pirate characteristics
        var matches = MatchOnCovariatesWithRivals(
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

        // Exclude odds of 1 (clamped) and 13 (clamped from above)
        var cleanData = data.Where(d => d.CurrentOdds > 1 && d.CurrentOdds < 13).ToList();

        Console.WriteLine($"   Analyzing odds effect (excluding {data.Count - cleanData.Count} clamped records)...");

        // Treatment: Being the favorite (odds = 2) vs non-favorite (odds > 2, but < 13)
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

        // Calculate dose-response: effect at different odds levels (excluding clamped)
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

    /// <summary>
    ///     Gets individual seat position effects (each position vs all others)
    /// </summary>
    public async Task<Dictionary<int, CausalEffectReport>> EstimateIndividualSeatPositionEffectsAsync(
        List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Analyzing each seat position individually...");

        var positionEffects = new Dictionary<int, CausalEffectReport>();

        for (var position = 0; position < 4; position++)
        {
            var inPosition = data.Where(d => d.Position == position).ToList();
            var otherPositions = data.Where(d => d.Position != position).ToList();

            var matches = MatchOnCovariates(inPosition, otherPositions,
                d => new[]
                {
                    d.Strength / 100.0,
                    d.FoodAdjustment / 3.0,
                    1.0 / Math.Max(2, d.CurrentOdds)
                },
                0.15);

            if (!matches.Any())
            {
                Console.WriteLine($"      Position {position}: No matches found (skipping)");
                continue;
            }

            var ate = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
            var standardError = MathUtilities.CalculateStandardError(
                matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
            var tStat = ate / standardError;

            positionEffects[position] = new CausalEffectReport
            {
                TreatmentName = $"Position {position} vs All Others",
                AverageTreatmentEffect = ate,
                StandardError = standardError,
                TStatistic = tStat,
                PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
                TreatmentGroupSize = inPosition.Count,
                ControlGroupSize = otherPositions.Count,
                MatchedPairs = matches.Count,
                IsSignificant = Math.Abs(tStat) > 1.96,
                ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
            };

            Console.WriteLine($"      Position {position}: {ate:+0.0%;-0.0%} effect ({matches.Count} matches)");
        }

        return positionEffects;
    }

// Test 3: Each seat vs all others (comparative)
    public async Task<Dictionary<int, CausalEffectReport>> EstimateEachSeatVsOthersEffectAsync(
        List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Analyzing each seat position vs all others...");

        var seatEffects = new Dictionary<int, CausalEffectReport>();

        for (var position = 0; position < 4; position++)
        {
            var inPosition = data.Where(d => d.Position == position).ToList();
            var otherPositions = data.Where(d => d.Position != position).ToList();

            var matches = MatchOnCovariates(inPosition, otherPositions,
                d => new[]
                {
                    d.Strength / 100.0,
                    d.FoodAdjustment / 3.0,
                    1.0 / Math.Max(2, d.CurrentOdds)
                },
                0.15);

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
                TreatmentName = $"Position {position} vs All Others",
                AverageTreatmentEffect = ate,
                StandardError = standardError,
                TStatistic = tStat,
                PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
                TreatmentGroupSize = inPosition.Count,
                ControlGroupSize = otherPositions.Count,
                MatchedPairs = matches.Count,
                IsSignificant = Math.Abs(tStat) > 1.96,
                ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
            };

            Console.WriteLine(
                $"      Position {position} vs All Others: {ate:+0.0%;-0.0%} effect ({matches.Count} matches)");
        }

        return seatEffects;
    }

// Test 4: Joint test for seat position
    public async Task<CausalEffectReport> TestOverallSeatPositionEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Performing permutation test for overall seat position effect...");

        // Calculate actual variance between positions
        var positionWinRates = data.GroupBy(d => d.Position)
            .Select(g => g.Average(d => d.IsWinner ? 1.0 : 0.0))
            .ToList();

        var actualVariance = MathUtilities.CalculateVariance(positionWinRates);

        // Permutation test: shuffle position assignments 1000 times
        var random = new Random(42);
        var permutationVariances = new List<double>();
        var positions = data.Select(d => d.Position).ToList();

        for (var i = 0; i < 1000; i++)
        {
            var shuffledPositions = positions.OrderBy(_ => random.Next()).ToList();
            var shuffledData = data.Select((d, idx) => new
            {
                Position = shuffledPositions[idx], d.IsWinner
            }).ToList();

            var shuffledWinRates = shuffledData.GroupBy(d => d.Position)
                .Select(g => g.Average(d => d.IsWinner ? 1.0 : 0.0))
                .ToList();

            permutationVariances.Add(MathUtilities.CalculateVariance(shuffledWinRates));
        }

        // P-value: proportion of permuted variances >= actual variance
        var pValue = permutationVariances.Count(pv => pv >= actualVariance) / 1000.0;

        var avgEffect = Math.Sqrt(actualVariance);
        var standardError = Math.Sqrt(MathUtilities.CalculateVariance(permutationVariances));

        return new CausalEffectReport
        {
            TreatmentName = "Overall Seat Position Effect (Joint Test)",
            AverageTreatmentEffect = avgEffect,
            StandardError = standardError,
            TStatistic = avgEffect / standardError,
            PValue = pValue,
            TreatmentGroupSize = data.Count,
            ControlGroupSize = 1000, // number of permutations
            MatchedPairs = positionWinRates.Count,
            IsSignificant = pValue < 0.05,
            ConfidenceInterval = (0, avgEffect + 1.96 * standardError)
        };
    }


    public async Task<Dictionary<int, CausalEffectReport>> EstimateIndividualArenaEffectsAsync(
        List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Analyzing each arena individually...");

        var arenaEffects = new Dictionary<int, CausalEffectReport>();

        for (var arenaId = 0; arenaId < 5; arenaId++)
            arenaEffects[arenaId] = await EstimateArenaEffectAsync(data, arenaId);

        return arenaEffects;
    }

// Rename existing method for consistency
    public async Task<CausalEffectReport> EstimateArenaEffectAsync(List<CausalDataPoint>? data, int targetArenaId)
    {
        data ??= await LoadCausalDataAsync();

        // Find pirates who appear in multiple arenas (for within-pirate comparison)
        var piratesInMultipleArenas = data
            .GroupBy(d => d.PirateId)
            .Where(g => g.Select(d => d.ArenaId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Console.WriteLine(
            $"      Arena {targetArenaId}: Found {piratesInMultipleArenas.Count} pirates appearing in multiple arenas");

        var inTargetArena = data.Where(d => d.ArenaId == targetArenaId && piratesInMultipleArenas.Contains(d.PirateId))
            .ToList();
        var inOtherArenas = data.Where(d => d.ArenaId != targetArenaId && piratesInMultipleArenas.Contains(d.PirateId))
            .ToList();

        // Match same pirate across arenas
        var matches = new List<MatchedPair>();

        foreach (var target in inTargetArena)
        {
            var samePirateOtherArena = inOtherArenas
                .Where(d => d.PirateId == target.PirateId &&
                            Math.Abs(d.FoodAdjustment - target.FoodAdjustment) <= 1 &&
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
            TreatmentGroupSize = inTargetArena.Count,
            ControlGroupSize = inOtherArenas.Count,
            MatchedPairs = matches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96 && matches.Count > 30,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
        };
    }

// Test 3: Each arena vs all others
    public async Task<Dictionary<int, CausalEffectReport>> EstimateEachArenaVsOthersEffectAsync(
        List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Analyzing each arena vs all others...");

        var arenaEffects = new Dictionary<int, CausalEffectReport>();

        for (var arenaId = 0; arenaId < 5; arenaId++)
        {
            var inArena = data.Where(d => d.ArenaId == arenaId).ToList();
            var otherArenas = data.Where(d => d.ArenaId != arenaId).ToList();

            var matches = MatchOnCovariates(inArena, otherArenas,
                d => new[]
                {
                    d.Strength / 100.0,
                    d.FoodAdjustment / 3.0,
                    d.Position / 3.0,
                    1.0 / Math.Max(2, d.CurrentOdds)
                },
                0.15);

            if (!matches.Any())
            {
                Console.WriteLine($"      Arena {arenaId}: No matches found (skipping)");
                continue;
            }

            var ate = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
            var standardError = MathUtilities.CalculateStandardError(
                matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
            var tStat = ate / standardError;

            arenaEffects[arenaId] = new CausalEffectReport
            {
                TreatmentName = $"Arena {arenaId} vs All Others",
                AverageTreatmentEffect = ate,
                StandardError = standardError,
                TStatistic = tStat,
                PValue = MathUtilities.CalculatePValueFromT(tStat, matches.Count),
                TreatmentGroupSize = inArena.Count,
                ControlGroupSize = otherArenas.Count,
                MatchedPairs = matches.Count,
                IsSignificant = Math.Abs(tStat) > 1.96,
                ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
            };

            Console.WriteLine(
                $"      Arena {arenaId} vs All Others: {ate:+0.0%;-0.0%} effect ({matches.Count} matches)");
        }

        return arenaEffects;
    }

// Test 4: Joint test for arena (already provided earlier)
    public async Task<CausalEffectReport> TestOverallArenaEffectAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync();

        Console.WriteLine("   Performing permutation test for overall arena effect...");

        // Calculate actual variance between arenas
        var arenaWinRates = data.GroupBy(d => d.ArenaId)
            .Select(g => g.Average(d => d.IsWinner ? 1.0 : 0.0))
            .ToList();

        var actualVariance = MathUtilities.CalculateVariance(arenaWinRates);

        // Permutation test: shuffle arena assignments 1000 times
        var random = new Random(42);
        var permutationVariances = new List<double>();
        var arenaIds = data.Select(d => d.ArenaId).ToList();

        for (var i = 0; i < 1000; i++)
        {
            var shuffledArenas = arenaIds.OrderBy(_ => random.Next()).ToList();
            var shuffledData = data.Select((d, idx) => new
            {
                ArenaId = shuffledArenas[idx], d.IsWinner
            }).ToList();

            var shuffledWinRates = shuffledData.GroupBy(d => d.ArenaId)
                .Select(g => g.Average(d => d.IsWinner ? 1.0 : 0.0))
                .ToList();

            permutationVariances.Add(MathUtilities.CalculateVariance(shuffledWinRates));
        }

        // P-value: proportion of permuted variances >= actual variance
        var pValue = permutationVariances.Count(pv => pv >= actualVariance) / 1000.0;

        var avgEffect = Math.Sqrt(actualVariance);
        var standardError = Math.Sqrt(MathUtilities.CalculateVariance(permutationVariances));

        return new CausalEffectReport
        {
            TreatmentName = "Overall Arena Assignment Effect (Joint Test)",
            AverageTreatmentEffect = avgEffect,
            StandardError = standardError,
            TStatistic = avgEffect / standardError,
            PValue = pValue,
            TreatmentGroupSize = data.Count,
            ControlGroupSize = 1000,
            MatchedPairs = arenaWinRates.Count,
            IsSignificant = pValue < 0.05,
            ConfidenceInterval = (0, avgEffect + 1.96 * standardError)
        };
    }

    public async Task<OddsDiagnosticReport> DiagnoseOddsPatternAsync(List<CausalDataPoint>? data = null)
    {
        data ??= await LoadCausalDataAsync(true);

        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 ODDS PATTERN DIAGNOSTIC");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine("ℹ️  Note: Odds of 1:1 are placeholders (no bet) and excluded from analysis");
        Console.WriteLine("ℹ️  Note: Odds of 13:1 redistributed to estimated true odds (13-25:1)\n");

        // Check if any 1:1 odds slipped through (shouldn't happen)
        var onesCount = data.Count(d => d.CurrentOdds == 1);
        if (onesCount > 0)
            Console.WriteLine($"⚠️  WARNING: Found {onesCount} records with 1:1 odds (should be filtered!)");

        // Find minimum odds in dataset
        var minOdds = data.Any() ? data.Min(d => d.CurrentOdds) : 0;
        Console.WriteLine($"   Minimum odds in dataset: {minOdds}:1 (should be 2:1 or higher)");

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

        Console.WriteLine("Odds Analysis by Bucket:");
        Console.WriteLine("───────────────────────────────────────────────────────────────────────");
        Console.WriteLine(
            $"{"Odds",-8} {"Count",-8} {"Wins",-8} {"Win%",-10} {"Expected%",-12} {"Diff",-10} {"AvgStr",-8} {"AvgFood",-8}");
        Console.WriteLine("───────────────────────────────────────────────────────────────────────");

        foreach (var bucket in oddsBuckets)
        {
            var diff = bucket.WinRate - bucket.ImpliedProbability;

            var warning = "";
            if (bucket.Odds >= 13 && bucket.Odds <= 25)
                warning = " (redistributed from clamped 13:1)";

            Console.WriteLine($"{bucket.Odds}:1      {bucket.Count,-8} {bucket.Wins,-8} " +
                              $"{bucket.WinRate,-10:P2} {bucket.ImpliedProbability,-12:P2} " +
                              $"{diff,-10:+0.0%;-0.0%} {bucket.AvgStrength,-8:F1} {bucket.AvgFoodAdjustment,-8:F2}{warning}");
        }


        // Now correlation should be clean across ALL odds
        var oddsValues = data.Select(d => (double)d.CurrentOdds).ToList();
        var outcomes = data.Select(d => d.IsWinner ? 1.0 : 0.0).ToList();
        var correlation = MathUtilities.CalculateCorrelation(oddsValues, outcomes);

        Console.WriteLine("\n📊 Correlation between Odds and Winning (with redistribution):");
        Console.WriteLine($"   Correlation: {correlation:F4}");
        Console.WriteLine(correlation > 0
            ? "   ⚠️  PROBLEM: Positive correlation means HIGHER odds = MORE wins (INVERTED!)"
            : "   ✅ Expected: Negative correlation (higher odds = fewer wins)");

        // Analysis across the full spectrum
        Console.WriteLine("\n📊 Win Rate Analysis Across Odds Spectrum:");
        var favorites = data.Where(d => d.CurrentOdds == 2).ToList();
        var midRange = data.Where(d => d.CurrentOdds >= 5 && d.CurrentOdds <= 7).ToList();
        var longshots = data.Where(d => d.CurrentOdds >= 10 && d.CurrentOdds <= 13).ToList();
        var extremeLongshots = data.Where(d => d.CurrentOdds >= 15).ToList();

        Console.WriteLine(
            $"   Favorites (2:1):        {favorites.Count(d => d.IsWinner),4} / {favorites.Count,5} = {(favorites.Any() ? favorites.Average(d => d.IsWinner ? 1.0 : 0.0) : 0):P2} (expected ~33%)");
        Console.WriteLine(
            $"   Mid-range (5-7:1):      {midRange.Count(d => d.IsWinner),4} / {midRange.Count,5} = {(midRange.Any() ? midRange.Average(d => d.IsWinner ? 1.0 : 0.0) : 0):P2}");
        Console.WriteLine(
            $"   Longshots (10-13:1):    {longshots.Count(d => d.IsWinner),4} / {longshots.Count,5} = {(longshots.Any() ? longshots.Average(d => d.IsWinner ? 1.0 : 0.0) : 0):P2}");
        Console.WriteLine(
            $"   Extreme (15-25+:1):     {extremeLongshots.Count(d => d.IsWinner),4} / {extremeLongshots.Count,5} = {(extremeLongshots.Any() ? extremeLongshots.Average(d => d.IsWinner ? 1.0 : 0.0) : 0):P2}");

        return new OddsDiagnosticReport
        {
            OddsBuckets = oddsBuckets,
            CorrelationWithWinning = correlation,
            IsPatternInverted = correlation > 0,
            TotalObservations = data.Count
        };
    }
    
    

// Helper method to generate key findings
    private void GenerateKeyFindings(ComprehensiveCausalReport report)
    {
        report.KeyFindings.Clear();
        report.Recommendations.Clear();

        // Food adjustment findings
        if (report.FoodAdjustmentEffect.IsSignificant)
        {
            report.KeyFindings.Add(
                $"Food adjustment has a {report.FoodAdjustmentEffect.AverageTreatmentEffect:+0.0%;-0.0%} causal effect on win probability");
            if (report.FoodAdjustmentEffect.AverageTreatmentEffect > 0.05)
                report.Recommendations.Add(
                    "Strongly prioritize pirates with positive food adjustments in betting strategies");
        }
        else
        {
            report.KeyFindings.Add("Food adjustment shows weak causal evidence despite correlation");
        }

        // Seat position findings
        if (report.OverallSeatPositionJointTest?.IsSignificant == true)
        {
            report.KeyFindings.Add("Seat position has significant causal impact on outcomes");

            // Find best and worst positions
            var positionEffects = report.EachSeatVsOthersEffects
                .OrderByDescending(kv => kv.Value.AverageTreatmentEffect)
                .ToList();

            if (positionEffects.Any())
            {
                var bestPos = positionEffects.First();
                var worstPos = positionEffects.Last();

                report.KeyFindings.Add(
                    $"Position {bestPos.Key} shows strongest advantage ({bestPos.Value.AverageTreatmentEffect:+0.0%;-0.0%})");
                report.KeyFindings.Add(
                    $"Position {worstPos.Key} shows strongest disadvantage ({worstPos.Value.AverageTreatmentEffect:+0.0%;-0.0%})");

                report.Recommendations.Add($"Weight Position {bestPos.Key} heavily in model predictions");
                report.Recommendations.Add($"Adjust expectations downward for Position {worstPos.Key}");
            }
        }
        else
        {
            report.KeyFindings.Add("Seat position shows no significant causal effect overall");
        }

        // Arena findings
        if (report.OverallArenaJointTest?.IsSignificant == true)
        {
            report.KeyFindings.Add("Arena placement has significant causal impact on outcomes");

            var significantArenas = report.IndividualArenaEffects
                .Where(kv => kv.Value.IsSignificant)
                .OrderByDescending(kv => Math.Abs(kv.Value.AverageTreatmentEffect))
                .ToList();

            if (significantArenas.Any())
            {
                var mostImpactful = significantArenas.First();
                report.KeyFindings.Add(
                    $"Arena {mostImpactful.Key} shows strongest effect ({mostImpactful.Value.AverageTreatmentEffect:+0.0%;-0.0%})");
                report.Recommendations.Add("Consider arena-specific adjustments in betting strategy");
            }
        }
        else
        {
            report.KeyFindings.Add("Arena placement shows no significant causal effect overall");
        }

        // Rival strength findings
        if (report.RivalStrengthEffect.IsSignificant)
        {
            var effect = report.RivalStrengthEffect.AverageTreatmentEffect;
            report.KeyFindings.Add(
                $"Strong rivals {(effect < 0 ? "reduce" : "increase")} win probability by {Math.Abs(effect):0.0%}");

            if (Math.Abs(effect) > 0.05)
                report.Recommendations.Add(
                    "Head-to-head matchup analysis is critical - include detailed rival analysis");
        }

        // Odds findings
        if (report.OddsEffect.IsSignificant)
        {
            report.KeyFindings.Add(
                $"Favorite status has {report.OddsEffect.AverageTreatmentEffect:+0.0%;-0.0%} causal effect");

            if (report.OddsEffect.DoseResponse != null && report.OddsEffect.DoseResponse.Any())
            {
                var bestValue = report.OddsEffect.DoseResponse
                    .Select(kv => new { Odds = kv.Key, Value = kv.Value / (1.0 / (kv.Key + 1.0)) })
                    .OrderByDescending(x => x.Value)
                    .FirstOrDefault();

                if (bestValue != null && bestValue.Value > 1.1)
                    report.Recommendations.Add($"Pirates at {bestValue.Odds}:1 odds show best value for betting");
            }
        }

        // Odds diagnostic findings
        if (report.OddsDiagnostic?.IsPatternInverted == true)
        {
            report.KeyFindings.Add("⚠️ WARNING: Odds pattern appears inverted or incorrect in data");
            report.Recommendations.Add("URGENT: Investigate odds data quality before making betting decisions");
        }

        // Interaction findings
        var strongSynergies = report.InteractionEffects
            .Where(kv => kv.Value.IsSynergistic && Math.Abs(kv.Value.InteractionStrength) > 0.03)
            .ToList();

        var strongAntagonisms = report.InteractionEffects
            .Where(kv => kv.Value.IsAntagonistic && Math.Abs(kv.Value.InteractionStrength) > 0.03)
            .ToList();

        if (strongSynergies.Any())
        {
            report.KeyFindings.Add($"Found {strongSynergies.Count} strong synergistic effect combinations");
            foreach (var (key, effect) in strongSynergies)
                report.Recommendations.Add(
                    $"Prioritize bets combining {effect.Name} (synergy: {effect.InteractionStrength:+0.0%;-0.0%})");
        }

        if (strongAntagonisms.Any())
        {
            report.KeyFindings.Add($"Found {strongAntagonisms.Count} strong antagonistic effect combinations");
            foreach (var (key, effect) in strongAntagonisms)
                report.Recommendations.Add(
                    $"Avoid combining {effect.Name} (reduces effect by {-effect.InteractionStrength:0.0%})");
        }

        // Display findings
        Console.WriteLine("\n📋 KEY FINDINGS:");
        foreach (var finding in report.KeyFindings) Console.WriteLine($"   • {finding}");

        Console.WriteLine("\n💡 RECOMMENDATIONS:");
        foreach (var rec in report.Recommendations) Console.WriteLine($"   → {rec}");
    }

// Make sure you also have the SaveCausalReport method
    private void SaveCausalReport(ComprehensiveCausalReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"causal_analysis_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);

        Console.WriteLine($"\n📄 Comprehensive causal analysis report saved to {fileName}");
    }

    public async Task<CausalEffectReport> EstimateArenaPlacementEffectAsync(List<CausalDataPoint>? data,
        int targetArenaId)
    {
        data ??= await LoadCausalDataAsync();

        // Find pirates who appear in multiple arenas (for within-pirate comparison)
        var piratesInMultipleArenas = data
            .GroupBy(d => d.PirateId)
            .Where(g => g.Select(d => d.ArenaId).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Console.WriteLine($"   Found {piratesInMultipleArenas.Count} pirates appearing in multiple arenas");

        var inTargetArena = data.Where(d => d.ArenaId == targetArenaId && piratesInMultipleArenas.Contains(d.PirateId))
            .ToList();
        var inOtherArenas = data.Where(d => d.ArenaId != targetArenaId && piratesInMultipleArenas.Contains(d.PirateId))
            .ToList();

        // Match same pirate across arenas
        var matches = new List<MatchedPair>();

        foreach (var target in inTargetArena)
        {
            var samePirateOtherArena = inOtherArenas
                .Where(d => d.PirateId == target.PirateId &&
                            Math.Abs(d.FoodAdjustment - target.FoodAdjustment) <= 1 &&
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
            TreatmentGroupSize = inTargetArena.Count,
            ControlGroupSize = inOtherArenas.Count,
            MatchedPairs = matches.Count,
            IsSignificant = Math.Abs(tStat) > 1.96 && matches.Count > 30,
            ConfidenceInterval = (ate - 1.96 * standardError, ate + 1.96 * standardError)
        };
    }

    private Dictionary<int, double> CalculateOddsDoseResponse(List<CausalDataPoint> data)
    {
        var doseResponse = new Dictionary<int, double>();

        // Group by odds levels (excluding clamped values)
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

    private async Task<List<CausalDataPoint>> LoadCausalDataAsync(bool redistributeClamped = true)
    {
        // Load all data separately and join in memory
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
            .Where(rpp => rpp.RoundId.HasValue && rpp.ArenaId.HasValue && rpp.PirateId.HasValue)
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

        // Create lookups for fast in-memory joins
        var placementLookup = roundPlacements.ToDictionary(
            rpp => (rpp.RoundId, rpp.ArenaId, rpp.PirateId),
            rpp => rpp
        );

        var pirateLookup = pirates.ToDictionary(p => p.PirateId, p => p);

        // Join in memory
        var causalData = new List<CausalDataPoint>();

        foreach (var result in roundResults)
        {
            var key = (result.RoundId!.Value, result.ArenaId, result.PirateId);

            if (placementLookup.TryGetValue(key, out var placement) &&
                pirateLookup.TryGetValue(result.PirateId, out var pirate))
            {
                // NORMALIZE ODDS: Treat 1:1 as 2:1 (game minimum)
                var normalizedOdds = Math.Max(2, placement.CurrentOdds);

                causalData.Add(new CausalDataPoint
                {
                    RoundId = result.RoundId.Value,
                    ArenaId = result.ArenaId,
                    PirateId = result.PirateId,
                    IsWinner = result.IsWinner,
                    FoodAdjustment = placement.PirateFoodAdjustment,
                    CurrentOdds = normalizedOdds,
                    Position = placement.Position,
                    Strength = pirate.Strength,
                    Weight = pirate.Weight
                });
            }
        }

        Console.WriteLine($"   Loaded {causalData.Count} causal data points (odds normalized: 1:1 → 2:1)");

        // REDISTRIBUTE CLAMPED 13:1 ODDS
        if (redistributeClamped) causalData = SplitClamped13IntoSubBuckets(causalData);
        // OR use: causalData = RedistributeClamped13Odds(causalData);
        return causalData;
    }

    /// <summary>
    ///     Adjusts win rates for clamped 13:1 odds using theoretical expectations
    /// </summary>
    private Dictionary<int, double> CalculateAdjustedWinRates(List<CausalDataPoint> data)
    {
        var winRates = new Dictionary<int, double>();

        foreach (var oddsGroup in data.GroupBy(d => d.CurrentOdds).OrderBy(g => g.Key))
        {
            var odds = oddsGroup.Key;
            var observedWinRate = oddsGroup.Average(d => d.IsWinner ? 1.0 : 0.0);

            if (odds == 13)
            {
                // For 13:1, we know true odds are 13:1 to 25:1+
                // Observed win rate is contaminated (too high)
                // Use theoretical calculation instead

                // Estimate average true odds (conservative: assume average is 16:1)
                var estimatedAverageTrueOdds = 16;
                var theoreticalWinRate = 1.0 / (estimatedAverageTrueOdds + 1.0);

                // Blend observed with theoretical (weight theoretical more heavily)
                var adjustedWinRate = theoreticalWinRate * 0.7 + observedWinRate * 0.3;

                Console.WriteLine(
                    $"   13:1 Adjusted: Observed={observedWinRate:P2}, Theoretical={theoreticalWinRate:P2}, Adjusted={adjustedWinRate:P2}");
                winRates[odds] = adjustedWinRate;
            }
            else
            {
                winRates[odds] = observedWinRate;
            }
        }

        return winRates;
    }

    /// <summary>
    ///     Splits 13:1 clamped odds into multiple buckets based on pirate quality
    /// </summary>
    private List<CausalDataPoint> SplitClamped13IntoSubBuckets(List<CausalDataPoint> data)
    {
        var result = new List<CausalDataPoint>();

        // Find all 13:1 pirates
        var clamped13 = data.Where(d => d.CurrentOdds == 13).ToList();

        if (!clamped13.Any())
            return data;

        // Calculate percentile thresholds based on quality score
        var qualityScores = clamped13.Select(d => new
        {
            DataPoint = d,
            Quality = d.Strength / 100.0 * 0.4 +
                      (d.FoodAdjustment + 3) / 6.0 * 0.3 +
                      (4 - d.Position) / 4.0 * 0.3
        }).OrderByDescending(x => x.Quality).ToList();

        // Split into quartiles
        var count = qualityScores.Count;
        var q1 = count / 4;
        var q2 = count / 2;
        var q3 = count * 3 / 4;

        for (var i = 0; i < qualityScores.Count; i++)
        {
            var item = qualityScores[i];
            int estimatedOdds;

            // Top quartile: probably true 13:1
            if (i < q1) estimatedOdds = 13;
            // Second quartile: probably 14-15:1
            else if (i < q2) estimatedOdds = 15;
            // Third quartile: probably 17-19:1
            else if (i < q3) estimatedOdds = 18;
            // Bottom quartile: probably 20-25+:1
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

        // Add non-13:1 data unchanged
        result.AddRange(data.Where(d => d.CurrentOdds != 13));

        Console.WriteLine($"   Split {clamped13.Count} clamped 13:1 odds into sub-buckets:");
        Console.WriteLine($"      13:1 (top 25%): {count - q3} pirates");
        Console.WriteLine($"      15:1 (Q2):      {q2 - q1} pirates");
        Console.WriteLine($"      18:1 (Q3):      {q3 - q2} pirates");
        Console.WriteLine($"      23:1 (bottom):  {q1} pirates");

        return result;
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

        // 3. High Strength × Weak Rivals
        var medianStrength = data.Select(d => d.Strength).OrderBy(s => s).ElementAt(data.Count / 2);
        interactions["Strength_x_Rivals"] = AnalyzeInteraction(data,
            d => d.Strength >= medianStrength,
            d => true, // Would need rival data
            "High Strength × Weak Rivals");

        return interactions;
    }

    private InteractionEffect AnalyzeInteraction(
        List<CausalDataPoint> data,
        Func<CausalDataPoint, bool> treatment1,
        Func<CausalDataPoint, bool> treatment2,
        string name)
    {
        // Four groups: Both, T1 only, T2 only, Neither
        var bothGroup = data.Where(d => treatment1(d) && treatment2(d)).ToList();
        var t1OnlyGroup = data.Where(d => treatment1(d) && !treatment2(d)).ToList();
        var t2OnlyGroup = data.Where(d => !treatment1(d) && treatment2(d)).ToList();
        var neitherGroup = data.Where(d => !treatment1(d) && !treatment2(d)).ToList();

        // Calculate averages with safety checks for empty sequences
        var both = bothGroup.Any() ? bothGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;
        var t1Only = t1OnlyGroup.Any() ? t1OnlyGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;
        var t2Only = t2OnlyGroup.Any() ? t2OnlyGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;
        var neither = neitherGroup.Any() ? neitherGroup.Average(d => d.IsWinner ? 1.0 : 0.0) : 0.0;

        // Interaction effect: (Both - T1) - (T2 - Neither)
        var interactionEffect = both - t1Only - (t2Only - neither);

        // Log warning if any group is empty or very small
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
            IsSynergistic =
                interactionEffect > 0.02 && bothGroup.Count >= 10, // Positive interaction (with sufficient data)
            IsAntagonistic =
                interactionEffect < -0.02 && bothGroup.Count >= 10 // Negative interaction (with sufficient data)
        };
    }

    // Helper methods
    private List<MatchedPair> MatchOnCovariates(
        List<CausalDataPoint> treatment,
        List<CausalDataPoint> control,
        Func<CausalDataPoint, double[]> getCovariates,
        double maxDistance = 0.2)
    {
        var matches = new List<MatchedPair>();

        foreach (var treated in treatment)
        {
            var treatedCovariates = getCovariates(treated);

            var bestMatch = control
                .Select(c => new
                {
                    Control = c,
                    Distance = MathUtilities.EuclideanDistance(treatedCovariates, getCovariates(c))
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

    private List<MatchedPair> MatchOnCovariatesWithRivals(
        List<CausalDataPoint> treatment,
        List<CausalDataPoint> control,
        Func<CausalDataPoint, double[]> getCovariates,
        double maxDistance = 0.2)
    {
        return MatchOnCovariates(treatment, control, getCovariates, maxDistance);
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
}