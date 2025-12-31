namespace NFCBets.Classical.Models;

public class PiratePrediction
{
    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public string ArenaName { get; set; } = "";
    public int PirateId { get; set; }
    public string PirateName { get; set; } = "";
    public float WinProbability { get; set; }
    public int Payout { get; set; }

    /// <summary>
    /// Corrected payout (minimum 2:1)
    /// </summary>
    public int CorrectedPayout => Math.Max(2, Payout);

    /// <summary>
    /// Implied probability from odds
    /// </summary>
    public float ImpliedProbability => 1f / CorrectedPayout;

    /// <summary>
    /// Edge over implied probability
    /// </summary>
    public float Edge => WinProbability - ImpliedProbability;

    /// <summary>
    /// Expected value for a 1-unit bet
    /// </summary>
    public float ExpectedValue => (WinProbability * (CorrectedPayout - 1)) - (1 - WinProbability);

    public override string ToString()
    {
        var arena = !string.IsNullOrEmpty(ArenaName) ? ArenaName : $"Arena {ArenaId}";
        var pirate = !string.IsNullOrEmpty(PirateName) ? PirateName : $"Pirate #{PirateId}";
        return $"{arena}: {pirate} ({CorrectedPayout}:1, {WinProbability:P0})";
    }
}