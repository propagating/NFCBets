namespace NFCBets.Causal.Models;

public class ComprehensiveCausalReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;

    // Food adjustment
    public CausalEffectReport FoodAdjustmentEffect { get; set; } = new();

    // Seat Position (2 tests only)
    public CausalEffectReport? OverallSeatPositionJointTest { get; set; } // Does it matter?
    public Dictionary<int, CausalEffectReport> EachSeatVsOthersEffects { get; set; } = new(); // Which positions?

    // Arena (2 tests only)
    public CausalEffectReport? OverallArenaJointTest { get; set; } // Does it matter?
    public Dictionary<int, CausalEffectReport> IndividualArenaEffects { get; set; } = new(); // Which arenas?

    // Other effects
    public CausalEffectReport RivalStrengthEffect { get; set; } = new();
    public CausalEffectReport OddsEffect { get; set; } = new();
    public OddsDiagnosticReport? OddsDiagnostic { get; set; }

    public Dictionary<string, InteractionEffect> InteractionEffects { get; set; } = new();
    public List<string> KeyFindings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}