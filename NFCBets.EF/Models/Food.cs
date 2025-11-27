using System;
using System.Collections.Generic;

namespace NFCBets.EF.Models;

public partial class Food
{
    public int Id { get; set; }

    public int FoodId { get; set; }

    public string FoodName { get; set; } = null!;

    public virtual ICollection<FoodCategoryFood> FoodCategoryFoods { get; set; } = new List<FoodCategoryFood>();

    public virtual ICollection<RoundFoodCourse> RoundFoodCourses { get; set; } = new List<RoundFoodCourse>();
}
