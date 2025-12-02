namespace NFCBets.Services.Models;

public class DataLeakageReport
{
    public bool HasLeakage { get; set; }
    public List<string> LeakageIssues { get; set; } = new();
    public string TrainRoundRange { get; set; } = "";
    public string TestRoundRange { get; set; } = "";
}