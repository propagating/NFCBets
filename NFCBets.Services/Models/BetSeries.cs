using NFCBets.Services.Enums;

namespace NFCBets.Services.Models;

public class BetSeries
{
    public string Name { get; set; } = "";
    public RiskLevelEnum RiskLevelEnum { get; set; }
    public List<Bet> Bets { get; set; } = new();
    public string Description { get; set; } = "";

    /// <summary>
    /// Total expected value across all bets
    /// </summary>
    public double TotalExpectedValue => Bets.Sum(b => b.ExpectedValue);

    /// <summary>
    /// Average win probability across all bets
    /// </summary>
    public double AverageWinProbability => Bets.Any() ? Bets.Average(b => b.CombinedWinProbability) : 0;

    /// <summary>
    /// Number of positive EV bets
    /// </summary>
    public int PositiveEvBetCount => Bets.Count(b => b.ExpectedValue > 0);
}