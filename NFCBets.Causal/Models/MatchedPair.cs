namespace NFCBets.Causal.Models;

public class MatchedPair
{
    public double TreatedOutcome { get; set; }
    public double ControlOutcome { get; set; }
    public double PropensityScore { get; set; }
    public double Distance { get; set; }
}