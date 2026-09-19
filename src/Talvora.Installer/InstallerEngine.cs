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

internal static partial class InstallerEngine
{
    private const string ServiceName = TalvoraConstants.ServiceName;
    private const string RunValueName = TalvoraConstants.TrayRunValueName;
    private const string McpUrl = TalvoraConstants.McpUrl;
    private const string HealthUrl = TalvoraConstants.HealthUrl;

    private static IReadOnlyList<string> ToolNames => TalvoraToolManifest.Names;

    private static readonly JsonSerializerOptions IndentedJsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions CaseInsensitiveJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
