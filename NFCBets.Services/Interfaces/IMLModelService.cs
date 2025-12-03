using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IMlModelService
{
    Task TrainModelAsync();
    Task TrainAndEvaluateModelAsync();
    Task TrainAndEvaluateCausallyInformedModelAsync(); // New method
    Task<List<PiratePrediction>> PredictAsync(List<PirateFeatureRecord> features);
    void SaveModel(string path);
    void LoadModel(string path);
}