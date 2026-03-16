namespace NFCBets.Utilities.Models;

public class PirateArenaSpecialist
{
    public int PirateId { get; set; }
    public int ArenaId { get; set; }
    public double ArenaWinRate { get; set; }
    public double OverallWinRate { get; set; }
    public double Advantage { get; set; }  // ArenaWinRate - OverallWinRate
    public int ArenaAppearances { get; set; }
    public bool IsPositiveSpecialist { get; set; }  // Outperforms in this arena
}