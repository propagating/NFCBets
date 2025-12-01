using NFCBets.Services.Enums;

namespace NFCBets.Services;

public class BetSeries
{
    public string Name { get; set; } = "";
    public RiskLevel RiskLevel { get; set; }
    public List<Bet> Bets { get; set; } = new();
    public string Description { get; set; } = "";
}