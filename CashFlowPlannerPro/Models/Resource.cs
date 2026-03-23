namespace CashFlowPlannerPro.Models;

public class Resource
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public double Availability { get; set; } = 1.0;
    public double HourlyRate { get; set; }
    public string? CreatedAt { get; set; }
}
