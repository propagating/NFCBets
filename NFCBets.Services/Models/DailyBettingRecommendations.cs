namespace NFCBets.Services;

public class DailyBettingRecommendations
{
    public int RoundId { get; set; }
    public DateTime GeneratedAt { get; set; }
    public List<BetSeries> BetSeries { get; set; } = new();
    public int TotalBets { get; set; }
}