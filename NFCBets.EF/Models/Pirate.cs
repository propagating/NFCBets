using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class Pirate
{
    public int Id { get; set; }

    public int PirateId { get; set; }

    public string PirateName { get; set; } = null!;

    public int? Strength { get; set; }

    public int? Weight { get; set; }

    public int? Wins { get; set; }

    public int? Losses { get; set; }

    public virtual ICollection<FoodCategoryAllergy> FoodCategoryAllergies { get; set; } = new List<FoodCategoryAllergy>();

    public virtual ICollection<FoodCategoryPreference> FoodCategoryPreferences { get; set; } = new List<FoodCategoryPreference>();

    public virtual ICollection<RoundPiratePlacement> RoundPiratePlacements { get; set; } = new List<RoundPiratePlacement>();

    public virtual ICollection<RoundResult> RoundResults { get; set; } = new List<RoundResult>();
}
