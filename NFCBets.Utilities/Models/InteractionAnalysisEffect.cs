namespace NFCBets.Causal.Models;

public class InteractionAnalysisEffect
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    // Main results
    public double InteractionStrength { get; set; }
    public double Effect1Alone { get; set; }
    public double Effect2Alone { get; set; }
    public double CombinedEffect { get; set; }
    public double ExpectedAdditiveEffect { get; set; }

    // Sample sizes
    public int Group00Count { get; set; } // Neither condition
    public int Group10Count { get; set; } // Only condition 1
    public int Group01Count { get; set; } // Only condition 2
    public int Group11Count { get; set; } // Both conditions

    // Win rates
    public double WinRate00 { get; set; }
    public double WinRate10 { get; set; }
    public double WinRate01 { get; set; }
    public double WinRate11 { get; set; }

    // Statistical significance
    public double PValue { get; set; }
    public bool IsSignificant { get; set; }

    // Classification
    public bool IsAntagonistic { get; set; }
    public bool IsSynergistic { get; set; }
    public bool IsThreeWay { get; set; }
}