namespace NFCBets.Causal.Models;

public class OddsDiagnosticReport
{
    public List<OddsBucket> OddsBuckets { get; set; } = new();
    public double CorrelationWithWinning { get; set; }
    public bool IsPatternInverted { get; set; }
    public int TotalObservations { get; set; }

    public string DiagnosisMessage => IsPatternInverted
        ? "⚠️ Odds appear inverted or calculated incorrectly"
        : "✅ Odds follow expected pattern";
}