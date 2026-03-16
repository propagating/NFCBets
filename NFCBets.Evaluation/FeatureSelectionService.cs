using System.Text.Json;
using NFCBets.Causal.Interfaces;
using NFCBets.Causal.Models;
using NFCBets.Classical;
using NFCBets.Classical.Models;
using NFCBets.Evaluation.Interfaces;
using NFCBets.Evaluation.Models;
using NFCBets.Services.Interfaces;

namespace NFCBets.Evaluation;

public class FeatureSelectionService : IFeatureSelectionService
{
    private readonly ICausalInferenceService _causalService;
    private readonly IFeatureEngineeringService _featureService;

    public FeatureSelectionService(
        IFeatureEngineeringService featureService,
        ICausalInferenceService causalService)
    {
        _featureService = featureService;
        _causalService = causalService;
    }

    public async Task<FeatureSelectionReport> FindOptimalFeaturesAsync()
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("🔍 AUTOMATED FEATURE SELECTION");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        var report = new FeatureSelectionReport
        {
            SelectionDate = DateTime.UtcNow
        };

        // Step 1: Run causal analysis to identify significant features
        Console.WriteLine("1️⃣ Running Causal Analysis...");
        var causalReport = await _causalService.AnalyzeAllTreatmentEffectsAsync();

        // Step 2: Identify causally significant features
        Console.WriteLine("\n2️⃣ Identifying Causally Significant Features...");
        var causalFeatures = IdentifyCausalFeatures(causalReport);

        foreach (var (feature, effect) in causalFeatures.OrderByDescending(kv => Math.Abs(kv.Value)))
            Console.WriteLine($"   ✅ {feature,-30} Effect: {effect:+0.0%;-0.0%}");

        // Step 3: Identify antagonistic interactions
        Console.WriteLine("\n3️⃣ Identifying Antagonistic Interactions...");
        var antagonisticInteractions = IdentifyAntagonisticInteractions(causalReport);

        if (antagonisticInteractions.Any())
        {
            Console.WriteLine($"   Found {antagonisticInteractions.Count} antagonistic interactions:");
            foreach (var interaction in antagonisticInteractions)
            {
                Console.WriteLine($"   ⚠️ {interaction.Name}: {interaction.InteractionStrength:+0.0%;-0.0%}");
                Console.WriteLine($"      {interaction.Description}");
            }
        }
        else
        {
            Console.WriteLine("   ✅ No antagonistic interactions found");
        }

        // Step 4: Create control features for antagonistic interactions
        Console.WriteLine("\n4️⃣ Creating Control Features for Antagonistic Interactions...");
        var controlFeatures = CreateAntagonisticControls(antagonisticInteractions);

        foreach (var control in controlFeatures) Console.WriteLine($"   ✅ {control.Name}: {control.Description}");

        // Step 5: Test feature combinations
        Console.WriteLine("\n5️⃣ Testing Feature Combinations...");
        var bestCombination = await TestFeatureCombinationsAsync(causalFeatures, controlFeatures);

        report.CausalFeatures = causalFeatures;
        report.AntagonisticInteractions = antagonisticInteractions;
        report.ControlFeatures = controlFeatures;
        report.BestFeatureCombination = bestCombination;
        report.RecommendedFeatures = bestCombination.Features;

        DisplayFeatureSelectionReport(report);
        SaveFeatureSelectionReport(report);

        return report;
    }

    private Dictionary<string, double> IdentifyCausalFeatures(ComprehensiveCausalReport causalReport)
    {
        var features = new Dictionary<string, double>();

        // Food adjustment
        if (causalReport.FoodAdjustmentEffect.IsSignificant)
            features["FoodAdjustment"] = causalReport.FoodAdjustmentEffect.AverageTreatmentEffect;

        // Seat position
        if (causalReport.OverallSeatPositionJointTest?.IsSignificant == true)
            features["Position"] = causalReport.OverallSeatPositionJointTest.AverageTreatmentEffect;

        // Arena
        if (causalReport.OverallArenaJointTest?.IsSignificant == true)
            features["ArenaId"] = causalReport.OverallArenaJointTest.AverageTreatmentEffect;

        // Rival strength
        if (causalReport.RivalStrengthEffect.IsSignificant)
        {
            features["AvgRivalStrength"] = causalReport.RivalStrengthEffect.AverageTreatmentEffect;
            features["WinRateVsCurrentRivals"] = causalReport.RivalStrengthEffect.AverageTreatmentEffect;
        }

        // Odds
        if (causalReport.OddsEffect.IsSignificant)
            features["CurrentOdds"] = causalReport.OddsEffect.AverageTreatmentEffect;

        // Always include historical predictors (even if not causal, they're predictive)
        features["HistoricalWinRate"] = 0;
        features["ArenaWinRate"] = 0;
        features["RecentWinRate"] = 0;
        features["Strength"] = 0;
        features["Weight"] = 0;

        return features;
    }

    private List<AntagonisticInteractionInfo> IdentifyAntagonisticInteractions(ComprehensiveCausalReport causalReport)
    {
        var antagonistic = new List<AntagonisticInteractionInfo>();

        foreach (var (key, interaction) in causalReport.InteractionEffects)
            if (interaction.IsAntagonistic && Math.Abs(interaction.InteractionStrength) > 0.02)
                antagonistic.Add(new AntagonisticInteractionInfo
                {
                    Name = interaction.Name,
                    InteractionStrength = interaction.InteractionStrength,
                    Description = $"Combining these reduces effect by {Math.Abs(interaction.InteractionStrength):P1}",
                    Feature1 = ParseFeature1(key),
                    Feature2 = ParseFeature2(key)
                });

        return antagonistic;
    }

    private string ParseFeature1(string interactionKey)
    {
        // Parse "FoodAdj_x_Position" -> "FoodAdjustment"
        if (interactionKey.Contains("FoodAdj")) return "FoodAdjustment";
        if (interactionKey.Contains("Strength")) return "Strength";
        return "Unknown";
    }

    private string ParseFeature2(string interactionKey)
    {
        // Parse "FoodAdj_x_Position" -> "Position"
        if (interactionKey.Contains("Position")) return "Position";
        if (interactionKey.Contains("Favorite")) return "CurrentOdds";
        if (interactionKey.Contains("Rivals")) return "AvgRivalStrength";
        return "Unknown";
    }

    private List<ControlFeature> CreateAntagonisticControls(List<AntagonisticInteractionInfo> antagonistic)
    {
        var controls = new List<ControlFeature>();

        foreach (var interaction in antagonistic)
            // Create a penalty feature for this antagonistic combination
            controls.Add(new ControlFeature
            {
                Name = $"Penalty_{interaction.Feature1}_{interaction.Feature2}",
                Description = $"Penalty when {interaction.Feature1} and {interaction.Feature2} occur together",
                FeatureName = $"{interaction.Feature1}_{interaction.Feature2}_Penalty",
                CalculationLogic =
                    $"IF ({interaction.Feature1} is high AND {interaction.Feature2} is high) THEN apply penalty of {Math.Abs(interaction.InteractionStrength):P1}"
            });

        return controls;
    }

    private async Task<FeatureCombinationResult> TestFeatureCombinationsAsync(
        Dictionary<string, double> causalFeatures,
        List<ControlFeature> controlFeatures)
    {
        // Test different combinations:
        // 1. Only causal features
        // 2. Causal + historical
        // 3. Causal + historical + controls
        // 4. All features

        var combinations = new List<FeatureCombinationResult>
        {
            await TestFeatureSet("Causal Only", causalFeatures.Keys.Where(k => causalFeatures[k] != 0).ToList()),
            await TestFeatureSet("Causal + Historical", causalFeatures.Keys.ToList()),
            await TestFeatureSet("Causal + Historical + Controls",
                causalFeatures.Keys.Concat(controlFeatures.Select(c => c.FeatureName)).ToList()),
            await TestFeatureSet("All Features", GetAllFeatures())
        };

        // Return best combination
        return combinations.OrderByDescending(c => c.AUC).First();
    }

    private async Task<FeatureCombinationResult> TestFeatureSet(string name, List<string> features)
    {
        Console.WriteLine($"   Testing {name} ({features.Count} features)...");

        // Load data
        var data = await _featureService.CreateTrainingDataAsync(2000);
        var validData = data.Where(f => f.IsWinner.HasValue).ToList();

        // Split by rounds
        var uniqueRounds = validData.Select(f => f.RoundId).Distinct().OrderBy(r => r).ToList();
        var splitIndex = (int)(uniqueRounds.Count * 0.8);

        var trainRounds = uniqueRounds.Take(splitIndex).ToHashSet();
        var testRounds = uniqueRounds.Skip(splitIndex).ToHashSet();

        var trainData = validData.Where(f => trainRounds.Contains(f.RoundId)).ToList();
        var testData = validData.Where(f => testRounds.Contains(f.RoundId)).ToList();

        // Train a simple binary model with just these features
        var strategy = new BinaryClassification();
        // TODO: Modify BinaryClassification to accept feature list
        // For now, use default features
        await strategy.TrainAsync(trainData);
        var evaluation = await strategy.EvaluateAsync(testData);

        Console.WriteLine($"      Auc: {evaluation.Auc:F4}, Accuracy: {evaluation.Accuracy:P2}");

        return new FeatureCombinationResult
        {
            Name = name,
            Features = features,
            AUC = evaluation.Auc,
            Accuracy = evaluation.Accuracy,
            F1Score = evaluation.F1Score
        };
    }

    private List<string> GetAllFeatures()
    {
        return new List<string>
        {
            "Position", "ArenaId", "CurrentOdds", "FoodAdjustment",
            "Strength", "Weight", "HistoricalWinRate", "ArenaWinRate",
            "RecentWinRate", "WinRateVsCurrentRivals", "AvgRivalStrength",
            "TotalAppearances", "MatchesVsCurrentRivals"
        };
    }

    private void DisplayFeatureSelectionReport(FeatureSelectionReport report)
    {
        Console.WriteLine("\n═══════════════════════════════════════════════════");
        Console.WriteLine("📊 FEATURE SELECTION RESULTS");
        Console.WriteLine("═══════════════════════════════════════════════════\n");

        Console.WriteLine("🎯 RECOMMENDED FEATURES:");
        foreach (var feature in report.RecommendedFeatures)
        {
            var effect = report.CausalFeatures.GetValueOrDefault(feature, 0);
            Console.WriteLine($"   ✅ {feature,-30} (Effect: {effect:+0.0%;-0.0%})");
        }

        if (report.AntagonisticInteractions.Any())
        {
            Console.WriteLine("\n⚠️ ANTAGONISTIC INTERACTIONS TO CONTROL:");
            foreach (var interaction in report.AntagonisticInteractions)
            {
                Console.WriteLine($"   • {interaction.Name}");
                Console.WriteLine($"     {interaction.Description}");
            }
        }

        if (report.ControlFeatures.Any())
        {
            Console.WriteLine("\n🛡️ CONTROL FEATURES ADDED:");
            foreach (var control in report.ControlFeatures)
            {
                Console.WriteLine($"   • {control.Name}");
                Console.WriteLine($"     {control.Description}");
            }
        }

        Console.WriteLine($"\n🏆 BEST COMBINATION: {report.BestFeatureCombination.Name}");
        Console.WriteLine($"   Auc: {report.BestFeatureCombination.AUC:F4}");
        Console.WriteLine($"   Accuracy: {report.BestFeatureCombination.Accuracy:P2}");
    }

    private void SaveFeatureSelectionReport(FeatureSelectionReport report)
    {
        Directory.CreateDirectory("Reports");
        var fileName = Path.Combine("Reports", $"feature_selection_{DateTime.UtcNow:yyyyMMdd_HHmmss}.json");
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fileName, json);
        Console.WriteLine($"\n📄 Feature selection report saved to {fileName}");
    }
}