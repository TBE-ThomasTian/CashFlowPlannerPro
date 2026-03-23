namespace CashFlowPlannerPro.Models;

public class ProjectMilestone
{
    public long Id { get; set; }
    public long ProjectId { get; set; }
    public string Name { get; set; } = "";
    public string Status { get; set; } = "Offen"; // Offen, Aktiv, Review, Abgeschlossen
    public string? Deadline { get; set; }
    public string? Responsible { get; set; }
    public double HoursBudget { get; set; }
    public int Priority { get; set; } = 2; // 1=Hoch, 2=Mittel, 3=Niedrig
    public string? Dependencies { get; set; } // comma-separated milestone IDs
    public string? Notes { get; set; }
    public int SortOrder { get; set; }
    public string? CreatedAt { get; set; }
}
