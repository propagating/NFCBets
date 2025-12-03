namespace NFCBets.Causal.Models;

public class InteractionEffect
{
    public string Name { get; set; } = "";
    public double InteractionStrength { get; set; }
    public double BothTreatments { get; set; }
    public double Treatment1Only { get; set; }
    public double Treatment2Only { get; set; }
    public double Neither { get; set; }
    public bool IsSynergistic { get; set; }
    public bool IsAntagonistic { get; set; }
}