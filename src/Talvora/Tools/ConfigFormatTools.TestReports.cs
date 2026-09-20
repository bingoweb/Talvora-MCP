using System.ComponentModel;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

public static partial class ConfigFormatTools
{
[McpServerTool(
        Name = "talvora_test_report_summary",
        ReadOnly = true,
        OpenWorld = true,
        UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraTestReportSummary)),
     Description("Parse TRX, JUnit/xUnit-style XML, or NUnit3 test-result XML into a common summary with failed/error test details. maxFailures=0 requests the finite server maximum page; use failureOffset/nextFailureOffset to continue while the report is unchanged.")]
    public static TalvoraTestReportSummary TestReportSummary(
        string path,
        string format = "auto",
        int maxFailures = 200,
        long failureOffset = 0)
    {
        if (maxFailures < 0 || failureOffset < 0)
        {
            throw new ArgumentOutOfRangeException(
                "maxFailures and failureOffset cannot be negative.");
        }

        var effectiveMaxFailures =
            maxFailures == 0
                ? AbsoluteTestReportFailures
                : Math.Min(
                    maxFailures,
                    AbsoluteTestReportFailures);

        var fullPath = Path.GetFullPath(path);
        var document = LoadXmlDocument(fullPath);
        var detected = DetectTestReportFormat(
            document,
            format);

        return detected switch
        {
            "trx" => ParseTrx(
                fullPath,
                document,
                effectiveMaxFailures,
                failureOffset),
            "junit" => ParseJunit(
                fullPath,
                document,
                effectiveMaxFailures,
                failureOffset),
            "nunit3" => ParseNunit3(
                fullPath,
                document,
                effectiveMaxFailures,
                failureOffset),
            _ => throw new InvalidOperationException(
                $"Unsupported test report format: {detected}"),
        };
    }

    private static string DetectTestReportFormat(
        XmlDocument document,
        string requested)
    {
        var normalized =
            requested.Trim().ToLowerInvariant();

        if (normalized != "auto")
        {
            return normalized switch
            {
                "trx" => "trx",
                "junit" => "junit",
                "xunit" => "junit",
                "nunit" => "nunit3",
                "nunit3" => "nunit3",
                _ => normalized,
            };
        }

        var root =
            document.DocumentElement
            ?? throw new InvalidDataException(
                "Test report XML has no root element.");

        return root.LocalName switch
        {
            "TestRun" => "trx",
            "testsuite" => "junit",
            "testsuites" => "junit",
            "test-run" => "nunit3",
            _ => throw new InvalidDataException(
                $"Unable to detect test report format from root element '{root.LocalName}'."),
        };
    }

    private static TalvoraTestReportSummary
        ParseTrx(
            string path,
            XmlDocument document,
            int maxFailures,
            long failureOffset)
    {
        var counters =
            document.SelectSingleNode(
                "//*[local-name()='Counters']");

        var total =
            ParseIntAttribute(counters, "total");
        var passed =
            ParseIntAttribute(counters, "passed");
        var failed =
            ParseIntAttribute(counters, "failed");
        var errors =
            ParseIntAttribute(counters, "error") +
            ParseIntAttribute(counters, "timeout") +
            ParseIntAttribute(counters, "aborted");
        var skipped =
            ParseIntAttribute(counters, "notExecuted") +
            ParseIntAttribute(counters, "inconclusive");

        var failures =
            new List<TalvoraTestFailure>();
        var truncated = false;
        long seenFailures = 0;

        var nodes =
            document.SelectNodes(
                "//*[local-name()='UnitTestResult']");

        if (nodes is not null)
        {
            foreach (XmlNode node in nodes)
            {
                var outcome =
                    node.Attributes?["outcome"]?.Value
                    ?? string.Empty;

                if (string.Equals(
                        outcome,
                        "Passed",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        outcome,
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (seenFailures < failureOffset)
                {
                    seenFailures++;
                    continue;
                }

                if (failures.Count >= maxFailures)
                {
                    truncated = true;
                    break;
                }

                var errorInfo =
                    node.SelectSingleNode(
                        ".//*[local-name()='ErrorInfo']");

                failures.Add(
                    new TalvoraTestFailure(
                        node.Attributes?["testName"]?.Value
                        ?? string.Empty,
                        outcome,
                        ParseDuration(
                            node.Attributes?["duration"]?.Value),
                        errorInfo?.SelectSingleNode(
                            "./*[local-name()='Message']")?.InnerText,
                        errorInfo?.SelectSingleNode(
                            "./*[local-name()='StackTrace']")?.InnerText));
                seenFailures++;
            }
        }

        var other =
            Math.Max(
                0,
                total -
                passed -
                failed -
                errors -
                skipped);

        return new TalvoraTestReportSummary(
            path,
            "trx",
            total,
            passed,
            failed,
            errors,
            skipped,
            other,
            null,
            failures.Count,
            truncated,
            failures,
            failureOffset,
            truncated
                ? checked(failureOffset + failures.Count)
                : null);
    }

    private static TalvoraTestReportSummary
        ParseJunit(
            string path,
            XmlDocument document,
            int maxFailures,
            long failureOffset)
    {
        var suites =
            document.SelectNodes(
                "//*[local-name()='testsuite']");

        var total = 0;
        var failed = 0;
        var errors = 0;
        var skipped = 0;
        double? duration = 0;

        if (suites is not null)
        {
            foreach (XmlNode suite in suites)
            {
                total +=
                    ParseIntAttribute(
                        suite,
                        "tests");
                failed +=
                    ParseIntAttribute(
                        suite,
                        "failures");
                errors +=
                    ParseIntAttribute(
                        suite,
                        "errors");
                skipped +=
                    ParseIntAttribute(
                        suite,
                        "skipped") +
                    ParseIntAttribute(
                        suite,
                        "disabled");

                var suiteDuration =
                    ParseDoubleAttribute(
                        suite,
                        "time");

                if (suiteDuration is null)
                {
                    duration = null;
                }
                else if (duration is not null)
                {
                    duration += suiteDuration;
                }
            }
        }

        var failures =
            new List<TalvoraTestFailure>();
        var truncated = false;
        long seenFailures = 0;

        var cases =
            document.SelectNodes(
                "//*[local-name()='testcase']");

        if (cases is not null)
        {
            foreach (XmlNode testCase in cases)
            {
                var failure =
                    testCase.SelectSingleNode(
                        "./*[local-name()='failure']");
                var error =
                    testCase.SelectSingleNode(
                        "./*[local-name()='error']");

                if (failure is null &&
                    error is null)
                {
                    continue;
                }

                if (seenFailures < failureOffset)
                {
                    seenFailures++;
                    continue;
                }

                if (failures.Count >= maxFailures)
                {
                    truncated = true;
                    break;
                }

                var detail =
                    failure ?? error!;
                var className =
                    testCase.Attributes?["classname"]?.Value;
                var name =
                    testCase.Attributes?["name"]?.Value
                    ?? string.Empty;

                failures.Add(
                    new TalvoraTestFailure(
                        string.IsNullOrWhiteSpace(className)
                            ? name
                            : className + "." + name,
                        failure is not null
                            ? "Failed"
                            : "Error",
                        ParseDoubleAttribute(
                            testCase,
                            "time"),
                        detail.Attributes?["message"]?.Value
                        ?? detail.InnerText,
                        detail.InnerText));
                seenFailures++;
            }
        }

        var passed =
            Math.Max(
                0,
                total -
                failed -
                errors -
                skipped);

        return new TalvoraTestReportSummary(
            path,
            "junit",
            total,
            passed,
            failed,
            errors,
            skipped,
            0,
            duration,
            failures.Count,
            truncated,
            failures,
            failureOffset,
            truncated
                ? checked(failureOffset + failures.Count)
                : null);
    }

    private static TalvoraTestReportSummary
        ParseNunit3(
            string path,
            XmlDocument document,
            int maxFailures,
            long failureOffset)
    {
        var root =
            document.DocumentElement
            ?? throw new InvalidDataException(
                "NUnit report XML has no root element.");

        var total =
            ParseIntAttribute(root, "total");
        var passed =
            ParseIntAttribute(root, "passed");
        var failed =
            ParseIntAttribute(root, "failed");
        var skipped =
            ParseIntAttribute(root, "skipped");
        var inconclusive =
            ParseIntAttribute(
                root,
                "inconclusive");
        var duration =
            ParseDoubleAttribute(
                root,
                "duration");

        var failures =
            new List<TalvoraTestFailure>();
        var truncated = false;
        long seenFailures = 0;

        var cases =
            document.SelectNodes(
                "//*[local-name()='test-case' and @result='Failed']");

        if (cases is not null)
        {
            foreach (XmlNode testCase in cases)
            {
                if (seenFailures < failureOffset)
                {
                    seenFailures++;
                    continue;
                }

                if (failures.Count >= maxFailures)
                {
                    truncated = true;
                    break;
                }

                var failure =
                    testCase.SelectSingleNode(
                        "./*[local-name()='failure']");

                failures.Add(
                    new TalvoraTestFailure(
                        testCase.Attributes?["fullname"]?.Value
                        ?? testCase.Attributes?["name"]?.Value
                        ?? string.Empty,
                        "Failed",
                        ParseDoubleAttribute(
                            testCase,
                            "duration"),
                        failure?.SelectSingleNode(
                            "./*[local-name()='message']")?.InnerText,
                        failure?.SelectSingleNode(
                            "./*[local-name()='stack-trace']")?.InnerText));
                seenFailures++;
            }
        }

        return new TalvoraTestReportSummary(
            path,
            "nunit3",
            total,
            passed,
            failed,
            0,
            skipped,
            inconclusive,
            duration,
            failures.Count,
            truncated,
            failures,
            failureOffset,
            truncated
                ? checked(failureOffset + failures.Count)
                : null);
    }

    private static int ParseIntAttribute(
        XmlNode? node,
        string name)
    {
        return int.TryParse(
            node?.Attributes?[name]?.Value,
            out var value)
            ? value
            : 0;
    }

    private static double? ParseDoubleAttribute(
        XmlNode? node,
        string name)
    {
        return double.TryParse(
            node?.Attributes?[name]?.Value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : null;
    }

    private static double? ParseDuration(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (TimeSpan.TryParse(
                value,
                System.Globalization.CultureInfo.InvariantCulture,
                out var timeSpan))
        {
            return timeSpan.TotalSeconds;
        }

        return double.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var seconds)
            ? seconds
            : null;
    }
}
