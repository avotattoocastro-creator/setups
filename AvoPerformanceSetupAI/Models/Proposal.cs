namespace AvoPerformanceSetupAI.Models;

public class Proposal
{
    public string Section { get; set; } = string.Empty;
    public string Parameter { get; set; } = string.Empty;
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Delta { get; set; } = string.Empty;
    /// <summary>Human-readable explanation of why this proposal was generated.</summary>
    public string Reason { get; set; } = string.Empty;
}
