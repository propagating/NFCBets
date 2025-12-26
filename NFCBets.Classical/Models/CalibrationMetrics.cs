namespace NFCBets.Classical.Models;

public class CalibrationMetrics
{
    public List<BinCalibration> Bins { get; set; } = new();
    public double OverallCalibrationError { get; set; }
}