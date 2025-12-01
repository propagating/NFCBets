namespace NFCBets.EF.Models;

public class RoundFoodCourse
{
    public int Id { get; set; }

    public int RoundId { get; set; }

    public int ArenaId { get; set; }

    public int FoodId { get; set; }

    public virtual Arena Arena { get; set; } = null!;

    public virtual Food Food { get; set; } = null!;
}