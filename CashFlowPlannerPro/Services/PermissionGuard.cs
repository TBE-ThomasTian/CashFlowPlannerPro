namespace CashFlowPlannerPro.Services;

/// <summary>
/// Central defensive authorization check for write operations. UI state is only
/// a convenience; every mutation must pass this check immediately before it can
/// reach the database or an external integration.
/// </summary>
public static class PermissionGuard
{
    public static bool CanEdit(string pageKey) => App.CanEdit(pageKey);

    public static bool EnsureSessionValid(string action)
    {
        try
        {
            if (App.TryValidateCurrentSession(out _))
                return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogException("session.validation_failed", ex, new { action });
        }

        App.InvalidateCurrentSession(action);
        return false;
    }

    public static bool DemandEdit(string pageKey, string action)
    {
        if (!EnsureSessionValid(action))
            return false;

        if (App.CanEdit(pageKey))
            return true;

        AppLogger.Audit(
            "authorization.denied",
            action,
            success: false,
            new { pageKey });
        return false;
    }
}
