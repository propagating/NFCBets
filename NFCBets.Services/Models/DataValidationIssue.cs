using NFCBets.Services.Enums;

namespace NFCBets.Services.Models;

public class DataValidationIssue
{
    public ValidationSeverityEnum Severity { get; set; }
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public int AffectedRecords { get; set; }
    public List<string> Details { get; set; } = new();
}