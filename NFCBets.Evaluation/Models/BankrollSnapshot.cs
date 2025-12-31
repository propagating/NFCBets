namespace NFCBets.Evaluation.Models;

public class BankrollSnapshot
{
    public int RoundNumber { get; set; }
    public int RoundId { get; set; }
    public decimal Bankroll { get; set; }
    public decimal DrawdownFromPeak { get; set; }
}