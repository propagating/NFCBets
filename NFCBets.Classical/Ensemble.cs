using NFCBets.Classical.Interfaces;
using NFCBets.Classical.Models;

namespace NFCBets.Classical;

/// <summary>
/// Combines predictions from multiple strategies
/// Weights: 40% Binary, 30% Multi-Class, 30% Ranking
/// </summary>
public class EnsembleStrategy : IMlStrategy
{
    public string StrategyName => "Ensemble (Weighted Combination)";

    private readonly BinaryClassification _binaryStrategy;
    private readonly MultiClassPerArena _multiClassStrategy;
    private readonly LearnToRank _rankingStrategy;

    public EnsembleStrategy()
    {
        _binaryStrategy = new BinaryClassification();
        _multiClassStrategy = new MultiClassPerArena();
        _rankingStrategy = new LearnToRank();
    }

    public async Task TrainAsync(List<PirateFeatureRecord> trainingData)
    {
        Console.WriteLine($"🏋️ Training {StrategyName}...");

        await _binaryStrategy.TrainAsync(trainingData);
        await _multiClassStrategy.TrainAsync(trainingData);
        await _rankingStrategy.TrainAsync(trainingData);
    }

    public Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features)
    {
        throw new NotImplementedException();
    }

    public Task<ModelEvaluationReport> EvaluateAsync(List<PirateFeatureRecord> testData)
    {
        throw new NotImplementedException();
    }

    public void SaveModel(string path)
    {
        throw new NotImplementedException();
    }

    public void LoadModel(string path)
    {
        throw new NotImplementedException();
    }
}
