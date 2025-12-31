namespace NFCBets.Classical.Models;

public class BradleyTerryModelData
{
    public Dictionary<int, double> PirateStrengths { get; set; } = new();
    public Dictionary<int, double> PositionModifiers { get; set; } = new();
    public Dictionary<int, double> ArenaModifiers { get; set; } = new();
}