using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using RetroGameCoverDownloader.Helpers;

namespace RetroGameCoverDownloader.Services;

public static class ScreenshotService
{
    private static readonly string ScreenshotsFolder = AppInfo.ScreenshotsFolderPath;

    public static (bool Success, string? FilePath) CaptureForegroundWindow()
    {
        try
        {
            Directory.CreateDirectory(ScreenshotsFolder);
        }
        catch
        {
            return (false, null);
        }

        var testFile = Path.Combine(ScreenshotsFolder, ".writetest");
        try
        {
            File.WriteAllText(testFile, "");
            File.Delete(testFile);
        }
        catch
        {
            return (false, null);
        }

        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
            return (false, null);

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            return (false, null);

        using var bitmap = new Bitmap(width, height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0,
            new Size(width, height));

        var fileName = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
        var filePath = Path.Combine(ScreenshotsFolder, fileName);

        bitmap.Save(filePath, ImageFormat.Png);
        return (true, filePath);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(IntPtr hWnd, out Rect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
