using Microsoft.Win32;
using Talvora.Shared;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Installer;

internal static class InstallerLog
{
    private static string PathName =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Talvora",
            "Installer",
            "install.log");

    public static void Write(string message, Exception? exception = null) =>
        FileLog.Write(PathName, message, exception);
}
