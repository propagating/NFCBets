namespace NFCBets.Services.Models;

public class FoodClubRoundData
{
    public List<List<int>> Pirates { get; set; } = new();
    public List<List<int>> OpeningOdds { get; set; } = new();
    public List<List<int>> CurrentOdds { get; set; } = new();
    public List<List<int>> Foods { get; set; } = new();
    public List<int> Winners { get; set; } = new();
    public int Round { get; set; }
    public DateTime Start { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime LastChange { get; set; }
}