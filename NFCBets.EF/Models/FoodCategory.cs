using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class FoodCategory
{
    public int Id { get; set; }

    public int? FoodCategoryId { get; set; }

    public string FoodCategoryName { get; set; } = null!;

    public virtual ICollection<FoodCategoryAllergy> FoodCategoryAllergies { get; set; } = new List<FoodCategoryAllergy>();

    public virtual ICollection<FoodCategoryFood> FoodCategoryFoods { get; set; } = new List<FoodCategoryFood>();

    public virtual ICollection<FoodCategoryPreference> FoodCategoryPreferences { get; set; } = new List<FoodCategoryPreference>();
}
