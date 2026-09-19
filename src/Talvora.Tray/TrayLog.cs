using System.Diagnostics;
using Talvora.Shared;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Tray;

internal static class TrayLog
{
    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "Tray",
            "tray.log");

    public static void Write(string message, Exception? exception = null) =>
        FileLog.Write(LogPath, message, exception);
}
