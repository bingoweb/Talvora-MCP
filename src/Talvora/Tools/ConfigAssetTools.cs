using System.ComponentModel;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;

namespace Talvora.Tools;

public sealed record TalvoraTextRangeResponse(
    string Path,
    int StartLine,
    int LinesRead,
    bool EndReached,
    string Text);

public sealed record TalvoraTextTailResponse(
    string Path,
    int TotalLines,
    int StartLine,
    int LinesRead,
    string Text);

public sealed record TalvoraAppendTextResponse(
    string Path,
    int CharactersAppended,
    long FileLength,
    bool NewLineAppended);

public sealed record TalvoraJsonGetResponse(
    string Path,
    string Pointer,
    bool Found,
    string Kind,
    string? ValueJson);

public sealed record TalvoraJsonMutationResponse(
    string Path,
    string Pointer,
    bool Changed,
    string? BackupPath);

public sealed record TalvoraArchiveEntry(
    string FullName,
    long Length,
    long CompressedLength,
    DateTimeOffset LastWriteTime,
    bool IsDirectory,
    int ExternalAttributes);

public sealed record TalvoraArchiveListResponse(
    string ArchivePath,
    int Count,
    IReadOnlyList<TalvoraArchiveEntry> Entries);

public sealed record TalvoraArchiveCreateResponse(
    string SourceDirectory,
    string ArchivePath,
    long Length,
    int EntryCount,
    string Compression,
    bool IncludedBaseDirectory);

public sealed record TalvoraArchiveExtractResponse(
    string ArchivePath,
    string DestinationDirectory,
    int EntriesExtracted,
    long BytesExtracted,
    bool Overwrite,
    bool AllowOutsideDestination);

public sealed record TalvoraHttpDownloadResponse(
    string Url,
    string FinalUrl,
    int StatusCode,
    string? ReasonPhrase,
    string DestinationPath,
    long ExistingBytes,
    long BytesWritten,
    long FileLength,
    bool Resumed,
    string Sha256);

[McpServerToolType]
public static partial class ConfigAssetTools
{
}
