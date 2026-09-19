using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using Talvora.Shared;
using Tomlyn;
using Tomlyn.Model;
using YamlDotNet.Serialization;

namespace Talvora.Tools;

public sealed record TalvoraStructuredConfigGetResponse(
    string Path,
    string Format,
    string Pointer,
    bool Found,
    string Kind,
    string? ValueJson);

public sealed record TalvoraStructuredConfigMutationResponse(
    string Path,
    string Format,
    string Pointer,
    bool Changed,
    string? BackupPath);

[McpServerToolType]
public static partial class StructuredConfigTools
{
    private static readonly IDeserializer YamlDeserializer =
        new DeserializerBuilder().Build();

    private static readonly ISerializer YamlSerializer =
        new SerializerBuilder().Build();
}
