using System.Text.Json.Serialization;

namespace NFCBets.Repository.Models;

public class Change
{
    [JsonPropertyName("arena")]
    public int Arena { get; set; }
    
    [JsonPropertyName("pirate")]
    public int Pirate { get; set; }
    
    [JsonPropertyName("old")]
    public int Old { get; set; }
    
    [JsonPropertyName("new")]
    public int New { get; set; }
    
    [JsonPropertyName("t")]
    public DateTime Timestamp { get; set; }
}
