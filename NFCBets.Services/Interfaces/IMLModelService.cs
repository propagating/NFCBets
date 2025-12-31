using NFCBets.Classical.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Services.Interfaces;

public interface IMlModelService
{
    // Training
    Task TrainModelAsync();
    Task TrainAndEvaluateModelAsync();
    Task TrainAndEvaluateCausallyInformedModelAsync();

    // Model persistence
    void SaveModel(string path);
    void LoadModel(string path);

    // Predictions
    /// <summary>
    /// Predict for a specific round (loads features from database, includes names)
    /// </summary>
    Task<List<PiratePrediction>> PredictRoundAsync(int roundId);

    /// <summary>
    /// Predict from pre-loaded PirateFeatureRecord (for backtesting/evaluation)
    /// </summary>
    Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features, bool useCache = true);

    /// <summary>
    /// Predict from pre-loaded MlPirateFeature (for strategy comparison)
    /// </summary>
    Task<List<PiratePrediction>> PredictAsync(List<MlPirateFeature> features, bool useCache = true);

    /// <summary>
    /// Clear the prediction cache
    /// </summary>
    void ClearPredictionCache();
}