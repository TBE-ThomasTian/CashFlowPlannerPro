namespace CashFlowPlannerPro.Models;

/// <summary>
/// Security-relevant state captured for an authenticated user session.
/// The security stamp changes whenever credentials or effective permissions
/// change, allowing the UI to invalidate an already open session.
/// </summary>
public sealed class UserSessionState
{
    public long UserId { get; init; }
    public string Username { get; init; } = "";
    public bool IsActive { get; init; }
    public string SecurityStamp { get; init; } = "";
    public Dictionary<string, string> Permissions { get; init; } = new(StringComparer.Ordinal);
}
