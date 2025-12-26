namespace NFCBets.Classical.Models;

public class PirateProbability
{
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public int Position { get; set; }
    public int Odds { get; set; }
    public double Probability { get; set; }
}