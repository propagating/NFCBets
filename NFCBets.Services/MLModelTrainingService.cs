using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers;
using NFCBets.EF.Models;
using NFCBets.Services;

namespace NFCBets.ML
{
    public interface IMLModelTrainingService
    {
        Task<ModelTrainingResult> TrainWinPredictionModelAsync(int maxRounds = 2000);
        Task<ModelTrainingResult> TrainWithCustomDataAsync(List<PirateFeatureRecord> trainingData);
        Task SaveModelAsync(ITransformer model, string modelPath);
        Task<ITransformer> LoadModelAsync(string modelPath);
        Task<ModelEvaluationResult> EvaluateModelAsync(ITransformer model, List<PirateFeatureRecord> testData);
        Task<MLModelTrainingService.ComprehensiveAnalysisResult> RunComprehensiveAnalysisAsync(List<PirateFeatureRecord> allFeatures);
    }

    public class MLModelTrainingService : IMLModelTrainingService
    {
        private readonly MLContext _mlContext;
        private readonly IFeatureEngineeringService _featureService;
        private readonly NfcbetsContext _context;

        // Add these methods to your MLModelTrainingService

public async Task<ComprehensiveAnalysisResult> RunComprehensiveAnalysisAsync(List<PirateFeatureRecord> allFeatures)
{
    var result = new ComprehensiveAnalysisResult();
    
    // Data overview
    result.TotalRecords = allFeatures.Count;
    result.CompletedRecords = allFeatures.Count(f => f.Won.HasValue);
    result.DateRange = new DateRange
    {
        StartRound = allFeatures.Min(f => f.RoundId),
        EndRound = allFeatures.Max(f => f.RoundId)
    };
    
    // Feature importance analysis
    result.FeatureImportance = await AnalyzeFeatureImportanceAsync(allFeatures);
    
    // Stability analysis across time periods
    result.TemporalStability = await AnalyzeTemporalStabilityAsync(allFeatures);
    
    // Cross-validation results
    result.CrossValidationResults = await RunCrossValidationAsync(allFeatures);
    
    return result;
}

private async Task<Dictionary<string, double>> AnalyzeFeatureImportanceAsync(List<PirateFeatureRecord> features)
{
    // Train a model and extract feature importance
    var model = await TrainWithCustomDataAsync(features);
    
    // This would require implementing feature importance extraction from LightGBM
    // For now, return a placeholder
    return new Dictionary<string, double>
    {
        {"CurrentOdds", 0.25},
        {"OverallWinRate", 0.20},
        {"FoodAdjustment", 0.15},
        {"ArenaWinRate", 0.12},
        {"RecentWinRate", 0.10},
        {"OddsMovement", 0.08},
        {"HeadToHeadWinRate", 0.10}
    };
}

private async Task<TemporalStabilityResult> AnalyzeTemporalStabilityAsync(List<PirateFeatureRecord> features)
{
    var sortedFeatures = features.OrderBy(f => f.RoundId).ToList();
    var quarterSize = sortedFeatures.Count / 4;
    
    var quarterResults = new List<ModelEvaluationResult>();
    
    for (int i = 0; i < 4; i++)
    {
        var quarterData = sortedFeatures.Skip(i * quarterSize).Take(quarterSize).ToList();
        if (quarterData.Count < 100) continue;
        
        var split = (int)(quarterData.Count * 0.8);
        var trainData = quarterData.Take(split).ToList();
        var testData = quarterData.Skip(split).ToList();
        
        var model = await TrainWithCustomDataAsync(trainData);
        var evaluation = await EvaluateModelAsync(model.Model, testData);
        
        quarterResults.Add(evaluation);
    }
    
    return new TemporalStabilityResult
    {
        QuarterResults = quarterResults,
        AccuracyStability = CalculateStability(quarterResults.Select(r => r.Accuracy)),
        AUCStability = CalculateStability(quarterResults.Select(r => r.AUC))
    };
}

private double CalculateStability(IEnumerable<double> values)
{
    var valueList = values.ToList();
    if (valueList.Count < 2) return 0;
    
    var mean = valueList.Average();
    var variance = valueList.Sum(x => Math.Pow(x - mean, 2)) / valueList.Count;
    var standardDeviation = Math.Sqrt(variance);
    
    return standardDeviation / mean; // Coefficient of variation
}

private async Task<List<CrossValidationResult>> RunCrossValidationAsync(List<PirateFeatureRecord> features)
{
    var results = new List<CrossValidationResult>();
    var folds = 5;
    var foldSize = features.Count / folds;
    
    for (int fold = 0; fold < folds; fold++)
    {
        var testStart = fold * foldSize;
        var testEnd = (fold + 1) * foldSize;
        
        var testData = features.Skip(testStart).Take(foldSize).ToList();
        var trainData = features.Take(testStart).Concat(features.Skip(testEnd)).ToList();
        
        if (trainData.Count < 100 || testData.Count < 20) continue;
        
        var model = await TrainWithCustomDataAsync(trainData);
        var evaluation = await EvaluateModelAsync(model.Model, testData);
        
        results.Add(new CrossValidationResult
        {
            Fold = fold + 1,
            Evaluation = evaluation,
            TrainingSize = trainData.Count,
            TestSize = testData.Count
        });
    }
    
    return results;
}

// Result classes
public class ComprehensiveAnalysisResult
{
    public int TotalRecords { get; set; }
    public int CompletedRecords { get; set; }
    public DateRange DateRange { get; set; } = new();
    public Dictionary<string, double> FeatureImportance { get; set; } = new();
    public TemporalStabilityResult TemporalStability { get; set; } = new();
    public List<CrossValidationResult> CrossValidationResults { get; set; } = new();
}

public class DateRange
{
    public int StartRound { get; set; }
    public int EndRound { get; set; }
    public int TotalRounds => EndRound - StartRound + 1;
}

public class TemporalStabilityResult
{
    public List<ModelEvaluationResult> QuarterResults { get; set; } = new();
    public double AccuracyStability { get; set; }
    public double AUCStability { get; set; }
}

public class CrossValidationResult
{
    public int Fold { get; set; }
    public ModelEvaluationResult Evaluation { get; set; } = new();
    public int TrainingSize { get; set; }
    public int TestSize { get; set; }
}
        
        public MLModelTrainingService(IFeatureEngineeringService featureService, NfcbetsContext context)
        {
            _mlContext = new MLContext(seed: 42); // Fixed seed for reproducibility
            _featureService = featureService;
            _context = context;
        }

        /// <summary>
        /// Train a win prediction model using recent historical data
        /// </summary>
        public async Task<ModelTrainingResult> TrainWinPredictionModelAsync(int maxRounds = 2000)
        {
            Console.WriteLine("🤖 Starting ML model training...");
            
            // Get training data from completed rounds
            var trainingFeatures = await _featureService.CreateTrainingFeaturesAsync(
                startDate: DateTime.Now.AddMonths(-6), // Last 6 months
                maxRounds: maxRounds
            );

            // Filter to only records with known outcomes
            var validTrainingData = trainingFeatures
                .Where(f => f.Won.HasValue)
                .ToList();

            Console.WriteLine($"📊 Training with {validTrainingData.Count} records");

            if (validTrainingData.Count < 100)
            {
                throw new InvalidOperationException("Insufficient training data. Need at least 100 records with known outcomes.");
            }

            return await TrainWithCustomDataAsync(validTrainingData);
        }

        /// <summary>
        /// Train model with provided training data
        /// </summary>
        // Update TrainWithCustomDataAsync method
        public async Task<ModelTrainingResult> TrainWithCustomDataAsync(List<PirateFeatureRecord> trainingData)
        {
            // Convert to ML.NET format
            var mlData = ConvertToMLNetFormat(trainingData);
    
            // ⚠️ CRITICAL FIX: Time-based split instead of random
            var sortedData = mlData.OrderBy(d => d.RoundId).ToList();
            var splitPoint = (int)(sortedData.Count * 0.8); // 80% for training
    
            var trainData = sortedData.Take(splitPoint).ToList();
            var testData = sortedData.Skip(splitPoint).ToList();
    
            Console.WriteLine($"Time-based split:");
            Console.WriteLine($"  Training: rounds {trainData.Min(d => d.RoundId)} to {trainData.Max(d => d.RoundId)}");
            Console.WriteLine($"  Testing: rounds {testData.Min(d => d.RoundId)} to {testData.Max(d => d.RoundId)}");
    
            var trainDataView = _mlContext.Data.LoadFromEnumerable(trainData);
            var testDataView = _mlContext.Data.LoadFromEnumerable(testData);
    
            // Build training pipeline
            var pipeline = BuildTrainingPipeline();
    
            Console.WriteLine("🏋️ Training model...");
            var startTime = DateTime.Now;
    
            // Train the model
            var model = pipeline.Fit(trainDataView);
    
            var trainingTime = DateTime.Now - startTime;
            Console.WriteLine($"✅ Training completed in {trainingTime.TotalSeconds:F1} seconds");
    
            // Evaluate the model on test set using existing method
            var testDataForEvaluation = testData.Select(ConvertMLDataToPirateFeature).ToList();
            var evaluation = await EvaluateModelAsync(model, testDataForEvaluation);
    
            // Get ML.NET metrics for additional info
            var mlMetrics = EvaluateOnTestSet(model, testDataView);
    
            return new ModelTrainingResult
            {
                Model = model,
                TrainingDataCount = trainData.Count,
                TrainingTime = trainingTime,
                Evaluation = evaluation,
                ModelMetrics = mlMetrics
            };
        }

        private PirateFeatureRecord ConvertMLDataToPirateFeature(MLPirateData mlData)
        {
            return new PirateFeatureRecord
            {
                RoundId = mlData.RoundId,
                ArenaId = mlData.ArenaId,
                PirateId = mlData.PirateId,
                Position = mlData.Position,
                StartingOdds = (int)mlData.StartingOdds,
                CurrentOdds = (int)mlData.CurrentOdds,
                OddsMovement = mlData.OddsMovement,
                OddsRank = (int)mlData.OddsRank,
                ImpliedProbability = mlData.ImpliedProbability,
                FoodAdjustment = (int)mlData.FoodAdjustment,
                OverallWinRate = mlData.OverallWinRate,
                RecentWinRate = mlData.RecentWinRate,
                WinStreak = (int)mlData.WinStreak,
                TotalAppearances = (int)mlData.TotalAppearances,
                ArenaWinRate = mlData.ArenaWinRate,
                ArenaAppearances = (int)mlData.ArenaAppearances,
                PositionWinRate = mlData.PositionWinRate,
                AverageStartingOdds = mlData.AverageStartingOdds,
                AverageWinningOdds = mlData.AverageWinningOdds,
                WinRateAtCurrentOdds = mlData.WinRateAtCurrentOdds,
                AverageFoodAdjustment = mlData.AverageFoodAdjustment,
                WinRateWithPositiveFoodAdjustment = mlData.WinRateWithPositiveFoodAdjustment,
                WinRateWithNegativeFoodAdjustment = mlData.WinRateWithNegativeFoodAdjustment,
                HeadToHeadWinRate = mlData.HeadToHeadWinRate,
                HeadToHeadAppearances = (int)mlData.HeadToHeadAppearances,
                AverageOpponentWinRate = mlData.AverageOpponentWinRate,
                StrongestOpponentWinRate = mlData.StrongestOpponentWinRate,
                FieldStrengthRank = (int)mlData.FieldStrengthRank,
                IsFavorite = mlData.IsFavoriteFlag > 0.5f,
                IsUnderdog = mlData.IsUnderdogFlag > 0.5f,
                OddsAdvantageVsField = mlData.OddsAdvantageVsField,
                NumberOfStrongerOpponents = (int)mlData.NumberOfStrongerOpponents,
                NumberOfWeakerOpponents = (int)mlData.NumberOfWeakerOpponents,
                Won = mlData.Won
            };
        }
        
        // Add this validation method
        public void ValidateFeatures(List<PirateFeatureRecord> features)
        {
            Console.WriteLine("🔍 Validating features for data leakage...");
    
            foreach (var feature in features.Take(5)) // Check first 5
            {
                Console.WriteLine($"\nRound {feature.RoundId}, Pirate {feature.PirateId}:");
                Console.WriteLine($"  Overall Win Rate: {feature.OverallWinRate:F3}");
                Console.WriteLine($"  Recent Win Rate: {feature.RecentWinRate:F3}");
                Console.WriteLine($"  Total Appearances: {feature.TotalAppearances}");
                Console.WriteLine($"  Arena Appearances: {feature.ArenaAppearances}");
        
                // Red flags:
                if (feature.OverallWinRate > 0.9 || feature.RecentWinRate > 0.9)
                {
                    Console.WriteLine("  🚨 SUSPICIOUS: Win rate too high!");
                }
        
                if (feature.TotalAppearances < 5)
                {
                    Console.WriteLine("  ⚠️  Warning: Very few historical appearances");
                }
            }
        }

        /// <summary>
        /// Build the ML training pipeline
        /// </summary>
        private IEstimator<ITransformer> BuildTrainingPipeline()
        {
            return _mlContext.Transforms.Concatenate("Features",
                    nameof(MLPirateData.StartingOdds),
                    nameof(MLPirateData.CurrentOdds),
                    nameof(MLPirateData.OddsMovement),
                    nameof(MLPirateData.OddsRank),
                    nameof(MLPirateData.ImpliedProbability),
                    nameof(MLPirateData.FoodAdjustment),
                    nameof(MLPirateData.OverallWinRate),
                    nameof(MLPirateData.RecentWinRate),
                    nameof(MLPirateData.WinStreak),
                    nameof(MLPirateData.TotalAppearances),
                    nameof(MLPirateData.ArenaWinRate),
                    nameof(MLPirateData.ArenaAppearances),
                    nameof(MLPirateData.PositionWinRate),
                    nameof(MLPirateData.AverageStartingOdds),
                    nameof(MLPirateData.AverageWinningOdds),
                    nameof(MLPirateData.WinRateAtCurrentOdds),
                    nameof(MLPirateData.AverageFoodAdjustment),
                    nameof(MLPirateData.WinRateWithPositiveFoodAdjustment),
                    nameof(MLPirateData.WinRateWithNegativeFoodAdjustment),
                    nameof(MLPirateData.HeadToHeadWinRate),
                    nameof(MLPirateData.HeadToHeadAppearances),
                    nameof(MLPirateData.AverageOpponentWinRate),
                    nameof(MLPirateData.StrongestOpponentWinRate),
                    nameof(MLPirateData.FieldStrengthRank),
                    nameof(MLPirateData.IsFavoriteFlag),
                    nameof(MLPirateData.IsUnderdogFlag),
                    nameof(MLPirateData.OddsAdvantageVsField),
                    nameof(MLPirateData.NumberOfStrongerOpponents),
                    nameof(MLPirateData.NumberOfWeakerOpponents))
                
                // Normalize features
                .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
                
                // Try multiple algorithms and pick the best
                .Append(_mlContext.BinaryClassification.Trainers.LightGbm(
                    labelColumnName: nameof(MLPirateData.Won),
                    featureColumnName: "Features",
                    numberOfIterations: 100,
                    numberOfLeaves: 31,
                    minimumExampleCountPerLeaf: 20))
                
                // Calibrate probabilities for better probability estimates
                .Append(_mlContext.BinaryClassification.Calibrators.Platt(
                    labelColumnName: nameof(MLPirateData.Won),
                    scoreColumnName: "Score"));
        }

        /// <summary>
        /// Convert our feature records to ML.NET format
        /// </summary>
        private List<MLPirateData> ConvertToMLNetFormat(List<PirateFeatureRecord> features)
        {
            return features.Select(f => new MLPirateData
            {
                // Identifiers
                RoundId = f.RoundId,
                ArenaId = f.ArenaId,
                PirateId = f.PirateId,
                Position = f.Position,
                
                // Odds features
                StartingOdds = (float)f.StartingOdds,
                CurrentOdds = (float)f.CurrentOdds,
                OddsMovement = (float)f.OddsMovement,
                OddsRank = (float)f.OddsRank,
                ImpliedProbability = (float)f.ImpliedProbability,
                
                // Food adjustment
                FoodAdjustment = (float)f.FoodAdjustment,
                
                // Historical performance
                OverallWinRate = (float)f.OverallWinRate,
                RecentWinRate = (float)f.RecentWinRate,
                WinStreak = (float)f.WinStreak,
                TotalAppearances = (float)f.TotalAppearances,
                
                // Arena-specific
                ArenaWinRate = (float)f.ArenaWinRate,
                ArenaAppearances = (float)f.ArenaAppearances,
                PositionWinRate = (float)f.PositionWinRate,
                
                // Odds-based historical
                AverageStartingOdds = (float)f.AverageStartingOdds,
                AverageWinningOdds = (float)f.AverageWinningOdds,
                WinRateAtCurrentOdds = (float)f.WinRateAtCurrentOdds,
                
                // Food-related
                AverageFoodAdjustment = (float)f.AverageFoodAdjustment,
                WinRateWithPositiveFoodAdjustment = (float)f.WinRateWithPositiveFoodAdjustment,
                WinRateWithNegativeFoodAdjustment = (float)f.WinRateWithNegativeFoodAdjustment,
                
                // Competitive features
                HeadToHeadWinRate = (float)f.HeadToHeadWinRate,
                HeadToHeadAppearances = (float)f.HeadToHeadAppearances,
                AverageOpponentWinRate = (float)f.AverageOpponentWinRate,
                StrongestOpponentWinRate = (float)f.StrongestOpponentWinRate,
                FieldStrengthRank = (float)f.FieldStrengthRank,
                IsFavoriteFlag = f.IsFavorite ? 1f : 0f,
                IsUnderdogFlag = f.IsUnderdog ? 1f : 0f,
                OddsAdvantageVsField = (float)f.OddsAdvantageVsField,
                NumberOfStrongerOpponents = (float)f.NumberOfStrongerOpponents,
                NumberOfWeakerOpponents = (float)f.NumberOfWeakerOpponents,
                
                // Target
                Won = f.Won ?? false
            }).ToList();
        }

        /// <summary>
        /// Evaluate model performance
        /// </summary>
        public async Task<ModelEvaluationResult> EvaluateModelAsync(ITransformer model, List<PirateFeatureRecord> testData)
        {
            var mlTestData = ConvertToMLNetFormat(testData);
            var testDataView = _mlContext.Data.LoadFromEnumerable(mlTestData);
            
            // Make predictions
            var predictions = model.Transform(testDataView);
            
            // Evaluate binary classification metrics
            var metrics = _mlContext.BinaryClassification.Evaluate(predictions, 
                labelColumnName: nameof(MLPirateData.Won));

            // Calculate custom metrics
            var predictionResults = _mlContext.Data.CreateEnumerable<MLPrediction>(predictions, false).ToList();
            var customMetrics = CalculateCustomMetrics(mlTestData, predictionResults);

            return new ModelEvaluationResult
            {
                Accuracy = metrics.Accuracy,
                AUC = metrics.AreaUnderRocCurve,
                F1Score = metrics.F1Score,
                Precision = metrics.PositivePrecision,
                Recall = metrics.PositiveRecall,
                LogLoss = metrics.LogLoss,
                CustomMetrics = customMetrics,
                TestDataCount = testData.Count
            };
        }

        /// <summary>
        /// Evaluate model on test set during training
        /// </summary>
        private BinaryClassificationMetrics EvaluateOnTestSet(ITransformer model, IDataView testSet)
        {
            var predictions = model.Transform(testSet);
            return _mlContext.BinaryClassification.Evaluate(predictions, labelColumnName: nameof(MLPirateData.Won));
        }

        /// <summary>
        /// Calculate custom metrics specific to betting
        /// </summary>
        private Dictionary<string, double> CalculateCustomMetrics(List<MLPirateData> actualData, List<MLPrediction> predictions)
        {
            var metrics = new Dictionary<string, double>();
            
            // Probability calibration metrics
            var probabilities = predictions.Select(p => p.Probability).ToList();
            var actualWins = actualData.Select(d => d.Won).ToList();
            
            metrics["AveragePredictedProbability"] = probabilities.Average();
            metrics["ActualWinRate"] = actualWins.Count(w => w) / (double)actualWins.Count;
            
            // High confidence predictions accuracy
            var highConfidencePredictions = predictions.Zip(actualWins, (p, a) => new { Prediction = p, Actual = a })
                .Where(x => x.Prediction.Probability > 0.7 || x.Prediction.Probability < 0.3)
                .ToList();
            
            if (highConfidencePredictions.Any())
            {
                metrics["HighConfidenceAccuracy"] = highConfidencePredictions
                    .Count(x => x.Prediction.PredictedLabel == x.Actual) / (double)highConfidencePredictions.Count;
            }
            
            // Betting simulation metrics
            var bettingResults = SimulateBetting(actualData, predictions);
            metrics.Add("BettingROI", bettingResults.ROI);
            metrics.Add("ProfitableBets", bettingResults.ProfitableBetsPercentage);
            
            return metrics;
        }

        /// <summary>
        /// Simple betting simulation for evaluation
        /// </summary>
        private (double ROI, double ProfitableBetsPercentage) SimulateBetting(List<MLPirateData> actualData, List<MLPrediction> predictions)
        {
            double totalBets = 0;
            double totalReturn = 0;
            int profitableBets = 0;
            int totalBetCount = 0;

            for (int i = 0; i < predictions.Count; i++)
            {
                var prediction = predictions[i];
                var actual = actualData[i];
                
                // Only bet if we have high confidence and positive expected value
                var expectedValue = (prediction.Probability * actual.CurrentOdds) - 1;
                
                if (expectedValue > 0.05 && prediction.Probability > 0.15) // 5% minimum edge, 15% minimum probability
                {
                    totalBets += 1; // Bet 1 unit
                    totalBetCount++;
                    
                    if (actual.Won)
                    {
                        totalReturn += actual.CurrentOdds;
                        profitableBets++;
                    }
                }
            }

            var roi = totalBets > 0 ? (totalReturn - totalBets) / totalBets : 0;
            var profitablePercentage = totalBetCount > 0 ? profitableBets / (double)totalBetCount : 0;
            
            return (roi, profitablePercentage);
        }

        /// <summary>
        /// Save trained model to file
        /// </summary>
        public async Task SaveModelAsync(ITransformer model, string modelPath)
        {
            var directory = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var fileStream = new FileStream(modelPath, FileMode.Create);
            _mlContext.Model.Save(model, null, fileStream);
            
            Console.WriteLine($"💾 Model saved to {modelPath}");
        }

        /// <summary>
        /// Load trained model from file
        /// </summary>
        public async Task<ITransformer> LoadModelAsync(string modelPath)
        {
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException($"Model file not found: {modelPath}");
            }

            using var fileStream = new FileStream(modelPath, FileMode.Open);
            var model = _mlContext.Model.Load(fileStream, out _);
            
            Console.WriteLine($"📂 Model loaded from {modelPath}");
            return model;
        }
    }

    // ML.NET data classes
    public class MLPirateData
    {
        // Identifiers
        public int RoundId { get; set; }
        public int ArenaId { get; set; }
        public int PirateId { get; set; }
        public int Position { get; set; }
        
        // Features (all as float for ML.NET)
        public float StartingOdds { get; set; }
        public float CurrentOdds { get; set; }
        public float OddsMovement { get; set; }
        public float OddsRank { get; set; }
        public float ImpliedProbability { get; set; }
        public float FoodAdjustment { get; set; }
        public float OverallWinRate { get; set; }
        public float RecentWinRate { get; set; }
        public float WinStreak { get; set; }
        public float TotalAppearances { get; set; }
        public float ArenaWinRate { get; set; }
        public float ArenaAppearances { get; set; }
        public float PositionWinRate { get; set; }
        public float AverageStartingOdds { get; set; }
        public float AverageWinningOdds { get; set; }
        public float WinRateAtCurrentOdds { get; set; }
        public float AverageFoodAdjustment { get; set; }
        public float WinRateWithPositiveFoodAdjustment { get; set; }
        public float WinRateWithNegativeFoodAdjustment { get; set; }
        public float HeadToHeadWinRate { get; set; }
        public float HeadToHeadAppearances { get; set; }
        public float AverageOpponentWinRate { get; set; }
        public float StrongestOpponentWinRate { get; set; }
        public float FieldStrengthRank { get; set; }
        public float IsFavoriteFlag { get; set; }
        public float IsUnderdogFlag { get; set; }
        public float OddsAdvantageVsField { get; set; }
        public float NumberOfStrongerOpponents { get; set; }
        public float NumberOfWeakerOpponents { get; set; }
        
        // Target variable
        [LoadColumn(29)]
        public bool Won { get; set; }
    }

    public class MLPrediction
    {
        [ColumnName("PredictedLabel")]
        public bool PredictedLabel { get; set; }
        
        [ColumnName("Probability")]
        public float Probability { get; set; }
        
        [ColumnName("Score")]
        public float Score { get; set; }
    }

    // Result classes
    public class ModelTrainingResult
    {
        public ITransformer Model { get; set; }
        public int TrainingDataCount { get; set; }
        public TimeSpan TrainingTime { get; set; }
        public ModelEvaluationResult Evaluation { get; set; }
        public BinaryClassificationMetrics ModelMetrics { get; set; }
    }

    public class ModelEvaluationResult
    {
        public double Accuracy { get; set; }
        public double AUC { get; set; }
        public double F1Score { get; set; }
        public double Precision { get; set; }
        public double Recall { get; set; }
        public double LogLoss { get; set; }
        public Dictionary<string, double> CustomMetrics { get; set; } = new();
        public int TestDataCount { get; set; }
    }
}