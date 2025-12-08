namespace NFCBets.Services.Models;

public class FoldResult
{
    public int FoldNumber { get; set; }
    public int TrainSize { get; set; }
    public int TestSize { get; set; }
    public double Accuracy { get; set; }
    public double AUC { get; set; }
    public double F1Score { get; set; }
    public double Precision { get; set; }
    public double Recall { get; set; }
    public double LogLoss { get; set; }
}