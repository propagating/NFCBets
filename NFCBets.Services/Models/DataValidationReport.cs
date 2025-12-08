namespace NFCBets.Services.Models;

public class DataValidationReport
{
    public DateTime ValidationDate { get; set; }
    public int? StartRound { get; set; }
    public int? EndRound { get; set; }
    public List<DataValidationIssue> Issues { get; set; } = new();
    public bool IsValid { get; set; }
    public bool ValidationPassed { get; set; }
}