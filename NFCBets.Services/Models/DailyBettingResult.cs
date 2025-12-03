using NFCBets.Utilities.Models;

namespace NFCBets.Services.Models;

public class DailyBettingResult
{
    public int RoundId { get; set; }
    public string SeriesName { get; set; } = "";
    public BetSeriesResult Result { get; set; } = new();
}
