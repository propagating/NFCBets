namespace NFCBets.Evaluation.Enums;

public enum BettingStrategyTypeEnum
{
    Flat,           // Fixed bet size
    Kelly,          // Kelly criterion
    QuarterKelly,   // 25% of Kelly
    HalfKelly,      // 50% of Kelly
    ValueBetting,   // Only bet when edge > threshold
    Proportional    // Bet proportional to confidence
}