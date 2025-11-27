using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class RoundFoodCourse
{
    public int Id { get; set; }

    public int RounId { get; set; }

    public int ArenaId { get; set; }

    public int FoodId { get; set; }

    public virtual Arena Arena { get; set; } = null!;

    public virtual Food Food { get; set; } = null!;
}
