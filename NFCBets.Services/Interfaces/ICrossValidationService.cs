using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface ICrossValidationService
{
    Task<CrossValidationReport> PerformKFoldCrossValidationAsync(int k = 5);
    Task<CrossValidationReport> PerformTimeSeriesCrossValidationAsync(int numFolds = 5);
}