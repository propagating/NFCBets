using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class FoodCategoryAllergy
{
    public int Id { get; set; }

    public int FoodCategoryId { get; set; }

    public int PirateId { get; set; }

    public virtual FoodCategory FoodCategory { get; set; } = null!;

    public virtual Pirate Pirate { get; set; } = null!;
}
