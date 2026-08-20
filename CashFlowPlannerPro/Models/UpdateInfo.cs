namespace CashFlowPlannerPro.Models;

public class UpdateInfo
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Mandatory { get; set; }
}
