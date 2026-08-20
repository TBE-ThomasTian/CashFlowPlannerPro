using System.Diagnostics;
using System.IO;

namespace CashFlowPlannerPro.Services;

public static class SafeDocumentLauncher
{
    public static bool TryOpenLocalPdf(string? path, out string error)
    {
        error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "Es ist keine PDF-Datei hinterlegt.";
                return false;
            }

            if (!Path.IsPathFullyQualified(path))
            {
                error = "Die PDF-Datei muss über einen vollständigen lokalen Pfad geöffnet werden.";
                return false;
            }

            var fullPath = Path.GetFullPath(path.Trim());
            if (fullPath.StartsWith("\\\\", StringComparison.Ordinal) ||
                new Uri(fullPath).IsUnc)
            {
                error = "PDF-Dateien von Netzwerkpfaden werden aus Sicherheitsgründen nicht direkt geöffnet.";
                return false;
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                error = "Es können ausschließlich PDF-Dateien geöffnet werden.";
                return false;
            }

            var root = Path.GetPathRoot(fullPath);
            if (!string.IsNullOrWhiteSpace(root) && new DriveInfo(root).DriveType == DriveType.Network)
            {
                error = "PDF-Dateien von Netzlaufwerken werden aus Sicherheitsgründen nicht direkt geöffnet.";
                return false;
            }

            if (!File.Exists(fullPath))
            {
                error = "Die hinterlegte PDF-Datei wurde nicht gefunden.";
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
            return true;
        }
        catch
        {
            error = "Die PDF-Datei konnte nicht sicher geöffnet werden.";
            return false;
        }
    }
}
