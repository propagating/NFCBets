using NFCBets.Causal.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Causal;

public class InteractionEffectAnalyzer
{
    public async Task<InteractionAnalysisReport> AnalyzeAllInteractionsAsync(List<PirateFeatureRecord> data)
    {
        Console.WriteLine("\n🔬 Running Interaction Effect Analysis...\n");

        var report = new InteractionAnalysisReport
        {
            AnalysisDate = DateTime.UtcNow,
            TotalRecords = data.Count
        };

        // Original two-way interactions
        var twoWayInteractions = new List<InteractionAnalysisEffect>
        {
            AnalyzeFoodPositionInteraction(data),
            AnalyzeFoodFavoriteInteraction(data),
            AnalyzeStrengthPositionInteraction(data),
            AnalyzeStrengthWeakRivalsInteraction(data),
            AnalyzeFavoriteInexperiencedInteraction(data),
            AnalyzeLowStrengthFavoriteInteraction(data),
            AnalyzeUndervaluedStrongInteraction(data),
            AnalyzeArenaSpecialistModerateOddsInteraction(data),
            AnalyzeHotStreakBeatsRivalsInteraction(data),
            AnalyzeFoodPosition3Interaction(data)
        };

        // Three-way interactions
        var threeWayInteractions = new List<InteractionAnalysisEffect>
        {
            AnalyzeFoodPositionStrengthInteraction(data),
            AnalyzeUndervaluedStrongBeatsRivalsInteraction(data)
        };

        // NEW: Arena-specific analyses
        var arenaInteractions = AnalyzeArenaSpecificEffects(data);

        // Combine all interactions
        report.Interactions = twoWayInteractions
            .Concat(threeWayInteractions)
            .Concat(arenaInteractions)
            .Where(i => i != null)
            .ToList()!;

        // Classify interactions
        foreach (var interaction in report.Interactions)
        {
            if (interaction.IsAntagonistic && interaction.IsSignificant)
                report.AntagonisticInteractions.Add(interaction);
            else if (interaction.IsSynergistic && interaction.IsSignificant)
                report.SynergisticInteractions.Add(interaction);
            else
                report.NeutralInteractions.Add(interaction);
        }

        // Display results
        DisplayInteractionResults(report);
        DisplayArenaAnalysis(data, arenaInteractions);

        return report;
    }

    #region Arena-Specific Analysis

    private List<InteractionAnalysisEffect> AnalyzeArenaSpecificEffects(List<PirateFeatureRecord> data)
    {
        var effects = new List<InteractionAnalysisEffect>();

        Console.WriteLine("   Analyzing arena-specific effects...");

        // 1. Arena-Strength interaction (do strong pirates do better in certain arenas?)
        effects.Add(AnalyzeArenaStrengthInteraction(data));

        // 2. Arena-Position interaction (does position matter more in certain arenas?)
        effects.Add(AnalyzeArenaPositionInteraction(data));

        // 3. Arena-Food interaction (does food matter more in certain arenas?)
        effects.Add(AnalyzeArenaFoodInteraction(data));

        // 4. Arena specialist effect (pirates who consistently outperform in specific arenas)
        effects.AddRange(AnalyzeArenaSpecialists(data));

        // 5. Arena-Favorite interaction (do favorites perform differently by arena?)
        effects.Add(AnalyzeArenaFavoriteInteraction(data));

        return effects.Where(e => e != null).ToList()!;
    }

    private InteractionAnalysisEffect AnalyzeArenaStrengthInteraction(List<PirateFeatureRecord> data)
    {
        // Hypothesis: Strong pirates (top 25% strength) perform differently across arenas
        var strongThreshold = data.Select(d => d.Strength).OrderByDescending(s => s).Skip(data.Count / 4).First();

        var arenaStrengthWinRates = new Dictionary<int, (double StrongWinRate, double WeakWinRate, int StrongCount, int WeakCount)>();

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = data.Where(d => d.ArenaId == arenaId && d.IsWinner.HasValue).ToList();
            
            var strongPirates = arenaData.Where(d => d.Strength >= strongThreshold).ToList();
            var weakPirates = arenaData.Where(d => d.Strength < strongThreshold).ToList();

            var strongWinRate = strongPirates.Any() ? strongPirates.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0.25;
            var weakWinRate = weakPirates.Any() ? weakPirates.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0.25;

            arenaStrengthWinRates[arenaId] = (strongWinRate, weakWinRate, strongPirates.Count, weakPirates.Count);
        }

        // Find arena with biggest strength advantage difference
        var maxDiffArena = arenaStrengthWinRates
            .OrderByDescending(kvp => kvp.Value.StrongWinRate - kvp.Value.WeakWinRate)
            .First();

        var minDiffArena = arenaStrengthWinRates
            .OrderBy(kvp => kvp.Value.StrongWinRate - kvp.Value.WeakWinRate)
            .First();

        var avgStrongWinRate = arenaStrengthWinRates.Values.Average(v => v.StrongWinRate);
        var avgWeakWinRate = arenaStrengthWinRates.Values.Average(v => v.WeakWinRate);
        var variance = arenaStrengthWinRates.Values
            .Select(v => v.StrongWinRate - v.WeakWinRate)
            .Select(diff => Math.Pow(diff - (avgStrongWinRate - avgWeakWinRate), 2))
            .Average();

        return new InteractionAnalysisEffect
        {
            Name = "Arena-Strength Interaction",
            Description = $"Strength advantage varies by arena. Arena {maxDiffArena.Key} favors strong pirates most ({maxDiffArena.Value.StrongWinRate - maxDiffArena.Value.WeakWinRate:P1} advantage), Arena {minDiffArena.Key} least ({minDiffArena.Value.StrongWinRate - minDiffArena.Value.WeakWinRate:P1})",
            InteractionStrength = variance,
            Effect1Alone = avgStrongWinRate,
            Effect2Alone = avgWeakWinRate,
            CombinedEffect = maxDiffArena.Value.StrongWinRate,
            IsSignificant = variance > 0.001,
            IsSynergistic = maxDiffArena.Value.StrongWinRate > avgStrongWinRate,
            IsAntagonistic = false
        };
    }

    private InteractionAnalysisEffect AnalyzeArenaPositionInteraction(List<PirateFeatureRecord> data)
    {
        // Analyze position effect by arena
        var arenaPositionWinRates = new Dictionary<int, Dictionary<int, double>>();

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            arenaPositionWinRates[arenaId] = new Dictionary<int, double>();
            var arenaData = data.Where(d => d.ArenaId == arenaId && d.IsWinner.HasValue).ToList();

            for (int pos = 0; pos < 4; pos++)
            {
                var posData = arenaData.Where(d => d.Position == pos).ToList();
                arenaPositionWinRates[arenaId][pos] = posData.Any() 
                    ? posData.Average(d => d.IsWinner == true ? 1.0 : 0.0) 
                    : 0.25;
            }
        }

        // Find which arena has the most position-dependent results
        var positionVariances = arenaPositionWinRates.ToDictionary(
            kvp => kvp.Key,
            kvp => {
                var avg = kvp.Value.Values.Average();
                return kvp.Value.Values.Select(v => Math.Pow(v - avg, 2)).Average();
            });

        var highestVarianceArena = positionVariances.OrderByDescending(kvp => kvp.Value).First();
        var lowestVarianceArena = positionVariances.OrderBy(kvp => kvp.Value).First();

        var bestPositionInHighVariance = arenaPositionWinRates[highestVarianceArena.Key]
            .OrderByDescending(kvp => kvp.Value)
            .First();

        return new InteractionAnalysisEffect
        {
            Name = "Arena-Position Interaction",
            Description = $"Position matters most in Arena {highestVarianceArena.Key} (variance: {highestVarianceArena.Value:F4}). Best position: {bestPositionInHighVariance.Key} ({bestPositionInHighVariance.Value:P1}). Least position-dependent: Arena {lowestVarianceArena.Key}",
            InteractionStrength = highestVarianceArena.Value - lowestVarianceArena.Value,
            Effect1Alone = 0.25,
            Effect2Alone = 0.25,
            CombinedEffect = bestPositionInHighVariance.Value,
            IsSignificant = highestVarianceArena.Value > 0.005,
            IsSynergistic = bestPositionInHighVariance.Value > 0.30,
            IsAntagonistic = false
        };
    }

    private InteractionAnalysisEffect AnalyzeArenaFoodInteraction(List<PirateFeatureRecord> data)
    {
        // Analyze food effect by arena
        var positiveFood = data.Where(d => d.FoodAdjustment > 0 && d.IsWinner.HasValue).ToList();
        var negativeFood = data.Where(d => d.FoodAdjustment < 0 && d.IsWinner.HasValue).ToList();

        var arenaFoodEffects = new Dictionary<int, double>();

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaPositiveFood = positiveFood.Where(d => d.ArenaId == arenaId).ToList();
            var arenaNegativeFood = negativeFood.Where(d => d.ArenaId == arenaId).ToList();

            var positiveWinRate = arenaPositiveFood.Any() 
                ? arenaPositiveFood.Average(d => d.IsWinner == true ? 1.0 : 0.0) 
                : 0.25;
            var negativeWinRate = arenaNegativeFood.Any() 
                ? arenaNegativeFood.Average(d => d.IsWinner == true ? 1.0 : 0.0) 
                : 0.25;

            arenaFoodEffects[arenaId] = positiveWinRate - negativeWinRate;
        }

        var maxFoodEffectArena = arenaFoodEffects.OrderByDescending(kvp => kvp.Value).First();
        var minFoodEffectArena = arenaFoodEffects.OrderBy(kvp => kvp.Value).First();
        var avgFoodEffect = arenaFoodEffects.Values.Average();

        return new InteractionAnalysisEffect
        {
            Name = "Arena-Food Interaction",
            Description = $"Food effect varies by arena. Strongest in Arena {maxFoodEffectArena.Key} ({maxFoodEffectArena.Value:P1}), weakest in Arena {minFoodEffectArena.Key} ({minFoodEffectArena.Value:P1})",
            InteractionStrength = maxFoodEffectArena.Value - minFoodEffectArena.Value,
            Effect1Alone = avgFoodEffect,
            CombinedEffect = maxFoodEffectArena.Value,
            IsSignificant = Math.Abs(maxFoodEffectArena.Value - minFoodEffectArena.Value) > 0.05,
            IsSynergistic = maxFoodEffectArena.Value > avgFoodEffect * 1.2,
            IsAntagonistic = minFoodEffectArena.Value < avgFoodEffect * 0.5
        };
    }

    private List<InteractionAnalysisEffect> AnalyzeArenaSpecialists(List<PirateFeatureRecord> data)
    {
        var effects = new List<InteractionAnalysisEffect>();

        // Find pirates with significantly different win rates in specific arenas
        var piratesWithEnoughData = data
            .GroupBy(d => d.PirateId)
            .Where(g => g.Count() >= 20)  // Need enough data
            .Select(g => g.Key)
            .ToList();

        var arenaSpecialists = new List<(int PirateId, int ArenaId, double ArenaWinRate, double OverallWinRate, double Advantage, int ArenaAppearances)>();

        foreach (var pirateId in piratesWithEnoughData)
        {
            var pirateData = data.Where(d => d.PirateId == pirateId && d.IsWinner.HasValue).ToList();
            var overallWinRate = pirateData.Average(d => d.IsWinner == true ? 1.0 : 0.0);

            for (int arenaId = 1; arenaId <= 5; arenaId++)
            {
                var arenaData = pirateData.Where(d => d.ArenaId == arenaId).ToList();
                if (arenaData.Count < 5) continue;  // Need minimum appearances

                var arenaWinRate = arenaData.Average(d => d.IsWinner == true ? 1.0 : 0.0);
                var advantage = arenaWinRate - overallWinRate;

                if (Math.Abs(advantage) > 0.10)  // 10% difference threshold
                {
                    arenaSpecialists.Add((pirateId, arenaId, arenaWinRate, overallWinRate, advantage, arenaData.Count));
                }
            }
        }

        // Report top arena specialists
        var topSpecialists = arenaSpecialists
            .OrderByDescending(s => s.Advantage)
            .Take(5)
            .ToList();

        var worstArenas = arenaSpecialists
            .OrderBy(s => s.Advantage)
            .Take(5)
            .ToList();

        if (topSpecialists.Any())
        {
            var best = topSpecialists.First();
            effects.Add(new InteractionAnalysisEffect
            {
                Name = "Arena Specialists (Positive)",
                Description = $"Found {topSpecialists.Count} pirates who significantly outperform in specific arenas. Best: Pirate {best.PirateId} in Arena {best.ArenaId} ({best.ArenaWinRate:P1} vs {best.OverallWinRate:P1} overall, +{best.Advantage:P1})",
                InteractionStrength = topSpecialists.Average(s => s.Advantage),
                Effect1Alone = topSpecialists.Average(s => s.OverallWinRate),
                CombinedEffect = topSpecialists.Average(s => s.ArenaWinRate),
                IsSignificant = true,
                IsSynergistic = true,
                IsAntagonistic = false,
                Group11Count = topSpecialists.Sum(s => s.ArenaAppearances)
            });
        }

        if (worstArenas.Any() && worstArenas.First().Advantage < -0.05)
        {
            var worst = worstArenas.First();
            effects.Add(new InteractionAnalysisEffect
            {
                Name = "Arena Specialists (Negative)",
                Description = $"Found {worstArenas.Count(w => w.Advantage < -0.05)} pirates who significantly underperform in specific arenas. Worst: Pirate {worst.PirateId} in Arena {worst.ArenaId} ({worst.ArenaWinRate:P1} vs {worst.OverallWinRate:P1} overall, {worst.Advantage:P1})",
                InteractionStrength = Math.Abs(worstArenas.Where(w => w.Advantage < -0.05).Average(s => s.Advantage)),
                Effect1Alone = worstArenas.Average(s => s.OverallWinRate),
                CombinedEffect = worstArenas.Average(s => s.ArenaWinRate),
                IsSignificant = true,
                IsSynergistic = false,
                IsAntagonistic = true,
                Group11Count = worstArenas.Sum(s => s.ArenaAppearances)
            });
        }

        return effects;
    }

    private InteractionAnalysisEffect AnalyzeArenaFavoriteInteraction(List<PirateFeatureRecord> data)
    {
        // Do favorites (lowest odds) perform differently by arena?
        var arenaFavoritePerformance = new Dictionary<int, (double FavoriteWinRate, double FavoriteByOddsWinRate, int Count)>();

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = data.Where(d => d.ArenaId == arenaId && d.IsWinner.HasValue).ToList();
            
            // Group by round to find favorites
            var rounds = arenaData.GroupBy(d => d.RoundId);
            var favoriteResults = new List<bool>();

            foreach (var round in rounds)
            {
                var favorite = round.OrderBy(p => p.CurrentOdds).First();
                if (favorite.IsWinner.HasValue)
                {
                    favoriteResults.Add(favorite.IsWinner.Value);
                }
            }

            var favoriteWinRate = favoriteResults.Any() 
                ? favoriteResults.Average(w => w ? 1.0 : 0.0) 
                : 0.25;

            arenaFavoritePerformance[arenaId] = (favoriteWinRate, favoriteWinRate, favoriteResults.Count);
        }

        var bestArenaForFavorites = arenaFavoritePerformance.OrderByDescending(kvp => kvp.Value.FavoriteWinRate).First();
        var worstArenaForFavorites = arenaFavoritePerformance.OrderBy(kvp => kvp.Value.FavoriteWinRate).First();
        var avgFavoriteWinRate = arenaFavoritePerformance.Values.Average(v => v.FavoriteWinRate);

        return new InteractionAnalysisEffect
        {
            Name = "Arena-Favorite Interaction",
            Description = $"Favorites perform best in Arena {bestArenaForFavorites.Key} ({bestArenaForFavorites.Value.FavoriteWinRate:P1}), worst in Arena {worstArenaForFavorites.Key} ({worstArenaForFavorites.Value.FavoriteWinRate:P1}). Avg: {avgFavoriteWinRate:P1}",
            InteractionStrength = bestArenaForFavorites.Value.FavoriteWinRate - worstArenaForFavorites.Value.FavoriteWinRate,
            Effect1Alone = avgFavoriteWinRate,
            CombinedEffect = bestArenaForFavorites.Value.FavoriteWinRate,
            IsSignificant = Math.Abs(bestArenaForFavorites.Value.FavoriteWinRate - worstArenaForFavorites.Value.FavoriteWinRate) > 0.08,
            IsSynergistic = bestArenaForFavorites.Value.FavoriteWinRate > avgFavoriteWinRate * 1.1,
            IsAntagonistic = worstArenaForFavorites.Value.FavoriteWinRate < avgFavoriteWinRate * 0.9
        };
    }

    #endregion

    #region Display Methods

    private void DisplayArenaAnalysis(List<PirateFeatureRecord> data, List<InteractionAnalysisEffect> arenaEffects)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🏟️ ARENA-SPECIFIC ANALYSIS");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        // Per-arena summary
        Console.WriteLine("📊 Win Rate by Arena:");
        Console.WriteLine($"   {"Arena",-8} {"Win Rate",-12} {"Favorite Win%",-15} {"Avg Strength",-15} {"Rounds"}");
        Console.WriteLine($"   {new string('─', 60)}");

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = data.Where(d => d.ArenaId == arenaId && d.IsWinner.HasValue).ToList();
            var rounds = arenaData.Select(d => d.RoundId).Distinct().Count();
            var avgStrength = arenaData.Any() ? arenaData.Average(d => d.Strength) : 0;
            
            // Calculate favorite win rate
            var favoriteWins = 0;
            var totalRounds = 0;
            foreach (var round in arenaData.GroupBy(d => d.RoundId))
            {
                var favorite = round.OrderBy(p => p.CurrentOdds).First();
                if (favorite.IsWinner == true) favoriteWins++;
                totalRounds++;
            }
            var favoriteWinRate = totalRounds > 0 ? (double)favoriteWins / totalRounds : 0;

            // Overall win rate (should be ~25% for random)
            var winRate = arenaData.Any() ? arenaData.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

            Console.WriteLine($"   Arena {arenaId,-3} {winRate:P1,-12} {favoriteWinRate:P1,-15} {avgStrength:F1,-15} {rounds}");
        }

        Console.WriteLine();

        // Position analysis by arena
        Console.WriteLine("📊 Position Win Rates by Arena:");
        Console.WriteLine($"   {"Arena",-8} {"Pos 0",-10} {"Pos 1",-10} {"Pos 2",-10} {"Pos 3",-10} {"Best Pos"}");
        Console.WriteLine($"   {new string('─', 60)}");

        for (int arenaId = 1; arenaId <= 5; arenaId++)
        {
            var arenaData = data.Where(d => d.ArenaId == arenaId && d.IsWinner.HasValue).ToList();
            var posWinRates = new double[4];
            
            for (int pos = 0; pos < 4; pos++)
            {
                var posData = arenaData.Where(d => d.Position == pos).ToList();
                posWinRates[pos] = posData.Any() ? posData.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0.25;
            }

            var bestPos = Array.IndexOf(posWinRates, posWinRates.Max());

            Console.WriteLine($"   Arena {arenaId,-3} {posWinRates[0]:P1,-10} {posWinRates[1]:P1,-10} {posWinRates[2]:P1,-10} {posWinRates[3]:P1,-10} {bestPos}");
        }

        Console.WriteLine();

        // Display arena-specific interaction effects
        Console.WriteLine("📊 Arena Interaction Effects:");
        foreach (var effect in arenaEffects.Where(e => e != null))
        {
            var symbol = effect.IsSynergistic ? "🟢" : (effect.IsAntagonistic ? "🔴" : "⚪");
            var sigMarker = effect.IsSignificant ? "***" : "";
            Console.WriteLine($"   {symbol} {effect.Name} {sigMarker}");
            Console.WriteLine($"      {effect.Description}");
            Console.WriteLine($"      Strength: {effect.InteractionStrength:F4}");
            Console.WriteLine();
        }
    }

    private void DisplayInteractionResults(InteractionAnalysisReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🔬 INTERACTION EFFECT ANALYSIS RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine($"Total records analyzed: {report.TotalRecords:N0}");
        Console.WriteLine($"Total interactions found: {report.Interactions.Count}");
        Console.WriteLine($"   🔴 Antagonistic (significant): {report.AntagonisticInteractions.Count}");
        Console.WriteLine($"   🟢 Synergistic (significant): {report.SynergisticInteractions.Count}");
        Console.WriteLine($"   ⚪ Neutral/Non-significant: {report.NeutralInteractions.Count}");
        Console.WriteLine();

        if (report.AntagonisticInteractions.Any())
        {
            Console.WriteLine("🔴 ANTAGONISTIC INTERACTIONS (Combined effect < expected):");
            foreach (var effect in report.AntagonisticInteractions.OrderByDescending(e => Math.Abs(e.InteractionStrength)))
            {
                Console.WriteLine($"   • {effect.Name}");
                Console.WriteLine($"     {effect.Description}");
                Console.WriteLine($"     Strength: {effect.InteractionStrength:F4}, Effect1: {effect.Effect1Alone:P1}, Effect2: {effect.Effect2Alone:P1}, Combined: {effect.CombinedEffect:P1}");
                Console.WriteLine();
            }
        }

        if (report.SynergisticInteractions.Any())
        {
            Console.WriteLine("🟢 SYNERGISTIC INTERACTIONS (Combined effect > expected):");
            foreach (var effect in report.SynergisticInteractions.OrderByDescending(e => e.InteractionStrength))
            {
                Console.WriteLine($"   • {effect.Name}");
                Console.WriteLine($"     {effect.Description}");
                Console.WriteLine($"     Strength: {effect.InteractionStrength:F4}, Effect1: {effect.Effect1Alone:P1}, Effect2: {effect.Effect2Alone:P1}, Combined: {effect.CombinedEffect:P1}");
                Console.WriteLine();
            }
        }
    }

    #endregion

    #region Original Two-Way Interactions

    private InteractionAnalysisEffect AnalyzeFoodPositionInteraction(List<PirateFeatureRecord> data)
    {
        // High food + bad position interaction
        var hasHighFood = data.Where(d => d.FoodAdjustment > 0);
        var hasBadPosition = data.Where(d => d.Position >= 2);
        
        var group00 = data.Where(d => d.FoodAdjustment <= 0 && d.Position < 2 && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.FoodAdjustment > 0 && d.Position < 2 && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.FoodAdjustment <= 0 && d.Position >= 2 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.FoodAdjustment > 0 && d.Position >= 2 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Food-Position Interaction",
            Description = "High food bonus combined with bad position (2-3)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 100,
            IsAntagonistic = interactionStrength < -0.02,
IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeFoodFavoriteInteraction(List<PirateFeatureRecord> data)
    {
        // High food + favorite status interaction
        var group00 = data.Where(d => d.FoodAdjustment <= 0 && d.CurrentOdds > 3 && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.FoodAdjustment > 0 && d.CurrentOdds > 3 && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.FoodAdjustment <= 0 && d.CurrentOdds <= 3 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.FoodAdjustment > 0 && d.CurrentOdds <= 3 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Food-Favorite Interaction",
            Description = "High food bonus combined with favorite status (odds <= 3)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 100,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeStrengthPositionInteraction(List<PirateFeatureRecord> data)
    {
        var strengthThreshold = data.Select(d => d.Strength).OrderByDescending(s => s).Skip(data.Count / 4).First();

        var group00 = data.Where(d => d.Strength < strengthThreshold && d.Position < 2 && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.Strength >= strengthThreshold && d.Position < 2 && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.Strength < strengthThreshold && d.Position >= 2 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.Strength >= strengthThreshold && d.Position >= 2 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Strength-Position Interaction",
            Description = "High strength combined with bad position (2-3)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 100,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeStrengthWeakRivalsInteraction(List<PirateFeatureRecord> data)
    {
        var strengthThreshold = data.Select(d => d.Strength).OrderByDescending(s => s).Skip(data.Count / 4).First();
        var avgRivalStrengthMedian = data.Select(d => d.AvgRivalStrength).OrderBy(s => s).Skip(data.Count / 2).First();

        var group00 = data.Where(d => d.Strength < strengthThreshold && d.AvgRivalStrength >= avgRivalStrengthMedian && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.Strength >= strengthThreshold && d.AvgRivalStrength >= avgRivalStrengthMedian && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.Strength < strengthThreshold && d.AvgRivalStrength < avgRivalStrengthMedian && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.Strength >= strengthThreshold && d.AvgRivalStrength < avgRivalStrengthMedian && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Strength-WeakRivals Interaction",
            Description = "High strength combined with weak rivals (below median)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 100,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeFavoriteInexperiencedInteraction(List<PirateFeatureRecord> data)
    {
        var histWinRateMedian = data.Select(d => d.HistoricalWinRate).OrderBy(s => s).Skip(data.Count / 2).First();

        var group00 = data.Where(d => d.CurrentOdds > 3 && d.HistoricalWinRate >= histWinRateMedian && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.CurrentOdds <= 3 && d.HistoricalWinRate >= histWinRateMedian && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.CurrentOdds > 3 && d.HistoricalWinRate < histWinRateMedian && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.CurrentOdds <= 3 && d.HistoricalWinRate < histWinRateMedian && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Favorite-Inexperienced Interaction",
            Description = "Favorite status combined with low historical win rate",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 50,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeLowStrengthFavoriteInteraction(List<PirateFeatureRecord> data)
    {
        var strengthMedian = data.Select(d => d.Strength).OrderBy(s => s).Skip(data.Count / 2).First();

        var group00 = data.Where(d => d.Strength >= strengthMedian && d.CurrentOdds > 3 && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.Strength < strengthMedian && d.CurrentOdds > 3 && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.Strength >= strengthMedian && d.CurrentOdds <= 3 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.Strength < strengthMedian && d.CurrentOdds <= 3 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "LowStrength-Favorite Interaction",
            Description = "Low strength (below median) combined with favorite status",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 50,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeUndervaluedStrongInteraction(List<PirateFeatureRecord> data)
    {
        // Strong pirate with high odds (undervalued)
        var strengthThreshold = data.Select(d => d.Strength).OrderByDescending(s => s).Skip(data.Count / 4).First();

        var group00 = data.Where(d => d.Strength < strengthThreshold && d.CurrentOdds <= 5 && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.Strength >= strengthThreshold && d.CurrentOdds <= 5 && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.Strength < strengthThreshold && d.CurrentOdds > 5 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.Strength >= strengthThreshold && d.CurrentOdds > 5 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Undervalued-Strong Interaction",
            Description = "High strength combined with high odds (>5:1, undervalued by market)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 50,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeArenaSpecialistModerateOddsInteraction(List<PirateFeatureRecord> data)
    {
        // Arena specialist (high arena win rate) with moderate odds
        var arenaWinRateThreshold = data.Select(d => d.ArenaWinRate).OrderByDescending(s => s).Skip(data.Count / 4).First();

        var group00 = data.Where(d => d.ArenaWinRate < arenaWinRateThreshold && (d.CurrentOdds <= 3 || d.CurrentOdds > 7) && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.ArenaWinRate >= arenaWinRateThreshold && (d.CurrentOdds <= 3 || d.CurrentOdds > 7) && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.ArenaWinRate < arenaWinRateThreshold && d.CurrentOdds > 3 && d.CurrentOdds <= 7 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.ArenaWinRate >= arenaWinRateThreshold && d.CurrentOdds > 3 && d.CurrentOdds <= 7 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "ArenaSpecialist-ModerateOdds Interaction",
            Description = "High arena win rate combined with moderate odds (3-7:1)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 50,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeHotStreakBeatsRivalsInteraction(List<PirateFeatureRecord> data)
    {
        // Hot streak (high recent win rate) combined with good record vs current rivals
        var recentWinRateThreshold = data.Select(d => d.RecentWinRate).OrderByDescending(s => s).Skip(data.Count / 4).First();
        var rivalWinRateThreshold = data.Select(d => d.WinRateVsCurrentRivals).OrderByDescending(s => s).Skip(data.Count / 4).First();

        var group00 = data.Where(d => d.RecentWinRate < recentWinRateThreshold && d.WinRateVsCurrentRivals < rivalWinRateThreshold && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.RecentWinRate >= recentWinRateThreshold && d.WinRateVsCurrentRivals < rivalWinRateThreshold && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.RecentWinRate < recentWinRateThreshold && d.WinRateVsCurrentRivals >= rivalWinRateThreshold && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.RecentWinRate >= recentWinRateThreshold && d.WinRateVsCurrentRivals >= rivalWinRateThreshold && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "HotStreak-BeatsRivals Interaction",
            Description = "High recent win rate combined with good record vs current rivals",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 50,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    private InteractionAnalysisEffect AnalyzeFoodPosition3Interaction(List<PirateFeatureRecord> data)
    {
        // Positive food + position 3 (last position) - potential synergy or antagonism
        var group00 = data.Where(d => d.FoodAdjustment <= 0 && d.Position != 3 && d.IsWinner.HasValue).ToList();
        var group10 = data.Where(d => d.FoodAdjustment > 0 && d.Position != 3 && d.IsWinner.HasValue).ToList();
        var group01 = data.Where(d => d.FoodAdjustment <= 0 && d.Position == 3 && d.IsWinner.HasValue).ToList();
        var group11 = data.Where(d => d.FoodAdjustment > 0 && d.Position == 3 && d.IsWinner.HasValue).ToList();

        var winRate00 = group00.Any() ? group00.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate10 = group10.Any() ? group10.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate01 = group01.Any() ? group01.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;
        var winRate11 = group11.Any() ? group11.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0;

        var effect1Alone = winRate10 - winRate00;
        var effect2Alone = winRate01 - winRate00;
        var expectedAdditive = winRate00 + effect1Alone + effect2Alone;
        var interactionStrength = winRate11 - expectedAdditive;

        return new InteractionAnalysisEffect
        {
            Name = "Food-Position3 Interaction",
            Description = "Positive food adjustment combined with last position (position 3)",
            InteractionStrength = interactionStrength,
            Effect1Alone = effect1Alone,
            Effect2Alone = effect2Alone,
            CombinedEffect = winRate11,
            ExpectedAdditiveEffect = expectedAdditive,
            Group00Count = group00.Count,
            Group10Count = group10.Count,
            Group01Count = group01.Count,
            Group11Count = group11.Count,
            WinRate00 = winRate00,
            WinRate10 = winRate10,
            WinRate01 = winRate01,
            WinRate11 = winRate11,
            IsSignificant = Math.Abs(interactionStrength) > 0.02 && group11.Count >= 50,
            IsAntagonistic = interactionStrength < -0.02,
            IsSynergistic = interactionStrength > 0.02
        };
    }

    #endregion

    #region Three-Way Interactions

    private InteractionAnalysisEffect AnalyzeFoodPositionStrengthInteraction(List<PirateFeatureRecord> data)
    {
        // Three-way: Food + Position + Strength
        var strengthThreshold = data.Select(d => d.Strength).OrderByDescending(s => s).Skip(data.Count / 4).First();

        // All 8 combinations for 3 binary variables
        var groups = new Dictionary<string, List<PirateFeatureRecord>>
        {
            ["000"] = data.Where(d => d.FoodAdjustment <= 0 && d.Position < 2 && d.Strength < strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["001"] = data.Where(d => d.FoodAdjustment <= 0 && d.Position < 2 && d.Strength >= strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["010"] = data.Where(d => d.FoodAdjustment <= 0 && d.Position >= 2 && d.Strength < strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["011"] = data.Where(d => d.FoodAdjustment <= 0 && d.Position >= 2 && d.Strength >= strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["100"] = data.Where(d => d.FoodAdjustment > 0 && d.Position < 2 && d.Strength < strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["101"] = data.Where(d => d.FoodAdjustment > 0 && d.Position < 2 && d.Strength >= strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["110"] = data.Where(d => d.FoodAdjustment > 0 && d.Position >= 2 && d.Strength < strengthThreshold && d.IsWinner.HasValue).ToList(),
            ["111"] = data.Where(d => d.FoodAdjustment > 0 && d.Position >= 2 && d.Strength >= strengthThreshold && d.IsWinner.HasValue).ToList()
        };

        var winRates = groups.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Any() ? kvp.Value.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0.25
        );

        // Calculate three-way interaction effect
        // E(ABC) = actual_111 - (expected from all lower-order effects)
        var baseline = winRates["000"];
        var effectFood = winRates["100"] - baseline;
        var effectPosition = winRates["010"] - baseline;
        var effectStrength = winRates["001"] - baseline;
        
        var twoWayFP = winRates["110"] - baseline - effectFood - effectPosition;
var twoWayFS = winRates["101"] - baseline - effectFood - effectStrength;
        var twoWayPS = winRates["011"] - baseline - effectPosition - effectStrength;

        var expectedThreeWay = baseline + effectFood + effectPosition + effectStrength + twoWayFP + twoWayFS + twoWayPS;
        var threeWayInteraction = winRates["111"] - expectedThreeWay;

        return new InteractionAnalysisEffect
        {
            Name = "Food-Position-Strength 3-Way Interaction",
            Description = "Three-way interaction between food bonus, bad position, and high strength",
            InteractionStrength = threeWayInteraction,
            Effect1Alone = effectFood,
            Effect2Alone = effectPosition,
            CombinedEffect = winRates["111"],
            ExpectedAdditiveEffect = expectedThreeWay,
            Group00Count = groups["000"].Count,
            Group10Count = groups["100"].Count,
            Group01Count = groups["010"].Count,
            Group11Count = groups["111"].Count,
            WinRate00 = winRates["000"],
            WinRate10 = winRates["100"],
            WinRate01 = winRates["010"],
            WinRate11 = winRates["111"],
            IsSignificant = Math.Abs(threeWayInteraction) > 0.03 && groups["111"].Count >= 50,
            IsAntagonistic = threeWayInteraction < -0.03,
            IsSynergistic = threeWayInteraction > 0.03,
            IsThreeWay = true
        };
    }

    private InteractionAnalysisEffect AnalyzeUndervaluedStrongBeatsRivalsInteraction(List<PirateFeatureRecord> data)
    {
        // Three-way: Undervalued (high odds) + Strong + Beats Rivals
        var strengthThreshold = data.Select(d => d.Strength).OrderByDescending(s => s).Skip(data.Count / 4).First();
        var rivalWinRateThreshold = data.Select(d => d.WinRateVsCurrentRivals).OrderByDescending(s => s).Skip(data.Count / 3).First();

        var groups = new Dictionary<string, List<PirateFeatureRecord>>
        {
            ["000"] = data.Where(d => d.CurrentOdds <= 5 && d.Strength < strengthThreshold && d.WinRateVsCurrentRivals < rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["001"] = data.Where(d => d.CurrentOdds <= 5 && d.Strength < strengthThreshold && d.WinRateVsCurrentRivals >= rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["010"] = data.Where(d => d.CurrentOdds <= 5 && d.Strength >= strengthThreshold && d.WinRateVsCurrentRivals < rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["011"] = data.Where(d => d.CurrentOdds <= 5 && d.Strength >= strengthThreshold && d.WinRateVsCurrentRivals >= rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["100"] = data.Where(d => d.CurrentOdds > 5 && d.Strength < strengthThreshold && d.WinRateVsCurrentRivals < rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["101"] = data.Where(d => d.CurrentOdds > 5 && d.Strength < strengthThreshold && d.WinRateVsCurrentRivals >= rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["110"] = data.Where(d => d.CurrentOdds > 5 && d.Strength >= strengthThreshold && d.WinRateVsCurrentRivals < rivalWinRateThreshold && d.IsWinner.HasValue).ToList(),
            ["111"] = data.Where(d => d.CurrentOdds > 5 && d.Strength >= strengthThreshold && d.WinRateVsCurrentRivals >= rivalWinRateThreshold && d.IsWinner.HasValue).ToList()
        };

        var winRates = groups.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Any() ? kvp.Value.Average(d => d.IsWinner == true ? 1.0 : 0.0) : 0.25
        );

        var baseline = winRates["000"];
        var effectUndervalued = winRates["100"] - baseline;
        var effectStrong = winRates["010"] - baseline;
        var effectBeatsRivals = winRates["001"] - baseline;

        var twoWayUS = winRates["110"] - baseline - effectUndervalued - effectStrong;
        var twoWayUB = winRates["101"] - baseline - effectUndervalued - effectBeatsRivals;
        var twoWaySB = winRates["011"] - baseline - effectStrong - effectBeatsRivals;

        var expectedThreeWay = baseline + effectUndervalued + effectStrong + effectBeatsRivals + twoWayUS + twoWayUB + twoWaySB;
        var threeWayInteraction = winRates["111"] - expectedThreeWay;

        return new InteractionAnalysisEffect
        {
            Name = "Undervalued-Strong-BeatsRivals 3-Way Interaction",
            Description = "Three-way: High odds (undervalued) + High strength + Good record vs rivals",
            InteractionStrength = threeWayInteraction,
            Effect1Alone = effectUndervalued,
            Effect2Alone = effectStrong,
            CombinedEffect = winRates["111"],
            ExpectedAdditiveEffect = expectedThreeWay,
            Group00Count = groups["000"].Count,
            Group10Count = groups["100"].Count,
            Group01Count = groups["010"].Count,
            Group11Count = groups["111"].Count,
            WinRate00 = winRates["000"],
            WinRate10 = winRates["100"],
            WinRate01 = winRates["010"],
            WinRate11 = winRates["111"],
            IsSignificant = Math.Abs(threeWayInteraction) > 0.03 && groups["111"].Count >= 30,
            IsAntagonistic = threeWayInteraction < -0.03,
            IsSynergistic = threeWayInteraction > 0.03,
            IsThreeWay = true
        };
    }

    #endregion
}