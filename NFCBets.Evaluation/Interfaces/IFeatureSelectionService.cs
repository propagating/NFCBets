using NFCBets.Evaluation.Models;

namespace NFCBets.Evaluation.Interfaces;

public interface IFeatureSelectionService
{
    Task<FeatureSelectionReport> FindOptimalFeaturesAsync();
}