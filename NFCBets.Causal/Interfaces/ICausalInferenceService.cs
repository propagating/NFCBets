using NFCBets.Causal.Models;

namespace NFCBets.Causal.Interfaces;

public interface ICausalInferenceService
{
    Task<ComprehensiveCausalReport> AnalyzeAllTreatmentEffectsAsync();

    // Core treatment effects
    Task<CausalEffectReport> EstimateFoodAdjustmentEffectAsync(List<CausalDataPoint>? data = null);
    Task<CausalEffectReport> EstimateRivalStrengthEffectAsync(List<CausalDataPoint>? data = null);
    Task<CausalEffectReport> EstimateOddsEffectAsync(List<CausalDataPoint>? data = null);

    // Seat Position (2 methods)
    Task<CausalEffectReport> TestOverallSeatPositionEffectAsync(List<CausalDataPoint>? data = null);
    Task<Dictionary<int, CausalEffectReport>> EstimateEachSeatVsOthersEffectAsync(List<CausalDataPoint>? data = null);

    // Arena (2 methods)
    Task<CausalEffectReport> TestOverallArenaEffectAsync(List<CausalDataPoint>? data = null);
    Task<Dictionary<int, CausalEffectReport>> EstimateIndividualArenaEffectsAsync(List<CausalDataPoint>? data = null);

    // Diagnostics
    Task<OddsDiagnosticReport> DiagnoseOddsPatternAsync(List<CausalDataPoint>? data = null);
}