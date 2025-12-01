using NFCBets.Services.Enums;
using NFCBets.Services.Models;

namespace NFCBets.Services
{
    public class BettingStrategyService : IBettingStrategyService
    {
        private const int MIN_BETS_REQUIRED = 10;
        
        public List<BetSeries> GenerateBetSeries(List<PiratePrediction> predictions)
        {
            return new List<BetSeries>
            {
                GenerateConservativeSeries(predictions),
                GenerateBalancedSeries(predictions),
                GenerateModerateSeries(predictions),
                GenerateAggressiveSeries(predictions),
                GenerateHighRiskSeries(predictions)
            };
        }

        private BetSeries GenerateConservativeSeries(List<PiratePrediction> predictions)
        {
            var bets = new List<Bet>();

            // Strategy: Only bet on pirates with >50% win probability
            var safePicks = predictions
                .Where(p => p.WinProbability > 0.5f)
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key,
                    g => new List<PiratePrediction> { g.OrderByDescending(p => p.WinProbability).First() });

            // Generate combinations with high confidence
            var combinations = GenerateBetCombinations(safePicks, 1, 5); 

            foreach (var combo in combinations.OrderByDescending(c => c.ExpectedValue).Take(10))
            {
                bets.Add(combo);
            }

            return new BetSeries
            {
                Name = "Conservative",
                RiskLevel = RiskLevel.Low,
                Bets = bets,
                Description = "High probability picks (>50% win chance)"
            };
        }

        private BetSeries GenerateBalancedSeries(List<PiratePrediction> predictions)
        {
            var bets = new List<Bet>();

            // Strategy: Mix of safe and moderate picks
            var picks = predictions
                .Where(p => p.WinProbability > 0.25f)
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.WinProbability).Take(1).ToList());

            var combinations = GenerateBetCombinations(picks, 1, 5);

            foreach (var combo in combinations.OrderByDescending(c => c.ExpectedValue).Take(10))
            {
                bets.Add(combo);
            }

            return new BetSeries
            {
                Name = "Balanced",
                RiskLevel = RiskLevel.Medium,
                Bets = bets,
                Description = "Mix of safe and moderate picks (>25% win chance)"
            };
        }

        private BetSeries GenerateModerateSeries(List<PiratePrediction> predictions)
        {
            var bets = new List<Bet>();

            var picks = predictions
                .Where(p => p.WinProbability > 0.15f)
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => CalculateEV(p)).Take(1).ToList());

            var combinations = GenerateBetCombinations(picks, 1, 5);

            foreach (var combo in combinations.OrderByDescending(c => c.ExpectedValue).Take(10))
            {
                bets.Add(combo);
            }

            return new BetSeries
            {
                Name = "Moderate",
                RiskLevel = RiskLevel.MediumHigh,
                Bets = bets,
                Description = "Higher payout potential (>15% win chance)"
            };
        }

        private BetSeries GenerateAggressiveSeries(List<PiratePrediction> predictions)
        {
            var bets = new List<Bet>();

            // Include all pirates with positive EV
            var picks = predictions
                .Where(p => CalculateEV(p) > 0)
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => CalculateEV(p)).ToList());

            var combinations = GenerateBetCombinations(picks, 1, 5); 

            foreach (var combo in combinations.OrderByDescending(c => c.ExpectedValue).Take(10))
            {
                bets.Add(combo);
            }

            return new BetSeries
            {
                Name = "Aggressive",
                RiskLevel = RiskLevel.High,
                Bets = bets,
                Description = "Maximum EV picks with positive expected value"
            };
        }

        private BetSeries GenerateHighRiskSeries(List<PiratePrediction> predictions)
        {
            var bets = new List<Bet>();

            // Go for maximum payout combinations
            var picks = predictions
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Payout).Take(1).ToList());

            var combinations = GenerateBetCombinations(picks, 1, 5); // Always 5 pirates (max payout)

            foreach (var combo in combinations.OrderByDescending(c => c.TotalPayout).Take(10))
            {
                bets.Add(combo);
            }

            return new BetSeries
            {
                Name = "High Risk / High Reward",
                RiskLevel = RiskLevel.VeryHigh,
                Bets = bets,
                Description = "Maximum payout combinations, all 5 arenas, highest odds"
            };
        }

        private double CalculateEV(PiratePrediction prediction)
        {
            return (prediction.WinProbability * prediction.Payout) - 1.0;
        }

        private List<Bet> GenerateBetCombinations(Dictionary<int, List<PiratePrediction>> picksPerArena, int minPirates,
            int maxPirates)
        {
            var allBets = new List<Bet>();
            var arenaIds = picksPerArena.Keys.OrderBy(x => x).ToList(); // Arena 0-4 (or 1-5)

            // Generate all valid combinations where:
            // - Each bet contains between minPirates and maxPirates
            // - Each arena contributes AT MOST 1 pirate to a bet
            // - A bet wins only if ALL selected pirates win their arenas

            GenerateCombinationsRecursive(
                picksPerArena,
                arenaIds,
                new List<PiratePrediction>(),
                0,
                minPirates,
                maxPirates,
                allBets
            );

            return allBets;
        }

        private void GenerateCombinationsRecursive(
            Dictionary<int, List<PiratePrediction>> picksPerArena,
            List<int> arenaIds,
            List<PiratePrediction> currentBet,
            int arenaIndex,
            int minPirates,
            int maxPirates,
            List<Bet> results)
        {
            // Base case: we've considered all arenas
            if (arenaIndex == arenaIds.Count)
            {
                // Only add bet if it has the right number of pirates
                if (currentBet.Count >= minPirates && currentBet.Count <= maxPirates)
                {
                    var bet = CreateBet(currentBet);
                    results.Add(bet);
                }

                return;
            }

            var currentArenaId = arenaIds[arenaIndex];

            // OPTION 1: Skip this arena entirely (don't pick a pirate from it)
            // Example: {Arena1: Pirate2, Arena3: Pirate5} skips Arena0, Arena2, Arena4
            GenerateCombinationsRecursive(picksPerArena, arenaIds, currentBet, arenaIndex + 1, minPirates, maxPirates,
                results);

            // OPTION 2: Pick ONE pirate from this arena
            // Example: Adding Arena2: Pirate6 to the current bet
            if (picksPerArena.TryGetValue(currentArenaId, out var piratesInArena))
            {
                foreach (var pirate in piratesInArena)
                {
                    // Add this pirate to the bet
                    currentBet.Add(pirate);

                    // Recurse to next arena
                    GenerateCombinationsRecursive(picksPerArena, arenaIds, currentBet, arenaIndex + 1, minPirates,
                        maxPirates, results);

                    // Backtrack (remove this pirate to try other combinations)
                    currentBet.RemoveAt(currentBet.Count - 1);
                }
            }
        }

        private Bet CreateBet(List<PiratePrediction> pirates)
        {
            // A bet wins if ALL selected pirates win their arenas
            // Combined probability = Product of individual probabilities
            var combinedProbability = pirates.Aggregate(1.0, (acc, p) => acc * p.WinProbability);

            // Total payout = Product of individual payouts
            var totalPayout = pirates.Aggregate(1, (acc, p) => acc * p.Payout);

            // Expected Value = (Probability × Payout) - Cost
            var expectedValue = (combinedProbability * totalPayout) - 1.0;

            return new Bet
            {
                Pirates = new List<PiratePrediction>(pirates),
                CombinedWinProbability = combinedProbability,
                TotalPayout = totalPayout,
                ExpectedValue = expectedValue,
                ArenasCovered = pirates.Select(p => p.ArenaId).ToList()
            };
        }

        private Dictionary<int, List<PiratePrediction>> PreFilterPirates(
            List<PiratePrediction> predictions,
            int maxPerArena = 3,
            float minProbability = 0.05f)
        {
            return predictions
                .Where(p => p.WinProbability > minProbability)
                .GroupBy(p => p.ArenaId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(p => CalculateEV(p))
                        .Take(maxPerArena) // Only keep top 3 pirates per arena
                        .ToList()
                );
        }

        // Optimization 2: Use greedy beam search instead of exhaustive
        private List<Bet> GenerateBetCombinationsBeamSearch(
            Dictionary<int, List<PiratePrediction>> picksPerArena,
            int minPirates,
            int maxPirates,
            int beamWidth = 100)
        {
            var arenaIds = picksPerArena.Keys.OrderBy(x => x).ToList();
            var currentBeam = new List<List<PiratePrediction>> { new List<PiratePrediction>() };

            foreach (var arenaId in arenaIds)
            {
                var nextBeam = new List<List<PiratePrediction>>();

                foreach (var currentBet in currentBeam)
                {
                    // Option 1: Skip this arena
                    if (currentBet.Count < maxPirates)
                    {
                        nextBeam.Add(new List<PiratePrediction>(currentBet));
                    }

                    // Option 2: Add each pirate from this arena
                    if (picksPerArena.TryGetValue(arenaId, out var pirates))
                    {
                        foreach (var pirate in pirates)
                        {
                            var newBet = new List<PiratePrediction>(currentBet) { pirate };
                            nextBeam.Add(newBet);
                        }
                    }
                }

                // Keep only top beamWidth candidates by EV
                currentBeam = nextBeam
                    .Where(bet => bet.Count <= maxPirates)
                    .Select(bet => new { Bet = bet, EV = CalculateBetEV(bet) })
                    .OrderByDescending(x => x.EV)
                    .Take(beamWidth)
                    .Select(x => x.Bet)
                    .ToList();
            }

            // Filter to valid bet sizes and create Bet objects
            return currentBeam
                .Where(bet => bet.Count >= minPirates && bet.Count <= maxPirates)
                .Select(CreateBet)
                .ToList();
        }

        private double CalculateBetEV(List<PiratePrediction> pirates)
        {
            if (!pirates.Any()) return double.MinValue;

            var combinedProbability = pirates.Aggregate(1.0, (acc, p) => acc * p.WinProbability);
            var totalPayout = pirates.Aggregate(1, (acc, p) => acc * p.Payout);
            return (combinedProbability * totalPayout) - 1.0;
        }

        // Optimization 3: Parallel processing for different series


    public List<BetSeries> GenerateBetSeriesParallel(List<PiratePrediction> predictions)
    {
        var seriesTasks = new[]
        {
            Task.Run(() => GenerateConservativeSeriesOptimized(predictions)),
            Task.Run(() => GenerateBalancedSeriesOptimized(predictions)),
            Task.Run(() => GenerateModerateSeriesOptimized(predictions)),
            Task.Run(() => GenerateAggressiveSeriesOptimized(predictions)),
            Task.Run(() => GenerateHighRiskSeriesOptimized(predictions))
        };

        Task.WaitAll(seriesTasks);

        var series = seriesTasks.Select(t => t.Result).ToList();

        // Validate each series has at least 10 unique bets
        foreach (var s in series)
        {
            if (s.Bets.Count < MIN_BETS_REQUIRED)
            {
                Console.WriteLine($"⚠️ Warning: {s.Name} only generated {s.Bets.Count} bets. Attempting to generate more...");
                s.Bets = EnsureMinimumBets(s.Bets, predictions, s.RiskLevel);
            }

            // Ensure bets are unique
            s.Bets = EnsureUniqueBets(s.Bets);
        }

        return series;
    }

    private List<Bet> EnsureMinimumBets(List<Bet> existingBets, List<PiratePrediction> predictions, RiskLevel riskLevel)
    {
        var bets = new List<Bet>(existingBets);
        var existingCombinations = new HashSet<string>(bets.Select(GetBetSignature));

        // Generate more permissive combinations until we have 10
        var allPicks = predictions
            .GroupBy(p => p.ArenaId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.WinProbability).Take(5).ToList());

        // Adjust parameters based on how many we need
        var needed = MIN_BETS_REQUIRED - bets.Count;
        var (minPirates, maxPirates) = riskLevel switch
        {
            RiskLevel.Low => (1, 3),
            RiskLevel.Medium => (1, 4),
            RiskLevel.MediumHigh => (2, 5),
            RiskLevel.High => (3, 5),
            RiskLevel.VeryHigh => (4, 5),
            _ => (1, 5)
        };

        var additionalBets = GenerateBetCombinationsBeamSearch(allPicks, minPirates, maxPirates, beamWidth: 500);

        foreach (var bet in additionalBets)
        {
            var signature = GetBetSignature(bet);
            if (!existingCombinations.Contains(signature))
            {
                bets.Add(bet);
                existingCombinations.Add(signature);

                if (bets.Count >= MIN_BETS_REQUIRED)
                    break;
            }
        }

        return bets;
    }

    private List<Bet> EnsureUniqueBets(List<Bet> bets)
    {
        var uniqueBets = new List<Bet>();
        var signatures = new HashSet<string>();

        foreach (var bet in bets)
        {
            var signature = GetBetSignature(bet);
            if (signatures.Add(signature))
            {
                uniqueBets.Add(bet);
            }
        }

        return uniqueBets;
    }

    private string GetBetSignature(Bet bet)
    {
        // Create a unique signature for the bet based on selected pirates
        return string.Join(",", bet.Pirates.OrderBy(p => p.ArenaId).Select(p => $"{p.ArenaId}:{p.PirateId}"));
    }

        // Optimized series generation
        private BetSeries GenerateConservativeSeriesOptimized(List<PiratePrediction> predictions)
        {
            var safePicks = PreFilterPirates(predictions, maxPerArena: 5, minProbability: 0.5f);
            var combinations = GenerateBetCombinationsBeamSearch(safePicks, 1, 5, beamWidth: 50);

            return new BetSeries
            {
                Name = "Conservative",
                RiskLevel = RiskLevel.Low,
                Bets = combinations.OrderByDescending(c => c.ExpectedValue).Take(10).ToList(),
                Description = "High probability picks (>50% win chance)"
            };
        }

        private BetSeries GenerateBalancedSeriesOptimized(List<PiratePrediction> predictions)
        {
            var picks = PreFilterPirates(predictions, maxPerArena: 5, minProbability: 0.25f);
            var combinations = GenerateBetCombinationsBeamSearch(picks, 1, 5, beamWidth: 100);

            return new BetSeries
            {
                Name = "Balanced",
                RiskLevel = RiskLevel.Medium,
                Bets = combinations.OrderByDescending(c => c.ExpectedValue).Take(10).ToList(),
                Description = "Mix of safe and moderate picks (>25% win chance)"
            };
        }

        private BetSeries GenerateModerateSeriesOptimized(List<PiratePrediction> predictions)
        {
            var picks = PreFilterPirates(predictions, maxPerArena: 5, minProbability: 0.15f);
            var combinations = GenerateBetCombinationsBeamSearch(picks, 1, 5, beamWidth: 150);

            return new BetSeries
            {
                Name = "Moderate",
                RiskLevel = RiskLevel.MediumHigh,
                Bets = combinations.OrderByDescending(c => c.ExpectedValue).Take(10).ToList(),
                Description = "Higher payout potential (>15% win chance)"
            };
        }

        private BetSeries GenerateAggressiveSeriesOptimized(List<PiratePrediction> predictions)
        {
            var picks = predictions
                .Where(p => CalculateEV(p) > 0)
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => CalculateEV(p)).Take(4).ToList());

            var combinations = GenerateBetCombinationsBeamSearch(picks, 1, 5, beamWidth: 200);

            return new BetSeries
            {
                Name = "Aggressive",
                RiskLevel = RiskLevel.High,
                Bets = combinations.OrderByDescending(c => c.ExpectedValue).Take(10).ToList(),
                Description = "Maximum EV picks with positive expected value"
            };
        }

        private BetSeries GenerateHighRiskSeriesOptimized(List<PiratePrediction> predictions)
        {
            var picks = predictions
                .GroupBy(p => p.ArenaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Payout).Take(3).ToList());

            var combinations = GenerateBetCombinationsBeamSearch(picks, 1, 5, beamWidth: 100);

            return new BetSeries
            {
                Name = "High Risk / High Reward",
                RiskLevel = RiskLevel.VeryHigh,
                Bets = combinations.OrderByDescending(c => c.TotalPayout).Take(10).ToList(),
                Description = "Maximum payout combinations, all 5 arenas, highest odds"
            };
        }
    }
}