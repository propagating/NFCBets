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
    // Standard betting configurations to test
    private static readonly List<BacktestConfiguration> StandardConfigurations = new()
    {
        new() { BettingStrategy = BettingStrategyTypeEnum.Flat, MinEdgeRequired = 0.05m, MaxBetPercentage = 0.02m },
        new() { BettingStrategy = BettingStrategyTypeEnum.Flat, MinEdgeRequired = 0.10m, MaxBetPercentage = 0.02m },
        new() { BettingStrategy = BettingStrategyTypeEnum.Flat, MinEdgeRequired = 0.05m, MaxBetPercentage = 0.05m },
        new() { BettingStrategy = BettingStrategyTypeEnum.QuarterKelly, MinEdgeRequired = 0.05m, MaxBetPercentage = 0.10m },
        new() { BettingStrategy = BettingStrategyTypeEnum.QuarterKelly, MinEdgeRequired = 0.08m, MaxBetPercentage = 0.10m },
        new() { BettingStrategy = BettingStrategyTypeEnum.QuarterKelly, MinEdgeRequired = 0.10m, MaxBetPercentage = 0.10m },
        new() { BettingStrategy = BettingStrategyTypeEnum.HalfKelly, MinEdgeRequired = 0.05m, MaxBetPercentage = 0.15m },
        new() { BettingStrategy = BettingStrategyTypeEnum.HalfKelly, MinEdgeRequired = 0.10m, MaxBetPercentage = 0.15m },
        new() { BettingStrategy = BettingStrategyTypeEnum.Kelly, MinEdgeRequired = 0.10m, MaxBetPercentage = 0.20m },
        new() { BettingStrategy = BettingStrategyTypeEnum.Kelly, MinEdgeRequired = 0.15m, MaxBetPercentage = 0.25m },
        new() { BettingStrategy = BettingStrategyTypeEnum.ValueBetting, MinEdgeRequired = 0.05m, MaxBetPercentage = 0.10m },
        new() { BettingStrategy = BettingStrategyTypeEnum.ValueBetting, MinEdgeRequired = 0.10m, MaxBetPercentage = 0.10m },
        new() { BettingStrategy = BettingStrategyTypeEnum.Proportional, MinEdgeRequired = 0.05m, MaxBetPercentage = 0.10m },
    };

    public async Task<BacktestResult> RunBacktestAsync(
        IMlStrategy strategy,
        List<PirateFeatureRecord> historicalData,
        BacktestConfiguration config)
    {
        var result = new BacktestResult
        {
            StrategyName = strategy.StrategyName,
            BettingStrategyName = GetConfigName(config),
            Configuration = config,
            StartingBankroll = config.StartingBankroll
        };

        var validData = historicalData.Where(f => f.IsWinner.HasValue).ToList();
        var uniqueRounds = validData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        
        var trainSplitIndex = (int)(uniqueRounds.Count * 0.7);
        var trainRounds = uniqueRounds.Take(trainSplitIndex).ToHashSet();
        var testRounds = uniqueRounds.Skip(trainSplitIndex).Take(config.RoundsToSimulate).ToList();

        if (testRounds.Count == 0)
            return result;

        var trainData = validData.Where(f => trainRounds.Contains(f.RoundId)).ToList();
        var testData = validData.Where(f => testRounds.Contains(f.RoundId)).ToList();

        await strategy.TrainAsync(trainData, null);

        var predictions = await strategy.PredictAsync(testData);
        var predictionLookup = predictions
            .GroupBy(p => (p.RoundId, p.ArenaId))
            .ToDictionary(g => g.Key, g => g.ToList());

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

        for (int i = 1; i <= 5; i++)
            result.ArenaResults[i] = new ArenaBacktestResult { ArenaId = i };

        foreach (var roundId in testRounds)
        {
            roundNumber++;

            var arenas = testData
                .Where(f => f.RoundId == roundId)
                .Select(f => f.ArenaId)
                .Distinct();

            foreach (var arenaId in arenas)
            {
                if (!config.BetAllArenas && config.SpecificArenaId.HasValue && 
                    arenaId != config.SpecificArenaId.Value)
                    continue;

                var key = (roundId, arenaId);
                if (!predictionLookup.TryGetValue(key, out var arenaPredictions))
                    continue;

                var bestBet = FindBestBet(arenaPredictions, config);
                if (bestBet == null)
                    continue;

                decimal betAmount = CalculateBetSize(bankroll, bestBet, config);
                if (betAmount <= 0 || betAmount > bankroll)
                    continue;

                var actualWinner = testData
                    .FirstOrDefault(f => f.RoundId == roundId && f.ArenaId == arenaId && f.IsWinner == true);

                if (actualWinner == null)
                    continue;

                bool won = bestBet.PirateId == actualWinner.PirateId;
                decimal profitLoss = won ? betAmount * (decimal)(bestBet.Payout - 1) : -betAmount;
                bankroll += profitLoss;

                result.BetHistory.Add(new BetRecord
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
                });

                var arenaResult = result.ArenaResults[arenaId];
                arenaResult.BetsPlaced++;
                if (won) arenaResult.BetsWon++;
                arenaResult.Profit += profitLoss;

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

            if (roundNumber % 5 == 0 && lastBankroll > 0)
            {
                dailyReturns.Add((bankroll - lastBankroll) / lastBankroll);
                lastBankroll = bankroll;
            }
        }

        CalculateFinalStats(result, config, maxDrawdown, peakBankroll, 
            maxWinStreak, maxLoseStreak, currentWinStreak, currentLoseStreak,
            dailyReturns, testRounds.Count);

        return result;
    }

    private string GetConfigName(BacktestConfiguration config)
    {
        var minEdgeStr = (config.MinEdgeRequired * 100).ToString("F0");
        var maxBetStr = (config.MaxBetPercentage * 100).ToString("F0");
        return $"{config.BettingStrategy} (Edge>={minEdgeStr}%, Max{maxBetStr}%)";
    }

    private PiratePrediction? FindBestBet(List<PiratePrediction> predictions, BacktestConfiguration config)
    {
        return predictions
            .Select(p => new
            {
                Prediction = p,
                Edge = p.WinProbability - (1.0f / (float)Math.Max(2, p.Payout))
            })
            .Where(x => x.Edge >= (float)config.MinEdgeRequired)
            .OrderByDescending(x => x.Edge * x.Prediction.Payout)
            .Select(x => x.Prediction)
            .FirstOrDefault();
    }

    private decimal CalculateBetSize(decimal bankroll, PiratePrediction bet, BacktestConfiguration config)
    {
        decimal betSize = 0;
        var prob = (decimal)bet.WinProbability;
        var payout = (decimal)bet.Payout;
        var b = payout - 1;

        if (b <= 0) return 0;

        switch (config.BettingStrategy)
        {
            case BettingStrategyTypeEnum.Flat:
                betSize = bankroll * 0.02m;
                break;

            case BettingStrategyTypeEnum.Kelly:
                var kelly = (b * prob - (1 - prob)) / b;
                betSize = bankroll * Math.Max(0, kelly);
                break;

            case BettingStrategyTypeEnum.QuarterKelly:
                var kellyQ = (b * prob - (1 - prob)) / b;
                betSize = bankroll * Math.Max(0, kellyQ) * 0.25m;
                break;

            case BettingStrategyTypeEnum.HalfKelly:
                var kellyH = (b * prob - (1 - prob)) / b;
                betSize = bankroll * Math.Max(0, kellyH) * 0.5m;
                break;

            case BettingStrategyTypeEnum.ValueBetting:
                var edge = prob - (1m / payout);
                betSize = bankroll * Math.Min(edge * 2, config.MaxBetPercentage);
                break;

            case BettingStrategyTypeEnum.Proportional:
                betSize = bankroll * prob * 0.1m;
                break;
        }

        betSize = Math.Min(betSize, bankroll * config.MaxBetPercentage);
        
        if (betSize < bankroll * 0.001m)
            return 0;

        return Math.Round(betSize, 2);
    }

    private void CalculateFinalStats(
        BacktestResult result,
        BacktestConfiguration config,
        decimal maxDrawdown,
        decimal peakBankroll,
        int maxWinStreak,
        int maxLoseStreak,
        int currentWinStreak,
        int currentLoseStreak,
        List<decimal> dailyReturns,
        int totalRounds)
    {
        result.FinalBankroll = result.BetHistory.Any() 
            ? result.BetHistory.Last().BankrollAfter 
            : config.StartingBankroll;
        result.TotalProfit = result.FinalBankroll - config.StartingBankroll;
        result.ROI = config.StartingBankroll > 0 ? result.TotalProfit / config.StartingBankroll : 0;
        result.TotalRounds = totalRounds;
        result.TotalBetsPlaced = result.BetHistory.Count;
        result.BetsWon = result.BetHistory.Count(b => b.Won);
        result.BetsLost = result.BetHistory.Count(b => !b.Won);
        result.WinRate = result.TotalBetsPlaced > 0 ? (decimal)result.BetsWon / result.TotalBetsPlaced : 0;
        result.TotalWagered = result.BetHistory.Sum(b => b.BetAmount);
        result.AverageBetSize = result.TotalBetsPlaced > 0 ? result.TotalWagered / result.TotalBetsPlaced : 0;
        
        result.MaxDrawdown = maxDrawdown;
        result.MaxDrawdownPercentage = peakBankroll > 0 ? maxDrawdown / peakBankroll : 0;
        result.MaxWinStreak = maxWinStreak;
        result.MaxLoseStreak = maxLoseStreak;
        result.CurrentStreak = currentWinStreak > 0 ? currentWinStreak : -currentLoseStreak;

        if (result.BetHistory.Any())
        {
            result.AverageEdge = result.BetHistory.Average(b => b.Edge);
            result.AveragePayout = result.BetHistory.Average(b => b.Payout);
            result.ExpectedValue = result.BetHistory.Average(b => 
                (decimal)b.PredictedProbability * (b.Payout - 1) - (1 - (decimal)b.PredictedProbability));

            var grossProfit = result.BetHistory.Where(b => b.Won).Sum(b => b.ProfitLoss);
            var grossLoss = Math.Abs(result.BetHistory.Where(b => !b.Won).Sum(b => b.ProfitLoss));
            result.ProfitFactor = grossLoss > 0 ? grossProfit / grossLoss : (grossProfit > 0 ? 999m : 0m);
        }

        if (dailyReturns.Count > 1)
        {
            var avgReturn = dailyReturns.Average();
            var stdDev = CalculateStdDev(dailyReturns);
            var downsideStdDev = CalculateDownsideStdDev(dailyReturns);
            
            result.SharpeRatio = stdDev > 0 ? (avgReturn / stdDev) * (decimal)Math.Sqrt(73) : 0;
            result.SortinoRatio = downsideStdDev > 0 ? (avgReturn / downsideStdDev) * (decimal)Math.Sqrt(73) : 0;
            
            if (totalRounds > 0)
            {
                var annualizationFactor = 365.0 / totalRounds;
                result.AnnualizedROI = (decimal)Math.Pow((double)(1 + result.ROI), annualizationFactor) - 1;
            }
        }

        foreach (var arenaResult in result.ArenaResults.Values)
        {
            var arenaBets = result.BetHistory.Where(b => b.ArenaId == arenaResult.ArenaId).ToList();
            if (arenaBets.Any())
            {
                var arenaWagered = arenaBets.Sum(b => b.BetAmount);
                arenaResult.ROI = arenaWagered > 0 ? arenaResult.Profit / arenaWagered : 0;
                arenaResult.AverageEdge = arenaBets.Average(b => b.Edge);
            }
        }
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

        var startingBankrollStr = config.StartingBankroll.ToString("N0");
        var minEdgeStr = (config.MinEdgeRequired * 100).ToString("F0");
        var maxBetStr = (config.MaxBetPercentage * 100).ToString("F0");

        Console.WriteLine($"Configuration:");
        Console.WriteLine($"   Starting Bankroll: $${startingBankrollStr}");
        Console.WriteLine($"   Rounds to Simulate: {config.RoundsToSimulate}");
        Console.WriteLine($"   Betting Strategy: {config.BettingStrategy}");
        Console.WriteLine($"   Min Edge Required: {minEdgeStr}%");
        Console.WriteLine($"   Max Bet %: {maxBetStr}%");
        Console.WriteLine();

        foreach (var (name, strategy) in strategies)
        {
            Console.WriteLine($"📊 Backtesting {strategy.StrategyName}...");
            
            try
            {
                var result = await RunBacktestAsync(strategy, historicalData, config);
                results.Add(result);

                var finalBankrollStr = result.FinalBankroll.ToString("N0");
                var roiStr = (result.ROI * 100).ToString("F1");
                var winRateStr = (result.WinRate * 100).ToString("F1");
                var maxDdStr = (result.MaxDrawdownPercentage * 100).ToString("F1");
                
                Console.WriteLine($"   Final: $${finalBankrollStr} | ROI: {roiStr}% | Win: {winRateStr}% | MaxDD: {maxDdStr}%");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ FAILED: {ex.Message}");
            }
        }

        Console.WriteLine();
        DisplayComparisonResults(results);

        return results;
    }

    public async Task<FullComparisonReport> RunFullComparisonAsync(
        Dictionary<string, IMlStrategy> strategies,
        List<PirateFeatureRecord> historicalData,
        int roundsToTest = 1000)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("💰 COMPREHENSIVE ML + BETTING STRATEGY BACKTEST");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════════════\n");

        var report = new FullComparisonReport
        {
            ReportDate = DateTime.UtcNow,
            TotalRoundsTested = roundsToTest,
            TotalMlStrategies = strategies.Count,
            TotalBettingConfigurations = StandardConfigurations.Count,
            TotalCombinationsTested = strategies.Count * StandardConfigurations.Count
        };

        Console.WriteLine($"Testing {strategies.Count} ML strategies × {StandardConfigurations.Count} betting configurations");
        Console.WriteLine($"Total combinations: {report.TotalCombinationsTested}");
        Console.WriteLine($"Rounds per backtest: {roundsToTest}\n");

        foreach (var (strategyKey, strategy) in strategies)
        {
            Console.WriteLine($"\n{new string('═', 90)}");
            Console.WriteLine($"📊 ML STRATEGY: {strategy.StrategyName}");
            Console.WriteLine($"{new string('═', 90)}");

            var strategyComparison = new StrategyBettingComparison
            {
                MlStrategyName = strategy.StrategyName
            };

            var validData = historicalData.Where(f => f.IsWinner.HasValue).ToList();
            var uniqueRounds = validData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
            var trainSplitIndex = (int)(uniqueRounds.Count * 0.7);
            var trainRounds = uniqueRounds.Take(trainSplitIndex).ToHashSet();
            var trainData = validData.Where(f => trainRounds.Contains(f.RoundId)).ToList();

            Console.WriteLine($"   Training on {trainData.Count} records...");
            
            try
            {
                await strategy.TrainAsync(trainData, null);
                Console.WriteLine($"   ✅ Training complete\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"   ❌ Training failed: {ex.Message}\n");
                continue;
            }

            // Header with proper spacing
            Console.WriteLine($"   {"Betting Configuration",-45} {"ROI",-9} {"Win%",-7} {"Sharpe",-8} {"MaxDD",-7} {"Bets",-6} {"PF"}");
            Console.WriteLine($"   {new string('─', 95)}");

            foreach (var baseConfig in StandardConfigurations)
            {
                var config = new BacktestConfiguration
                {
                    BettingStrategy = baseConfig.BettingStrategy,
                    MinEdgeRequired = baseConfig.MinEdgeRequired,
                    MaxBetPercentage = baseConfig.MaxBetPercentage,
                    RoundsToSimulate = roundsToTest,
                    StartingBankroll = 10000m,
                    BetAllArenas = true
                };

                try
                {
                    var result = await RunBacktestAsync(strategy, historicalData, config);
                    var configName = GetConfigName(config);
                    result.BettingStrategyName = configName;
                    
                    strategyComparison.BettingResults[configName] = result;
                    report.AllResults.Add(result);

                    // Format all values as strings first
                    var displayName = configName.Length > 43 ? configName[..43] : configName;
                    var roiStr = (result.ROI * 100).ToString("F1") + "%";
                    var winStr = (result.WinRate * 100).ToString("F0") + "%";
                    var sharpeStr = result.SharpeRatio.ToString("F2");
                    var ddStr = (result.MaxDrawdownPercentage * 100).ToString("F0") + "%";
                    var pfStr = result.ProfitFactor > 10 ? ">10" : result.ProfitFactor.ToString("F1");

                    var prefix = result.ROI > 0 ? "✅" : (result.ROI < -0.1m ? "❌" : "⚠️");

                    Console.WriteLine($"   {prefix} {displayName,-43} {roiStr,-9} {winStr,-7} {sharpeStr,-8} {ddStr,-7} {result.TotalBetsPlaced,-6} {pfStr}");
                }
                catch (Exception ex)
                {
                    var configName = GetConfigName(config);
                    Console.WriteLine($"   ❌ {configName,-43} FAILED: {ex.Message}");
                }
            }

            // Best for this strategy
            if (strategyComparison.BettingResults.Any())
            {
                var bestByRoi = strategyComparison.BettingResults.Values
                    .OrderByDescending(r => r.ROI)
                    .First();
                var bestBySharpe = strategyComparison.BettingResults.Values
                    .OrderByDescending(r => r.SharpeRatio)
                    .First();

                strategyComparison.BestBettingStrategy = bestByRoi.BettingStrategyName;
                strategyComparison.BestROI = bestByRoi.ROI;
                strategyComparison.BestSharpe = bestBySharpe.SharpeRatio;

                var bestRoiStr = (bestByRoi.ROI * 100).ToString("F1") + "%";
                var bestSharpeStr = bestBySharpe.SharpeRatio.ToString("F2");

                Console.WriteLine($"   {new string('─', 95)}");
                Console.WriteLine($"   🏆 Best ROI:    {bestByRoi.BettingStrategyName} ({bestRoiStr})");
                Console.WriteLine($"   📈 Best Sharpe: {bestBySharpe.BettingStrategyName} ({bestSharpeStr})");
            }

            report.MlStrategyResults.Add(strategyComparison);
        }

        // Find overall bests
        if (report.AllResults.Any())
        {
            report.BestOverallROI = report.AllResults
                .Where(r => r.TotalBetsPlaced >= 50)
                .OrderByDescending(r => r.ROI)
                .FirstOrDefault();
            
            report.BestRiskAdjusted = report.AllResults
                .Where(r => r.TotalBetsPlaced >= 50)
                .OrderByDescending(r => r.SharpeRatio)
                .FirstOrDefault();
            
            report.MostConsistent = report.AllResults
                .Where(r => r.TotalBetsPlaced >= 50)
                .OrderByDescending(r => r.WinRate)
                .FirstOrDefault();
            
            report.LowestDrawdown = report.AllResults
                .Where(r => r.TotalBetsPlaced >= 50 && r.ROI > 0)
                .OrderBy(r => r.MaxDrawdownPercentage)
                .FirstOrDefault();

            report.BestProfitFactor = report.AllResults
                .Where(r => r.TotalBetsPlaced >= 50)
                .OrderByDescending(r => r.ProfitFactor)
                .FirstOrDefault();
        }

        DisplayFullComparisonReport(report);

        return report;
    }

    public void DisplayBacktestResults(BacktestResult result)
    {
        Console.WriteLine($"\n{new string('═', 70)}");
        Console.WriteLine($"📊 BACKTEST: {result.StrategyName}");
        Console.WriteLine($"   Betting: {result.BettingStrategyName}");
        Console.WriteLine($"{new string('═', 70)}\n");

        // Format strings
        var startStr = result.StartingBankroll.ToString("N0");
        var finalStr = result.FinalBankroll.ToString("N0");
        var profitStr = result.TotalProfit >= 0 
            ? "+" + result.TotalProfit.ToString("N0") 
            : result.TotalProfit.ToString("N0");
        var roiStr = (result.ROI * 100).ToString("F2") + "%";
        var annualRoiStr = (result.AnnualizedROI * 100).ToString("F2") + "%";
        var winRateStr = (result.WinRate * 100).ToString("F1") + "%";
        var wageredStr = result.TotalWagered.ToString("N0");
        var avgBetStr = result.AverageBetSize.ToString("N2");
        var maxDdStr = result.MaxDrawdown.ToString("N0");
        var maxDdPctStr = (result.MaxDrawdownPercentage * 100).ToString("F1") + "%";
        var sharpeStr = result.SharpeRatio.ToString("F2");
        var sortinoStr = result.SortinoRatio.ToString("F2");
        var pfStr = result.ProfitFactor > 100 ? ">100" : result.ProfitFactor.ToString("F2");
        var avgEdgeStr = (result.AverageEdge * 100).ToString("F2") + "%";
        var avgPayoutStr = result.AveragePayout.ToString("F2");
        var evStr = (result.ExpectedValue * 100).ToString("F2") + "%";

        Console.WriteLine("💰 PROFITABILITY");
        Console.WriteLine($"   Starting Bankroll:  $${startStr}");
        Console.WriteLine($"   Final Bankroll:     $${finalStr}");
        Console.WriteLine($"   Total Profit:       $${profitStr}");
        Console.WriteLine($"   ROI:                {roiStr}");
        Console.WriteLine($"   Annualized ROI:     {annualRoiStr}");
        Console.WriteLine();

        Console.WriteLine("📈 BETTING STATISTICS");
        Console.WriteLine($"   Total Rounds:       {result.TotalRounds}");
        Console.WriteLine($"   Bets Placed:        {result.TotalBetsPlaced}");
        Console.WriteLine($"   Bets Won:           {result.BetsWon}");
        Console.WriteLine($"   Bets Lost:          {result.BetsLost}");
        Console.WriteLine($"   Win Rate:           {winRateStr}");
        Console.WriteLine($"   Total Wagered:      $${wageredStr}");
        Console.WriteLine($"   Average Bet:        $${avgBetStr}");
        Console.WriteLine();

        Console.WriteLine("⚠️ RISK METRICS");
        Console.WriteLine($"   Max Drawdown:       $${maxDdStr} ({maxDdPctStr})");
        Console.WriteLine($"   Sharpe Ratio:       {sharpeStr}");
        Console.WriteLine($"   Sortino Ratio:      {sortinoStr}");
        Console.WriteLine($"   Profit Factor:      {pfStr}");
        Console.WriteLine($"   Max Win Streak:     {result.MaxWinStreak}");
        Console.WriteLine($"   Max Lose Streak:    {result.MaxLoseStreak}");
        Console.WriteLine();

        Console.WriteLine("📊 EDGE ANALYSIS");
        Console.WriteLine($"   Average Edge:       {avgEdgeStr}");
        Console.WriteLine($"   Average Payout:     {avgPayoutStr}:1");
        Console.WriteLine($"   Expected Value:     {evStr}");
        Console.WriteLine();

        Console.WriteLine("🏟️ PER-ARENA BREAKDOWN");
        Console.WriteLine($"   {"Arena",-8} {"Bets",-7} {"Won",-6} {"Win%",-8} {"Profit",-14} {"ROI",-10} {"Avg Edge"}");
Console.WriteLine($"   {new string('─', 65)}");

        foreach (var arena in result.ArenaResults.Values.OrderByDescending(a => a.Profit))
        {
            if (arena.BetsPlaced == 0) continue;

            var arenaWinRate = (decimal)arena.BetsWon / arena.BetsPlaced;
            var arenaWinStr = (arenaWinRate * 100).ToString("F0") + "%";
            var arenaProfitStr = arena.Profit >= 0 
                ? "$" + arena.Profit.ToString("N0") 
                : "-$" + Math.Abs(arena.Profit).ToString("N0");
            var arenaRoiStr = (arena.ROI * 100).ToString("F1") + "%";
            var arenaEdgeStr = (arena.AverageEdge * 100).ToString("F1") + "%";

            Console.WriteLine($"   Arena {arena.ArenaId,-3} {arena.BetsPlaced,-7} {arena.BetsWon,-6} {arenaWinStr,-8} {arenaProfitStr,-14} {arenaRoiStr,-10} {arenaEdgeStr}");
        }
    }

    public void DisplayComparisonResults(List<BacktestResult> results)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("🏆 BACKTEST COMPARISON RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        var sorted = results.OrderByDescending(r => r.ROI).ToList();

        Console.WriteLine($"{"Rank",-5} {"Strategy",-35} {"ROI",-10} {"Win%",-8} {"Profit",-14} {"MaxDD",-9} {"Sharpe",-8} {"Bets"}");
        Console.WriteLine(new string('─', 105));

        for (int i = 0; i < sorted.Count; i++)
        {
            var r = sorted[i];
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => "  " };
            var rank = $"{medal}{i + 1}";

            var strategy = r.StrategyName.Length > 33 ? r.StrategyName[..33] : r.StrategyName;
            var roiStr = (r.ROI * 100).ToString("F1") + "%";
            var winRateStr = (r.WinRate * 100).ToString("F1") + "%";
            var profitStr = r.TotalProfit >= 0 
                ? "$" + r.TotalProfit.ToString("N0") 
                : "-$" + Math.Abs(r.TotalProfit).ToString("N0");
            var maxDdStr = (r.MaxDrawdownPercentage * 100).ToString("F1") + "%";
            var sharpeStr = r.SharpeRatio.ToString("F2");

            Console.WriteLine($"{rank,-5} {strategy,-35} {roiStr,-10} {winRateStr,-8} {profitStr,-14} {maxDdStr,-9} {sharpeStr,-8} {r.TotalBetsPlaced}");
        }

        Console.WriteLine(new string('─', 105));

        if (!sorted.Any()) return;

        // Find best performers
        var best = sorted.First();
        var mostProfitable = sorted.OrderByDescending(r => r.TotalProfit).First();
        var safest = sorted.OrderBy(r => r.MaxDrawdownPercentage).First();
        var bestSharpe = sorted.OrderByDescending(r => r.SharpeRatio).First();
        var highestWinRate = sorted.OrderByDescending(r => r.WinRate).First();

        var bestRoiStr = (best.ROI * 100).ToString("F2") + "%";
        var mostProfitStr = mostProfitable.TotalProfit.ToString("N0");
        var safestDdStr = (safest.MaxDrawdownPercentage * 100).ToString("F2") + "%";
        var bestSharpeStr = bestSharpe.SharpeRatio.ToString("F2");
        var highestWinRateStr = (highestWinRate.WinRate * 100).ToString("F2") + "%";

        Console.WriteLine($"\n📊 ANALYSIS:");
        Console.WriteLine($"   🏆 Best ROI:           {best.StrategyName} ({bestRoiStr})");
        Console.WriteLine($"   💰 Most Profitable:    {mostProfitable.StrategyName} (${mostProfitStr})");
        Console.WriteLine($"   🛡️ Safest (Low DD):    {safest.StrategyName} ({safestDdStr} max DD)");
        Console.WriteLine($"   📈 Best Risk-Adjusted: {bestSharpe.StrategyName} (Sharpe: {bestSharpeStr})");
        Console.WriteLine($"   🎯 Highest Win Rate:   {highestWinRate.StrategyName} ({highestWinRateStr})");

        var profitable = sorted.Count(r => r.TotalProfit > 0);
        var unprofitable = sorted.Count(r => r.TotalProfit <= 0);
        Console.WriteLine($"\n   ✅ Profitable strategies: {profitable}/{sorted.Count}");
        Console.WriteLine($"   ❌ Unprofitable strategies: {unprofitable}/{sorted.Count}");

        var avgRoiStr = (sorted.Average(r => r.ROI) * 100).ToString("F2") + "%";
        var avgWinRateStr = (sorted.Average(r => r.WinRate) * 100).ToString("F2") + "%";
        var avgSharpeStr = sorted.Average(r => r.SharpeRatio).ToString("F2");

        Console.WriteLine($"\n   📊 Average ROI across all strategies: {avgRoiStr}");
        Console.WriteLine($"   📊 Average Win Rate: {avgWinRateStr}");
        Console.WriteLine($"   📊 Average Sharpe Ratio: {avgSharpeStr}");
    }

    public void DisplayFullComparisonReport(FullComparisonReport report)
    {
        Console.WriteLine("\n\n═══════════════════════════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("📊 FULL BACKTEST COMPARISON REPORT");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════════════════════════\n");

        Console.WriteLine($"Report Date: {report.ReportDate:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine($"Rounds Tested: {report.TotalRoundsTested}");
        Console.WriteLine($"ML Strategies: {report.TotalMlStrategies}");
        Console.WriteLine($"Betting Configurations: {report.TotalBettingConfigurations}");
        Console.WriteLine($"Total Combinations: {report.TotalCombinationsTested}");

        // Top 25 by ROI
        Console.WriteLine("\n🏆 TOP 25 COMBINATIONS BY ROI:");
        Console.WriteLine($"{"Rank",-5} {"ML Strategy",-25} {"Betting Config",-38} {"ROI",-9} {"Win%",-7} {"Sharpe",-8} {"MaxDD",-7} {"PF",-6} {"Bets"}");
        Console.WriteLine(new string('─', 115));

        var top25 = report.AllResults
            .Where(r => r.TotalBetsPlaced >= 20)
            .OrderByDescending(r => r.ROI)
            .Take(25)
            .ToList();

        for (int i = 0; i < top25.Count; i++)
        {
            var r = top25[i];
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => "  " };
            var rank = $"{medal}{i + 1}";

            var mlName = r.StrategyName.Length > 23 ? r.StrategyName[..23] : r.StrategyName;
            var betName = r.BettingStrategyName.Length > 36 ? r.BettingStrategyName[..36] : r.BettingStrategyName;

            var roiStr = (r.ROI * 100).ToString("F1") + "%";
            var winStr = (r.WinRate * 100).ToString("F0") + "%";
            var sharpeStr = r.SharpeRatio.ToString("F2");
            var ddStr = (r.MaxDrawdownPercentage * 100).ToString("F0") + "%";
            var pfStr = r.ProfitFactor > 10 ? ">10" : r.ProfitFactor.ToString("F1");

            Console.WriteLine($"{rank,-5} {mlName,-25} {betName,-38} {roiStr,-9} {winStr,-7} {sharpeStr,-8} {ddStr,-7} {pfStr,-6} {r.TotalBetsPlaced}");
        }

        // Top 10 by Sharpe
        Console.WriteLine("\n📈 TOP 10 BY RISK-ADJUSTED RETURNS (SHARPE RATIO):");
        Console.WriteLine($"{"Rank",-5} {"ML Strategy",-25} {"Betting Config",-38} {"Sharpe",-8} {"ROI",-9} {"MaxDD",-7} {"Sortino"}");
        Console.WriteLine(new string('─', 105));

        var topSharpe = report.AllResults
            .Where(r => r.TotalBetsPlaced >= 50 && r.ROI > -0.5m)
            .OrderByDescending(r => r.SharpeRatio)
            .Take(10)
            .ToList();

        for (int i = 0; i < topSharpe.Count; i++)
        {
            var r = topSharpe[i];
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => "  " };
            var rank = $"{medal}{i + 1}";

            var mlName = r.StrategyName.Length > 23 ? r.StrategyName[..23] : r.StrategyName;
            var betName = r.BettingStrategyName.Length > 36 ? r.BettingStrategyName[..36] : r.BettingStrategyName;

            var sharpeStr = r.SharpeRatio.ToString("F2");
            var roiStr = (r.ROI * 100).ToString("F1") + "%";
            var ddStr = (r.MaxDrawdownPercentage * 100).ToString("F0") + "%";
            var sortinoStr = r.SortinoRatio.ToString("F2");

            Console.WriteLine($"{rank,-5} {mlName,-25} {betName,-38} {sharpeStr,-8} {roiStr,-9} {ddStr,-7} {sortinoStr}");
        }

        // Best per ML Strategy
        Console.WriteLine("\n🤖 BEST CONFIGURATION PER ML STRATEGY:");
        Console.WriteLine($"{"ML Strategy",-35} {"Best Betting Config",-40} {"ROI",-10} {"Sharpe"}");
        Console.WriteLine(new string('─', 95));

        foreach (var mlResult in report.MlStrategyResults.OrderByDescending(m => m.BestROI))
        {
            if (!mlResult.BettingResults.Any()) continue;

            var mlName = mlResult.MlStrategyName.Length > 33 ? mlResult.MlStrategyName[..33] : mlResult.MlStrategyName;
            var betName = mlResult.BestBettingStrategy.Length > 38 ? mlResult.BestBettingStrategy[..38] : mlResult.BestBettingStrategy;
            var roiStr = (mlResult.BestROI * 100).ToString("F2") + "%";
            var sharpeStr = mlResult.BestSharpe.ToString("F2");

            var prefix = mlResult.BestROI > 0 ? "✅" : "❌";
            Console.WriteLine($"{prefix} {mlName,-33} {betName,-40} {roiStr,-10} {sharpeStr}");
        }

        // Best per Betting Strategy
        Console.WriteLine("\n💰 BEST ML MODEL PER BETTING STRATEGY:");
        Console.WriteLine($"{"Betting Config",-45} {"Best ML Strategy",-30} {"ROI",-10} {"Bets"}");
        Console.WriteLine(new string('─', 95));

        var bettingGroups = report.AllResults
            .GroupBy(r => r.BettingStrategyName)
            .Select(g => new
            {
                Config = g.Key,
                Best = g.OrderByDescending(r => r.ROI).First()
            })
            .OrderByDescending(x => x.Best.ROI);

        foreach (var bg in bettingGroups)
        {
            var configName = bg.Config.Length > 43 ? bg.Config[..43] : bg.Config;
            var mlName = bg.Best.StrategyName.Length > 28 ? bg.Best.StrategyName[..28] : bg.Best.StrategyName;
            var roiStr = (bg.Best.ROI * 100).ToString("F2") + "%";

            var prefix = bg.Best.ROI > 0 ? "✅" : "❌";
            Console.WriteLine($"{prefix} {configName,-43} {mlName,-30} {roiStr,-10} {bg.Best.TotalBetsPlaced}");
        }

        // Overall Winners
        Console.WriteLine("\n🏆 OVERALL WINNERS:");
        Console.WriteLine(new string('═', 80));

        if (report.BestOverallROI != null)
        {
            var r = report.BestOverallROI;
            var roiStr = (r.ROI * 100).ToString("F2") + "%";
            var profitStr = r.TotalProfit.ToString("N0");
            Console.WriteLine($"\n   💰 BEST ROI: {r.StrategyName} + {r.BettingStrategyName}");
            Console.WriteLine($"      ROI: {roiStr} | Profit: ${profitStr} | Bets: {r.TotalBetsPlaced}");
        }

        if (report.BestRiskAdjusted != null)
        {
            var r = report.BestRiskAdjusted;
            var sharpeStr = r.SharpeRatio.ToString("F2");
            var roiStr = (r.ROI * 100).ToString("F2") + "%";
            Console.WriteLine($"\n   📈 BEST RISK-ADJUSTED: {r.StrategyName} + {r.BettingStrategyName}");
            Console.WriteLine($"      Sharpe: {sharpeStr} | ROI: {roiStr} | Bets: {r.TotalBetsPlaced}");
        }

        if (report.LowestDrawdown != null)
        {
            var r = report.LowestDrawdown;
            var ddStr = (r.MaxDrawdownPercentage * 100).ToString("F1") + "%";
            var roiStr = (r.ROI * 100).ToString("F2") + "%";
            Console.WriteLine($"\n   🛡️ LOWEST DRAWDOWN (Profitable): {r.StrategyName} + {r.BettingStrategyName}");
            Console.WriteLine($"      Max DD: {ddStr} | ROI: {roiStr} | Bets: {r.TotalBetsPlaced}");
        }

        if (report.BestProfitFactor != null)
        {
            var r = report.BestProfitFactor;
            var pfStr = r.ProfitFactor > 100 ? ">100" : r.ProfitFactor.ToString("F2");
            var roiStr = (r.ROI * 100).ToString("F2") + "%";
            Console.WriteLine($"\n   ⚖️ BEST PROFIT FACTOR: {r.StrategyName} + {r.BettingStrategyName}");
            Console.WriteLine($"      PF: {pfStr} | ROI: {roiStr} | Bets: {r.TotalBetsPlaced}");
        }

        if (report.MostConsistent != null)
        {
            var r = report.MostConsistent;
            var winRateStr = (r.WinRate * 100).ToString("F1") + "%";
            var roiStr = (r.ROI * 100).ToString("F2") + "%";
            Console.WriteLine($"\n   🎯 MOST CONSISTENT (Highest Win Rate): {r.StrategyName} + {r.BettingStrategyName}");
            Console.WriteLine($"      Win Rate: {winRateStr} | ROI: {roiStr} | Bets: {r.TotalBetsPlaced}");
        }

        // Statistical Summary
        Console.WriteLine("\n\n📊 STATISTICAL SUMMARY:");
        Console.WriteLine(new string('─', 60));

        var profitableCount = report.AllResults.Count(r => r.ROI > 0);
        var totalCount = report.AllResults.Count;
        var profitablePct = totalCount > 0 ? (profitableCount * 100.0 / totalCount).ToString("F1") : "0";

        var avgRoi = report.AllResults.Any() ? report.AllResults.Average(r => r.ROI) : 0;
        var avgRoiStr = (avgRoi * 100).ToString("F2") + "%";

        var avgSharpe = report.AllResults.Any() ? report.AllResults.Average(r => r.SharpeRatio) : 0;
        var avgSharpeStr = avgSharpe.ToString("F2");

        var avgWinRate = report.AllResults.Any() ? report.AllResults.Average(r => r.WinRate) : 0;
        var avgWinRateStr = (avgWinRate * 100).ToString("F1") + "%";

        var avgBets = report.AllResults.Any() ? report.AllResults.Average(r => r.TotalBetsPlaced) : 0;
        var avgBetsStr = avgBets.ToString("F0");

        Console.WriteLine($"   Profitable combinations: {profitableCount}/{totalCount} ({profitablePct}%)");
        Console.WriteLine($"   Average ROI: {avgRoiStr}");
        Console.WriteLine($"   Average Sharpe: {avgSharpeStr}");
        Console.WriteLine($"   Average Win Rate: {avgWinRateStr}");
        Console.WriteLine($"   Average Bets per Config: {avgBetsStr}");

        // Edge Analysis
        var lowEdgeResults = report.AllResults.Where(r => r.Configuration.MinEdgeRequired <= 0.05m).ToList();
        var highEdgeResults = report.AllResults.Where(r => r.Configuration.MinEdgeRequired >= 0.10m).ToList();

        if (lowEdgeResults.Any() && highEdgeResults.Any())
        {
            var lowEdgeAvgRoi = (lowEdgeResults.Average(r => r.ROI) * 100).ToString("F2") + "%";
            var highEdgeAvgRoi = (highEdgeResults.Average(r => r.ROI) * 100).ToString("F2") + "%";
            var lowEdgeAvgBets = lowEdgeResults.Average(r => r.TotalBetsPlaced).ToString("F0");
            var highEdgeAvgBets = highEdgeResults.Average(r => r.TotalBetsPlaced).ToString("F0");

            Console.WriteLine();
            Console.WriteLine($"   📊 Edge Threshold Analysis:");
            Console.WriteLine($"      Low edge (≤5%):  Avg ROI {lowEdgeAvgRoi}, Avg Bets {lowEdgeAvgBets}");
            Console.WriteLine($"      High edge (≥10%): Avg ROI {highEdgeAvgRoi}, Avg Bets {highEdgeAvgBets}");

            if (lowEdgeResults.Average(r => r.ROI) > highEdgeResults.Average(r => r.ROI))
            {
                Console.WriteLine($"      → Lower edge thresholds captured more profitable opportunities");
            }
            else
            {
                Console.WriteLine($"      → Higher edge requirements improved average returns");
            }
        }

        // Betting Strategy Analysis
        Console.WriteLine();
        Console.WriteLine($"   📊 Betting Strategy Analysis:");
        
        var strategyGroups = report.AllResults
            .GroupBy(r => r.Configuration.BettingStrategy)
            .Select(g => new
            {
                Strategy = g.Key,
                AvgRoi = g.Average(r => r.ROI),
                AvgSharpe = g.Average(r => r.SharpeRatio),
                ProfitableCount = g.Count(r => r.ROI > 0),
                TotalCount = g.Count()
            })
            .OrderByDescending(x => x.AvgRoi);

        foreach (var sg in strategyGroups)
        {
            var avgRoiSg = (sg.AvgRoi * 100).ToString("F1") + "%";
            var avgSharpeSg = sg.AvgSharpe.ToString("F2");
            var profitableSg = $"{sg.ProfitableCount}/{sg.TotalCount}";
            Console.WriteLine($"      {sg.Strategy,-18} Avg ROI: {avgRoiSg,-8} Sharpe: {avgSharpeSg,-6} Profitable: {profitableSg}");
        }

        // Recommendations
        Console.WriteLine("\n\n💡 RECOMMENDATIONS:");
        Console.WriteLine(new string('─', 60));

        if (report.BestOverallROI != null && report.BestRiskAdjusted != null)
        {
            if (report.BestOverallROI.StrategyName == report.BestRiskAdjusted.StrategyName &&
                report.BestOverallROI.BettingStrategyName == report.BestRiskAdjusted.BettingStrategyName)
            {
                Console.WriteLine($"   ✅ CLEAR WINNER: {report.BestOverallROI.StrategyName}");
                Console.WriteLine($"      With: {report.BestOverallROI.BettingStrategyName}");
                Console.WriteLine($"      Dominates on both ROI and risk-adjusted metrics!");
            }
            else
            {
                Console.WriteLine($"   📈 For MAXIMUM RETURNS:");
                Console.WriteLine($"      ML Model: {report.BestOverallROI.StrategyName}");
                Console.WriteLine($"      Betting:  {report.BestOverallROI.BettingStrategyName}");
                
                var bestRoiStr = (report.BestOverallROI.ROI * 100).ToString("F1") + "%";
                var bestDdStr = (report.BestOverallROI.MaxDrawdownPercentage * 100).ToString("F1") + "%";
                Console.WriteLine($"      Expected: {bestRoiStr} ROI, {bestDdStr} max drawdown");
                
                Console.WriteLine();
                Console.WriteLine($"   🛡️ For SAFER RETURNS:");
                Console.WriteLine($"      ML Model: {report.BestRiskAdjusted.StrategyName}");
                Console.WriteLine($"      Betting:  {report.BestRiskAdjusted.BettingStrategyName}");
                
                var safeRoiStr = (report.BestRiskAdjusted.ROI * 100).ToString("F1") + "%";
                var safeSharpeStr = report.BestRiskAdjusted.SharpeRatio.ToString("F2");
                Console.WriteLine($"      Expected: {safeRoiStr} ROI, {safeSharpeStr} Sharpe ratio");
            }
        }

        if (report.LowestDrawdown != null && report.LowestDrawdown.ROI > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"   🛡️ For CAPITAL PRESERVATION:");
            Console.WriteLine($"      ML Model: {report.LowestDrawdown.StrategyName}");
            Console.WriteLine($"      Betting:  {report.LowestDrawdown.BettingStrategyName}");
            
            var ddStr = (report.LowestDrawdown.MaxDrawdownPercentage * 100).ToString("F1") + "%";
            var roiStr = (report.LowestDrawdown.ROI * 100).ToString("F1") + "%";
            Console.WriteLine($"      Max drawdown only {ddStr} while still achieving {roiStr} ROI");
        }

        // General advice
        Console.WriteLine();
        Console.WriteLine($"   💡 GENERAL ADVICE:");
        
        var kellyResults = report.AllResults.Where(r => r.Configuration.BettingStrategy == BettingStrategyTypeEnum.Kelly).ToList();
        var quarterKellyResults = report.AllResults.Where(r => r.Configuration.BettingStrategy == BettingStrategyTypeEnum.QuarterKelly).ToList();

        if (kellyResults.Any() && quarterKellyResults.Any())
        {
            var kellyAvgDd = kellyResults.Average(r => r.MaxDrawdownPercentage);
            var qKellyAvgDd = quarterKellyResults.Average(r => r.MaxDrawdownPercentage);
            
            if (kellyAvgDd > qKellyAvgDd * 1.5m)
            {
                Console.WriteLine($"      • Full Kelly has significantly higher drawdowns - consider Quarter Kelly");
            }
        }

        var avgProfitableEdge = report.AllResults
            .Where(r => r.ROI > 0)
            .Select(r => r.AverageEdge)
            .DefaultIfEmpty(0)
            .Average();
        
        var avgEdgeStr = (avgProfitableEdge * 100).ToString("F1") + "%";
        Console.WriteLine($"      • Profitable strategies average {avgEdgeStr} edge per bet");
        
        var avgProfitableWinRate = report.AllResults
            .Where(r => r.ROI > 0)
            .Select(r => r.WinRate)
            .DefaultIfEmpty(0)
            .Average();
        
        var winRateProfStr = (avgProfitableWinRate * 100).ToString("F1") + "%";
        Console.WriteLine($"      • Profitable strategies average {winRateProfStr} win rate");
    }

    private decimal CalculateStdDev(List<decimal> values)
    {
        if (values.Count < 2) return 0.0001m;
        var avg = values.Average();
        var sumSquares = values.Sum(v => (v - avg) * (v - avg));
        return (decimal)Math.Sqrt((double)(sumSquares / (values.Count - 1)));
    }

    private decimal CalculateDownsideStdDev(List<decimal> values)
    {
        var negativeReturns = values.Where(v => v < 0).ToList();
        if (negativeReturns.Count < 2) return 0.0001m;
        return CalculateStdDev(negativeReturns);
    }
}