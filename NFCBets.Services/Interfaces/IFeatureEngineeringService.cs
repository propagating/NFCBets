using NFCBets.Utilities.Models;

namespace NFCBets.Services.Interfaces;

public interface IFeatureEngineeringService
{
    /// <summary>
    /// Create training data from historical rounds
    /// </summary>
    Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 4000);

    /// <summary>
    /// Create features for a specific round (for predictions)
    /// </summary>
    Task<List<PirateFeatureRecord>> CreateFeaturesForRoundAsync(int roundId);

    /// <summary>
    /// Get pirate names for a list of pirate IDs
    /// </summary>
    Task<Dictionary<int, string>> GetPirateNamesAsync(IEnumerable<int> pirateIds);

    /// <summary>
    /// Get all pirate names (cached for performance)
    /// </summary>
    Task<Dictionary<int, string>> GetAllPirateNamesAsync();
}