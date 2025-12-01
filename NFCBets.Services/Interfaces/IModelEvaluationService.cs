using Microsoft.ML;
using NFCBets.EF.Models;
using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IModelEvaluationService
{
    Task<ModelEvaluationReport> EvaluateModelAsync(ITransformer model, List<PirateFeatureRecord> testData);
    Task<FeatureImportanceReport> AnalyzeFeatureImportanceAsync(List<PirateFeatureRecord> trainingData);
    Task<DataLeakageReport> CheckForDataLeakageAsync(List<PirateFeatureRecord> features, NfcbetsContext context);
}