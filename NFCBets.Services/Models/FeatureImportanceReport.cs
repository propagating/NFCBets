namespace NFCBets.Services.Models;

public class FeatureImportanceReport
{
    public List<(string FeatureName, double Importance)> FeatureImportance { get; set; } = new();
}