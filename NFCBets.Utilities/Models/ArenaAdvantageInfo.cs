namespace NFCBets.Utilities.Models;

public class ArenaAdvantageInfo
{
    public int PirateId { get; set; }
    public int ArenaId { get; set; }
    public bool IsSpecialist { get; set; }
    public double SpecialistAdvantage { get; set; }
    public bool IsPositiveSpecialist { get; set; }
    public int ArenaAppearances { get; set; }
    public double ArenaFavoriteWinRate { get; set; }
    public double ArenaFoodEffect { get; set; }
    public double ArenaStrengthEffect { get; set; }
    public int ArenaBestPosition { get; set; }
}