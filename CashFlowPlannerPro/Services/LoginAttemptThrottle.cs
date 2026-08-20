namespace CashFlowPlannerPro.Services;

/// <summary>
/// Process-local, bounded backoff for application-login attempts. It does not
/// persist passwords, usernames, or connection details; callers pass an opaque
/// SHA-256 key.
/// </summary>
public static class LoginAttemptThrottle
{
    private const int MaxTrackedKeys = 2048;
    private const int MaximumDelaySeconds = 32;
    private static readonly long RetentionMilliseconds = (long)TimeSpan.FromMinutes(15).TotalMilliseconds;
    private static readonly object SyncRoot = new();
    private static readonly Dictionary<string, AttemptState> Attempts = new(StringComparer.Ordinal);

    public static TimeSpan GetRemainingDelay(string opaqueKey)
    {
        if (string.IsNullOrWhiteSpace(opaqueKey))
            return TimeSpan.Zero;

        lock (SyncRoot)
        {
            var now = Environment.TickCount64;
            CleanupExpired(now);
            if (!Attempts.TryGetValue(opaqueKey, out var state))
                return TimeSpan.Zero;

            state.LastSeenTick = now;
            var remainingMilliseconds = state.NextAllowedTick - now;
            return remainingMilliseconds > 0
                ? TimeSpan.FromMilliseconds(remainingMilliseconds)
                : TimeSpan.Zero;
        }
    }

    public static TimeSpan RegisterFailure(string opaqueKey)
    {
        if (string.IsNullOrWhiteSpace(opaqueKey))
            return TimeSpan.FromSeconds(1);

        lock (SyncRoot)
        {
            var now = Environment.TickCount64;
            CleanupExpired(now);
            EnsureCapacityFor(opaqueKey);

            if (!Attempts.TryGetValue(opaqueKey, out var state))
            {
                state = new AttemptState();
                Attempts[opaqueKey] = state;
            }

            state.FailureCount = Math.Min(state.FailureCount + 1, 32);
            var exponent = Math.Min(state.FailureCount - 1, 5);
            var delaySeconds = Math.Min(1 << exponent, MaximumDelaySeconds);
            state.NextAllowedTick = now + delaySeconds * 1000L;
            state.LastSeenTick = now;
            return TimeSpan.FromSeconds(delaySeconds);
        }
    }

    public static void RegisterSuccess(string opaqueKey)
    {
        if (string.IsNullOrWhiteSpace(opaqueKey))
            return;

        lock (SyncRoot)
            Attempts.Remove(opaqueKey);
    }

    private static void EnsureCapacityFor(string opaqueKey)
    {
        if (Attempts.ContainsKey(opaqueKey) || Attempts.Count < MaxTrackedKeys)
            return;

        var oldest = Attempts.MinBy(pair => pair.Value.LastSeenTick);
        if (!string.IsNullOrEmpty(oldest.Key))
            Attempts.Remove(oldest.Key);
    }

    private static void CleanupExpired(long now)
    {
        if (Attempts.Count == 0)
            return;

        foreach (var key in Attempts
                     .Where(pair => now - pair.Value.LastSeenTick > RetentionMilliseconds)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            Attempts.Remove(key);
        }
    }

    private sealed class AttemptState
    {
        public int FailureCount { get; set; }
        public long NextAllowedTick { get; set; }
        public long LastSeenTick { get; set; }
    }
}
