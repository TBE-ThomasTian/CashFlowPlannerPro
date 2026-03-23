namespace CashFlowPlannerPro.Models;

public class HardwareAllocation
{
    public long Id { get; set; }
    public long ResourceId { get; set; }    // Employee
    public long HardwareId { get; set; }    // Hardware resource
    public long ProjectId { get; set; }     // Project
    public string Date { get; set; } = "";  // yyyy-MM-dd
    public double Hours { get; set; } = 8.0;
    public string? Notes { get; set; }
    public string? CreatedAt { get; set; }

    // Display properties (populated at runtime)
    public string? HardwareName { get; set; }
    public string? HardwareColor { get; set; }
    public string? ProjectName { get; set; }
}
