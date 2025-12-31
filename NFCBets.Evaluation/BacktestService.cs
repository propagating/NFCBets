using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;
using NFCBets.Evaluation.Enums;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation;

public class BacktestService : IBacktestService
{
    public async Task<BacktestResult> RunBacktestAsync(
        IMlStrategy strategy,
        List<PirateFeatureRecord> historicalData,
        BacktestConfiguration? config = null)
    {
        config ??= new BacktestConfiguration();
        
        var result = new BacktestResult
        {
            StrategyName = strategy.StrategyName,
            Configuration = config,
            StartingBankroll = config.StartingBankroll
        };

        // Split data: 70% for training, 30% for backtesting
        var validData = historicalData.Where(f => f.IsWinner.HasValue).ToList();
        var uniqueRounds = validData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        
        var trainSplitIndex = (int)(uniqueRounds.Count * 0.7);
        var trainRounds = uniqueRounds.Take(trainSplitIndex).ToHashSet();
        var testRounds = uniqueRounds.Skip(trainSplitIndex).Take(config.RoundsToSimulate).ToList();

        if (!testRounds.Any())
        {
            Console.WriteLine($"      ⚠️ No test rounds available");
            return result;
        }

        var trainData = validData.Where(f => trainRounds.Contains(f.RoundId)).ToList();
        var testData = validData.Where(f => testRounds.Contains(f.RoundId)).ToList();

        Console.WriteLine($"      Training on {trainData.Count} records ({trainRounds.Count} rounds)");
        Console.WriteLine($"      Backtesting on {testData.Count} records ({testRounds.Count} rounds)");

        // Train the model
        try
        {
            await strategy.TrainAsync(trainData, null);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Training failed: {ex.Message}");
            return result;
        }

        // Get predictions for test data
        List<PiratePrediction> predictions;
        try
        {
            predictions = await strategy.PredictAsync(testData);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"      ❌ Prediction failed: {ex.Message}");
            return result;
        }

        if (!predictions.Any())
        {
            Console.WriteLine($"      ⚠️ No predictions generated");
            return result;
        }

        // Create lookup for predictions
        var predictionLookup = predictions
            .GroupBy(p => (p.RoundId, p.ArenaId))
            .ToDictionary(
                g => g.Key,
                g => g.ToList());

        // Run simulation
        decimal bankroll = config.StartingBankroll;
        decimal peakBankroll = bankroll;
        decimal maxDrawdown = 0;
        
        int currentWinStreak = 0;
        int currentLoseStreak = 0;
        int maxWinStreak = 0;
        int maxLoseStreak = 0;
        
        var dailyReturns = new List<decimal>();
        decimal lastBankroll = bankroll;
        
        int roundNumber = 0;

        // Initialize arena results
        for (int i = 1; i <= 5; i++)
        {
            result.ArenaResults[i] = new ArenaBacktestResult { ArenaId = i };
        }

        // Process each round
        foreach (var roundId in testRounds)
        {
            roundNumber++;
            decimal roundStartBankroll = bankroll;

            // Get all arenas for this round
            var arenas = testData
                .Where(f => f.RoundId == roundId)
                .Select(f => f.ArenaId)
                .Distinct();

            foreach (var arenaId in arenas)
            {
                if (!config.BetAllArenas && config.SpecificArenaId.HasValue && arenaId != config.SpecificArenaId.Value)
                    continue;

                var key = (roundId, arenaId);
                if (!predictionLookup.TryGetValue(key, out var arenaPredictions))
                    continue;

                // Find best bet in this arena
                var bestBet = FindBestBet(arenaPredictions, config);
                if (bestBet == null)
                    continue;

                // Calculate bet size
                decimal betAmount = CalculateBetSize(bankroll, bestBet, config);
                if (betAmount <= 0 || betAmount > bankroll)
                    continue;

                // Get actual result
                var actualWinner = testData
                    .FirstOrDefault(f => f.RoundId == roundId && f.ArenaId == arenaId && f.IsWinner == true);

                if (actualWinner == null)
                    continue;

                bool won = bestBet.PirateId == actualWinner.PirateId;
                decimal profitLoss = won ? betAmount * (decimal)(bestBet.Payout - 1) : -betAmount;
                bankroll += profitLoss;

                // Only store detailed history if requested (memory optimization)
                if (config.IncludeDetailedHistory)
                {
                    var betRecord = new BetRecord
                    {
                        RoundId = roundId,
                        ArenaId = arenaId,
                        PirateId = bestBet.PirateId,
                        BetAmount = betAmount,
                        Payout = (decimal)bestBet.Payout,
                        PredictedProbability = bestBet.WinProbability,
                        ImpliedProbability = 1m / (decimal)bestBet.Payout,
                        Edge = (decimal)bestBet.WinProbability - (1m / (decimal)bestBet.Payout),
                        Won = won,
                        ProfitLoss = profitLoss,
                        BankrollAfter = bankroll
                    };
                    result.BetHistory.Add(betRecord);
                }

                // Update totals
                result.TotalBetsPlaced++;
                result.TotalWagered += betAmount;
                if (won) result.BetsWon++;
                else result.BetsLost++;

                // Track edge for analysis
                var edge = (decimal)bestBet.WinProbability - (1m / (decimal)bestBet.Payout);
                result.AverageEdge = ((result.AverageEdge * (result.TotalBetsPlaced - 1)) + edge) / result.TotalBetsPlaced;
                result.AveragePayout = ((result.AveragePayout * (result.TotalBetsPlaced - 1)) + (decimal)bestBet.Payout) / result.TotalBetsPlaced;

                // Update arena stats
                var arenaResult = result.ArenaResults[arenaId];
                arenaResult.BetsPlaced++;
                if (won) arenaResult.BetsWon++;
                arenaResult.Profit += profitLoss;

                // Update streaks
                if (won)
                {
                    currentWinStreak++;
                    currentLoseStreak = 0;
                    maxWinStreak = Math.Max(maxWinStreak, currentWinStreak);
                }
                else
                {
                    currentLoseStreak++;
                    currentWinStreak = 0;
                    maxLoseStreak = Math.Max(maxLoseStreak, currentLoseStreak);
                }
            }

            // Record bankroll snapshot
            peakBankroll = Math.Max(peakBankroll, bankroll);
            var drawdown = peakBankroll - bankroll;
            maxDrawdown = Math.Max(maxDrawdown, drawdown);

            result.BankrollHistory.Add(new BankrollSnapshot
            {
                RoundNumber = roundNumber,
                RoundId = roundId,
                Bankroll = bankroll,
                DrawdownFromPeak = drawdown
            });

            // Calculate daily return for Sharpe ratio (every 5 rounds = 1 "day")
            if (roundNumber % 5 == 0)
            {
                var dailyReturn = lastBankroll > 0 ? (bankroll - lastBankroll) / lastBankroll : 0;
                dailyReturns.Add(dailyReturn);
                lastBankroll = bankroll;
            }
        }

        // Calculate final statistics
        result.FinalBankroll = bankroll;
        result.TotalProfit = bankroll - config.StartingBankroll;
        result.ROI = config.StartingBankroll > 0 ? result.TotalProfit / config.StartingBankroll : 0;
        result.TotalRounds = testRounds.Count;
        result.WinRate = result.TotalBetsPlaced > 0 ? (decimal)result.BetsWon / result.TotalBetsPlaced : 0;
        result.AverageBetSize = result.TotalBetsPlaced > 0 ? result.TotalWagered / result.TotalBetsPlaced : 0;
        
        result.MaxDrawdown = maxDrawdown;
        result.MaxDrawdownPercentage = peakBankroll > 0 ? maxDrawdown / peakBankroll : 0;
        result.MaxWinStreak = maxWinStreak;
        result.MaxLoseStreak = maxLoseStreak;
        result.CurrentStreak = currentWinStreak > 0 ? currentWinStreak : -currentLoseStreak;

        // Expected value calculation
        if (result.TotalBetsPlaced > 0)
        {
            result.ExpectedValue = result.AverageEdge;
        }

        // Profit factor
        var grossProfit = result.BetHistory.Where(b => b.Won).Sum(b => b.ProfitLoss);
        var grossLoss = Math.Abs(result.BetHistory.Where(b => !b.Won).Sum(b => b.ProfitLoss));
        
        // If no detailed history, estimate from totals
        if (!config.IncludeDetailedHistory && result.TotalBetsPlaced > 0)
        {
            grossProfit = result.TotalProfit > 0 ? result.TotalProfit + (result.BetsLost * result.AverageBetSize) : 0;
            grossLoss = result.TotalProfit < 0 ? Math.Abs(result.TotalProfit) + (result.BetsWon * result.AverageBetSize * (result.AveragePayout - 1)) : result.BetsLost * result.AverageBetSize;
        }
        
        result.ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : grossProfit > 0 ? 999m : 0;

        // Sharpe and Sortino ratios
        if (dailyReturns.Count > 1)
        {
            var avgReturn = dailyReturns.Average();
            var stdDev = CalculateStdDev(dailyReturns);
            var downsideStdDev = CalculateDownsideStdDev(dailyReturns);
            
            // Annualized (assuming ~73 "days" per year based on 365 rounds)
            result.SharpeRatio = stdDev > 0 ? (avgReturn / stdDev) * (decimal)Math.Sqrt(73) : 0;
            result.SortinoRatio = downsideStdDev > 0 ? (avgReturn / downsideStdDev) * (decimal)Math.Sqrt(73) : 0;
            
            // Annualized ROI
            if (testRounds.Count > 0)
            {
                var periodsPerYear = 365.0 / testRounds.Count;
                result.AnnualizedROI = (decimal)(Math.Pow((double)(1 + result.ROI), periodsPerYear) - 1);
            }
        }

        // Calculate arena ROIs
        foreach (var arenaResult in result.ArenaResults.Values)
        {
            if (arenaResult.BetsPlaced > 0)
            {
                var arenaWagered = arenaResult.BetsPlaced * result.AverageBetSize;
                arenaResult.ROI = arenaWagered > 0 ? arenaResult.Profit / arenaWagered : 0;
            }
        }

        return result;
    }

    private PiratePrediction? FindBestBet(List<PiratePrediction> predictions, BacktestConfiguration config)
    {
        if (predictions == null || !predictions.Any())
            return null;

        var valueBets = predictions
            .Where(p => p.Payout >= 2)  // Valid payout
            .Select(p => new
            {
                Prediction = p,
                ImpliedProb = 1.0f / Math.Max(2, p.Payout),
                Edge = p.WinProbability - (1.0f / Math.Max(2, p.Payout)),
                EV = p.WinProbability * (p.Payout - 1) - (1 - p.WinProbability)
            })
            .Where(x => x.Edge >= (float)config.MinEdgeRequired && x.Prediction.WinProbability > 0.1f)
            .OrderByDescending(x => x.EV)  // Best expected value
            .FirstOrDefault();

        return valueBets?.Prediction;
    }

    private decimal CalculateBetSize(decimal bankroll, PiratePrediction bet, BacktestConfiguration config)
    {
        if (bankroll <= 0 || bet.WinProbability <= 0 || bet.Payout < 2)
            return 0;

        decimal betSize = 0;
        
        var prob = (decimal)bet.WinProbability;
        var payout = (decimal)bet.Payout;
        var edge = prob - (1m / payout);

        switch (config.BettingStrategy)
        {
            case BettingStrategyTypeEnum.Flat:
                betSize = bankroll * 0.02m;  // 2% flat bet
                break;

            case BettingStrategyTypeEnum.Kelly:
                // Kelly formula: f* = (bp - q) / b
                // where b = payout - 1, p = win prob, q = 1 - p
                var b = payout - 1;
                if (b > 0)
                {
                    var kellyFraction = (b * prob - (1 - prob)) / b;
                    betSize = bankroll * Math.Max(0, kellyFraction);
                }
                break;

            case BettingStrategyTypeEnum.QuarterKelly:
                var bQ = payout - 1;
                if (bQ > 0)
                {
                    var kellyQ = (bQ * prob - (1 - prob)) / bQ;
                    betSize = bankroll * Math.Max(0, kellyQ) * 0.25m;
                }
                break;

            case BettingStrategyTypeEnum.HalfKelly:
                var bH = payout - 1;
                if (bH > 0)
                {
                    var kellyH = (bH * prob - (1 - prob)) / bH;
                    betSize = bankroll * Math.Max(0, kellyH) * 0.5m;
                }
                break;

            case BettingStrategyTypeEnum.ValueBetting:
                // Bet proportional to edge
                betSize = bankroll * Math.Min(edge * 2, config.MaxBetPercentage);
                break;

            case BettingStrategyTypeEnum.Proportional:
                // Bet proportional to confidence
                betSize = bankroll * prob * 0.1m;
                break;
        }

        // Apply maximum bet limit
        betSize = Math.Min(betSize, bankroll * config.MaxBetPercentage);
        
        // Minimum bet threshold (0.1% of bankroll)
        if (betSize < bankroll * 0.001m)
            return 0;

        return Math.Round(betSize, 2);
    }

    public async Task<List<BacktestResult>> CompareStrategiesBacktestAsync(
        Dictionary<string, IMlStrategy> strategies,
        List<PirateFeatureRecord> historicalData,
        BacktestConfiguration? config = null)
    {
        config ??= new BacktestConfiguration();
        var results = new List<BacktestResult>();

        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("💰 ML STRATEGY BACKTEST COMPARISON");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine($"Configuration:");
        Console.WriteLine($"   Starting Bankroll: $${config.StartingBankroll:N0}");
        Console.WriteLine($"   Rounds to Simulate: {config.RoundsToSimulate}");
        Console.WriteLine($"   Betting Strategy: {config.BettingStrategy}");
        Console.WriteLine($"   Min Edge Required: {config.MinEdgeRequired:P1}");
        Console.WriteLine($"   Max Bet %: {config.MaxBetPercentage:P1}");
        Console.WriteLine();

        int completed = 0;
        int total = strategies.Count;

        foreach (var (name, strategy) in strategies)
        {
            completed++;
            Console.WriteLine($"📊 [{completed}/{total}] Backtesting {strategy.StrategyName}...");
            
            try
            {
                var result = await RunBacktestAsync(strategy, historicalData, config);
                results.Add(result);
                
                var profitIndicator = result.TotalProfit >= 0 ? "✅" : "❌";
                Console.WriteLine($"{profitIndicator} Final: $${result.FinalBankroll:N2} | " +
                    $"ROI: {result.ROI:P2} | Win Rate: {result.WinRate:P1} | Bets: {result.TotalBetsPlaced}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ FAILED: {ex.Message}\n");
            }
        }

        if (results.Any())
        {
            DisplayComparisonResults(results);
            SaveBacktestReport(results, config);
        }

        return results;
    }

    public void DisplayBacktestResults(BacktestResult result)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine($"📊 BACKTEST RESULTS: {result.StrategyName}");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var profitColor = result.TotalProfit >= 0 ? "✅" : "❌";

        Console.WriteLine($"💰 PROFITABILITY {profitColor}");
        Console.WriteLine($"   Starting Bankroll:  $${result.StartingBankroll:N2}");
        Console.WriteLine($"   Final Bankroll:     $${result.FinalBankroll:N2}");
        Console.WriteLine($"   Total Profit:       $${result.TotalProfit:N2}");
        Console.WriteLine($"   ROI:                {result.ROI:P2}");
        Console.WriteLine($"   Annualized ROI:     {result.AnnualizedROI:P2}");
        Console.WriteLine();

        Console.WriteLine("📈 BETTING STATISTICS");
        Console.WriteLine($"   Total Rounds:       {result.TotalRounds}");
        Console.WriteLine($"   Bets Placed:        {result.TotalBetsPlaced}");
        Console.WriteLine($"   Bets Won:           {result.BetsWon}");
        Console.WriteLine($"   Bets Lost:          {result.BetsLost}");
        Console.WriteLine($"   Win Rate:           {result.WinRate:P2}");
        Console.WriteLine($"   Total Wagered:      $${result.TotalWagered:N2}");
        Console.WriteLine($"   Average Bet:        $${result.AverageBetSize:N2}");
        Console.WriteLine();

        Console.WriteLine("⚠️ RISK METRICS");
        Console.WriteLine($"   Max Drawdown:       $${result.MaxDrawdown:N2} ({result.MaxDrawdownPercentage:P2})");
        Console.WriteLine($"   Sharpe Ratio:       {result.SharpeRatio:F2}");
        Console.WriteLine($"   Sortino Ratio:      {result.SortinoRatio:F2}");
        Console.WriteLine($"   Profit Factor:      {result.ProfitFactor:F2}");
        Console.WriteLine();

        Console.WriteLine("🔥 STREAKS");
        Console.WriteLine($"   Max Win Streak:     {result.MaxWinStreak}");
        Console.WriteLine($"   Max Lose Streak:    {result.MaxLoseStreak}");
        Console.WriteLine();

        Console.WriteLine("📊 EDGE ANALYSIS");
        Console.WriteLine($"   Average Edge:       {result.AverageEdge:P2}");
        Console.WriteLine($"   Average Payout:     {result.AveragePayout:F2}:1");
        Console.WriteLine($"   Expected Value:     {result.ExpectedValue:P2}");
        Console.WriteLine();

        Console.WriteLine("🏟️ PER-ARENA BREAKDOWN");
        Console.WriteLine($"   {"Arena",-8} {"Bets",-8} {"Won",-8} {"Win%",-10} {"Profit",-12} {"ROI",-10}");
        Console.WriteLine($"   {new string('─', 56)}");
        
        foreach (var arena in result.ArenaResults.Values.OrderByDescending(a => a.Profit))
        {
            if (arena.BetsPlaced == 0) continue;
            var winRate = (decimal)arena.BetsWon / arena.BetsPlaced;
            var profitStr = arena.Profit >= 0 ? $"$${arena.Profit:N2}" : $"-$${Math.Abs(arena.Profit):N2}";
            Console.WriteLine($"   Arena {arena.ArenaId,-3} {arena.BetsPlaced,-8} {arena.BetsWon,-8} {winRate:P1,-10} {profitStr,-12} {arena.ROI:P1}");
        }
    }

    public void DisplayComparisonResults(List<BacktestResult> results)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("🏆 BACKTEST COMPARISON RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        // Sort by ROI
        var sorted = results.OrderByDescending(r => r.ROI).ToList();

        Console.WriteLine($"{"Rank",-5} {"Strategy",-40} {"ROI",-10} {"Win%",-9} {"Profit",-14} {"MaxDD",-10} {"Sharpe",-8} {"Bets",-6}");
        Console.WriteLine(new string('─', 105));

for (int i = 0; i < sorted.Count; i++)
        {
            var r = sorted[i];
            var medal = i switch
            {
                0 => "🥇",
                1 => "🥈",
                2 => "🥉",
                _ => "  "
            };

            var profitStr = r.TotalProfit >= 0 ? $"${r.TotalProfit:N0}" : $"-${Math.Abs(r.TotalProfit):N0}";
            var roiStr = r.ROI >= 0 ? $"+{r.ROI:P1}" : $"{r.ROI:P1}";
            
            Console.WriteLine($"{medal}{i + 1,-3} {r.StrategyName,-40} {roiStr,-10} {r.WinRate:P1,-9} {profitStr,-14} {r.MaxDrawdownPercentage:P1,-10} {r.SharpeRatio:F2,-8} {r.TotalBetsPlaced,-6}");
        }

        Console.WriteLine(new string('─', 105));

        // Categorize results
        var profitable = sorted.Where(r => r.TotalProfit > 0).ToList();
        var unprofitable = sorted.Where(r => r.TotalProfit <= 0).ToList();

        Console.WriteLine($"\n📊 SUMMARY:");
        Console.WriteLine($"   ✅ Profitable strategies:   {profitable.Count}/{sorted.Count}");
        Console.WriteLine($"   ❌ Unprofitable strategies: {unprofitable.Count}/{sorted.Count}");

        if (profitable.Any())
        {
            Console.WriteLine($"\n   💰 Average ROI (profitable): {profitable.Average(r => r.ROI):P2}");
            Console.WriteLine($"   💰 Total profit (all profitable): ${profitable.Sum(r => r.TotalProfit):N2}");
        }

        // Find best performers in different categories
        if (sorted.Any())
        {
            var best = sorted.First();
            var mostProfitable = sorted.OrderByDescending(r => r.TotalProfit).First();
            var safest = sorted.Where(r => r.TotalProfit > 0).OrderBy(r => r.MaxDrawdownPercentage).FirstOrDefault();
            var bestSharpe = sorted.OrderByDescending(r => r.SharpeRatio).First();
            var highestWinRate = sorted.OrderByDescending(r => r.WinRate).First();
            var mostBets = sorted.OrderByDescending(r => r.TotalBetsPlaced).First();

            Console.WriteLine($"\n🏆 CATEGORY WINNERS:");
            Console.WriteLine($"   📈 Best ROI:              {best.StrategyName} ({best.ROI:P2})");
            Console.WriteLine($"   💰 Most Profitable:       {mostProfitable.StrategyName} (${mostProfitable.TotalProfit:N2})");
            Console.WriteLine($"   📊 Best Risk-Adjusted:    {bestSharpe.StrategyName} (Sharpe: {bestSharpe.SharpeRatio:F2})");
            Console.WriteLine($"   🎯 Highest Win Rate:      {highestWinRate.StrategyName} ({highestWinRate.WinRate:P2})");
            Console.WriteLine($"   🎲 Most Active:           {mostBets.StrategyName} ({mostBets.TotalBetsPlaced} bets)");
            
            if (safest != null)
            {
                Console.WriteLine($"   🛡️ Safest (Low Drawdown): {safest.StrategyName} ({safest.MaxDrawdownPercentage:P2} max DD)");
            }

            // Overall recommendation
            Console.WriteLine($"\n🎯 RECOMMENDED STRATEGY:");
            
            // Score strategies based on multiple factors
            var scores = sorted.Select(r => new
            {
                Result = r,
                Score = CalculateOverallScore(r)
            }).OrderByDescending(x => x.Score).ToList();

            var recommended = scores.First();
            Console.WriteLine($"   {recommended.Result.StrategyName}");
            Console.WriteLine($"   ROI: {recommended.Result.ROI:P2} | Win Rate: {recommended.Result.WinRate:P2} | Sharpe: {recommended.Result.SharpeRatio:F2}");
            Console.WriteLine($"   Score: {recommended.Score:F2}/100");
        }

        // Print bankroll growth chart (ASCII)
        if (sorted.Any() && sorted.First().BankrollHistory.Count > 10)
        {
            Console.WriteLine("\n📈 BANKROLL GROWTH (Top 3 Strategies):");
            PrintBankrollChart(sorted.Take(3).ToList());
        }
    }

    private decimal CalculateOverallScore(BacktestResult result)
    {
        // Weighted scoring system (0-100)
        decimal score = 50; // Base score

        // ROI component (up to 30 points)
        score += Math.Min(30, Math.Max(-30, result.ROI * 100));

        // Win rate component (up to 15 points)
        score += (result.WinRate - 0.5m) * 30; // Bonus for >50% win rate

        // Sharpe ratio component (up to 15 points)
        score += Math.Min(15, result.SharpeRatio * 5);

        // Drawdown penalty (up to -20 points)
        score -= result.MaxDrawdownPercentage * 40;

        // Profit factor bonus (up to 10 points)
        if (result.ProfitFactor > 1)
        {
            score += Math.Min(10, (result.ProfitFactor - 1) * 5);
        }

        // Activity bonus (small bonus for more bets = more confidence)
        if (result.TotalBetsPlaced > 100)
        {
            score += 5;
        }

        return Math.Max(0, Math.Min(100, score));
    }

    private void PrintBankrollChart(List<BacktestResult> results)
    {
        const int chartWidth = 60;
        const int chartHeight = 12;

        if (!results.Any() || !results.First().BankrollHistory.Any())
            return;

        // Find min/max across all results
        var allBankrolls = results.SelectMany(r => r.BankrollHistory.Select(b => b.Bankroll)).ToList();
        var minBankroll = allBankrolls.Min();
        var maxBankroll = allBankrolls.Max();
        var range = maxBankroll - minBankroll;

        if (range == 0) return;

        var startingBankroll = results.First().StartingBankroll;
        
        // Symbols for different strategies
        var symbols = new[] { '█', '▓', '░' };
        var labels = new List<string>();

        Console.WriteLine();
        Console.WriteLine($"   ${maxBankroll:N0} ┤");

        for (int row = chartHeight - 1; row >= 0; row--)
        {
            var rowValue = minBankroll + (range * row / (chartHeight - 1));
            var label = row == chartHeight - 1 ? "" : row == 0 ? $"   ${minBankroll:N0}" : "        ";
            
            if (row == chartHeight / 2)
            {
                label = $"   ${(minBankroll + range / 2):N0}";
            }

            Console.Write($"{label,12} │");

            // Sample points across the chart width
            for (int col = 0; col < chartWidth; col++)
            {
                var pointIndex = (int)((double)col / chartWidth * results.First().BankrollHistory.Count);
                pointIndex = Math.Min(pointIndex, results.First().BankrollHistory.Count - 1);

                char chartChar = ' ';

                for (int s = 0; s < results.Count && s < symbols.Length; s++)
                {
                    if (pointIndex < results[s].BankrollHistory.Count)
                    {
                        var bankroll = results[s].BankrollHistory[pointIndex].Bankroll;
                        var normalizedValue = (bankroll - minBankroll) / range;
                        var chartRow = (int)(normalizedValue * (chartHeight - 1));

                        if (chartRow == row)
                        {
                            chartChar = symbols[s];
                            break;
                        }
                    }
                }

                // Draw starting bankroll reference line
                var startingRow = (int)((startingBankroll - minBankroll) / range * (chartHeight - 1));
                if (row == startingRow && chartChar == ' ')
                {
                    chartChar = '·';
                }

                Console.Write(chartChar);
            }

            Console.WriteLine();
        }

        Console.WriteLine($"             └{'─'.ToString().PadRight(chartWidth, '─')}");
        Console.WriteLine($"              Round 1{new string(' ', chartWidth - 15)}Round {results.First().TotalRounds}");

        // Legend
        Console.WriteLine("\n   Legend:");
        for (int i = 0; i < results.Count && i < symbols.Length; i++)
        {
            var r = results[i];
            var indicator = r.TotalProfit >= 0 ? "✅" : "❌";
            Console.WriteLine($"   {symbols[i]} {r.StrategyName} {indicator} (${r.FinalBankroll:N0})");
        }
        Console.WriteLine($"   · Starting bankroll (${startingBankroll:N0})");
    }

    private void SaveBacktestReport(List<BacktestResult> results, BacktestConfiguration config)
    {
        try
        {
            Directory.CreateDirectory("Reports");
            var fileName = Path.Combine("Reports", $"backtest_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

            var report = new
            {
                GeneratedAt = DateTime.UtcNow,
                Configuration = config,
                Summary = new
                {
                    TotalStrategies = results.Count,
                    ProfitableStrategies = results.Count(r => r.TotalProfit > 0),
                    BestROI = results.Max(r => r.ROI),
                    WorstROI = results.Min(r => r.ROI),
                    AverageROI = results.Average(r => r.ROI),
                    BestStrategy = results.OrderByDescending(r => r.ROI).First().StrategyName
                },
                Results = results.Select(r => new
                {
                    r.StrategyName,
                    r.FinalBankroll,
                    r.TotalProfit,
                    ROI = $"{r.ROI:P2}",
                    WinRate = $"{r.WinRate:P2}",
                    r.TotalBetsPlaced,
                    r.BetsWon,
                    r.BetsLost,
                    MaxDrawdown = $"{r.MaxDrawdownPercentage:P2}",
                    r.SharpeRatio,
                    r.ProfitFactor,
                    r.MaxWinStreak,
                    r.MaxLoseStreak,
                    ArenaBreakdown = r.ArenaResults.Values.Select(a => new
                    {
                        a.ArenaId,
                        a.BetsPlaced,
                        a.BetsWon,
                        a.Profit,
                        ROI = $"{a.ROI:P2}"
                    })
                })
            };

            var json = System.Text.Json.JsonSerializer.Serialize(report, 
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, json);
            
            Console.WriteLine($"\n📄 Backtest report saved to {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ Could not save report: {ex.Message}");
        }
    }

    private decimal CalculateStdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0;
        var avg = values.Average();
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumSquares / (values.Count - 1)));
    }

    private decimal CalculateDownsideStdDev(List<decimal> values)
    {
        var negativeReturns = values.Where(v => v < 0).ToList();
        if (negativeReturns.Count < 2) return 0.0001m;  // Avoid division by zero
        return CalculateStdDev(negativeReturns);
    }
}