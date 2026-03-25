namespace CashFlowPlannerPro.Models;

public class TimeEntry
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public long ProjectId { get; set; }
    public string ProjectName { get; set; } = "";
    public string ActivityType { get; set; } = "";
    public string Description { get; set; } = "";
    public string EntryDate { get; set; } = "";
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public double DurationHours { get; set; }
    public bool IsRunning { get; set; }
    public string? CreatedAt { get; set; }
}

public class TimeSummary
{
    public string Label { get; set; } = "";
    public double Hours { get; set; }
}
