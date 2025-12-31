using NFCBets.Classical.Models;

namespace NFCBets.Evaluation.Models;

public class FeatureSelectionReport
{
    public DateTime SelectionDate { get; set; }
    public Dictionary<string, double> CausalFeatures { get; set; } = new();
    public List<AntagonisticInteractionInfo> AntagonisticInteractions { get; set; } = new();
    public List<ControlFeature> ControlFeatures { get; set; } = new();
    public FeatureCombinationResult BestFeatureCombination { get; set; } = new();
    public List<string> RecommendedFeatures { get; set; } = new();
}