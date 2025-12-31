using NFCBets.Causal.Models;

namespace NFCBets.Utilities.Models;

public class InteractionAnalysisReport
{
    public DateTime AnalysisDate { get; set; }
    public int TotalRecords { get; set; }
    public List<InteractionAnalysisEffect> Interactions { get; set; } = new();
    public List<InteractionAnalysisEffect> AntagonisticInteractions { get; set; } = new();
    public List<InteractionAnalysisEffect> SynergisticInteractions { get; set; } = new();
    public List<InteractionAnalysisEffect> NeutralInteractions { get; set; } = new();
}