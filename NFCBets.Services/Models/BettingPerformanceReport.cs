namespace NFCBets.Services.Models;

public class BettingPerformanceReport
{
    public int StartRound { get; set; }
    public int EndRound { get; set; }
    public List<StrategyMetrics> StrategyResults { get; set; } = new();
}
