using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using ModelContextProtocol.Server;
using Talvora.SourceEditing;

namespace Talvora.Tools;

public static partial class ConfigFormatTools
{
[McpServerTool(
        Name = "talvora_xml_query",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraXmlQueryResponse)),
     Description("Evaluate an arbitrary XPath expression against any accessible XML file with a finite 4 MiB response-character budget. Supports namespace prefix mappings and scalar XPath results. For node sets, maxResults=0 requests the finite server maximum page; use resultOffset/nextResultOffset to continue while the XML is unchanged. Oversized single nodes or scalar results are rejected instead of returning an unbounded MCP payload.")]
    public static TalvoraXmlQueryResponse XmlQuery(
        string path,
        string xpath,
        Dictionary<string, string>? namespaces = null,
        int maxResults = 200,
        long resultOffset = 0)
    {
        if (string.IsNullOrWhiteSpace(xpath))
        {
            throw new ArgumentException(
                "XPath expression is required.",
                nameof(xpath));
        }
        if (maxResults < 0 || resultOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxResults and resultOffset cannot be negative.");
        }

        var effectiveMaxResults =
            maxResults == 0
                ? AbsoluteXmlQueryResults
                : Math.Min(
                    maxResults,
                    AbsoluteXmlQueryResults);

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var navigator = document.CreateNavigator()
            ?? throw new InvalidOperationException(
                "Unable to create XML navigator.");
        var manager = CreateNamespaceManager(
            navigator.NameTable,
            namespaces);

        var result = navigator.Evaluate(
            xpath,
            manager);

        if (result is XPathNodeIterator iterator)
        {
            var nodes =
                new List<TalvoraXmlNodeResult>();
            var truncated = false;
            long matchingIndex = 0;
            long responseCharacters = 0;

            while (iterator.MoveNext())
            {
                if (iterator.Current is null)
                {
                    continue;
                }

                if (matchingIndex < resultOffset)
                {
                    matchingIndex++;
                    continue;
                }

                if (nodes.Count >= effectiveMaxResults)
                {
                    truncated = true;
                    break;
                }

                var node =
                    ToXmlNodeResult(
                        iterator.Current);
                var nodeCharacters =
                    EstimateXmlNodeResponseCharacters(
                        node);
                if (responseCharacters + nodeCharacters >
                    AbsoluteXmlQueryResponseCharacters)
                {
                    if (nodes.Count == 0)
                    {
                        throw new InvalidOperationException(
                            $"XML result at offset {matchingIndex} exceeds the 4 MiB response-character budget. Use a narrower XPath expression or talvora_read_text_range.");
                    }

                    truncated = true;
                    break;
                }

                nodes.Add(node);
                responseCharacters +=
                    nodeCharacters;
                matchingIndex++;
            }

            return new TalvoraXmlQueryResponse(
                fullPath,
                xpath,
                "nodes",
                null,
                nodes.Count,
                truncated,
                nodes,
                resultOffset,
                truncated
                    ? checked(resultOffset + nodes.Count)
                    : null);
        }

        var scalarValue =
            Convert.ToString(
                result,
                System.Globalization.CultureInfo.InvariantCulture);
        if (scalarValue is not null &&
            scalarValue.Length >
                AbsoluteXmlQueryResponseCharacters)
        {
            throw new InvalidOperationException(
                "XML scalar result exceeds the 4 MiB response-character budget. Use a narrower XPath expression or talvora_read_text_range.");
        }

        return new TalvoraXmlQueryResponse(
            fullPath,
            xpath,
            result?.GetType().Name ?? "null",
            scalarValue,
            0,
            false,
            [],
            0,
            null);
    }

    [McpServerTool(
        Name = "talvora_xml_set",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Compatibility XML mutator for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Supports arbitrary XPath plus expectedMatches.")]
    public static TalvoraConfigMutationResponse XmlSet(
        string path,
        string xpath,
        string value,
        Dictionary<string, string>? namespaces = null,
        int expectedMatches = -1)
    {
        if (expectedMatches < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedMatches));
        }

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var navigator = document.CreateNavigator()
            ?? throw new InvalidOperationException(
                "Unable to create XML navigator.");
        var manager = CreateNamespaceManager(
            navigator.NameTable,
            namespaces);

        var iterator = navigator.Select(
            xpath,
            manager);
        var nodes = new List<XPathNavigator>();

        while (iterator.MoveNext())
        {
            if (iterator.Current is not null)
            {
                nodes.Add(
                    iterator.Current.Clone());
            }
        }

        if (expectedMatches >= 0 &&
            nodes.Count != expectedMatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMatches} XPath match(es), found {nodes.Count}. XML was not modified.");
        }

        var changed = false;

        foreach (var node in nodes)
        {
            if (!string.Equals(
                    node.Value,
                    value,
                    StringComparison.Ordinal))
            {
                node.SetValue(value);
                changed = true;
            }
        }

        if (changed)
        {
            SaveXmlDocument(
                document,
                fullPath,
                "talvora_xml_set/delete");
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            nodes.Count,
            changed);
    }

    [McpServerTool(
        Name = "talvora_xml_delete",
        Destructive = true,
        Idempotent = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraConfigMutationResponse)),
     Description("Compatibility XML delete tool for ordinary/non-workspace files. Inside recognized development workspaces, source/config mutation is rejected with SOURCE_EDIT_POLICY_VIOLATION. " + SourceEditRoutingContract.LegacyMutationRouting + " Supports arbitrary XPath plus expectedMatches.")]
    public static TalvoraConfigMutationResponse XmlDelete(
        string path,
        string xpath,
        Dictionary<string, string>? namespaces = null,
        int expectedMatches = -1)
    {
        if (expectedMatches < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedMatches));
        }

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var navigator = document.CreateNavigator()
            ?? throw new InvalidOperationException(
                "Unable to create XML navigator.");
        var manager = CreateNamespaceManager(
            navigator.NameTable,
            namespaces);

        var iterator = navigator.Select(
            xpath,
            manager);
        var nodes = new List<XPathNavigator>();

        while (iterator.MoveNext())
        {
            if (iterator.Current is not null)
            {
                nodes.Add(
                    iterator.Current.Clone());
            }
        }

        if (expectedMatches >= 0 &&
            nodes.Count != expectedMatches)
        {
            throw new InvalidOperationException(
                $"Expected {expectedMatches} XPath match(es), found {nodes.Count}. XML was not modified.");
        }

        foreach (var node in nodes
                     .OrderByDescending(
                         GetNavigatorDepth))
        {
            node.DeleteSelf();
        }

        if (nodes.Count > 0)
        {
            SaveXmlDocument(
                document,
                fullPath,
                "talvora_xml_set/delete");
        }

        return new TalvoraConfigMutationResponse(
            fullPath,
            nodes.Count,
            nodes.Count > 0);
    }

    private static XmlDocument LoadXmlDocument(
        string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
        };

        var document = new XmlDocument
        {
            PreserveWhitespace = true,
            XmlResolver = null,
        };

        using var reader =
            XmlReader.Create(
                path,
                settings);

        document.Load(reader);
        return document;
    }

    private static void SaveXmlDocument(
        XmlDocument document,
        string path,
        string toolName)
    {
        SourceMutationPolicy.EnsureLegacyTextMutationAllowed(
            path,
            toolName);
        using var writer = XmlWriter.Create(
            path,
            new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None,
            });

        document.Save(writer);
    }

    private static XmlNamespaceManager
        CreateNamespaceManager(
            XmlNameTable nameTable,
            Dictionary<string, string>? namespaces)
    {
        var manager =
            new XmlNamespaceManager(nameTable);

        foreach (var pair in namespaces
                     ?? new Dictionary<string, string>())
        {
            manager.AddNamespace(
                pair.Key,
                pair.Value);
        }

        return manager;
    }

    private static TalvoraXmlNodeResult
        ToXmlNodeResult(
            XPathNavigator navigator)
    {
        var attributes =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        if (navigator.NodeType ==
                XPathNodeType.Element &&
            navigator.HasAttributes)
        {
            var clone =
                navigator.Clone();

            if (clone.MoveToFirstAttribute())
            {
                do
                {
                    attributes[clone.Name] =
                        clone.Value;
                }
                while (clone.MoveToNextAttribute());
            }
        }

        return new TalvoraXmlNodeResult(
            navigator.NodeType.ToString(),
            navigator.Name,
            navigator.Value,
            navigator.OuterXml,
            attributes);
    }

    private static long EstimateXmlNodeResponseCharacters(
        TalvoraXmlNodeResult node)
    {
        long characters =
            128L +
            node.NodeType.Length +
            node.Name.Length +
            node.Value.Length +
            node.OuterXml.Length;

        foreach (var pair in node.Attributes)
        {
            characters +=
                32L +
                pair.Key.Length +
                pair.Value.Length;
        }

        return characters;
    }

    private static int GetNavigatorDepth(
        XPathNavigator navigator)
    {
        var clone =
            navigator.Clone();
        var depth = 0;

        while (clone.MoveToParent())
        {
            depth++;
        }

        return depth;
    }
}
