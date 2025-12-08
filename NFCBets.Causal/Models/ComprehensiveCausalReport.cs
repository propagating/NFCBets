namespace NFCBets.Causal.Models;

public class ComprehensiveCausalReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public CausalEffectReport FoodAdjustmentEffect { get; set; } = new();

    // Seat Position - 4 tests
    public CausalEffectReport SeatPositionEffect { get; set; } = new(); // Test 1: Overall with breakdown

    public Dictionary<int, CausalEffectReport> IndividualSeatPositionEffects { get; set; } =
        new(); // Test 2: Individual reports

    public Dictionary<int, CausalEffectReport> EachSeatVsOthersEffects { get; set; } = new(); // Test 3: Comparative
    public CausalEffectReport? OverallSeatPositionJointTest { get; set; } // Test 4: Joint test

    // Arena - 4 tests
    public Dictionary<int, CausalEffectReport> IndividualArenaEffects { get; set; } =
        new(); // Test 2: Individual reports

    public Dictionary<int, CausalEffectReport> EachArenaVsOthersEffects { get; set; } = new(); // Test 3: Comparative
    public CausalEffectReport? OverallArenaJointTest { get; set; } // Test 4: Joint test

    public CausalEffectReport RivalStrengthEffect { get; set; } = new();
    public CausalEffectReport OddsEffect { get; set; } = new();
    public OddsDiagnosticReport? OddsDiagnostic { get; set; }

    public Dictionary<string, InteractionEffect> InteractionEffects { get; set; } = new();
    public List<string> KeyFindings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}