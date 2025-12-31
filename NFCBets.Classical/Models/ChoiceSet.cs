namespace NFCBets.Classical.Models;

internal class ChoiceSet
{
    public List<double[]> Alternatives { get; set; } = new();
    public int ChosenIndex { get; set; }
    public int RoundId { get; set; }
    public List<int> PirateIds { get; set; } = new();
}