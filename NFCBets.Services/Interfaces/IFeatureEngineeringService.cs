using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IFeatureEngineeringService
{
    Task<List<PirateFeatureRecord>> CreateFeaturesForRoundAsync(int roundId);
    Task<List<PirateFeatureRecord>> CreateTrainingDataAsync(int maxRounds = 3800);
}