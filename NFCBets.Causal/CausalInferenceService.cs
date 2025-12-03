using Microsoft.EntityFrameworkCore;
using NFCBets.Causal.Interfaces;
using NFCBets.Causal.Models;
using NFCBets.EF.Models;
using NFCBets.Utilities;

namespace NFCBets.Causal
{
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

            // 2. Seat Position Effect
            Console.WriteLine("\n2️⃣ Analyzing Seat Position Effect...");
            report.SeatPositionEffect = await EstimateSeatPositionEffectAsync(allData);
            DisplayEffect(report.SeatPositionEffect);

            // 3. Arena Effects (compare each arena)
            Console.WriteLine("\n3️⃣ Analyzing Arena Placement Effects...");
            report.ArenaEffects = new Dictionary<int, CausalEffectReport>();
            for (int arenaId = 0; arenaId < 5; arenaId++)
            {
                report.ArenaEffects[arenaId] = await EstimateArenaPlacementEffectAsync(allData, arenaId);
                Console.WriteLine($"   Arena {arenaId}: {report.ArenaEffects[arenaId].AverageTreatmentEffect:+0.0%;-0.0%}");
            }

            // 4. Rival Strength Effect
            Console.WriteLine("\n4️⃣ Analyzing Rival Strength Effect...");
            report.RivalStrengthEffect = await EstimateRivalStrengthEffectAsync(allData);
            DisplayEffect(report.RivalStrengthEffect);

            // 5. Odds Effect (being favorite vs underdog)
            Console.WriteLine("\n5️⃣ Analyzing Odds/Favorite Status Effect...");
            report.OddsEffect = await EstimateOddsEffectAsync(allData);
            DisplayEffect(report.OddsEffect);

            // 6. Interaction Effects
            Console.WriteLine("\n6️⃣ Analyzing Interaction Effects...");
            report.InteractionEffects = await AnalyzeInteractionEffectsAsync(allData);
            DisplayInteractionEffects(report.InteractionEffects);

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

            for (int position = 0; position < 4; position++)
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
                    maxDistance: 0.15);

                if (matches.Any())
                {
                    positionEffects[position] = matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average();
                }
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
            var standardError = MathUtilities.CalculateStandardError(overallMatches.Select(m => m.TreatedOutcome - m.ControlOutcome));
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

        public async Task<CausalEffectReport> EstimateArenaPlacementEffectAsync(List<CausalDataPoint>? data, int targetArenaId)
        {
            data ??= await LoadCausalDataAsync();

            // Find pirates who appear in multiple arenas (for within-pirate comparison)
            var piratesInMultipleArenas = data
                .GroupBy(d => d.PirateId)
                .Where(g => g.Select(d => d.ArenaId).Distinct().Count() > 1)
                .Select(g => g.Key)
                .ToList();

            Console.WriteLine($"   Found {piratesInMultipleArenas.Count} pirates appearing in multiple arenas");

            var inTargetArena = data.Where(d => d.ArenaId == targetArenaId && piratesInMultipleArenas.Contains(d.PirateId)).ToList();
            var inOtherArenas = data.Where(d => d.ArenaId != targetArenaId && piratesInMultipleArenas.Contains(d.PirateId)).ToList();

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
                {
                    matches.Add(new MatchedPair
                    {
                        TreatedOutcome = target.IsWinner ? 1.0 : 0.0,
                        ControlOutcome = samePirateOtherArena.IsWinner ? 1.0 : 0.0,
                        PropensityScore = 0.5
                    });
                }
            }

            var ate = matches.Any() ? matches.Select(m => m.TreatedOutcome - m.ControlOutcome).Average() : 0;
            var standardError = matches.Any() ? MathUtilities.CalculateStandardError(matches.Select(m => m.TreatedOutcome - m.ControlOutcome)) : 0;
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
            var medianRivalStrength = dataWithRivalStrength.Select(d => d.AvgRivalStrength).OrderBy(s => s).ElementAt(dataWithRivalStrength.Count / 2);

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
            var standardError = MathUtilities.CalculateStandardError(matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
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

            // Treatment: Being the favorite (odds = 2) vs non-favorite (odds > 2)
            var favorites = data.Where(d => d.CurrentOdds <= 2).ToList();
            var nonFavorites = data.Where(d => d.CurrentOdds > 2).ToList();

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
            var standardError = MathUtilities.CalculateStandardError(matches.Select(m => m.TreatedOutcome - m.ControlOutcome));
            var tStat = ate / standardError;

            // Also calculate dose-response: effect at different odds levels
            var doseResponse = CalculateOddsDoseResponse(data);

            return new CausalEffectReport
            {
                TreatmentName = "Being Favorite (Odds ≤2 vs >2)",
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


        private async Task<List<CausalDataPoint>> LoadCausalDataAsync()
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
    }

    Console.WriteLine($"   Loaded {causalData.Count} causal data points");

    return causalData;
}

        private Dictionary<int, double> CalculateOddsDoseResponse(List<CausalDataPoint> data)
        {
            var doseResponse = new Dictionary<int, double>();

            // Group by odds levels
            var oddsLevels = new[] { 2, 3, 4, 5, 7, 10, 13 };

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
                treatment1: d => d.FoodAdjustment >= 1,
                treatment2: d => d.Position <= 1,
                name: "Positive Food × Front Position");

            // 2. Food Adjustment × Being Favorite
            interactions["FoodAdj_x_Favorite"] = AnalyzeInteraction(data,
                treatment1: d => d.FoodAdjustment >= 1,
                treatment2: d => d.CurrentOdds <= 2,
                name: "Positive Food × Favorite Status");

            // 3. High Strength × Weak Rivals
            var medianStrength = data.Select(d => d.Strength).OrderBy(s => s).ElementAt(data.Count / 2);
            interactions["Strength_x_Rivals"] = AnalyzeInteraction(data,
                treatment1: d => d.Strength >= medianStrength,
                treatment2: d => true, // Would need rival data
                name: "High Strength × Weak Rivals");

            return interactions;
        }

        private InteractionEffect AnalyzeInteraction(
            List<CausalDataPoint> data,
            Func<CausalDataPoint, bool> treatment1,
            Func<CausalDataPoint, bool> treatment2,
            string name)
        {
            // Four groups: Both, T1 only, T2 only, Neither
            var both = data.Where(d => treatment1(d) && treatment2(d)).Average(d => d.IsWinner ? 1.0 : 0.0);
            var t1Only = data.Where(d => treatment1(d) && !treatment2(d)).Average(d => d.IsWinner ? 1.0 : 0.0);
            var t2Only = data.Where(d => !treatment1(d) && treatment2(d)).Average(d => d.IsWinner ? 1.0 : 0.0);
            var neither = data.Where(d => !treatment1(d) && !treatment2(d)).Average(d => d.IsWinner ? 1.0 : 0.0);

            // Interaction effect: (Both - T1) - (T2 - Neither)
            var interactionEffect = (both - t1Only) - (t2Only - neither);

            return new InteractionEffect
            {
                Name = name,
                InteractionStrength = interactionEffect,
                BothTreatments = both,
                Treatment1Only = t1Only,
                Treatment2Only = t2Only,
                Neither = neither,
                IsSynergistic = interactionEffect > 0.02, // Positive interaction
                IsAntagonistic = interactionEffect < -0.02 // Negative interaction
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
                {
                    matches.Add(new MatchedPair
                    {
                        TreatedOutcome = treated.IsWinner ? 1.0 : 0.0,
                        ControlOutcome = bestMatch.Control.IsWinner ? 1.0 : 0.0,
                        PropensityScore = 1.0 - bestMatch.Distance,
                        Distance = bestMatch.Distance
                    });
                }
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
            Console.WriteLine($"      95% CI: [{effect.ConfidenceInterval.Lower:+0.0%;-0.0%}, {effect.ConfidenceInterval.Upper:+0.0%;-0.0%}]");
            Console.WriteLine($"      Sample: {effect.TreatmentGroupSize} treated, {effect.ControlGroupSize} control, {effect.MatchedPairs} matched pairs");

            if (effect.DoseResponse != null && effect.DoseResponse.Any())
            {
                Console.WriteLine($"      Dose-Response:");
                foreach (var (dose, response) in effect.DoseResponse.OrderBy(kv => kv.Key))
                {
                    Console.WriteLine($"         Odds {dose}:1 → {response:P2} win rate");
                }
            }

            if (effect.PositionEffects != null && effect.PositionEffects.Any())
            {
                Console.WriteLine($"      By Position:");
                foreach (var (position, posEffect) in effect.PositionEffects.OrderBy(kv => kv.Key))
                {
                    Console.WriteLine($"         Position {position}: {posEffect:+0.0%;-0.0%} effect");
                }
            }
        }

        private void DisplayInteractionEffects(Dictionary<string, InteractionEffect> interactions)
        {
            foreach (var (key, interaction) in interactions)
            {
                Console.WriteLine($"   {interaction.Name}:");
                Console.WriteLine($"      Interaction Strength: {interaction.InteractionStrength:+0.0%;-0.0%}");
                
                if (interaction.IsSynergistic)
                    Console.WriteLine($"      🔵 Synergistic: Combining treatments is better than sum of parts");
                else if (interaction.IsAntagonistic)
                    Console.WriteLine($"      🔴 Antagonistic: Treatments interfere with each other");
                else
                    Console.WriteLine($"      ⚪ Additive: Effects are independent");

                Console.WriteLine($"      Win Rates:");
                Console.WriteLine($"         Both treatments: {interaction.BothTreatments:P2}");
                Console.WriteLine($"         Treatment 1 only: {interaction.Treatment1Only:P2}");
                Console.WriteLine($"         Treatment 2 only: {interaction.Treatment2Only:P2}");
                Console.WriteLine($"         Neither: {interaction.Neither:P2}");
            }
        }

        private void SaveCausalReport(ComprehensiveCausalReport report)
        {
            Directory.CreateDirectory("Reports");
            var fileName = Path.Combine("Reports", $"causal_analysis_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
            
            var json = System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, json);
            
            Console.WriteLine($"\n📄 Causal analysis report saved to {fileName}");
        }
    }

    // Data classes
}