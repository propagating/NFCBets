namespace NFCBets.Causal.Models;

public class ComprehensiveCausalReport
{
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    public CausalEffectReport FoodAdjustmentEffect { get; set; } = new();
    public CausalEffectReport SeatPositionEffect { get; set; } = new();
    public Dictionary<int, CausalEffectReport> ArenaEffects { get; set; } = new();
    public CausalEffectReport RivalStrengthEffect { get; set; } = new();
    public CausalEffectReport OddsEffect { get; set; } = new();
    public Dictionary<string, InteractionEffect> InteractionEffects { get; set; } = new();
        
    public List<string> KeyFindings { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
}