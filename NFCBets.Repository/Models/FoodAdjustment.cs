namespace NFCBets.Repository.Models;

public class RoundFoodAdjustment
{
    public int RoundId { get; set; }
    public int Arena { get; set; }
    public int PirateId { get; set; }
    public int FoodId { get; set; }
    public double FoodAdjustment { get; set; } // Positive = boost, negative = penalty
    public bool HasAllergy { get; set; }
    public bool HasPreference { get; set; }
    public string FoodName { get; set; } = "";
}