using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IMlModelService
{
    Task TrainModelAsync();
    Task TrainAndEvaluateModelAsync(); // New method with evaluation
    Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features);
    void SaveModel(string path);
    void LoadModel(string path);
}