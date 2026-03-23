namespace CashFlowPlannerPro.Models;

public class ResourceAllocation
{
    public long Id { get; set; }
    public long ResourceId { get; set; }
    public long ProjectId { get; set; }
    public string Date { get; set; } = "";
    public double Hours { get; set; } = 8.0;
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }
    public string? ProjectName { get; set; }
    public string? ProjectColor { get; set; }
}
