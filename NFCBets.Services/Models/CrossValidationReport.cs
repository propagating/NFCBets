namespace NFCBets.Services.Models;

public class CrossValidationReport
{
    public string Method { get; set; } = "";
    public int NumFolds { get; set; }
    public List<FoldResult> FoldResults { get; set; } = new();
    public double AverageAccuracy { get; set; }
    public double AverageAUC { get; set; }
    public double AverageF1Score { get; set; }
    public double StdDevAccuracy { get; set; }
    public double StdDevAUC { get; set; }
}