using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CashFlowPlannerPro.Services;

public static class AppLogger
{
    private const long MaxLogFileBytes = 5 * 1024 * 1024;
    private const int MaxValueLength = 32 * 1024;
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CashFlowPlannerPro",
        "Logs");

    // Handles JSON, connection strings, HTTP headers and diagnostic key/value
    // text. Quoted or bracketed values may contain whitespace and separators;
    // unquoted values stop at the usual field/query delimiters.
    private static readonly Regex SecretRegex = new(
        @"(?ix)
          (?<prefix>
            (?<![a-z0-9])
            (?:
              (?:db|database)?[_-]?password(?:[_-]?hash)? |
              pwd |
              (?:access|refresh|id)?[_-]?token |
              api[_-]?key |
              authorization |
              (?:client[_-]?)?secret
            )
            (?![a-z0-9])
            [\""']?\s*(?:=|:)\s*
          )
          (?<value>
            \""(?:\\.|[^\""\\])*\"" |
            '(?:\\.|[^'\\])*' |
            \{[^}\r\n]*\} |
            \[[^\]\r\n]*\] |
            \([^\)\r\n]*\) |
            [^;,\r\n&}\]]+
          )",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public static string LogException(string eventName, Exception exception, object? context = null)
    {
        var reference = Guid.NewGuid().ToString("N")[..12];
        Write("error", eventName, exception.Message, reference, exception, context);
        return reference;
    }

    public static void Info(string eventName, string message, object? context = null)
        => Write("info", eventName, message, null, null, context);

    public static void Audit(string action, string target, bool success, object? context = null)
    {
        Write(
            "audit",
            action,
            success ? "success" : "failed",
            null,
            null,
            new
            {
                user = Sanitize(App.CurrentUsername),
                target = Sanitize(target),
                context
            });
    }

    public static void AuditAs(
        string action,
        string target,
        bool success,
        string actorUsername,
        long actorUserId,
        object? context = null)
    {
        Write(
            "audit",
            action,
            success ? "success" : "failed",
            null,
            null,
            new
            {
                user = Sanitize(actorUsername),
                userId = actorUserId > 0 ? actorUserId : (long?)null,
                target = Sanitize(target),
                context
            });
    }

    private static void Write(
        string level,
        string eventName,
        string message,
        string? reference,
        Exception? exception,
        object? context)
    {
        try
        {
            var entry = new
            {
                timestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                level,
                eventName = Sanitize(eventName),
                reference,
                message = Sanitize(message),
                exceptionType = exception?.GetType().FullName,
                stackTrace = Sanitize(exception?.StackTrace),
                context = SanitizeContext(context)
            };

            var line = JsonSerializer.Serialize(entry) + Environment.NewLine;
            lock (SyncRoot)
            {
                Directory.CreateDirectory(LogDirectory);
                var path = ResolveCurrentLogPath();
                File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                CleanupOldLogs();
            }
        }
        catch
        {
            // Logging must never hide or replace the original application error.
        }
    }

    private static string ResolveCurrentLogPath()
    {
        var baseName = $"app-{DateTime.UtcNow:yyyyMMdd}";
        var path = Path.Combine(LogDirectory, baseName + ".jsonl");
        if (!File.Exists(path) || new FileInfo(path).Length < MaxLogFileBytes)
            return path;

        for (var index = 1; index < 1000; index++)
        {
            path = Path.Combine(LogDirectory, $"{baseName}-{index:D3}.jsonl");
            if (!File.Exists(path) || new FileInfo(path).Length < MaxLogFileBytes)
                return path;
        }

        return Path.Combine(LogDirectory, $"{baseName}-{Guid.NewGuid():N}.jsonl");
    }

    private static object? SanitizeContext(object? context)
    {
        if (context == null)
            return null;

        try
        {
            return Sanitize(JsonSerializer.Serialize(context));
        }
        catch
        {
            return Sanitize(context.ToString());
        }
    }

    private static string? Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var truncated = value.Length <= MaxValueLength ? value : value[..MaxValueLength];
        try
        {
            return SecretRegex.Replace(
                truncated,
                match => match.Groups["prefix"].Value + "[REDACTED]");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[REDACTED: value could not be safely logged]";
        }
    }

    internal static string RedactForDiagnostics(string value) => Sanitize(value) ?? string.Empty;

    private static void CleanupOldLogs()
    {
        var cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (var path in Directory.EnumerateFiles(LogDirectory, "app-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var file = new FileInfo(path);
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && file.LastWriteTimeUtc < cutoff)
                    file.Delete();
            }
            catch { }
        }
    }
}
