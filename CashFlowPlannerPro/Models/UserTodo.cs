namespace CashFlowPlannerPro.Models;

public class UserTodo
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string Status { get; set; } = "Offen"; // Offen, InArbeit, Erledigt
    public int Priority { get; set; } = 2; // 1=Hoch, 2=Mittel, 3=Niedrig
    public string? DueDate { get; set; }
    public long? ProjectId { get; set; }
    public long? MilestoneId { get; set; }
    public string? CreatedAt { get; set; }
}
