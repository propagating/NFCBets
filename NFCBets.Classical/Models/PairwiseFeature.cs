namespace NFCBets.Classical.Models;

public class PairwiseFeature
{
    // Pirate A features
    public float A_Strength { get; set; }
    public float A_Odds { get; set; }
    public float A_Food { get; set; }
    public float A_HistWin { get; set; }
    public float A_Position { get; set; }
    public float A_InteractionPenalty { get; set; }
    public float A_InteractionBonus { get; set; }

    // Pirate B features
    public float B_Strength { get; set; }
    public float B_Odds { get; set; }
    public float B_Food { get; set; }
    public float B_HistWin { get; set; }
    public float B_Position { get; set; }
    public float B_InteractionPenalty { get; set; }
    public float B_InteractionBonus { get; set; }

    // Difference features
    public float Diff_Strength { get; set; }
    public float Diff_Odds { get; set; }
    public float Diff_Food { get; set; }
    public float Diff_HistWin { get; set; }
    public float Diff_InteractionPenalty { get; set; }
    public float Diff_InteractionBonus { get; set; }

    // Label: Does A beat B?
    public bool A_Wins { get; set; }
}