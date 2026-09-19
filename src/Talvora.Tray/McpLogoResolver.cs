using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Talvora.Tray;

internal static class McpLogoResolver
{
    public static BitmapSource? TryResolve(string managedMcpId)
    {
        var executable = managedMcpId.ToLowerInvariant() switch
        {
            "talvora" => Environment.ProcessPath,
            "gitea" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Gitea",
                "gitea.exe"),
            "playwright" => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "Google",
                "Chrome",
                "Application",
                "chrome.exe"),
            _ => null,
        };

        if (string.IsNullOrWhiteSpace(executable) ||
            !File.Exists(executable))
        {
            return null;
        }

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executable);
            if (icon is null)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHIcon(
                icon.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(48, 48));
            source.Freeze();
            return source;
        }
        catch (Exception ex) when (
            ex is ArgumentException or
            System.ComponentModel.Win32Exception or
            IOException)
        {
            return null;
        }
    }
}
