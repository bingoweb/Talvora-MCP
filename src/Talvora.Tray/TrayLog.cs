using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using Talvora.Shared;

namespace Talvora.Tray;

internal static class TrayLog
{
    internal static string PathName =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "Tray",
            "tray.log");

    public static void Write(string message, Exception? exception = null) =>
        FileLog.Write(PathName, message, exception, maxBytes: 3L * 1024 * 1024);
}