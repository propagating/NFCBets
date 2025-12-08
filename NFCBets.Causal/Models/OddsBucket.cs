namespace NFCBets.Causal.Models;

public class OddsBucket
{
    public int Odds { get; set; }
    public int Count { get; set; }
    public int Wins { get; set; }
    public double WinRate { get; set; }
    public double ImpliedProbability { get; set; }
    public double AvgStrength { get; set; }
    public double AvgFoodAdjustment { get; set; }
    public double AvgPosition { get; set; }
}