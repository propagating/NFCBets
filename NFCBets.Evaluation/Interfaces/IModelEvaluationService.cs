using Microsoft.ML;
using NFCBets.Classical.Models;
using NFCBets.EF.Models;
using NFCBets.Services.Models;
using NFCBets.Utilities.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IModelEvaluationService
{
    Task<ModelEvaluationReport> EvaluateModelAsync(ITransformer model, List<PirateFeatureRecord> testData);
    Task<FeatureImportanceReport> AnalyzeFeatureImportanceAsync(List<PirateFeatureRecord> trainingData);
    Task<DataLeakageReport> CheckForDataLeakageAsync(List<PirateFeatureRecord> features, NfcbetsContext context);
}