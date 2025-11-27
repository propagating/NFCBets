using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class FoodCategoryFood
{
    public int Id { get; set; }

    public int FoodId { get; set; }

    public int FoodCategoryId { get; set; }

    public virtual Food Food { get; set; } = null!;

    public virtual FoodCategory FoodCategory { get; set; } = null!;
}
