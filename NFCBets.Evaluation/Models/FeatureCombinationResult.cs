namespace NFCBets.Evaluation.Models;

public class FeatureCombinationResult
{
    public string Name { get; set; } = "";
    public List<string> Features { get; set; } = new();
    public double AUC { get; set; }
    public double Accuracy { get; set; }
    public double F1Score { get; set; }
}