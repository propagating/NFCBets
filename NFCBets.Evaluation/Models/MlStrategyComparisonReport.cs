namespace NFCBets.Evaluation.Models;


public class MlStrategyComparisonReport
{
    public DateTime ComparisonDate { get; set; }
    public Dictionary<string, MlStrategyResult> StrategyResults { get; set; } = new();
    public string BestByAUC { get; set; } = "";
    public string BestByAccuracy { get; set; } = "";
    public string BestByF1 { get; set; } = "";
}