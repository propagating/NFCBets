namespace NFCBets.Causal.Models;

public class CausalEffectReport
{
    public string TreatmentName { get; set; } = "";
    public double AverageTreatmentEffect { get; set; }
    public double StandardError { get; set; }
    public double TStatistic { get; set; }
    public double PValue { get; set; }
    public int TreatmentGroupSize { get; set; }
    public int ControlGroupSize { get; set; }
    public int MatchedPairs { get; set; }
    public bool IsSignificant { get; set; }
    public (double Lower, double Upper) ConfidenceInterval { get; set; }
    public Dictionary<int, double>? DoseResponse { get; set; }
    public Dictionary<int, double>? PositionEffects { get; set; }
}