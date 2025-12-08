namespace NFCBets.Services.Models;

public class FeatureSelectionResult
{
    public List<string> SelectedFeatures { get; set; } = new();
    public List<string> ExcludedFeatures { get; set; } = new();
    public Dictionary<string, double> FeatureEffects { get; set; } = new();
}