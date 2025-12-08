namespace NFCBets.Causal.Models;

public class CausalDataPoint
{
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public bool IsWinner { get; set; }
    public int FoodAdjustment { get; set; }
    public int CurrentOdds { get; set; }
    public int Position { get; set; }
    public int Strength { get; set; }
    public int Weight { get; set; }
}