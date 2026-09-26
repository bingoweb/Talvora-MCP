using static SmokeSupport;

internal static partial class SmokeScenarios
{
    private static async Task RunWorkspaceConfigAssetsAsync(SmokeWorkspaceContext context)
    {
        var byName = context.ByName;
        var smokeId = context.SmokeId;
        var root = context.Root;
        var developerRangeFile = context.DeveloperRangeFile;
        var developerJsonFile = context.DeveloperJsonFile;
        var developerDotenvFile = context.DeveloperDotenvFile;
        var developerIniFile = context.DeveloperIniFile;
        var developerXmlFile = context.DeveloperXmlFile;
        var developerJUnitFile = context.DeveloperJUnitFile;
        var developerArchiveSource = context.DeveloperArchiveSource;
        var developerArchiveZip = context.DeveloperArchiveZip;
        var developerArchiveExtract = context.DeveloperArchiveExtract;
        var developerDownloadFile = context.DeveloperDownloadFile;
        var noFinalDotenvFile =
            Path.Combine(root, "no-final.env");
        var noFinalIniFile =
            Path.Combine(root, "no-final.ini");
        var manyDotenvFile =
            Path.Combine(root, "many.env");
        var manyIniFile =
            Path.Combine(root, "many.ini");
        await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = developerRangeFile,
                    ["content"] = "line-one\r\nline-two\r\nline-three",
                });
                var appendTextResult = await EnsureSuccess(byName["talvora_append_text"], new()
                {
                    ["path"] = developerRangeFile,
                    ["content"] = "\r\nline-four",
                    ["appendNewLine"] = false,
                });
                if (appendTextResult.StructuredContent is not { } appendTextJson ||
                    appendTextJson.GetProperty("charactersAppended").GetInt32() <= 0)
                {
                    throw new InvalidOperationException("append_text did not append content.");
                }
                
                var rangeResult = await EnsureSuccess(byName["talvora_read_text_range"], new()
                {
                    ["path"] = developerRangeFile,
                    ["startLine"] = 2,
                    ["lineCount"] = 2,
                });
                if (rangeResult.StructuredContent is not { } rangeJson ||
                    rangeJson.GetProperty("linesRead").GetInt32() != 2 ||
                    rangeJson.GetProperty("endReached").GetBoolean() ||
                    !string.Equals(
                        rangeJson.GetProperty("text").GetString(),
                        "line-two" + Environment.NewLine +
                        "line-three" + Environment.NewLine,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("read_text_range returned unexpected lines.");
                }
                
                var tailResult = await EnsureSuccess(byName["talvora_tail_text"], new()
                {
                    ["path"] = developerRangeFile,
                    ["lineCount"] = 2,
                });
                if (tailResult.StructuredContent is not { } tailJson ||
                    tailJson.GetProperty("totalLines").GetInt32() != 4 ||
                    !string.Equals(
                        tailJson.GetProperty("text").GetString(),
                        "line-three" + Environment.NewLine + "line-four",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("tail_text returned unexpected lines.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = developerDotenvFile,
                    ["content"] = "ALPHA=one\r\nexport BETA=\"two words\"\r\n",
                });
                
                var dotenvListResult = await EnsureSuccess(byName["talvora_dotenv_list"], new()
                {
                    ["path"] = developerDotenvFile,
                });
                if (dotenvListResult.StructuredContent is not { } dotenvListJson ||
                    dotenvListJson.GetProperty("count").GetInt32() != 2)
                {
                    throw new InvalidOperationException("dotenv_list did not parse two entries.");
                }

                await File.WriteAllLinesAsync(
                    manyDotenvFile,
                    Enumerable.Range(0, 1200)
                        .Select(index =>
                            $"K{index:D4}=value-{index:D4}"));
                var dotenvPage1 =
                    await EnsureSuccess(
                        byName["talvora_dotenv_list"],
                        new()
                        {
                            ["path"] = manyDotenvFile,
                            ["maxResults"] = 500,
                            ["resultOffset"] = 0,
                        });
                if (dotenvPage1.StructuredContent is not { } dotenvPage1Json ||
                    dotenvPage1Json.GetProperty("count").GetInt32() != 500 ||
                    dotenvPage1Json.GetProperty("totalEntries").GetInt32() != 1200 ||
                    !dotenvPage1Json.GetProperty("truncated").GetBoolean() ||
                    dotenvPage1Json.GetProperty("nextResultOffset").GetInt32() != 500)
                {
                    throw new InvalidOperationException(
                        "dotenv_list did not enforce deterministic response pagination.");
                }
                
                var dotenvGetResult = await EnsureSuccess(byName["talvora_dotenv_get"], new()
                {
                    ["path"] = developerDotenvFile,
                    ["key"] = "BETA",
                });
                if (dotenvGetResult.StructuredContent is not { } dotenvGetJson ||
                    !dotenvGetJson.GetProperty("found").GetBoolean() ||
                    !dotenvGetJson.GetProperty("exported").GetBoolean() ||
                    !string.Equals(dotenvGetJson.GetProperty("value").GetString(), "two words", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("dotenv_get did not decode the exported quoted value.");
                }
                
                var dotenvSetResult = await EnsureSuccess(byName["talvora_dotenv_set"], new()
                {
                    ["path"] = developerDotenvFile,
                    ["key"] = "ALPHA",
                    ["value"] = "updated value",
                    ["replaceAll"] = true,
                });
                if (dotenvSetResult.StructuredContent is not { } dotenvSetJson ||
                    !dotenvSetJson.GetProperty("changed").GetBoolean() ||
                    dotenvSetJson.GetProperty("matches").GetInt32() != 1)
                {
                    throw new InvalidOperationException("dotenv_set did not update ALPHA.");
                }
                
                var dotenvUpdatedGet = await EnsureSuccess(byName["talvora_dotenv_get"], new()
                {
                    ["path"] = developerDotenvFile,
                    ["key"] = "ALPHA",
                });
                if (dotenvUpdatedGet.StructuredContent is not { } dotenvUpdatedJson ||
                    !string.Equals(dotenvUpdatedJson.GetProperty("value").GetString(), "updated value", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("dotenv_get did not return the updated value.");
                }
                
                var dotenvDeleteResult = await EnsureSuccess(byName["talvora_dotenv_delete"], new()
                {
                    ["path"] = developerDotenvFile,
                    ["key"] = "BETA",
                });
                if (dotenvDeleteResult.StructuredContent is not { } dotenvDeleteJson ||
                    !dotenvDeleteJson.GetProperty("changed").GetBoolean() ||
                    dotenvDeleteJson.GetProperty("matches").GetInt32() != 1)
                {
                    throw new InvalidOperationException("dotenv_delete did not remove BETA.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = developerIniFile,
                    ["content"] = "[app]\r\nmode=dev\r\nport=7000\r\n",
                });
                
                var iniGetResult = await EnsureSuccess(byName["talvora_ini_get"], new()
                {
                    ["path"] = developerIniFile,
                    ["section"] = "app",
                    ["key"] = "mode",
                });
                if (iniGetResult.StructuredContent is not { } iniGetJson ||
                    !iniGetJson.GetProperty("found").GetBoolean() ||
                    !string.Equals(iniGetJson.GetProperty("value").GetString(), "dev", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("ini_get did not return app.mode.");
                }
                
                var iniSetResult = await EnsureSuccess(byName["talvora_ini_set"], new()
                {
                    ["path"] = developerIniFile,
                    ["section"] = "app",
                    ["key"] = "port",
                    ["value"] = "7676",
                });
                if (iniSetResult.StructuredContent is not { } iniSetJson ||
                    !iniSetJson.GetProperty("changed").GetBoolean() ||
                    iniSetJson.GetProperty("matches").GetInt32() != 1)
                {
                    throw new InvalidOperationException("ini_set did not update app.port.");
                }
                
                var iniListResult = await EnsureSuccess(byName["talvora_ini_list"], new()
                {
                    ["path"] = developerIniFile,
                    ["section"] = "app",
                });
                if (iniListResult.StructuredContent is not { } iniListJson ||
                    iniListJson.GetProperty("count").GetInt32() != 2 ||
                    !iniListJson.GetProperty("entries").EnumerateArray().Any(entry =>
                        string.Equals(entry.GetProperty("key").GetString(), "port", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(entry.GetProperty("value").GetString(), "7676", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("ini_list did not show the updated app.port.");
                }

                await File.WriteAllLinesAsync(
                    manyIniFile,
                    new[] { "[main]" }
                        .Concat(
                            Enumerable.Range(0, 1200)
                                .Select(index =>
                                    $"K{index:D4}=value-{index:D4}")));
                var iniLastPage =
                    await EnsureSuccess(
                        byName["talvora_ini_list"],
                        new()
                        {
                            ["path"] = manyIniFile,
                            ["section"] = "main",
                            ["maxResults"] = 500,
                            ["resultOffset"] = 1000,
                        });
                if (iniLastPage.StructuredContent is not { } iniLastPageJson ||
                    iniLastPageJson.GetProperty("count").GetInt32() != 200 ||
                    iniLastPageJson.GetProperty("totalEntries").GetInt32() != 1200 ||
                    iniLastPageJson.GetProperty("truncated").GetBoolean() ||
                    iniLastPageJson.GetProperty("nextResultOffset").ValueKind !=
                        System.Text.Json.JsonValueKind.Null)
                {
                    throw new InvalidOperationException(
                        "ini_list continuation did not terminate deterministically.");
                }
                
                var iniDeleteResult = await EnsureSuccess(byName["talvora_ini_delete"], new()
                {
                    ["path"] = developerIniFile,
                    ["section"] = "app",
                    ["key"] = "mode",
                });
                if (iniDeleteResult.StructuredContent is not { } iniDeleteJson ||
                    !iniDeleteJson.GetProperty("changed").GetBoolean() ||
                    iniDeleteJson.GetProperty("matches").GetInt32() != 1)
                {
                    throw new InvalidOperationException("ini_delete did not remove app.mode.");
                }

                await File.WriteAllTextAsync(
                    noFinalDotenvFile,
                    "A=1\r\nB=2",
                    new System.Text.UTF8Encoding(false));
                await EnsureSuccess(
                    byName["talvora_dotenv_set"],
                    new()
                    {
                        ["path"] = noFinalDotenvFile,
                        ["key"] = "B",
                        ["value"] = "3",
                    });
                var noFinalDotenvText =
                    await File.ReadAllTextAsync(
                        noFinalDotenvFile);
                if (!string.Equals(
                        noFinalDotenvText,
                        "A=1\r\nB=3",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "dotenv_set changed the original EOF newline state.");
                }

                await File.WriteAllTextAsync(
                    noFinalIniFile,
                    "[main]\r\nA=1\r\nB=2",
                    new System.Text.UTF8Encoding(false));
                await EnsureSuccess(
                    byName["talvora_ini_set"],
                    new()
                    {
                        ["path"] = noFinalIniFile,
                        ["section"] = "main",
                        ["key"] = "B",
                        ["value"] = "3",
                    });
                var noFinalIniText =
                    await File.ReadAllTextAsync(
                        noFinalIniFile);
                if (!string.Equals(
                        noFinalIniText,
                        "[main]\r\nA=1\r\nB=3",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "ini_set changed the original EOF newline state.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = developerXmlFile,
                    ["content"] = "<root><app mode=\"dev\"><port>7000</port><remove>yes</remove></app></root>",
                });
                
                var xmlQueryResult = await EnsureSuccess(byName["talvora_xml_query"], new()
                {
                    ["path"] = developerXmlFile,
                    ["xpath"] = "/root/app/port",
                });
                if (xmlQueryResult.StructuredContent is not { } xmlQueryJson ||
                    xmlQueryJson.GetProperty("count").GetInt32() != 1 ||
                    !string.Equals(
                        xmlQueryJson.GetProperty("nodes").EnumerateArray().Single().GetProperty("value").GetString(),
                        "7000",
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("xml_query did not return the port element.");
                }
                
                var xmlSetResult = await EnsureSuccess(byName["talvora_xml_set"], new()
                {
                    ["path"] = developerXmlFile,
                    ["xpath"] = "/root/app/port",
                    ["value"] = "7676",
                    ["expectedMatches"] = 1,
                });
                if (xmlSetResult.StructuredContent is not { } xmlSetJson ||
                    !xmlSetJson.GetProperty("changed").GetBoolean() ||
                    xmlSetJson.GetProperty("matches").GetInt32() != 1)
                {
                    throw new InvalidOperationException("xml_set did not update the port element.");
                }
                
                var xmlAttributeSet = await EnsureSuccess(byName["talvora_xml_set"], new()
                {
                    ["path"] = developerXmlFile,
                    ["xpath"] = "/root/app/@mode",
                    ["value"] = "prod",
                    ["expectedMatches"] = 1,
                });
                if (xmlAttributeSet.StructuredContent is not { } xmlAttributeSetJson ||
                    !xmlAttributeSetJson.GetProperty("changed").GetBoolean())
                {
                    throw new InvalidOperationException("xml_set did not update the mode attribute.");
                }
                
                var xmlDeleteResult = await EnsureSuccess(byName["talvora_xml_delete"], new()
                {
                    ["path"] = developerXmlFile,
                    ["xpath"] = "/root/app/remove",
                    ["expectedMatches"] = 1,
                });
                if (xmlDeleteResult.StructuredContent is not { } xmlDeleteJson ||
                    !xmlDeleteJson.GetProperty("changed").GetBoolean())
                {
                    throw new InvalidOperationException("xml_delete did not remove the requested element.");
                }
                
                var xmlVerifyResult = await EnsureSuccess(byName["talvora_xml_query"], new()
                {
                    ["path"] = developerXmlFile,
                    ["xpath"] = "concat(/root/app/@mode, ':', /root/app/port)",
                });
                if (xmlVerifyResult.StructuredContent is not { } xmlVerifyJson ||
                    !string.Equals(xmlVerifyJson.GetProperty("scalarValue").GetString(), "prod:7676", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("xml_query scalar verification failed.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = developerJUnitFile,
                    ["content"] = "<testsuites><testsuite name=\"smoke\" tests=\"2\" failures=\"1\" errors=\"0\" skipped=\"0\" time=\"0.12\"><testcase classname=\"Smoke\" name=\"Pass\" time=\"0.01\"/><testcase classname=\"Smoke\" name=\"Fail\" time=\"0.02\"><failure message=\"boom\">stack line</failure></testcase></testsuite></testsuites>",
                });
                
                var reportResult = await EnsureSuccess(byName["talvora_test_report_summary"], new()
                {
                    ["path"] = developerJUnitFile,
                    ["format"] = "auto",
                    ["maxFailures"] = 0,
                });
                if (reportResult.StructuredContent is not { } reportJson ||
                    !string.Equals(reportJson.GetProperty("format").GetString(), "junit", StringComparison.OrdinalIgnoreCase) ||
                    reportJson.GetProperty("total").GetInt32() != 2 ||
                    reportJson.GetProperty("passed").GetInt32() != 1 ||
                    reportJson.GetProperty("failed").GetInt32() != 1 ||
                    reportJson.GetProperty("failureCount").GetInt32() != 1 ||
                    !reportJson.GetProperty("failures").EnumerateArray().Any(failure =>
                        (failure.GetProperty("name").GetString() ?? string.Empty).Contains("Smoke.Fail", StringComparison.Ordinal)))
                {
                    throw new InvalidOperationException("test_report_summary did not parse the JUnit failure.");
                }
                
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = developerJsonFile,
                    ["content"] = "{\"name\":\"talvora\",\"settings\":{\"mode\":\"dev\"},\"items\":[1,2]}",
                });
                var jsonSetResult = await EnsureSuccess(byName["talvora_json_set"], new()
                {
                    ["path"] = developerJsonFile,
                    ["pointer"] = "/settings/port",
                    ["valueJson"] = "7676",
                    ["createMissing"] = true,
                    ["indented"] = true,
                });
                if (jsonSetResult.StructuredContent is not { } jsonSetJson ||
                    !jsonSetJson.GetProperty("changed").GetBoolean())
                {
                    throw new InvalidOperationException("json_set did not report the config change.");
                }
                
                await EnsureSuccess(byName["talvora_json_set"], new()
                {
                    ["path"] = developerJsonFile,
                    ["pointer"] = "/items/-",
                    ["valueJson"] = "3",
                    ["createMissing"] = true,
                });
                
                var jsonGetResult = await EnsureSuccess(byName["talvora_json_get"], new()
                {
                    ["path"] = developerJsonFile,
                    ["pointer"] = "/settings/port",
                    ["indented"] = false,
                });
                if (jsonGetResult.StructuredContent is not { } jsonGetJson ||
                    !jsonGetJson.GetProperty("found").GetBoolean() ||
                    !string.Equals(jsonGetJson.GetProperty("valueJson").GetString(), "7676", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("json_get did not return the configured port.");
                }
                
                var jsonDeleteResult = await EnsureSuccess(byName["talvora_json_delete"], new()
                {
                    ["path"] = developerJsonFile,
                    ["pointer"] = "/settings/mode",
                });
                if (jsonDeleteResult.StructuredContent is not { } jsonDeleteJson ||
                    !jsonDeleteJson.GetProperty("changed").GetBoolean())
                {
                    throw new InvalidOperationException("json_delete did not remove the configured mode.");
                }
                
                var deletedJsonGetResult = await EnsureSuccess(byName["talvora_json_get"], new()
                {
                    ["path"] = developerJsonFile,
                    ["pointer"] = "/settings/mode",
                });
                if (deletedJsonGetResult.StructuredContent is not { } deletedJsonGetJson ||
                    deletedJsonGetJson.GetProperty("found").GetBoolean())
                {
                    throw new InvalidOperationException("json_get reported a deleted JSON pointer as present.");
                }
                
                await EnsureSuccess(byName["talvora_create_directory"], new()
                {
                    ["path"] = Path.Combine(developerArchiveSource, "nested"),
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(developerArchiveSource, "a.txt"),
                    ["content"] = "archive-a-" + smokeId,
                });
                await EnsureSuccess(byName["talvora_write_text"], new()
                {
                    ["path"] = Path.Combine(developerArchiveSource, "nested", "b.txt"),
                    ["content"] = "archive-b-" + smokeId,
                });
                
                var archiveCreateResult = await EnsureSuccess(byName["talvora_archive_create"], new()
                {
                    ["sourceDirectory"] = developerArchiveSource,
                    ["archivePath"] = developerArchiveZip,
                    ["overwrite"] = true,
                    ["includeBaseDirectory"] = false,
                    ["compression"] = "optimal",
                });
                if (archiveCreateResult.StructuredContent is not { } archiveCreateJson ||
                    archiveCreateJson.GetProperty("entryCount").GetInt32() < 2)
                {
                    throw new InvalidOperationException("archive_create did not create the expected entries.");
                }
                
                var archiveListResult = await EnsureSuccess(byName["talvora_archive_list"], new()
                {
                    ["archivePath"] = developerArchiveZip,
                });
                if (archiveListResult.StructuredContent is not { } archiveListJson ||
                    archiveListJson.GetProperty("count").GetInt32() < 2 ||
                    !archiveListJson.GetProperty("entries").EnumerateArray().Any(entry =>
                        entry.TryGetProperty("fullName", out var fullName) &&
                        fullName.GetString() is { } entryName &&
                        entryName.Replace('\\', '/').EndsWith("nested/b.txt", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("archive_list did not report the nested entry.");
                }
                
                var archiveExtractResult = await EnsureSuccess(byName["talvora_archive_extract"], new()
                {
                    ["archivePath"] = developerArchiveZip,
                    ["destinationDirectory"] = developerArchiveExtract,
                    ["overwrite"] = true,
                    ["allowOutsideDestination"] = false,
                });
                if (archiveExtractResult.StructuredContent is not { } archiveExtractJson ||
                    archiveExtractJson.GetProperty("entriesExtracted").GetInt32() < 2 ||
                    !string.Equals(
                        await ReadToolText(
                            byName["talvora_read_text"],
                            Path.Combine(developerArchiveExtract, "nested", "b.txt")),
                        "archive-b-" + smokeId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("archive_extract did not restore the nested payload.");
                }
                
                var downloadResult = await EnsureSuccess(byName["talvora_http_download"], new()
                {
                    ["url"] = "http://127.0.0.1:7676/healthz",
                    ["destinationPath"] = developerDownloadFile,
                    ["overwrite"] = true,
                    ["resume"] = false,
                    ["timeoutSeconds"] = 30,
                });
                if (downloadResult.StructuredContent is not { } downloadJson ||
                    downloadJson.GetProperty("statusCode").GetInt32() != 200 ||
                    downloadJson.GetProperty("fileLength").GetInt64() <= 0 ||
                    string.IsNullOrWhiteSpace(downloadJson.GetProperty("sha256").GetString()) ||
                    !(await ReadToolText(byName["talvora_read_text"], developerDownloadFile))
                        .Contains("Talvora", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("http_download did not persist the Talvora health response.");
                }
    }
}
