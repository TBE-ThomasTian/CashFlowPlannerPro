namespace CashFlowPlannerPro.Models;

public class HardwareResource
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";  // e.g. "HPC", "AWS EC2", "GPU Server"
    public double CostPerHour { get; set; }
    public string Color { get; set; } = "#17a2b8";
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }
}
