namespace NFCBets.Evaluation.Models;



public class BacktestResult
{
    public string StrategyName { get; set; } = "";
    public BacktestConfiguration Configuration { get; set; } = new();
    
    // Overall performance
    public decimal StartingBankroll { get; set; }
    public decimal FinalBankroll { get; set; }
    public decimal TotalProfit { get; set; }
    public decimal ROI { get; set; }
    public decimal AnnualizedROI { get; set; }
    
    // Betting statistics
    public int TotalRounds { get; set; }
    public int TotalBetsPlaced { get; set; }
    public int BetsWon { get; set; }
    public int BetsLost { get; set; }
    public decimal WinRate { get; set; }
    public decimal AverageBetSize { get; set; }
    public decimal TotalWagered { get; set; }
    
    // Risk metrics
    public decimal MaxDrawdown { get; set; }
    public decimal MaxDrawdownPercentage { get; set; }
    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    public decimal ProfitFactor { get; set; }  // Gross profit / Gross loss
    
    // Streak analysis
    public int MaxWinStreak { get; set; }
    public int MaxLoseStreak { get; set; }
    public int CurrentStreak { get; set; }
    
    // Edge analysis
    public decimal AverageEdge { get; set; }
    public decimal AveragePayout { get; set; }
    public decimal ExpectedValue { get; set; }
    
    // Per-arena breakdown
    public Dictionary<int, ArenaBacktestResult> ArenaResults { get; set; } = new();
    
    // Bankroll history for charting
    public List<BankrollSnapshot> BankrollHistory { get; set; } = new();
    
    // Individual bet history
    public List<BetRecord> BetHistory { get; set; } = new();
}
