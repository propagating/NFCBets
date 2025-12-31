using NFCBets.Evaluation.Enums;

namespace NFCBets.Evaluation.Models;


public class BettingStrategyComparisonResult
{
    public BettingStrategyTypeEnum BettingStrategy { get; set; }
    public string MlStrategyName { get; set; } = "";
    public decimal ROI { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal WinRate { get; set; }
    public decimal MaxDrawdown { get; set; }
    public decimal SharpeRatio { get; set; }
    public int TotalBets { get; set; }
    public decimal FinalBankroll { get; set; }
}