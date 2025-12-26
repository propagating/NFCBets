namespace NFCBets.Evaluation.Models;

public class MlStrategyResult
{
    public string StrategyName { get; set; } = "";
    public double AUC { get; set; }
    public double Accuracy { get; set; }
    public double F1Score { get; set; }
    public double LogLoss { get; set; }
    public double TrainingTime { get; set; }
}