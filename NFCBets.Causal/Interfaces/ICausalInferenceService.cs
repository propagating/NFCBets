using NFCBets.Causal.Models;

namespace NFCBets.Causal.Interfaces;

public interface ICausalInferenceService
{
    Task<ComprehensiveCausalReport> AnalyzeAllTreatmentEffectsAsync();
    Task<CausalEffectReport> EstimateFoodAdjustmentEffectAsync(List<CausalDataPoint>? data = null);
    Task<CausalEffectReport> EstimateSeatPositionEffectAsync(List<CausalDataPoint>? data = null);
    Task<CausalEffectReport> EstimateArenaPlacementEffectAsync(List<CausalDataPoint>? data, int targetArenaId);
    Task<CausalEffectReport> EstimateRivalStrengthEffectAsync(List<CausalDataPoint>? data = null);
    Task<CausalEffectReport> EstimateOddsEffectAsync(List<CausalDataPoint>? data = null);
}