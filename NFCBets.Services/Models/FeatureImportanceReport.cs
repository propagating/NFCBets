namespace NFCBets.Services;

public class FeatureImportanceReport
{
    public List<(string FeatureName, double Importance)> FeatureImportance { get; set; } = new();
}