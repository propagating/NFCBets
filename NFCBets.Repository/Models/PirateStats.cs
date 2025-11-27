namespace NFCBets.Repository.Models;

public class PirateStats
{
    public int PirateId { get; set; }
    public int TotalAppearances { get; set; }
    public int TotalWins { get; set; }
    public double WinRate { get; set; }
    public double AverageOdds { get; set; }
    public double AveragePosition { get; set; }
}