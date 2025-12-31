namespace NFCBets.Classical.Models;

public class MultiOutputFeature
{
    // Composite scores for each pirate position
    public float Pirate0_Score { get; set; }
    public float Pirate1_Score { get; set; }
    public float Pirate2_Score { get; set; }
    public float Pirate3_Score { get; set; }

    // Interaction penalties
    public float Pirate0_Penalty { get; set; }
    public float Pirate1_Penalty { get; set; }
    public float Pirate2_Penalty { get; set; }
    public float Pirate3_Penalty { get; set; }

    // Interaction bonuses
    public float Pirate0_Bonus { get; set; }
    public float Pirate1_Bonus { get; set; }
    public float Pirate2_Bonus { get; set; }
    public float Pirate3_Bonus { get; set; }

    // Label: which position won (0-3)
    public int WinnerPosition { get; set; }
}