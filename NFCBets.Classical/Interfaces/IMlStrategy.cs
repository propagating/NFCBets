using NFCBets.Classical.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Classical.Interfaces;

public interface IMlStrategy
{
    string StrategyName { get; }
    Task TrainAsync(List<PirateFeatureRecord> trainingData, InteractionAnalysisReport interactionReport = null);
    Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features);
    Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData);
    void SaveModel(string path);
    void LoadModel(string path);
}