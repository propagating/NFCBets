namespace NFCBets.Services.Models;

public class BetSeriesResult
{
    public int TotalBets { get; set; }
    public int WinningBets { get; set; }
    public double BetCost { get; set; }
    public double TotalWinnings { get; set; }
    public double NetProfit { get; set; }
    public double ROI { get; set; }
}