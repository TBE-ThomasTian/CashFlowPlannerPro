namespace CashFlowPlannerPro.Models;

public class Resource
{
    public long Id { get; set; }
    public long? UserId { get; set; }
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public double Availability { get; set; } = 1.0;
    public double HourlyRate { get; set; }
    public int WorkStartHour { get; set; } = 8;
    public int WorkEndHour { get; set; } = 17;
    public string? CreatedAt { get; set; }
    public string? AvatarData { get; set; }

    public int WorkHoursPerDay => WorkEndHour - WorkStartHour;
}
