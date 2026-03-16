using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NFCBets.Causal;
using NFCBets.Causal.Interfaces;
using NFCBets.Classical;
using NFCBets.Classical.Interfaces;
using NFCBets.EF.Models;
using NFCBets.Evaluation;
using NFCBets.Evaluation.Enums;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services;
using NFCBets.Services.Enums;
using NFCBets.Services.Interfaces;
using NFCBets.Services.Models;
using NFCBets.Utilities;
using NFCBets.Utilities.Models;

namespace NFCBets;

internal class Program
{
    // Configuration
    private const int DefaultStartRound = 5700;
    private const int DefaultCurrentRound = 9730;
    private const string DefaultModelPath = "Models/foodclub_mp.cd.vd.r.e.cs.c.bt_model.zip";
    private const string CausalModelPath = "Models/foodclub_causal_model.zip";
    
    // Parsed arguments
    private static bool _measurePerformance;
    private static bool _forceCollect;
    private static bool _useParallel;
    private static int _startRound;
    private static int _currentRound;
    private static string _modelPath = DefaultModelPath;
    private static async Task Main(string[] args)
    {
        // Show help if requested
        if (args.Contains("--help") || args.Contains("-h") || args.Length == 0)
        {
            DisplayHelp();
            if (args.Length == 0)
            {
                // Default args for development
                args = new[]
                {
                    "--collect-data",
                    "--measure-performance",
                    "--analyze-interactions",
                    "--select-features",
                    "--retrain",
                    "--compare-ml",
                    "--ml-backtest",
                    "--compare-betting",
                    "--compare-strategies",
                    "--detailed-history"
                };
                Console.WriteLine("\n⚠️ No arguments provided. Using default development args.\n");
            }
            else
            {
                return;
            }
        }
// Parse global options
        ParseGlobalOptions(args);

        Console.WriteLine("🏴‍☠️ Welcome to the Food Club Betting Pipeline!");
        Console.WriteLine($"   Rounds: {_startRound} - {_currentRound}");
        Console.WriteLine($"   Performance Measurement: {(_measurePerformance ? "✅" : "❌")}");
        Console.WriteLine();

        // Build host and services
        using var host = BuildHost(args);
        var services = host.Services;

        // Execute commands
        await ExecuteCommandsAsync(args, services);

        // Display performance summary if measurement was enabled
        if (_measurePerformance)
        {
            PerformanceHelper.DisplaySummary();
        }

        Console.WriteLine("\n✅ Pipeline complete!");
    }

    #region Host Configuration

    private static IHost BuildHost(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .ConfigureServices(services =>
            {
                // Database
                services.AddDbContext<NfcbetsContext>();

                // Core Services
                services.AddScoped<IFoodAdjustmentService, FoodAdjustmentService>();
                services.AddScoped<IFeatureEngineeringService, FeatureEngineeringService>();
                services.AddScoped<IMlModelService, MlModelService>();
                services.AddScoped<IBettingStrategyService, BettingStrategyService>();
                services.AddScoped<IDailyBettingPipeline, DailyBettingPipeline>();

                // Causal Analysis
                services.AddScoped<ICausalInferenceService, CausalInferenceService>();

                // Evaluation Services
                services.AddScoped<IBettingPerformanceEvaluator, BettingPerformanceEvaluator>();
                services.AddScoped<IBettingStrategyComparisonService, BettingStrategyComparisonService>();
                services.AddScoped<IMlStrategyComparisonService, MlStrategyComparisonService>();
                services.AddScoped<ICrossValidationService, CrossValidationService>();
                services.AddScoped<IBacktestService, BacktestService>();

                // Data Services
                services.AddHttpClient<IFoodClubDataService, FoodClubDataService>();
                services.AddScoped<IDataValidationService, DataValidationService>();
                services.AddScoped<IFeatureSelectionService, FeatureSelectionService>();

                // Logging
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            })
            .Build();
    }

    #endregion

    #region Argument Parsing

    private static void ParseGlobalOptions(string[] args)
    {
        _measurePerformance = args.Contains("--measure-performance") || args.Contains("-p");
        _forceCollect = args.Contains("--force-collect");
        _useParallel = args.Contains("--parallel");
        _startRound = ParseIntArg(args, "--start-round=", DefaultStartRound);
        _currentRound = ParseIntArg(args, "--current-round=", DefaultCurrentRound);

        var modelArg = args.FirstOrDefault(a => a.StartsWith("--model="));
        if (modelArg != null)
        {
            _modelPath = modelArg.Split('=')[1];
        }
    }

    private static BacktestConfiguration ParseBacktestConfig(string[] args)
    {
        var config = new BacktestConfiguration
        {
            StartingBankroll = ParseDecimalArg(args, "--bankroll=", 4000m),
            RoundsToSimulate = ParseIntArg(args, "--rounds=", 1000),
            MinEdgeRequired = ParseDecimalArg(args, "--min-edge=", 0.05m),
            MaxBetPercentage = ParseDecimalArg(args, "--max-bet=", 0.10m),
            IncludeDetailedHistory = args.Contains("--detailed-history"),
            SaveBankrollSnapshots = true,
            BetAllArenas = true
        };

        // Parse betting strategy
        config.BettingStrategy = args switch
        {
            _ when args.Contains("--kelly") => BettingStrategyTypeEnum.Kelly,
            _ when args.Contains("--half-kelly") => BettingStrategyTypeEnum.HalfKelly,
            _ when args.Contains("--flat") => BettingStrategyTypeEnum.Flat,
            _ when args.Contains("--value") => BettingStrategyTypeEnum.ValueBetting,
            _ when args.Contains("--proportional") => BettingStrategyTypeEnum.Proportional,
            _ => BettingStrategyTypeEnum.QuarterKelly
        };

        // Parse specific arena
        var arenaArg = args.FirstOrDefault(a => a.StartsWith("--arena="));
        if (arenaArg != null && int.TryParse(arenaArg.Split('=')[1], out var arenaId))
        {
            config.BetAllArenas = false;
            config.SpecificArenaId = arenaId;
        }

        return config;
    }

    private static decimal ParseDecimalArg(string[] args, string prefix, decimal defaultValue)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix));
        if (arg != null && decimal.TryParse(arg.Substring(prefix.Length), out var value))
        {
            return value;
        }
        return defaultValue;
    }

    private static int ParseIntArg(string[] args, string prefix, int defaultValue)
    {
        var arg = args.FirstOrDefault(a => a.StartsWith(prefix));
        if (arg != null && int.TryParse(arg.Substring(prefix.Length), out var value))
        {
            return value;
        }
        return defaultValue;
    }

    #endregion

    #region Command Execution

    private static async Task ExecuteCommandsAsync(string[] args, IServiceProvider services)
    {
        // Data Collection
        if (args.Contains("--collect-data"))
        {
            await ExecuteDataCollectionAsync(services);
        }

        // Data Validation
        if (args.Contains("--validate-data"))
        {
            await ExecuteDataValidationAsync(services);
        }

        // Interaction Analysis (should run before ML comparisons)
        InteractionAnalysisReport? interactionReport = null;
        if (args.Contains("--analyze-interactions") || args.Contains("--full-analysis"))
        {
            interactionReport = await ExecuteInteractionAnalysisAsync(services);
        }

        // Feature Selection
        if (args.Contains("--select-features"))
        {
            await ExecuteFeatureSelectionAsync(services);
        }

        // Model Training
        if (args.Contains("--retrain") || !File.Exists(_modelPath))
        {
            bool evaluate = args.Contains("--evaluate");
            await ExecuteModelTrainingAsync(services, evaluate);
        }
        else
        {
            var mlService = services.GetRequiredService<IMlModelService>();
            Console.WriteLine("📂 Loading existing model...");
            mlService.LoadModel(_modelPath);
        }

        // Cross Validation
        if (ShouldRunCrossValidation(args))
        {
            await ExecuteCrossValidationAsync(services);
        }

        // ML Model Comparison (with optional backtest)
        if (args.Contains("--compare-ml") || args.Contains("--compare-ml-models"))
        {
            bool includeBacktest = !args.Contains("--no-backtest");
            var backtestConfig = ParseBacktestConfig(args);
            await ExecuteMlComparisonAsync(services, interactionReport, includeBacktest, backtestConfig);
        }

        // ML Backtest Only
        if (args.Contains("--ml-backtest"))
        {
            await ExecuteMlBacktestAsync(args, services);
        }

        // Betting Strategy Comparison (for ML models)
        if (args.Contains("--compare-betting"))
        {
            await ExecuteBettingComparisonAsync(args, services);
        }

        // Compare Optimization Strategies (existing functionality)
        if (args.Contains("--compare-strategies"))
        {
            await ExecuteOptimizationComparisonAsync(services);
        }

        // Legacy Backtest
        if (args.Contains("--backtest") && !args.Contains("--ml-backtest"))
        {
            await ExecuteLegacyBacktestAsync(services);
        }

        // Causal Model Training
        if (args.Contains("--causal"))
        {
            await ExecuteCausalTrainingAsync(services);
        }

        // Generate Recommendations (always runs unless --no-recommendations)
        if (!args.Contains("--no-recommendations"))
        {
            await ExecuteGenerateRecommendationsAsync(services);
        }
    }

    private static async Task ExecuteDataCollectionAsync(IServiceProvider services)
    {
        var dataService = services.GetRequiredService<IFoodClubDataService>();

        Console.WriteLine("📥 Collecting historical Food Club data...");
        Console.WriteLine($"   Force collect: {_forceCollect}");
        Console.WriteLine($"   Parallel: {_useParallel}");
        Console.WriteLine($"   Range: {_startRound} to {_currentRound}");

        if (_useParallel)
        {
            Console.WriteLine("   ⚠️ Parallel collection is experimental");
            await RunWithPerformanceAsync("Parallel data collection",
                () => dataService.CollectRangeParallelAsync(_startRound, _currentRound, _forceCollect, 10));
        }
        else
        {
            await RunWithPerformanceAsync("Sequential data collection",
                () => dataService.CollectRangeAsync(_startRound, _currentRound, _forceCollect));
        }
    }

    private static async Task ExecuteDataValidationAsync(IServiceProvider services)
    {
        Console.WriteLine("🔍 Validating data quality...");
        var validationService = services.GetRequiredService<IDataValidationService>();

        var report = await RunWithPerformanceAsync("Data validation",
            () => validationService.ValidateDataQualityAsync(_startRound, _currentRound));

        SaveJsonReport(report, "data_validation");
    }

    private static async Task<InteractionAnalysisReport> ExecuteInteractionAnalysisAsync(IServiceProvider services)
    {
        Console.WriteLine("🔬 Analyzing interaction effects...");
        var featureService = services.GetRequiredService<IFeatureEngineeringService>();

        var data = await RunWithPerformanceAsync("Load training data",
            () => featureService.CreateTrainingDataAsync(4000));

        var analyzer = new InteractionEffectAnalyzer();
        var report = await RunWithPerformanceAsync("Interaction analysis",
            () => analyzer.AnalyzeAllInteractionsAsync(data));

        Console.WriteLine($"   Found {report.AntagonisticInteractions.Count} antagonistic interactions");
        Console.WriteLine($"   Found {report.SynergisticInteractions.Count} synergistic interactions");

        return report;
    }

    private static async Task ExecuteFeatureSelectionAsync(IServiceProvider services)
    {
        Console.WriteLine("🔍 Running automated feature selection...");
        var featureSelection = services.GetRequiredService<IFeatureSelectionService>();

        var report = await RunWithPerformanceAsync("Feature selection",
            () => featureSelection.FindOptimalFeaturesAsync());

        Console.WriteLine($"\n💡 Recommended {report.RecommendedFeatures.Count} features for your model");
    }

    private static async Task ExecuteModelTrainingAsync(IServiceProvider services, bool evaluate)
    {
        var mlService = services.GetRequiredService<IMlModelService>();
        var evaluator = services.GetRequiredService<IBettingPerformanceEvaluator>();

        await RunWithPerformanceAsync("Find rounds with multiple winners",
            () => evaluator.FindRoundsWithMultipleWinnersAsync(_startRound, _currentRound));

        if (evaluate)
        {
            Console.WriteLine("🏋️ Training model with evaluation...");
            await RunWithPerformanceAsync("Training and evaluation",
                () => mlService.TrainAndEvaluateModelAsync());
        }
        else
        {
            Console.WriteLine("🏋️ Training model...");
            await RunWithPerformanceAsync("Model training",
                () => mlService.TrainModelAsync());
        }

        mlService.SaveModel(_modelPath);
        Console.WriteLine($"   Model saved to {_modelPath}");
    }

    private static bool ShouldRunCrossValidation(string[] args)
    {
        if (args.Contains("--force-cross-validate"))
            return true;

        if (args.Contains("--evaluate"))
            return false; // Already included in evaluation

        return args.Contains("--cross-validate");
    }

    private static async Task ExecuteCrossValidationAsync(IServiceProvider services)
    {
        Console.WriteLine("📊 Running cross-validation...");
        var crossValService = services.GetRequiredService<ICrossValidationService>();

        var timeSeriesCV = await RunWithPerformanceAsync("Time-series cross-validation",
            () => crossValService.PerformTimeSeriesCrossValidationAsync());

        var kFoldCV = await RunWithPerformanceAsync("K-fold cross-validation",
            () => crossValService.PerformKFoldCrossValidationAsync());

        var cvReport = new
        {
            TimeSeriesCV = timeSeriesCV,
            KFoldCV = kFoldCV,
            Recommendation = timeSeriesCV.AverageAUC > kFoldCV.AverageAUC
                ? "Use Time-Series CV results (better for temporal data)"
                : "Both methods show similar performance"
        };

        SaveJsonReport(cvReport, "cross_validation");
    }

    private static async Task ExecuteMlComparisonAsync(
        IServiceProvider services,
        InteractionAnalysisReport? interactionReport,
        bool includeBacktest,
        BacktestConfiguration backtestConfig)
    {
        Console.WriteLine("🏆 Comparing ML strategies...");
        var mlComparison = services.GetRequiredService<IMlStrategyComparisonService>();

        var report = await RunWithPerformanceAsync("ML strategy comparison",
            () => mlComparison.CompareAllStrategiesAsync(interactionReport, includeBacktest, backtestConfig));

        Console.WriteLine("\n🏆 ML COMPARISON SUMMARY:");
        Console.WriteLine($"   Best Statistical: {report.RecommendedStrategy} (Auc: {report.BestAuc:F4})");

        if (report.BacktestIncluded)
        {
            Console.WriteLine($"   Best Backtest: {report.BestBacktestStrategy} (ROI: {report.BestBacktestROI:P2})");
        }

        Console.WriteLine($"   Successful: {report.SuccessfulStrategies}/{report.TotalStrategiesTested}");

        foreach (var result in report.Results.Take(3))
        {
            Console.WriteLine($"   #{result.Rank} {result.StrategyName}: Auc={result.Auc:F4}");
        }
    }

    private static async Task ExecuteMlBacktestAsync(string[] args, IServiceProvider services)
    {
        var backtestConfig = ParseBacktestConfig(args);
        var strategyArg = args.FirstOrDefault(a => a.StartsWith("--strategy="));
        var specificStrategy = strategyArg?.Split('=')[1];

        var featureService = services.GetRequiredService<IFeatureEngineeringService>();
        var backtestService = services.GetRequiredService<IBacktestService>();

        var allData = await RunWithPerformanceAsync("Load historical data",
            () => featureService.CreateTrainingDataAsync(4000));

        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();
        Console.WriteLine($"   Loaded {validData.Count} valid records");

        if (specificStrategy != null)
        {
            // Backtest single strategy
            var strategy = CreateMlStrategy(specificStrategy);
            Console.WriteLine($"\n🎯 Backtesting {strategy.StrategyName}...");

            var result = await RunWithPerformanceAsync($"Backtest {strategy.StrategyName}",
                () => backtestService.RunBacktestAsync(strategy, validData, backtestConfig));

            backtestService.DisplayBacktestResults(result);
        }
        else
        {
            // Backtest all strategies
            var strategies = GetDefaultMlStrategies();

            await RunWithPerformanceAsync("Backtest all strategies",
                () => backtestService.CompareStrategiesBacktestAsync(strategies, validData, backtestConfig));
        }
    }

    private static async Task ExecuteBettingComparisonAsync(string[] args, IServiceProvider services)
    {
        var strategyArg = args.FirstOrDefault(a => a.StartsWith("--strategy="));
        var specificStrategy = strategyArg?.Split('=')[1];
        var bankroll = ParseDecimalArg(args, "--bankroll=", 10000m);
        var rounds = ParseIntArg(args, "--rounds=", 1000);

        var featureService = services.GetRequiredService<IFeatureEngineeringService>();
        var bettingComparison = services.GetRequiredService<IBettingStrategyComparisonService>();

        var allData = await RunWithPerformanceAsync("Load historical data",
            () => featureService.CreateTrainingDataAsync(4000));

        var validData = allData.Where(f => f.IsWinner.HasValue).ToList();

        if (specificStrategy != null)
        {
            var strategy = CreateMlStrategy(specificStrategy);
            Console.WriteLine($"\n📊 Comparing betting strategies for {strategy.StrategyName}...");

            await RunWithPerformanceAsync($"Betting comparison for {strategy.StrategyName}",
                () => bettingComparison.CompareBettingStrategiesForMlModelAsync(strategy, validData, bankroll, rounds));
        }
        else
        {
            Console.WriteLine("\n📊 Running full ML × Betting Strategy matrix...");

            await RunWithPerformanceAsync("Full betting strategy matrix",
                () => bettingComparison.CompareAllMlModelsWithBettingStrategiesAsync(validData, bankroll, rounds));
        }
    }

    private static async Task ExecuteOptimizationComparisonAsync(IServiceProvider services)
    {
        Console.WriteLine("📊 Comparing optimization strategies...");
        var comparisonService = services.GetRequiredService<IBettingStrategyComparisonService>();

        var report = await RunWithPerformanceAsync("Optimization comparison",
            () => comparisonService.CompareOptimizationMethodsAsync(_startRound, _currentRound));

        Console.WriteLine($"\n🏆 RECOMMENDATION: Use {report.BestBySharpe} for best risk-adjusted returns");
    }

    private static async Task ExecuteLegacyBacktestAsync(IServiceProvider services)
    {
        Console.WriteLine("💰 Running betting strategy backtest...");
        var evaluator = services.GetRequiredService<IBettingPerformanceEvaluator>();

        var report = await RunWithPerformanceAsync("Betting backtest",
            () => evaluator.BacktestBettingStrategyAsync(_startRound, _currentRound, BetOptimizationMethodEnum.RiskAdjusted));

        SaveBacktestReport(report);
    }

    private static async Task ExecuteCausalTrainingAsync(IServiceProvider services)
    {
        Console.WriteLine("🧬 Training causally-informed model...");
        var mlService = services.GetRequiredService<IMlModelService>();

        await RunWithPerformanceAsync("Causal model training",
            () => mlService.TrainAndEvaluateCausallyInformedModelAsync());

        mlService.SaveModel(CausalModelPath);
        Console.WriteLine($"   Causal model saved to {CausalModelPath}");
    }

    private static async Task ExecuteGenerateRecommendationsAsync(IServiceProvider services)
    {
        Console.WriteLine("\n💰 Generating betting recommendations...");
        var pipeline = services.GetRequiredService<IDailyBettingPipeline>();

        var recommendations = await RunWithPerformanceAsync("Generate recommendations",
            () => pipeline.GenerateRecommendationsAsync(_currentRound, BetOptimizationMethodEnum.RiskAdjusted));

        DisplayRecommendations(recommendations);
        SaveRecommendationsToFile(recommendations);
    }

    #endregion

    #region Performance Helper Wrappers

    private static async Task<T> RunWithPerformanceAsync<T>(string operationName, Func<Task<T>> operation)
    {
        if (_measurePerformance)
        {
            return await PerformanceHelper.MeasureAsync(operationName, operation);
        }
        return await operation();
    }

    private static async Task RunWithPerformanceAsync(string operationName, Func<Task> operation)
    {
        if (_measurePerformance)
        {
            await PerformanceHelper.MeasureAsync(operationName, operation);
        }
        else
        {
            await operation();
        }
    }

    #endregion

    #region ML Strategy Helpers

    private static IMlStrategy CreateMlStrategy(string name)
    {
        return name.ToLower() switch
        {
            "conditional" or "conditionallogistic" => new ConditionalLogisticRegression(),
            "multinomial" or "multinomiallogit" => new MultinomialLogit(),
            "bradley" or "bradleyterry" => new BradleyTerry(),
            "plackett" or "plackettluce" => new PlackettLuce(),
            "binary" or "binaryclassification" => new BinaryClassification(),
            "logistic" or "logisticregression" => new LogisticRegression(),
            "multiclass" or "multiclassperarena" => new MultiClassPerArena(),
            "pairwise" or "pairwisecomparison" => new PairwiseComparison(),
            "ranking" or "learntorank" => new LearnToRank(),
            "softmax" or "softmaxperarena" => new SoftmaxPerArena(),
            "stacking" or "stackingensemble" => new StackingEnsemble(),
            "normalized" or "normalizedensemble" => new NormalizedEnsemble(),
            "multioutput" => new MultiOutput(),
            "multiclasspairwise" => new MultiClassPairwise(),
            _ => throw new ArgumentException($"Unknown strategy: {name}. Use --help to see available strategies.")
        };
    }

    private static Dictionary<string, IMlStrategy> GetDefaultMlStrategies()
    {
        var strategies = new Dictionary<string, IMlStrategy>();

        TryAddStrategy(strategies, "MultinomialLogit", () => new MultinomialLogit());
        TryAddStrategy(strategies, "ConditionalLogistic", () => new ConditionalLogisticRegression());
        TryAddStrategy(strategies, "PlackettLuce", () => new PlackettLuce());
        TryAddStrategy(strategies, "BradleyTerry", () => new BradleyTerry());
        TryAddStrategy(strategies, "Binary", () => new BinaryClassification());
        TryAddStrategy(strategies, "MultiClass", () => new MultiClassPerArena());
        TryAddStrategy(strategies, "NormalizedEnsemble", () => new NormalizedEnsemble());
        TryAddStrategy(strategies, "Logistic", () => new LogisticRegression());

        return strategies;
    }

    private static void TryAddStrategy(Dictionary<string, IMlStrategy> strategies, string name, Func<IMlStrategy> factory)
    {
        try
        {
            strategies[name] = factory();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Could not load {name}: {ex.Message}");
        }
    }

    #endregion

   #region Display and Save Helpers

    private static readonly Dictionary<int, string> ArenaNames = new()
    {
        { 1, "Shipwreck" },
        { 2, "Lagoon" },
        { 3, "Treasure Island" },
        { 4, "Hidden Cove" },
        { 5, "Harpoon Harry's" }
    };

    private static string GetArenaName(int arenaId)
    {
        return ArenaNames.TryGetValue(arenaId, out var name) ? name : $"Arena {arenaId}";
    }

    private static void DisplayHelp()
    {
        Console.WriteLine(@"
╔═══════════════════════════════════════════════════════════════════════════════╗
║                    NFCBets - Food Club Betting Pipeline                        ║
╚═══════════════════════════════════════════════════════════════════════════════╝

USAGE:
    dotnet run -- [commands] [options]

COMMANDS:
  Data Management:
    --collect-data            Collect historical Food Club data
    --validate-data           Validate data quality
    --force-collect           Force re-collection of existing data
    --parallel                Use parallel collection (experimental)

  Analysis:
    --analyze-interactions    Run interaction effect analysis
    --select-features         Run automated feature selection
    --full-analysis           Run complete analysis pipeline

  Model Training:
    --retrain                 Force model retraining
    --evaluate                Include evaluation during training
    --causal                  Train causally-informed model
    --cross-validate          Run cross-validation
    --force-cross-validate    Force cross-validation even with --evaluate

  ML Comparison:
    --compare-ml              Compare all ML strategies (statistical + backtest)
    --compare-ml-models       Alias for --compare-ml
    --no-backtest             Skip backtest phase in ML comparison

  Backtesting:
    --ml-backtest             Run ML model backtest
    --backtest                Run legacy betting backtest
    --strategy=<name>         Specify single ML strategy to test

  Strategy Comparison:
    --compare-strategies      Compare optimization strategies (existing)
    --compare-betting         Compare betting strategies for ML models

GENERAL OPTIONS:
    -p, --measure-performance   Measure and display execution time
    -h, --help                  Display this help message
    --no-recommendations        Skip generating daily recommendations
    --start-round=<n>           Starting round (default: 5700)
    --current-round=<n>         Current round (default: 9730)
    --model=<path>              Model path (default: Models/foodclub_*.zip)

BETTING STRATEGY OPTIONS:
    --kelly                   Use full Kelly criterion
    --half-kelly              Use half Kelly criterion
    --quarter-kelly           Use quarter Kelly criterion (default)
    --flat                    Use flat 2% betting
    --value                   Use value-based betting
    --proportional            Use proportional betting

BACKTEST OPTIONS:
    --bankroll=<amount>       Starting bankroll (default: 10000)
    --rounds=<count>          Rounds to simulate (default: 1000)
    --min-edge=<percent>      Minimum edge to bet (default: 0.05)
    --max-bet=<percent>       Maximum bet percentage (default: 0.10)
    --detailed-history        Include detailed bet history
    --arena=<id>              Only bet on specific arena (1-5)

ARENAS:
    1 - Shipwreck
    2 - Lagoon
    3 - Treasure Island
    4 - Hidden Cove
    5 - Harpoon Harry's

AVAILABLE ML STRATEGIES:
    conditional     Conditional Logistic Regression (Choice Model)
    multinomial     Multinomial Logit (Choice Model)
    bradley         Bradley-Terry Competition Model
    plackett        Plackett-Luce (Generalized Bradley-Terry)
    binary          Binary Classification (LightGBM)
    logistic        Logistic Regression
    multiclass      Multi-Class Per Arena
    multiclasspairwise  Multi-Class with Pairwise Features
    pairwise        Pairwise Comparison
    ranking         Learn to Rank
    softmax         Softmax Per Arena
    stacking        Stacking Ensemble (Meta-Learner)
    normalized      Normalized Ensemble

EXAMPLES:
    # Full pipeline with performance measurement
    dotnet run -- --analyze-interactions --retrain --compare-ml -p

    # Quick statistical comparison only
    dotnet run -- --compare-ml --no-backtest

    # Backtest specific ML strategy
    dotnet run -- --ml-backtest --strategy=multinomial --half-kelly

    # Compare betting strategies for all ML models
    dotnet run -- --compare-betting --bankroll=5000 --rounds=500

    # Full data refresh and validation
    dotnet run -- --collect-data --force-collect --validate-data

    # Train causal model with feature selection
    dotnet run -- --select-features --causal -p

    # Backtest only on Shipwreck arena
    dotnet run -- --ml-backtest --strategy=bradley --arena=1

    # Generate recommendations without retraining
    dotnet run -- --current-round=9730
");
    }

    
    private static void DisplayRecommendations(DailyBettingRecommendations recommendations)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine($"🎲 FOOD CLUB BETTING RECOMMENDATIONS - Round {recommendations.RoundId}");
        Console.WriteLine($"📅 Generated: {recommendations.GeneratedAt:yyyy-MM-dd HH:mm:ss}");
        Console.WriteLine("📌 Note: All odds shown are corrected to minimum 2:1");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════\n");

        if (!recommendations.BetSeries.Any())
        {
            Console.WriteLine("⚠️ No betting strategies generated!");
            Console.WriteLine("   This may indicate:");
            Console.WriteLine("   - No valid predictions available");
            Console.WriteLine("   - All pirates filtered out (check for 1:1 odds)");
            Console.WriteLine("   - Insufficient data for this round");
            return;
        }

        foreach (var series in recommendations.BetSeries)
        {
            Console.WriteLine($"\n🎯 {series.Name.ToUpper()} STRATEGY ({series.RiskLevelEnum})");
            Console.WriteLine($"   {series.Description}");
            Console.WriteLine("   ─────────────────────────────────────────────────────────────────────────────");

            if (!series.Bets.Any())
            {
                Console.WriteLine("   ⚠️ No bets generated for this strategy");
                continue;
            }

            for (var i = 0; i < series.Bets.Count; i++)
            {
                var bet = series.Bets[i];
                DisplayBetLine(i + 1, bet);
            }

            var totalEV = series.Bets.Sum(b => b.ExpectedValue);
            var avgEV = series.Bets.Average(b => b.ExpectedValue);
            var avgProb = series.Bets.Average(b => b.CombinedWinProbability);

            Console.WriteLine("   ─────────────────────────────────────────────────────────────────────────────");
            Console.WriteLine($"   📊 {series.Bets.Count} bets | Avg Win: {avgProb:P1} | Total EV: {totalEV:+0.00;-0.00} | Avg EV: {avgEV:+0.00;-0.00}");
        }

        DisplayOverallSummary(recommendations);
    }


    private static void DisplayBetLine(int index, Bet bet)
    {
        var evIndicator = bet.ExpectedValue switch
        {
            > 1.0 => "🔥🔥",
            > 0.5 => "🔥",
            > 0 => "✅",
            _ => "⚠️"
        };

        // Names come directly from PiratePrediction
        var selections = bet.Pirates
            .OrderBy(p => p.ArenaId)
            .Select(p => $"{p.ArenaName}: {p.PirateName} ({p.CorrectedPayout}:1)");

        var selectionsStr = string.Join(" | ", selections);

        Console.WriteLine($"   {index,2}. {selectionsStr}");
        Console.WriteLine($"       → Payout: {bet.TotalPayout}:1 | Win Chance: {bet.CombinedWinProbability:P1} | EV: {bet.ExpectedValue:+0.00;-0.00} {evIndicator}");
        Console.WriteLine();
    }

      private static void DisplayOverallSummary(DailyBettingRecommendations recommendations)
    {
        var allBets = recommendations.BetSeries.SelectMany(s => s.Bets).ToList();

        if (!allBets.Any())
            return;

        var allPirates = allBets.SelectMany(b => b.Pirates).ToList();

        Console.WriteLine("\n═══════════════════════════════════════════════════════════════════════════════");
        Console.WriteLine("📊 OVERALL SUMMARY");
        Console.WriteLine("═══════════════════════════════════════════════════════════════════════════════");

        // By arena breakdown - names come directly from predictions
        Console.WriteLine("\n   📍 COVERAGE BY ARENA:");
        var byArena = allPirates.GroupBy(p => p.ArenaId).OrderBy(g => g.Key);
        foreach (var arenaGroup in byArena)
        {
            var arenaName = arenaGroup.First().ArenaName;
            var uniquePirates = arenaGroup.Select(p => p.PirateId).Distinct().Count();
            var totalSelections = arenaGroup.Count();
            var avgProb = arenaGroup.Average(p => p.WinProbability);
            var topPirate = arenaGroup.OrderByDescending(p => p.WinProbability).First();

            Console.WriteLine($"      {arenaName,-18} {totalSelections} picks ({uniquePirates} unique) | Avg: {avgProb:P0} | Top: {topPirate.PirateName} ({topPirate.WinProbability:P0})");
        }

        // Top individual pirate picks
        Console.WriteLine("\n   🏆 TOP PIRATE PICKS:");
        var topPirates = allPirates
            .GroupBy(p => (p.ArenaId, p.PirateId))
            .Select(g => new
            {
                Pirate = g.First(),
                AvgProb = g.Average(p => p.WinProbability),
                TimesPicked = g.Count(),
                BestOdds = g.Max(p => p.Payout)
            })
            .OrderByDescending(x => x.AvgProb)
            .Take(5)
            .ToList();

        for (var i = 0; i < topPirates.Count; i++)
        {
            var pick = topPirates[i];
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => "  " };
            Console.WriteLine($"      {medal} {pick.Pirate.PirateName} in {pick.Pirate.ArenaName}: {pick.AvgProb:P1} | {pick.TimesPicked}x picked | {pick.Pirate.CorrectedPayout}:1");
        }

        // Best value bets
        Console.WriteLine("\n   💰 BEST VALUE BETS:");
        var bestValue = allBets.OrderByDescending(b => b.ExpectedValue).Take(5).ToList();
        for (var i = 0; i < bestValue.Count; i++)
        {
            var bet = bestValue[i];
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => "  " };
            var pirates = string.Join(" + ", bet.Pirates.OrderBy(p => p.ArenaId).Select(p => p.PirateName));
            Console.WriteLine($"      {medal} {pirates}: EV {bet.ExpectedValue:+0.00} | {bet.TotalPayout}:1 | {bet.CombinedWinProbability:P1}");
        }

        // Safest bets
        Console.WriteLine("\n   🛡️ SAFEST BETS:");
        var safest = allBets.OrderByDescending(b => b.CombinedWinProbability).Take(5).ToList();
        for (var i = 0; i < safest.Count; i++)
        {
            var bet = safest[i];
            var medal = i switch { 0 => "🥇", 1 => "🥈", 2 => "🥉", _ => "  " };
            var pirates = string.Join(" + ", bet.Pirates.OrderBy(p => p.ArenaId).Select(p => p.PirateName));
            Console.WriteLine($"      {medal} {pirates}: {bet.CombinedWinProbability:P1} | {bet.TotalPayout}:1 | EV {bet.ExpectedValue:+0.00}");
        }

        // Totals
        Console.WriteLine("\n   📈 TOTALS:");
        Console.WriteLine($"      Strategies: {recommendations.BetSeries.Count}");
        Console.WriteLine($"      Total Bets: {allBets.Count}");
        Console.WriteLine($"      Arenas: {allPirates.Select(p => p.ArenaId).Distinct().Count()}/5");
        Console.WriteLine($"      Unique Pirates: {allPirates.Select(p => (p.ArenaId, p.PirateId)).Distinct().Count()}");
        Console.WriteLine($"      Total EV: {allBets.Sum(b => b.ExpectedValue):+0.00;-0.00}");
        Console.WriteLine($"      Avg Win Prob: {allBets.Average(b => b.CombinedWinProbability):P1}");

        // Risk assessment
        var posEvBets = allBets.Count(b => b.ExpectedValue > 0);
        var highConfBets = allBets.Count(b => b.CombinedWinProbability > 0.20);

        Console.WriteLine("\n   ⚡ RISK:");
        Console.WriteLine($"      +EV Bets: {posEvBets}/{allBets.Count} ({(double)posEvBets / allBets.Count:P0})");
        Console.WriteLine($"      High Conf (>20%): {highConfBets}/{allBets.Count}");

        // Strategy breakdown
        Console.WriteLine("\n   📋 BY STRATEGY:");
        foreach (var series in recommendations.BetSeries.Where(s => s.Bets.Any()))
        {
            var ev = series.Bets.Sum(b => b.ExpectedValue);
            var indicator = ev > 0 ? "✅" : "⚠️";
            Console.WriteLine($"      {indicator} {series.Name,-20} {series.Bets.Count} bets | EV: {ev:+0.00;-0.00}");
        }
    }

     private static void SaveRecommendationsToFile(DailyBettingRecommendations recommendations)
    {
        try
        {
            var fileName = $"Recommendations/round_{recommendations.RoundId}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json";
            Directory.CreateDirectory("Recommendations");

            // Names are already in the predictions - serialize with full detail
            var enrichedRecommendations = new
            {
                recommendations.RoundId,
                recommendations.GeneratedAt,
                recommendations.TotalBets,
                BetSeries = recommendations.BetSeries.Select(series => new
                {
                    series.Name,
                    series.Description,
                    RiskLevel = series.RiskLevelEnum.ToString(),
                    BetCount = series.Bets.Count,
                    TotalEV = series.Bets.Sum(b => b.ExpectedValue),
                    Bets = series.Bets.Select(bet => new
                    {
                        bet.CombinedWinProbability,
                        bet.TotalPayout,
                        bet.ExpectedValue,
                        Pirates = bet.Pirates.OrderBy(p => p.ArenaId).Select(p => new
                        {
                            p.ArenaId,
                            p.ArenaName,
                            p.PirateId,
                            p.PirateName,
                            p.WinProbability,
                            Payout = p.CorrectedPayout,
                            p.Edge,
                            IndividualEV = p.ExpectedValue
                        })
                    })
                }),
                Summary = new
                {
                    TotalStrategies = recommendations.BetSeries.Count,
                    TotalBets = recommendations.BetSeries.SelectMany(s => s.Bets).Count(),
                    TotalExpectedValue = recommendations.BetSeries.SelectMany(s => s.Bets).Sum(b => b.ExpectedValue),
                    AverageWinProbability = recommendations.BetSeries
                        .SelectMany(s => s.Bets)
                        .DefaultIfEmpty()
                        .Average(b => b?.CombinedWinProbability ?? 0),
                    ByArena = recommendations.BetSeries
                        .SelectMany(s => s.Bets)
                        .SelectMany(b => b.Pirates)
                        .GroupBy(p => p.ArenaId)
                        .Select(g => new
                        {
                            ArenaId = g.Key,
                            g.First().ArenaName,
                            SelectionCount = g.Count(),
                            UniquePirates = g.Select(p => p.PirateId).Distinct().Count(),
                            TopPirate = g.OrderByDescending(p => p.WinProbability).First().PirateName,
                            AverageWinProbability = g.Average(p => p.WinProbability)
                        })
                        .OrderBy(x => x.ArenaId),
                    TopPicks = recommendations.BetSeries
                        .SelectMany(s => s.Bets)
                        .SelectMany(b => b.Pirates)
                        .GroupBy(p => (p.ArenaId, p.PirateId))
                        .Select(g => new
                        {
                            g.First().ArenaName,
                            g.First().PirateName,
                            AverageProb = g.Average(p => p.WinProbability),
                            TimesPicked = g.Count()
                        })
                        .OrderByDescending(x => x.AverageProb)
                        .Take(10)
                }
            };

            var json = JsonSerializer.Serialize(enrichedRecommendations, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, json);

            Console.WriteLine($"\n💾 Recommendations saved to {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ Could not save recommendations: {ex.Message}");
        }
    }

    private static void SaveBacktestReport(BettingPerformanceReport report)
    {
        try
        {
            Directory.CreateDirectory("Reports");
            var fileName = Path.Combine("Reports", $"backtest_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, json);

            Console.WriteLine($"\n📄 Backtest report saved to {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ Could not save backtest report: {ex.Message}");
        }
    }

    private static void SaveJsonReport<T>(T report, string prefix)
    {
        try
        {
            Directory.CreateDirectory("Reports");
            var fileName = Path.Combine("Reports", $"{prefix}_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");

            var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(fileName, json);

            Console.WriteLine($"\n📄 Report saved to {fileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n⚠️ Could not save report: {ex.Message}");
        }
    }

    #endregion
}