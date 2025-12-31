namespace NFCBets.Utilities.Models;

public class PerformanceRecord
{
    public string OperationName { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public long MemoryDelta { get; set; }
    public DateTime Timestamp { get; set; }
}