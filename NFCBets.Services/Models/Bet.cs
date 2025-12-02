namespace NFCBets.Services.Models;

// Add ArenasCovered property to Bet class
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
            .Select(p => $"Arena{p.ArenaId}:Pirate{p.PirateId}({CorrectOdds(p.Payout)}:1)");

        var betString = string.Join(" + ", pirateDetails);

        return $"[{betString}] → {TotalPayout}:1 payout, " +
               $"{CombinedWinProbability:P2} win chance, " +
               $"EV: {ExpectedValue:+0.00;-0.00;0.00}";
    }
}