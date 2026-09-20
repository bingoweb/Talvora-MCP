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
using Talvora.SourceEditing;

namespace Talvora.Tools;

public static partial class DeveloperTools
{
[McpServerTool(
        Name = "talvora_read_bytes",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraReadBytesResponse)),
     Description("Read raw bytes from any accessible file and return them as base64. count=0 requests the finite server maximum window from offset; use nextOffset while responseLimited=true to continue.")]
    public static async Task<TalvoraReadBytesResponse> ReadBytes(
        string path,
        long offset = 0,
        int count = 0,
        CancellationToken cancellationToken = default)
    {
        if (offset < 0 || count < 0)
        {
            throw new ArgumentOutOfRangeException("offset and count cannot be negative.");
        }

        var fullPath = Path.GetFullPath(path);
        await using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            useAsync: true);

        if (offset > stream.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "offset is beyond end of file.");
        }

        stream.Position = offset;
        var available = stream.Length - offset;
        var callerRequested = count == 0
            ? available
            : Math.Min((long)count, available);
        var requested = checked((int)Math.Min(
            AbsoluteReadBytesResponseBytes,
            callerRequested));
        var responseLimited =
            callerRequested > requested;

        var buffer = new byte[requested];
        var read = 0;
        while (read < requested)
        {
            var current = await stream.ReadAsync(buffer.AsMemory(read, requested - read), cancellationToken);
            if (current == 0)
            {
                break;
            }
            read += current;
        }

        if (read != buffer.Length)
        {
            Array.Resize(ref buffer, read);
        }

        return new TalvoraReadBytesResponse(
            fullPath,
            offset,
            read,
            stream.Length,
            Convert.ToBase64String(buffer),
            responseLimited,
            responseLimited
                ? offset + read
                : null);
    }

    [McpServerTool(
        Name = "talvora_write_bytes",
        Destructive = true,
        Idempotent = false,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraWriteBytesResponse)),
     Description("General raw-byte write. Text-like payloads targeting source/text files inside recognized development workspaces are rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Binary and non-workspace compatibility remains supported.")]
    public static async Task<TalvoraWriteBytesResponse> WriteBytes(
        string path,
        string base64,
        bool append = false,
        long? offset = null,
        CancellationToken cancellationToken = default)
    {
        if (append && offset is not null)
        {
            throw new ArgumentException("append and offset cannot be used together.");
        }
        if (offset is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        var bytes = Convert.FromBase64String(base64);
        var fullPath = Path.GetFullPath(path);
        SourceMutationPolicy.EnsureLegacyByteMutationAllowed(
            fullPath,
            bytes,
            "talvora_write_bytes");
        var parent = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(parent))
        {
            Directory.CreateDirectory(parent);
        }

        FileMode mode;
        FileAccess access = FileAccess.Write;
        if (append)
        {
            mode = FileMode.Append;
        }
        else if (offset is not null)
        {
            mode = FileMode.OpenOrCreate;
        }
        else
        {
            mode = FileMode.Create;
        }

        await using var stream = new FileStream(
            fullPath,
            mode,
            access,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);

        if (offset is long position)
        {
            stream.Position = position;
        }

        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        return new TalvoraWriteBytesResponse(
            fullPath,
            bytes.Length,
            stream.Length,
            append,
            offset);
    }
}
