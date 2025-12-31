using NFCBets.Evaluation.Enums;

namespace NFCBets.Evaluation.Models;

public class BacktestConfiguration
{
    public decimal StartingBankroll { get; set; } = 10000m;
    public int RoundsToSimulate { get; set; } = 1000;
    public BettingStrategyTypeEnum BettingStrategy { get; set; } = BettingStrategyTypeEnum.QuarterKelly;
    public decimal MaxBetPercentage { get; set; } = 0.10m;  // Max 10% of bankroll per bet
    public decimal MinEdgeRequired { get; set; } = 0.05m;   // 5% minimum edge to bet
    public decimal KellyFraction { get; set; } = 0.25m;     // Quarter Kelly for safety
    public bool BetAllArenas { get; set; } = true;
    public int? SpecificArenaId { get; set; } = null;
    public bool IncludeDetailedHistory { get; set; } = false;  // For memory efficiency
    public bool SaveBankrollSnapshots { get; set; } = true;
}