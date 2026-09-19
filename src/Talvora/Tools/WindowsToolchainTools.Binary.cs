using Talvora.Shared;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class WindowsToolchainTools
{
[McpServerTool(
        Name = "talvora_pe_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPeInfoResponse)),
     Description("Inspect any accessible Windows Portable Executable (PE) file and return machine, subsystem, managed/native, DLL/EXE, size, and section information. Non-PE files return isPe=false instead of throwing.")]
    public static TalvoraPeInfoResponse PeInfo(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("PE source file was not found.", fullPath);
        }

        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream, PEStreamOptions.LeaveOpen);

            if (!pe.HasMetadata && pe.PEHeaders.PEHeader is null)
            {
                return new TalvoraPeInfoResponse(
                    fullPath, false, info.Length, null, false, false, false, null, 0);
            }

            var coff = pe.PEHeaders.CoffHeader;
            var header = pe.PEHeaders.PEHeader;
            var characteristics = coff.Characteristics;
            var isDll = (characteristics & System.Reflection.PortableExecutable.Characteristics.Dll) != 0;
            var isExe = !isDll;
            var isManaged = pe.HasMetadata;

            return new TalvoraPeInfoResponse(
                fullPath,
                header is not null,
                info.Length,
                coff.Machine.ToString(),
                isDll,
                isExe,
                isManaged,
                header?.Subsystem.ToString(),
                pe.PEHeaders.SectionHeaders.Length);
        }
        catch (BadImageFormatException)
        {
            return new TalvoraPeInfoResponse(
                fullPath, false, info.Length, null, false, false, false, null, 0);
        }
    }

    [McpServerTool(
        Name = "talvora_file_version_info",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraFileVersionInfoResponse)),
     Description("Return Windows file-version metadata for any accessible file. Fields may be null when the file has no version resource.")]
    public static TalvoraFileVersionInfoResponse FileVersionInfoTool(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("File was not found.", fullPath);
        }

        var info = FileVersionInfo.GetVersionInfo(fullPath);
        return new TalvoraFileVersionInfoResponse(
            fullPath,
            info.FileVersion,
            info.ProductVersion,
            info.ProductName,
            info.CompanyName,
            info.FileDescription,
            info.OriginalFilename,
            info.InternalName,
            info.LegalCopyright,
            info.IsDebug,
            info.IsPatched,
            info.IsPreRelease,
            info.IsPrivateBuild,
            info.IsSpecialBuild);
    }
}
