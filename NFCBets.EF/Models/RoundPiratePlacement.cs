using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class RoundPiratePlacement
{
    public int Id { get; set; }

    public int? RoundId { get; set; }

    public int? ArenaId { get; set; }

    public int? PirateId { get; set; }

    public int? PirateSeatPosition { get; set; }

    public int PirateFoodAdjustment { get; set; }

    public int StartingOdds { get; set; }

    public int? CurrentOdds { get; set; }

    public virtual Arena? Arena { get; set; }

    public virtual Pirate? Pirate { get; set; }
}
