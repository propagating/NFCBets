namespace NFCBets.Classical.Models;


internal class PlackettLuceModelData
{
    public Dictionary<int, double> PirateStrengths { get; set; } = new();
    public Dictionary<int, double> ArenaMultipliers { get; set; } = new();
    public List<double> FeatureWeights { get; set; } = new();
}