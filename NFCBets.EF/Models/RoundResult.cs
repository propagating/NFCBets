namespace NFCBets.EF.Models;

public class RoundResult
{
    public int Id { get; set; }

    public int ArenaId { get; set; }

    public int PirateId { get; set; }

    public int? EndingOdds { get; set; }

    public bool IsWinner { get; set; }

    public bool IsComplete { get; set; }

    public int? RoundId { get; set; }

    public virtual Arena Arena { get; set; } = null!;

    public virtual Pirate Pirate { get; set; } = null!;
}