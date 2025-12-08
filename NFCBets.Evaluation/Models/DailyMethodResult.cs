namespace NFCBets.Evaluation.Models;

public class DailyMethodResult
{
    public int RoundId { get; set; }
    public string StrategyName { get; set; } = "";
    public int TotalBets { get; set; }
    public int WinningBets { get; set; }
    public double NetProfit { get; set; }
}