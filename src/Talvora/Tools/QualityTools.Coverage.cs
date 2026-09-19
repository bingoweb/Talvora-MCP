using System.ComponentModel;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class QualityTools
{
    [McpServerTool(
        Name = "talvora_coverage_summary",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraCoverageSummaryResponse)),
     Description("Parse Cobertura, JaCoCo, lcov, or OpenCover coverage output into common line/branch/function totals and percentages. format=auto detects the report type.")]
    public static TalvoraCoverageSummaryResponse CoverageSummary(
        string path,
        string format = "auto")
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Coverage report was not found.",
                fullPath);
        }

        var requested = format.Trim().ToLowerInvariant();
        if (requested == "lcov" ||
            (requested == "auto" &&
             (fullPath.EndsWith(".info", StringComparison.OrdinalIgnoreCase) ||
              fullPath.EndsWith(".lcov", StringComparison.OrdinalIgnoreCase))))
        {
            return ParseLcov(fullPath);
        }

        var document = LoadCoverageXml(fullPath);
        var root = document.Root
            ?? throw new InvalidDataException(
                "Coverage XML has no root element.");

        var detected = requested == "auto"
            ? DetectCoverageXmlFormat(root)
            : NormalizeCoverageFormat(requested);

        return detected switch
        {
            "cobertura" => ParseCobertura(fullPath, root),
            "jacoco" => ParseJacoco(fullPath, root),
            "opencover" => ParseOpenCover(fullPath, root),
            _ => throw new InvalidOperationException(
                $"Unsupported coverage format: {detected}"),
        };
    }

    private static string DetectCoverageXmlFormat(XElement root)
    {
        if (root.Name.LocalName.Equals(
                "coverage",
                StringComparison.OrdinalIgnoreCase))
        {
            return "cobertura";
        }

        if (root.Name.LocalName.Equals(
                "report",
                StringComparison.OrdinalIgnoreCase) &&
            root.Elements().Any(element =>
                element.Name.LocalName.Equals(
                    "counter",
                    StringComparison.OrdinalIgnoreCase)))
        {
            return "jacoco";
        }

        if (root.Name.LocalName.Equals(
                "CoverageSession",
                StringComparison.OrdinalIgnoreCase))
        {
            return "opencover";
        }

        throw new InvalidDataException(
            $"Unable to detect coverage format from root '{root.Name.LocalName}'.");
    }

    private static string NormalizeCoverageFormat(string format) =>
        format switch
        {
            "cobertura" or "coverlet" => "cobertura",
            "jacoco" => "jacoco",
            "lcov" => "lcov",
            "opencover" => "opencover",
            _ => format,
        };

    private static TalvoraCoverageSummaryResponse ParseCobertura(
        string path,
        XElement root)
    {
        var linesTotal = ReadLongAttribute(root, "lines-valid");
        var linesCovered = ReadLongAttribute(root, "lines-covered");
        var branchesTotal = ReadLongAttribute(root, "branches-valid");
        var branchesCovered = ReadLongAttribute(root, "branches-covered");

        if (linesTotal == 0)
        {
            var lines = root
                .Descendants()
                .Where(element =>
                    element.Name.LocalName.Equals(
                        "line",
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();

            linesTotal = lines.LongLength;
            linesCovered = lines.LongCount(line =>
                ReadLongAttribute(line, "hits") > 0);
        }

        var files = root
            .Descendants()
            .Where(element =>
                element.Name.LocalName.Equals(
                    "class",
                    StringComparison.OrdinalIgnoreCase))
            .Select(element =>
                (string?)element.Attribute("filename"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new TalvoraCoverageSummaryResponse(
            path,
            "cobertura",
            files,
            Metric(linesCovered, linesTotal),
            Metric(branchesCovered, branchesTotal),
            Metric(0, 0));
    }

    private static TalvoraCoverageSummaryResponse ParseJacoco(
        string path,
        XElement root)
    {
        var counters = root
            .Elements()
            .Where(element =>
                element.Name.LocalName.Equals(
                    "counter",
                    StringComparison.OrdinalIgnoreCase))
            .ToDictionary(
                element =>
                    ((string?)element.Attribute("type") ?? string.Empty)
                        .ToUpperInvariant(),
                element => element,
                StringComparer.OrdinalIgnoreCase);

        var lines = CounterMetric(counters, "LINE");
        var branches = CounterMetric(counters, "BRANCH");
        var methods = CounterMetric(counters, "METHOD");

        var files = root
            .Descendants()
            .Where(element =>
                element.Name.LocalName.Equals(
                    "sourcefile",
                    StringComparison.OrdinalIgnoreCase))
            .Count();

        return new TalvoraCoverageSummaryResponse(
            path,
            "jacoco",
            files,
            lines,
            branches,
            methods);
    }

    private static TalvoraCoverageSummaryResponse ParseOpenCover(
        string path,
        XElement root)
    {
        var summary = root
            .Descendants()
            .FirstOrDefault(element =>
                element.Name.LocalName.Equals(
                    "Summary",
                    StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException(
                "OpenCover report has no Summary element.");

        var linesTotal =
            ReadLongAttribute(summary, "numSequencePoints");
        var linesCovered =
            ReadLongAttribute(summary, "visitedSequencePoints");
        var branchesTotal =
            ReadLongAttribute(summary, "numBranchPoints");
        var branchesCovered =
            ReadLongAttribute(summary, "visitedBranchPoints");
        var methodsTotal =
            ReadLongAttribute(summary, "numMethods");
        var methodsCovered =
            ReadLongAttribute(summary, "visitedMethods");

        var files = root
            .Descendants()
            .Where(element =>
                element.Name.LocalName.Equals(
                    "File",
                    StringComparison.OrdinalIgnoreCase))
            .Select(element =>
                (string?)element.Attribute("fullPath"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new TalvoraCoverageSummaryResponse(
            path,
            "opencover",
            files,
            Metric(linesCovered, linesTotal),
            Metric(branchesCovered, branchesTotal),
            Metric(methodsCovered, methodsTotal));
    }

    private static TalvoraCoverageSummaryResponse ParseLcov(
        string path)
    {
        long linesTotal = 0;
        long linesCovered = 0;
        long branchesTotal = 0;
        long branchesCovered = 0;
        long functionsTotal = 0;
        long functionsCovered = 0;
        var files = 0;

        foreach (var line in File.ReadLines(path))
        {
            if (line.StartsWith("SF:", StringComparison.Ordinal))
            {
                files++;
                continue;
            }

            AddLcovValue(line, "LF:", ref linesTotal);
            AddLcovValue(line, "LH:", ref linesCovered);
            AddLcovValue(line, "BRF:", ref branchesTotal);
            AddLcovValue(line, "BRH:", ref branchesCovered);
            AddLcovValue(line, "FNF:", ref functionsTotal);
            AddLcovValue(line, "FNH:", ref functionsCovered);
        }

        return new TalvoraCoverageSummaryResponse(
            path,
            "lcov",
            files,
            Metric(linesCovered, linesTotal),
            Metric(branchesCovered, branchesTotal),
            Metric(functionsCovered, functionsTotal));
    }

    private static void AddLcovValue(
        string line,
        string prefix,
        ref long target)
    {
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        if (long.TryParse(
                line.AsSpan(prefix.Length),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value))
        {
            target += value;
        }
    }

    private static TalvoraCoverageMetric CounterMetric(
        IReadOnlyDictionary<string, XElement> counters,
        string type)
    {
        if (!counters.TryGetValue(type, out var counter))
        {
            return Metric(0, 0);
        }

        var missed = ReadLongAttribute(counter, "missed");
        var covered = ReadLongAttribute(counter, "covered");
        return Metric(covered, missed + covered);
    }

    private static TalvoraCoverageMetric Metric(
        long covered,
        long total) =>
        new(
            covered,
            total,
            total > 0
                ? Math.Round(
                    100d * covered / total,
                    4,
                    MidpointRounding.AwayFromZero)
                : null);

    private static long ReadLongAttribute(
        XElement element,
        string name) =>
        long.TryParse(
            (string?)element.Attribute(name),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private static XDocument LoadCoverageXml(string path)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };

        using var reader = XmlReader.Create(path, settings);
        return XDocument.Load(
            reader,
            LoadOptions.None);
    }
}
