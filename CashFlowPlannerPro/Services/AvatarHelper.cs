using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CashFlowPlannerPro.Services;

public static class AvatarHelper
{
    private const int MaxSizeBytes = 500 * 1024; // 500KB
    private const int TargetSize = 128;
    private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };

    public static string? LoadAndValidateImage(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (Array.IndexOf(AllowedExtensions, ext) < 0)
            throw new InvalidOperationException("Nur JPG und PNG Dateien erlaubt.");

        var fileBytes = File.ReadAllBytes(filePath);
        if (fileBytes.Length > MaxSizeBytes * 4) // raw file limit before resize
            throw new InvalidOperationException("Datei zu groß (max. 2MB).");

        // Validate file header (magic bytes)
        if (!IsValidImageHeader(fileBytes))
            throw new InvalidOperationException("Ungültiges Bildformat.");

        // Resize to 128x128 and convert to PNG
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = new MemoryStream(fileBytes);
        bitmap.DecodePixelWidth = TargetSize;
        bitmap.DecodePixelHeight = TargetSize;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var ms = new MemoryStream();
        encoder.Save(ms);

        var result = Convert.ToBase64String(ms.ToArray());
        if (result.Length > 1_000_000) // ~750KB base64 limit
            throw new InvalidOperationException("Bild nach Komprimierung zu groß.");

        return result;
    }

    public static ImageSource? Base64ToImage(string? base64)
    {
        if (string.IsNullOrEmpty(base64)) return null;
        try
        {
            var bytes = Convert.FromBase64String(base64);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch { return null; }
    }

    public static ImageSource GetDefaultAvatar(string name)
    {
        // Create a circle with initials
        var initials = GetInitials(name);
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            var colors = new[] { "#3498db", "#e74c3c", "#2ecc71", "#f39c12", "#9b59b6", "#1abc9c" };
            var hash = Math.Abs(name.GetHashCode()) % colors.Length;
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[hash]));
            dc.DrawEllipse(brush, null, new System.Windows.Point(64, 64), 64, 64);
            var ft = new FormattedText(initials, System.Globalization.CultureInfo.CurrentCulture,
                System.Windows.FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 40, Brushes.White,
                VisualTreeHelper.GetDpi(dv).PixelsPerDip);
            dc.DrawText(ft, new System.Windows.Point(64 - ft.Width / 2, 64 - ft.Height / 2));
        }
        var rtb = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    private static string GetInitials(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2) return $"{parts[0][0]}{parts[1][0]}".ToUpper();
        return name[..Math.Min(2, name.Length)].ToUpper();
    }

    private static bool IsValidImageHeader(byte[] data)
    {
        if (data.Length < 4) return false;
        // PNG: 89 50 4E 47
        if (data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47) return true;
        // JPEG: FF D8 FF
        if (data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return true;
        return false;
    }
}
