using NFCBets.Classical.Models;

namespace NFCBets.Services.Models;

public class Bet
{
    public List<PiratePrediction> Pirates { get; set; } = new();
    public List<int> ArenasCovered { get; set; } = new();
    public double CombinedWinProbability { get; set; }
    public int TotalPayout { get; set; }
    public double ExpectedValue { get; set; }

    private int CorrectOdds(int displayedOdds)
    {
        return Math.Max(2, displayedOdds);
    }

    public override string ToString()
    {
        var pirateDetails = Pirates
            .OrderBy(p => p.ArenaId)
            .Select(p =>
            {
                var arena = !string.IsNullOrEmpty(p.ArenaName) ? p.ArenaName : $"Arena{p.ArenaId}";
                var pirate = !string.IsNullOrEmpty(p.PirateName) ? p.PirateName : $"Pirate{p.PirateId}";
                return $"{arena}: {pirate} ({CorrectOdds(p.Payout)}:1)";
            });

        var betString = string.Join(" + ", pirateDetails);

        return $"[{betString}] → {TotalPayout}:1 payout, " +
               $"{CombinedWinProbability:P2} win chance, " +
               $"EV: {ExpectedValue:+0.00;-0.00;0.00}";
    }

    /// <summary>
    /// Short display format for summaries
    /// </summary>
    public string ToShortString()
    {
        var pirates = string.Join(" + ", Pirates
            .OrderBy(p => p.ArenaId)
            .Select(p => !string.IsNullOrEmpty(p.PirateName) ? p.PirateName : $"P{p.PirateId}"));

        return $"[{pirates}] {TotalPayout}:1 @ {CombinedWinProbability:P0}";
    }
}