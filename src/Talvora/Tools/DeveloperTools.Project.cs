using System.ComponentModel;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
[McpServerTool(
        Name = "talvora_project_discover",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraProjectDiscoverResponse)),
     Description("Discover application-development projects under any accessible root. Recognizes .NET, Node, Python, Rust, Go, Maven, Gradle, CMake, Docker, and Git markers. maxResults=0 requests the finite server maximum page; use resultOffset/nextResultOffset to continue while the tree is unchanged.")]
    public static TalvoraProjectDiscoverResponse ProjectDiscover(
        string root,
        bool recursive = true,
        bool followReparsePoints = false,
        int maxResults = 500,
        long resultOffset = 0,
        CancellationToken cancellationToken = default)
    {
        if (maxResults < 0 || resultOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxResults and resultOffset cannot be negative.");
        }

        var effectiveMaxResults =
            maxResults == 0
                ? AbsoluteProjectDiscoverResults
                : Math.Min(
                    maxResults,
                    AbsoluteProjectDiscoverResults);

        var fullRoot = Path.GetFullPath(root);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException($"Project discovery root was not found: {fullRoot}");
        }

        var projects = new List<TalvoraProjectEntry>();
        var errors = new List<string>();
        var truncated = false;
        long matchingIndex = 0;

        foreach (var info in EnumerateTree(fullRoot, recursive, followReparsePoints, errors, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var type = GetProjectType(info);
            if (type is null)
            {
                continue;
            }

            if (matchingIndex < resultOffset)
            {
                matchingIndex++;
                continue;
            }

            if (projects.Count >= effectiveMaxResults)
            {
                truncated = true;
                break;
            }

            projects.Add(new TalvoraProjectEntry(
                info.FullName,
                type,
                info.Name));
            matchingIndex++;
        }

        return new TalvoraProjectDiscoverResponse(
            fullRoot,
            projects.Count,
            truncated,
            projects,
            errors,
            resultOffset,
            truncated
                ? checked(resultOffset + projects.Count)
                : null);
    }

    [McpServerTool(
        Name = "talvora_resolve_command",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCommandResolveResponse)),
     Description("Resolve an executable or command name using the Talvora service PATH and Windows PATHEXT. Returns every matching executable path when includeAll=true.")]
    public static TalvoraCommandResolveResponse ResolveCommand(
        string command,
        bool includeAll = true)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException("Command is required.", nameof(command));
        }

        var matches = CommandResolver.ResolveAll([command]);
        var result = includeAll ? matches : matches.Take(1).ToArray();

        return new TalvoraCommandResolveResponse(
            command,
            result.Count > 0,
            result);
    }

    private static string? GetProjectType(FileSystemInfo info)
    {
        var isDirectory = (info.Attributes & FileAttributes.Directory) != 0;
        if (isDirectory)
        {
            return string.Equals(info.Name, ".git", StringComparison.OrdinalIgnoreCase)
                ? "git-repository"
                : null;
        }

        var name = info.Name;
        var extension = Path.GetExtension(name);

        if (extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet-solution";
        }
        if (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase))
        {
            return "dotnet-project";
        }
        if (extension.Equals(".vcxproj", StringComparison.OrdinalIgnoreCase))
        {
            return "cpp-msbuild-project";
        }

        return name.ToLowerInvariant() switch
        {
            "package.json" => "node",
            "pyproject.toml" => "python",
            "requirements.txt" => "python",
            "pipfile" => "python",
            "cargo.toml" => "rust",
            "go.mod" => "go",
            "pom.xml" => "maven",
            "build.gradle" => "gradle",
            "build.gradle.kts" => "gradle",
            "cmakelists.txt" => "cmake",
            "dockerfile" => "docker",
            "docker-compose.yml" => "docker-compose",
            "docker-compose.yaml" => "docker-compose",
            "compose.yml" => "docker-compose",
            "compose.yaml" => "docker-compose",
            _ => null,
        };
    }
}
