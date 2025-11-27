using System.Text.Json.Serialization;

namespace NFCBets.Repository.Models;

public class FoodClubRound
{
    [JsonPropertyName("pirates")]
    public List<List<int>> Pirates { get; set; } = new();

    [JsonPropertyName("openingOdds")]
    public List<List<int>> OpeningOdds { get; set; } = new();

    [JsonPropertyName("currentOdds")]
    public List<List<int>> CurrentOdds { get; set; } = new();

    [JsonPropertyName("changes")]
    public List<Change>? Changes { get; set; } = new();

    [JsonPropertyName("round")]
    public int Round { get; set; }

    [JsonPropertyName("start")]
    public DateTime? Start { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("lastChange")]
    public DateTime? LastChange { get; set; }

    [JsonPropertyName("winners")]
    public List<int> Winners { get; set; } = new();

    [JsonPropertyName("foods")]
    public List<List<int>> Foods { get; set; } = new();

    
    // Computed properties
    public bool IsComplete => Winners.Count > 0;
    public int ArenaCount => Pirates.Count;
    public int TotalChanges => Changes?.Count ?? 0; 
    
}