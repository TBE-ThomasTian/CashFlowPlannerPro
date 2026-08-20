namespace CashFlowPlannerPro.Services;

public static class PasswordPolicy
{
    public const int MinimumLength = 12;
    public const int MaximumLength = 128;

    private static readonly HashSet<string> DisallowedPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "administrator",
        "password",
        "passwort",
        "password123",
        "passwort123",
        "123456789012",
        "qwertzuiop12",
        "qwertyuiop12"
    };

    public static bool TryValidate(string? password, string? username, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            error = string.Format(
                LocalizationManager.Get("PasswordPolicyMinimumLength"),
                MinimumLength);
            return false;
        }

        if (password.Length > MaximumLength)
        {
            error = string.Format(
                LocalizationManager.Get("PasswordPolicyMaximumLength"),
                MaximumLength);
            return false;
        }

        if (password.Any(char.IsControl))
        {
            error = LocalizationManager.Get("PasswordPolicyControlCharacters");
            return false;
        }

        var trimmedUsername = username?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmedUsername) &&
            password.Contains(trimmedUsername, StringComparison.OrdinalIgnoreCase))
        {
            error = LocalizationManager.Get("PasswordPolicyContainsUsername");
            return false;
        }

        if (DisallowedPasswords.Contains(password))
        {
            error = LocalizationManager.Get("PasswordPolicyCommonPassword");
            return false;
        }

        return true;
    }
}
