namespace NFCBets.Evaluation.Models;

public class ArenaBacktestResult
{
    public int ArenaId { get; set; }
    public int BetsPlaced { get; set; }
    public int BetsWon { get; set; }
    public decimal Profit { get; set; }
    public decimal ROI { get; set; }
    public decimal AverageEdge { get; set; }
}