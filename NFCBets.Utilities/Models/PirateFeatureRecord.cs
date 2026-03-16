namespace NFCBets.Utilities.Models;

/// <summary>
/// Raw feature record from database, before conversion to ML format
/// </summary>
public class PirateFeatureRecord
{
    #region Identifiers

    public int RoundId { get; set; }
    public int ArenaId { get; set; }
    public int PirateId { get; set; }
    public int Position { get; set; }

    #endregion

    #region Odds

    /// <summary>
    /// Current betting odds
    /// </summary>
    public int CurrentOdds { get; set; }

    /// <summary>
    /// Opening betting odds
    /// </summary>
    public int OpeningOdds { get; set; }

    #endregion

    #region Pirate Attributes

    /// <summary>
    /// Pirate's base strength stat
    /// </summary>
    public float Strength { get; set; }

    /// <summary>
    /// Pirate's weight stat
    /// </summary>
    public float Weight { get; set; }

    /// <summary>
    /// Food adjustment modifier for this round
    /// </summary>
    public float FoodAdjustment { get; set; }

    #endregion

    #region Historical Performance

    /// <summary>
    /// Overall historical win rate (0-1)
    /// </summary>
    public double HistoricalWinRate { get; set; }

    /// <summary>
    /// Total number of appearances
    /// </summary>
    public int TotalAppearances { get; set; }

    /// <summary>
    /// Win rate in this specific arena (0-1)
    /// </summary>
    public double ArenaWinRate { get; set; }

    /// <summary>
    /// Recent form win rate - last 20 matches (0-1)
    /// </summary>
    public double RecentWinRate { get; set; }

    #endregion

    #region Rival Statistics

    /// <summary>
    /// Win rate against current opponents (0-1)
    /// </summary>
    public double WinRateVsCurrentRivals { get; set; }

    /// <summary>
    /// Number of previous matches against current rivals
    /// </summary>
    public int MatchesVsCurrentRivals { get; set; }

    /// <summary>
    /// Average strength of current opponents
    /// </summary>
    public double AvgRivalStrength { get; set; }

    #endregion

    #region Label

    /// <summary>
    /// Whether this pirate won (null if unknown/future)
    /// </summary>
    public bool? IsWinner { get; set; }

    #endregion
}