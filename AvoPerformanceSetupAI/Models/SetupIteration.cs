namespace AvoPerformanceSetupAI.Models;

public class SetupIteration
{
    public string Setup { get; set; } = string.Empty;
    public string BestLap { get; set; } = string.Empty;
    public int Iter { get; set; }
    public bool Exported { get; set; }
    public bool IsSelected { get; set; }
}
