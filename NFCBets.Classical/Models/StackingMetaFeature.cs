namespace NFCBets.Classical.Models;

public class StackingMetaFeature
{
    // Base model predictions
    public float Model0_Prob { get; set; }
    public float Model1_Prob { get; set; }
    public float Model2_Prob { get; set; }
    public float Model3_Prob { get; set; }
    public float Model4_Prob { get; set; }
    
    // Original features
    public float Odds { get; set; }
    public float Strength { get; set; }
    public float Food { get; set; }
    public float Position { get; set; }
    public float HistWinRate { get; set; }
    public float OddsRank { get; set; }
    
    // Ensemble statistics
    public float MaxModelProb { get; set; }
    public float MinModelProb { get; set; }
    public float ModelProbStd { get; set; }
    
    // Label
    public bool Won { get; set; }
}