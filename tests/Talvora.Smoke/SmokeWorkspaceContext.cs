using ModelContextProtocol.Client;

internal sealed record SmokeWorkspaceContext(
    IReadOnlyDictionary<string, McpClientTool> ByName,
    string SmokeId,
    string RepositoryPath,
    string Root)
{
    internal string File => Path.Combine(Root, "hello.txt");
    internal string SearchToken => "talvora-search-" + SmokeId;
    internal string NestedDirectory => Path.Combine(Root, "created", "nested", "leaf");
    internal string CopySourceFile => Path.Combine(Root, "copy-source.txt");
    internal string CopyDestinationFile => Path.Combine(Root, "copy-destination.txt");
    internal string CopyCollisionFile => Path.Combine(Root, "copy-collision.txt");
    internal string DirectorySource => Path.Combine(Root, "directory-source");
    internal string DirectorySourceNested => Path.Combine(DirectorySource, "child", "grandchild");
    internal string DirectoryDestination => Path.Combine(Root, "directory-destination");
    internal string DirectoryNonRecursiveDestination => Path.Combine(Root, "directory-nonrecursive");
    internal string ReparseSource => Path.Combine(Root, "reparse-source");
    internal string ReparseLoop => Path.Combine(ReparseSource, "loop");
    internal string ReparseDestination => Path.Combine(Root, "reparse-destination");
    internal string MoveFileSource => Path.Combine(Root, "move-file-source.txt");
    internal string MoveFileDestination => Path.Combine(Root, "move-file-destination.txt");
    internal string MoveDirectorySource => Path.Combine(Root, "move-directory-source");
    internal string MoveDirectoryDestination => Path.Combine(Root, "move-directory-destination");
    internal string MoveCollisionSource => Path.Combine(Root, "move-collision-source.txt");
    internal string MoveCollisionDestination => Path.Combine(Root, "move-collision-destination.txt");
    internal string DeveloperBinaryFile => Path.Combine(Root, "developer-bytes.bin");
    internal string DeveloperPatchFile => Path.Combine(Root, "developer-patch.txt");
    internal string DeveloperProjectRoot => Path.Combine(Root, "developer-project");
    internal string DeveloperPackageJson => Path.Combine(DeveloperProjectRoot, "package.json");
    internal string InteractiveSessionMarker => Path.Combine(Root, "interactive-session-" + SmokeId + ".json");
    internal string DeveloperWatchFile => Path.Combine(Root, "watch-" + SmokeId + ".txt");
    internal string DotnetProjectRoot => Path.Combine(Root, "dotnet-smoke");
    internal string DotnetProjectFile => Path.Combine(DotnetProjectRoot, "Talvora.Dotnet.Smoke.csproj");
    internal string DotnetProgramFile => Path.Combine(DotnetProjectRoot, "Program.cs");
    internal string DotnetOutputDll => Path.Combine(DotnetProjectRoot, "bin", "Release", "net10.0", "Talvora.Dotnet.Smoke.dll");
    internal string PythonVenvRoot => Path.Combine(Root, "python-venv");
    internal string DeveloperRangeFile => Path.Combine(Root, "developer-range.txt");
    internal string DeveloperJsonFile => Path.Combine(Root, "developer-config.json");
    internal string DeveloperDotenvFile => Path.Combine(Root, ".env");
    internal string DeveloperIniFile => Path.Combine(Root, "developer.ini");
    internal string DeveloperXmlFile => Path.Combine(Root, "developer.xml");
    internal string DeveloperJUnitFile => Path.Combine(Root, "junit.xml");
    internal string DeveloperArchiveSource => Path.Combine(Root, "developer-archive-source");
    internal string DeveloperArchiveZip => Path.Combine(Root, "developer-assets.zip");
    internal string DeveloperArchiveExtract => Path.Combine(Root, "developer-archive-extract");
    internal string DeveloperDownloadFile => Path.Combine(Root, "developer-health-download.json");
}
