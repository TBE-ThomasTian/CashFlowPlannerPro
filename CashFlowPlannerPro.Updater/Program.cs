using System.Diagnostics;
using System.IO.Compression;

if (args.Length < 4)
    return 1;

string zipPath = args[0];
string appDirectory = args[1];
string appExePath = args[2];
if (!int.TryParse(args[3], out int appProcessId))
    return 1;

try
{
    WaitForProcessExit(appProcessId);

    string extractDirectory = Path.Combine(Path.GetTempPath(), "CashFlowPlannerPro_Update_" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(extractDirectory);

    ZipFile.ExtractToDirectory(zipPath, extractDirectory, true);
    CopyDirectory(extractDirectory, appDirectory);

    Process.Start(new ProcessStartInfo
    {
        FileName = appExePath,
        WorkingDirectory = appDirectory,
        UseShellExecute = true
    });

    TryDeleteDirectory(extractDirectory);
    TryDeleteFile(zipPath);
    return 0;
}
catch
{
    return 1;
}

static void WaitForProcessExit(int processId)
{
    try
    {
        using var process = Process.GetProcessById(processId);
        process.WaitForExit(30000);
    }
    catch
    {
    }
}

static void CopyDirectory(string sourceDirectory, string targetDirectory)
{
    foreach (var directory in Directory.GetDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        string relativePath = Path.GetRelativePath(sourceDirectory, directory);
        if (relativePath.StartsWith("updater", StringComparison.OrdinalIgnoreCase))
            continue;

        Directory.CreateDirectory(Path.Combine(targetDirectory, relativePath));
    }

    foreach (var file in Directory.GetFiles(sourceDirectory, "*", SearchOption.AllDirectories))
    {
        string relativePath = Path.GetRelativePath(sourceDirectory, file);
        if (relativePath.StartsWith("updater", StringComparison.OrdinalIgnoreCase))
            continue;

        string destination = Path.Combine(targetDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(file, destination, true);
    }
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, true);
    }
    catch
    {
    }
}

static void TryDeleteFile(string path)
{
    try
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    catch
    {
    }
}
