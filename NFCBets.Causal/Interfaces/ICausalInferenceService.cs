using NFCBets.Causal.Models;

namespace NFCBets.Causal.Interfaces;

public interface ICausalInferenceService
{
    Task<ComprehensiveCausalReport> AnalyzeAllTreatmentEffectsAsync();
    Task<CausalEffectReport> EstimateFoodAdjustmentEffectAsync(List<CausalDataPoint>? data = null);

    // SEAT POSITION - 4 tests
    Task<CausalEffectReport>
        EstimateSeatPositionEffectAsync(List<CausalDataPoint>? data = null); // Test 1: Overall with breakdown

    Task<Dictionary<int, CausalEffectReport>> EstimateIndividualSeatPositionEffectsAsync(
        List<CausalDataPoint>? data = null); // Test 2: Individual full reports

    Task<Dictionary<int, CausalEffectReport>>
        EstimateEachSeatVsOthersEffectAsync(List<CausalDataPoint>? data = null); // Test 3: Each vs others

    Task<CausalEffectReport>
        TestOverallSeatPositionEffectAsync(List<CausalDataPoint>? data = null); // Test 4: Joint test

    // ARENA - 4 tests
    Task<CausalEffectReport>
        EstimateArenaEffectAsync(List<CausalDataPoint>? data, int targetArenaId); // Individual arena (for Test 2)

    Task<Dictionary<int, CausalEffectReport>>
        EstimateIndividualArenaEffectsAsync(List<CausalDataPoint>? data = null); // Test 2: All arenas individually

    Task<Dictionary<int, CausalEffectReport>>
        EstimateEachArenaVsOthersEffectAsync(List<CausalDataPoint>? data = null); // Test 3: Each vs others

    Task<CausalEffectReport> TestOverallArenaEffectAsync(List<CausalDataPoint>? data = null); // Test 4: Joint test

    Task<CausalEffectReport> EstimateRivalStrengthEffectAsync(List<CausalDataPoint>? data = null);
    Task<CausalEffectReport> EstimateOddsEffectAsync(List<CausalDataPoint>? data = null);
    Task<OddsDiagnosticReport> DiagnoseOddsPatternAsync(List<CausalDataPoint>? data = null);
}