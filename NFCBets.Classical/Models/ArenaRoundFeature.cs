using Microsoft.ML.Data;

namespace NFCBets.Classical.Models;

public class ArenaRoundFeature
{
    // Pirate 0 features
    public float Pirate0_Strength { get; set; }
    public float Pirate0_Odds { get; set; }
    public float Pirate0_Food { get; set; }
    public float Pirate0_HistWin { get; set; }
    
    // Pirate 1 features
    public float Pirate1_Strength { get; set; }
    public float Pirate1_Odds { get; set; }
    public float Pirate1_Food { get; set; }
    public float Pirate1_HistWin { get; set; }
    
    // Pirate 2 features
    public float Pirate2_Strength { get; set; }
    public float Pirate2_Odds { get; set; }
    public float Pirate2_Food { get; set; }
    public float Pirate2_HistWin { get; set; }
    
    // Pirate 3 features
    public float Pirate3_Strength { get; set; }
    public float Pirate3_Odds { get; set; }
    public float Pirate3_Food { get; set; }
    public float Pirate3_HistWin { get; set; }
    
    [ColumnName("Label")]
    public uint WinnerPosition { get; set; } // 0, 1, 2, or 3
}