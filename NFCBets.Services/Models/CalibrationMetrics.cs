namespace NFCBets.Services;

public class CalibrationMetrics
{
    public List<BinCalibration> Bins { get; set; } = new();
    public double OverallCalibrationError { get; set; }
}