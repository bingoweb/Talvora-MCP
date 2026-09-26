$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$sharedProject = Join-Path $repoRoot 'src\Talvora.Shared\Talvora.Shared.csproj'
$atomicFileSource = Join-Path $repoRoot 'src\Talvora.Shared\AtomicFile.cs'
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('Talvora.AtomicFileRegression.' + [Guid]::NewGuid().ToString('N'))
$fixtureProject = Join-Path $tempRoot 'Fixture'
$fixtureData = Join-Path $tempRoot 'Data'

function Assert-True {
    param(
        [Parameter(Mandatory = $true)]
        [bool] $Condition,

        [Parameter(Mandatory = $true)]
        [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

try {
    $source = Get-Content -LiteralPath $atomicFileSource -Raw
    Assert-True ($source.Contains('FileOptions.WriteThrough')) 'AtomicFile must use write-through staged writes.'
    Assert-True ($source.Contains('Flush(flushToDisk: true)')) 'AtomicFile must flush staged file data through intermediate buffers.'
    Assert-True ($source.Contains('File.Replace(')) 'AtomicFile must use metadata-preserving replacement for existing destinations.'
    Assert-True ($source.Contains('ignoreMetadataErrors: false')) 'AtomicFile must fail closed instead of silently dropping destination metadata.'
    Assert-True ($source.Contains('MoveFileExW(')) 'AtomicFile must keep the durable move primitive for new destinations.'
    Assert-True ($source.Contains('MoveFileFlags.WriteThrough')) 'AtomicFile new-file publication must request MOVEFILE_WRITE_THROUGH.'
    Assert-True (-not $source.Contains('CopyDurableAsync(')) 'AtomicFile backup creation must be part of the metadata-preserving replace operation.'
    Assert-True (-not $source.Contains('FlushPublishedFile(')) 'AtomicFile must not reopen an already-published destination for a second write-handle flush.'

    New-Item -ItemType Directory -Path $fixtureProject -Force | Out-Null
    New-Item -ItemType Directory -Path $fixtureData -Force | Out-Null

    $escapedSharedProject = [Security.SecurityElement]::Escape($sharedProject)
    $projectXml = @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$escapedSharedProject" />
  </ItemGroup>
</Project>
"@
    [IO.File]::WriteAllText(
        (Join-Path $fixtureProject 'Fixture.csproj'),
        $projectXml,
        [Text.UTF8Encoding]::new($false))

    $program = @'
using System.Text;
using Talvora.Shared;

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

var root = args[0];
Directory.CreateDirectory(root);
var path = Path.Combine(root, "state.json");
var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

await AtomicFile.WriteAllTextAsync(
    path,
    "before",
    encoding);

var preservedCreationTime =
    new DateTime(
        2020,
        1,
        2,
        3,
        4,
        5,
        DateTimeKind.Utc);
File.SetCreationTimeUtc(
    path,
    preservedCreationTime);
File.SetAttributes(
    path,
    File.GetAttributes(path) |
    FileAttributes.Hidden);

var backupPath = await AtomicFile.WriteAllTextAsync(
    path,
    "after",
    encoding,
    createBackup: true);

Require(File.ReadAllText(path, encoding) == "after", "Primary publication did not contain the new content.");
Require(backupPath is not null, "Backup path was not returned.");
Require(File.ReadAllText(backupPath!, encoding) == "before", "Backup publication did not preserve the prior content.");
Require(
    Math.Abs(
        (File.GetCreationTimeUtc(path) -
         preservedCreationTime).TotalSeconds) < 1,
    "Metadata-preserving replacement changed the destination creation time.");
Require(
    (File.GetAttributes(path) &
     FileAttributes.Hidden) != 0,
    "Metadata-preserving replacement lost the destination file attributes.");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    await AtomicFile.WriteAllTextAsync(
        path,
        "cancelled",
        encoding,
        createBackup: true,
        cancelled.Token);
    throw new InvalidOperationException("Pre-cancelled publication unexpectedly succeeded.");
}
catch (OperationCanceledException)
{
}

Require(File.ReadAllText(path, encoding) == "after", "Pre-cancelled publication mutated the primary file.");
Require(File.ReadAllText(path + ".bak", encoding) == "before", "Pre-cancelled publication mutated the existing backup.");
Require(!Directory.EnumerateFiles(root, "*.tmp", SearchOption.TopDirectoryOnly).Any(), "AtomicFile left temporary files behind.");

Console.WriteLine("ATOMIC_FILE_DURABLE_METADATA_REGRESSION_GREEN");
'@
    [IO.File]::WriteAllText(
        (Join-Path $fixtureProject 'Program.cs'),
        $program,
        [Text.UTF8Encoding]::new($false))

    & dotnet run --project (Join-Path $fixtureProject 'Fixture.csproj') -c Release -- $fixtureData
    if ($LASTEXITCODE -ne 0) {
        throw "AtomicFile durability fixture failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
