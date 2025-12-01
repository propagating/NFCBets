namespace NFCBets.Services.Models;

public class PiratePrediction
{
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public float WinProbability { get; set; }
    public int Payout { get; set; }
}