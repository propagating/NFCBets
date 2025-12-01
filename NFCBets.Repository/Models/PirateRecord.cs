namespace NFCBets.Repository.Models;

public class PirateRecord
{
    public int RoundId { get; set; }
    public int Arena { get; set; }
    public int PirateId { get; set; }
    public int Position { get; set; }
    public int? OpeningOdds { get; set; }
    public int CurrentOdds { get; set; }
    public bool Won { get; set; }
    public DateTime? RoundStart { get; set; }
    public int TotalOddsChanges { get; set; }
    public double PriorWinRate { get; set; }
    public double RecentForm { get; set; }

    public double OddsMovement =>
        OpeningOdds.HasValue ? (double)(CurrentOdds - OpeningOdds.Value) / OpeningOdds.Value : 0;
}