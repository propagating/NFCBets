using Microsoft.ML;
using NFCBets.Utilities.Constants;
using NFCBets.Utilities.Models;

namespace NFCBets.Utilities;

/// <summary>
/// Centralized pipeline builder for all ML models
/// </summary>
public class PipelineBuilder(MLContext mlContext)
{
    #region Feature Name Collections

    /// <summary>
    /// Core features - always included
    /// </summary>
    public static string[] CoreFeatures =>
    [
        nameof(MlPirateFeature.Position),
        nameof(MlPirateFeature.ArenaId),
        nameof(MlPirateFeature.CurrentOdds),
        nameof(MlPirateFeature.OpeningOdds),
        nameof(MlPirateFeature.FoodAdjustment),
        nameof(MlPirateFeature.Strength),
        nameof(MlPirateFeature.Weight)
    ];

    /// <summary>
    /// Historical performance features
    /// </summary>
    public static string[] HistoricalFeatures =>
    [
        nameof(MlPirateFeature.HistoricalWinRate),
        nameof(MlPirateFeature.TotalAppearances),
        nameof(MlPirateFeature.ArenaWinRate),
        nameof(MlPirateFeature.RecentWinRate),
        nameof(MlPirateFeature.WinRateVsCurrentRivals),
        nameof(MlPirateFeature.MatchesVsCurrentRivals),
        nameof(MlPirateFeature.AvgRivalStrength)
    ];

    /// <summary>
    /// Derived/calculated features
    /// </summary>
    public static string[] DerivedFeatures =>
    [
        nameof(MlPirateFeature.OddsMovement),
        nameof(MlPirateFeature.OddsMovementPercent),
        nameof(MlPirateFeature.ImpliedProbability),
        nameof(MlPirateFeature.RelativeStrength),
        nameof(MlPirateFeature.EffectiveStrength)
    ];

    /// <summary>
    /// Binary indicator features
    /// </summary>
    public static string[] BinaryFeatures =>
    [
        nameof(MlPirateFeature.IsOddsFavorite),
        nameof(MlPirateFeature.IsStrengthFavorite),
        nameof(MlPirateFeature.IsEffectiveStrengthFavorite),
        nameof(MlPirateFeature.HasOddsShortened),
        nameof(MlPirateFeature.HasOddsDrifted),
        nameof(MlPirateFeature.HasPositiveFoodAdjustment),
        nameof(MlPirateFeature.HasNegativeFoodAdjustment),
        nameof(MlPirateFeature.IsPositionOne),
        nameof(MlPirateFeature.IsPositionTwo),
        nameof(MlPirateFeature.IsPositionThree),
        nameof(MlPirateFeature.IsPositionFour),
        nameof(MlPirateFeature.IsUndervalued),
        nameof(MlPirateFeature.IsHotStreak),
        nameof(MlPirateFeature.IsArenaSpecialist)
    ];

    /// <summary>
    /// Arena one-hot encoded features
    /// </summary>
    public static string[] ArenaFeatures =>
    [
        nameof(MlPirateFeature.IsArenaShipwreck),
        nameof(MlPirateFeature.IsArenaLagoon),
        nameof(MlPirateFeature.IsArenaTreasureIsland),
        nameof(MlPirateFeature.IsArenaHiddenCove),
        nameof(MlPirateFeature.IsArenaHarpoonHarrys)
    ];

    /// <summary>
    /// Antagonistic interaction penalty features
    /// </summary>
    public static string[] PenaltyFeatures =>
    [
        nameof(MlPirateFeature.PenaltyFoodPosition),
        nameof(MlPirateFeature.PenaltyFoodFavorite),
        nameof(MlPirateFeature.PenaltyStrengthPosition),
        nameof(MlPirateFeature.PenaltyStrengthWeakRivals),
        nameof(MlPirateFeature.PenaltyFavoriteInexperienced),
        nameof(MlPirateFeature.PenaltyLowStrengthFavorite),
        nameof(MlPirateFeature.PenaltyOddsShortenedLowStrength),
        nameof(MlPirateFeature.PenaltyArenaSpecialistColdStreak)
    ];

    /// <summary>
    /// Synergistic interaction bonus features
    /// </summary>
    public static string[] BonusFeatures =>
    [
        nameof(MlPirateFeature.BonusUndervaluedStrong),
        nameof(MlPirateFeature.BonusArenaSpecialistModerateOdds),
        nameof(MlPirateFeature.BonusHotStreakBeatsRivals),
        nameof(MlPirateFeature.BonusFoodPositionThree),
        nameof(MlPirateFeature.BonusOddsShortenedStrong),
        nameof(MlPirateFeature.BonusFavoriteArenaSpecialist),
        nameof(MlPirateFeature.BonusStrengthPlusFood),
        nameof(MlPirateFeature.BonusHotStreakFavorite)
    ];

    /// <summary>
    /// Three-way interaction features
    /// </summary>
    public static string[] ThreeWayFeatures =>
    [
        nameof(MlPirateFeature.ThreeWayFoodPositionStrength),
        nameof(MlPirateFeature.ThreeWayUndervaluedStrongBeatsRivals),
        nameof(MlPirateFeature.ThreeWayFavoriteSpecialistHotStreak),
        nameof(MlPirateFeature.ThreeWayStrengthFoodPositionThree)
    ];

    /// <summary>
    /// All features combined
    /// </summary>
    public static string[] AllFeatures => CoreFeatures
        .Concat(HistoricalFeatures)
        .Concat(DerivedFeatures)
        .Concat(BinaryFeatures)
        .Concat(ArenaFeatures)
        .Concat(PenaltyFeatures)
        .Concat(BonusFeatures)
        .Concat(ThreeWayFeatures)
        .ToArray();

    /// <summary>
    /// Minimal features for fast/simple models
    /// </summary>
    public static string[] MinimalFeatures =>
    [
        nameof(MlPirateFeature.CurrentOdds),
        nameof(MlPirateFeature.Strength),
        nameof(MlPirateFeature.FoodAdjustment),
        nameof(MlPirateFeature.HistoricalWinRate),
        nameof(MlPirateFeature.ArenaWinRate),
        nameof(MlPirateFeature.RecentWinRate),
        nameof(MlPirateFeature.ImpliedProbability),
        nameof(MlPirateFeature.IsOddsFavorite)
    ];

    /// <summary>
    /// Standard features (no interactions)
    /// </summary>
    public static string[] StandardFeatures => CoreFeatures
        .Concat(HistoricalFeatures)
        .Concat(DerivedFeatures)
        .Concat(BinaryFeatures)
        .ToArray();

    /// <summary>
    /// Features with interactions (full model)
    /// </summary>
    public static string[] FeaturesWithInteractions => StandardFeatures
        .Concat(ArenaFeatures)
        .Concat(PenaltyFeatures)
        .Concat(BonusFeatures)
        .Concat(ThreeWayFeatures)
        .ToArray();

    #endregion

    #region Pipeline Builders

    /// <summary>
    /// Build a full-featured LightGBM pipeline
    /// </summary>
    public IEstimator<ITransformer> BuildLightGbmPipeline(
        string[] features = null,
        int numberOfLeaves = 31,
        int minimumExampleCountPerLeaf = 20,
        double learningRate = 0.05,
        int numberOfIterations = 100)
    {
        return mlContext.Transforms.Concatenate("Features", features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: numberOfLeaves,
                minimumExampleCountPerLeaf: minimumExampleCountPerLeaf,
                learningRate: learningRate,
                numberOfIterations: numberOfIterations));
    }

    /// <summary>
    /// Build a fast training LightGBM pipeline
    /// </summary>
    public IEstimator<ITransformer> BuildFastLightGbmPipeline()
    {
        return BuildLightGbmPipeline(
            features: StandardFeatures,
            numberOfLeaves: 15,
            minimumExampleCountPerLeaf: 50,
            learningRate: 0.1,
            numberOfIterations: 50);
    }

    /// <summary>
    /// Build a conservative LightGBM pipeline (less overfitting)
    /// </summary>
    public IEstimator<ITransformer> BuildConservativeLightGbmPipeline()
    {
        return BuildLightGbmPipeline(
            features: StandardFeatures,
            numberOfLeaves: 10,
            minimumExampleCountPerLeaf: 100,
            learningRate: 0.02,
            numberOfIterations: 200);
    }

    /// <summary>
    /// Build an aggressive LightGBM pipeline (more complex)
    /// </summary>
    public IEstimator<ITransformer> BuildAggressiveLightGbmPipeline()
    {
        return BuildLightGbmPipeline(
            features: AllFeatures,
            numberOfLeaves: 63,
            minimumExampleCountPerLeaf: 10,
            learningRate: 0.05,
            numberOfIterations: 150);
    }

    /// <summary>
    /// Build a logistic regression pipeline
    /// </summary>
    public IEstimator<ITransformer> BuildLogisticRegressionPipeline(string[] features = null)
    {
        features ??= StandardFeatures;

        return mlContext.Transforms.Concatenate("Features", features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                l2Regularization: 0.1f));
    }

    /// <summary>
    /// Build a FastTree pipeline
    /// </summary>
    public IEstimator<ITransformer> BuildFastTreePipeline(string[] features = null)
    {
        features ??= StandardFeatures;

        return mlContext.Transforms.Concatenate("Features", features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 20,
                minimumExampleCountPerLeaf: 50,
                numberOfTrees: 100));
    }

    /// <summary>
    /// Build a FastForest pipeline
    /// </summary>
    public IEstimator<ITransformer> BuildFastForestPipeline(string[] features = null)
    {
        features ??= StandardFeatures;

        return mlContext.Transforms.Concatenate("Features", features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.FastForest(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 20,
                numberOfTrees: 100));
    }

    /// <summary>
    /// Build an averaged perceptron pipeline
    /// </summary>
    public IEstimator<ITransformer> BuildAveragedPerceptronPipeline(string[] features = null)
    {
        features ??= MinimalFeatures;

        return mlContext.Transforms.Concatenate("Features", features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.AveragedPerceptron(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfIterations: 100));
    }

    /// <summary>
    /// Build a calibrated pipeline (with Platt scaling)
    /// </summary>
    public IEstimator<ITransformer> BuildCalibratedPipeline(string[] features = null)
    {
        features ??= AllFeatures;

        return mlContext.Transforms.Concatenate("Features", features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features,
                numberOfLeaves: 31,
                minimumExampleCountPerLeaf: 20,
                learningRate: 0.05,
                numberOfIterations: 100))
            .Append(mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: ColumnNames.Label));
    }

    /// <summary>
    /// Build a pipeline with custom features
    /// </summary>
    public IEstimator<ITransformer> BuildCustomPipeline(
        string[] features,
        PipelineType pipelineType = PipelineType.LightGbm,
        bool calibrate = false)
    {
        
        var featurePipeline = mlContext.Transforms.Concatenate("Features",features)
            .Append(mlContext.Transforms.NormalizeMinMax("Features"));

        
        IEstimator<ITransformer> trainer = pipelineType switch
        {
            PipelineType.LightGbm => mlContext.BinaryClassification.Trainers.LightGbm(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features),
            PipelineType.FastTree => mlContext.BinaryClassification.Trainers.FastTree(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features),
            PipelineType.FastForest => mlContext.BinaryClassification.Trainers.FastForest(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features),
            PipelineType.LogisticRegression => mlContext.BinaryClassification.Trainers.LbfgsLogisticRegression(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features),
            PipelineType.AveragedPerceptron => mlContext.BinaryClassification.Trainers.AveragedPerceptron(
                labelColumnName: ColumnNames.Label,
                featureColumnName: ColumnNames.Features),
            _ => throw new ArgumentOutOfRangeException(nameof(pipelineType))
        };

        IEstimator<ITransformer> fullPipeline = featurePipeline.Append(trainer);

        if (calibrate)
        {
            fullPipeline = fullPipeline.Append(mlContext.BinaryClassification.Calibrators.Platt(
                labelColumnName: ColumnNames.Label));
        }
        return fullPipeline;
    }

    #endregion

    #region Feature Set Helpers

    /// <summary>
    /// Get features based on a preset configuration
    /// </summary>
    public static string[] GetFeatureSet(FeatureSet featureSet)
    {
        return featureSet switch
        {
            FeatureSet.Minimal => MinimalFeatures,
            FeatureSet.Standard => StandardFeatures,
            FeatureSet.WithInteractions => FeaturesWithInteractions,
            FeatureSet.All => AllFeatures,
            FeatureSet.CoreOnly => CoreFeatures,
            FeatureSet.HistoricalOnly => HistoricalFeatures,
            _ => AllFeatures
        };
    }

    /// <summary>
    /// Build a feature set from category flags
    /// </summary>
    public static string[] BuildFeatureSet(
        bool includeCore = true,
        bool includeHistorical = true,
        bool includeDerived = true,
        bool includeBinary = true,
        bool includeArena = true,
        bool includePenalties = true,
        bool includeBonuses = true,
        bool includeThreeWay = true)
    {
        var features = new List<string>();

        if (includeCore) features.AddRange(CoreFeatures);
        if (includeHistorical) features.AddRange(HistoricalFeatures);
        if (includeDerived) features.AddRange(DerivedFeatures);
        if (includeBinary) features.AddRange(BinaryFeatures);
        if (includeArena) features.AddRange(ArenaFeatures);
        if (includePenalties) features.AddRange(PenaltyFeatures);
        if (includeBonuses) features.AddRange(BonusFeatures);
        if (includeThreeWay) features.AddRange(ThreeWayFeatures);

        return features.ToArray();
    }

    /// <summary>
    /// Get feature count for logging
    /// </summary>
    public static int GetFeatureCount(string[] features = null)
    {
        return (features ?? AllFeatures).Length;
    }

    #endregion
}

/// <summary>
/// Pipeline type enumeration
/// </summary>
public enum PipelineType
{
    LightGbm,
    FastTree,
    FastForest,
    LogisticRegression,
    AveragedPerceptron
}

/// <summary>
/// Feature set presets
/// </summary>
public enum FeatureSet
{
    /// <summary>
    /// Minimal features for fast training
    /// </summary>
    Minimal,

    /// <summary>
    /// Standard features without interactions
    /// </summary>
    Standard,

    /// <summary>
    /// Standard features plus all interactions
    /// </summary>
    WithInteractions,

    /// <summary>
    /// All available features
    /// </summary>
    All,

    /// <summary>
    /// Core features only
    /// </summary>
    CoreOnly,

    /// <summary>
    /// Historical features only
    /// </summary>
    HistoricalOnly
}