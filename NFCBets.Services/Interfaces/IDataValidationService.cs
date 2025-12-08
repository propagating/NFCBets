using NFCBets.Services.Models;

namespace NFCBets.Services.Interfaces;

public interface IDataValidationService
{
    Task<DataValidationReport> ValidateDataQualityAsync(int? startRound = null, int? endRound = null);
}