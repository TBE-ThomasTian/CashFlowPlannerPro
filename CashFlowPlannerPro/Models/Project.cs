namespace CashFlowPlannerPro.Models;

public class Project
{
    public long Id { get; set; }
    public string ProjectNumber { get; set; } = "";
    public string Name { get; set; } = "";
    public string Color { get; set; } = "#3498db";
    public string? StartDate { get; set; }
    public string? EndDate { get; set; }
    public double Budget { get; set; }
    public string Status { get; set; } = "active";
    public string? CreatedAt { get; set; }
}
