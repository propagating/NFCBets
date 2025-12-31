namespace NFCBets.Evaluation.Models;

public class BetRecord
{
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public string PirateName { get; set; } = "";
    public decimal BetAmount { get; set; }
    public decimal Payout { get; set; }
    public float PredictedProbability { get; set; }
    public decimal ImpliedProbability { get; set; }
    public decimal Edge { get; set; }
    public bool Won { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal BankrollAfter { get; set; }
}