using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora.Shared;
using Talvora.SourceEditing;

namespace Talvora;

internal static class WorklogFailureClassifier
{
    private static readonly WorklogOutcome StaleRevision = new(
        WorklogFailureKind.StaleRevision,
        "Dosya bu arada değişmişti; eski hâline göre değişiklik yapılmadı. Kodunuz değişmedi.",
        "Ajan güncel hâli okuyup yeniden deneyecek.");
    private static readonly WorklogOutcome PatchMismatch = new(
        WorklogFailureKind.InvalidArguments,
        "Değişiklik isteği dosyanın güncel hâliyle eşleşmedi. Kodunuz değişmedi.",
        "Ajan isteği düzeltip yeniden deneyebilir.");
    private static readonly WorklogOutcome SyntaxFailure = new(
        WorklogFailureKind.Other,
        "Değişiklik sözdizimi kontrolünden geçmedi. Kodunuz değişmedi.",
        "Ajan hatayı düzeltip yeniden deneyebilir.");
    private static readonly WorklogOutcome TimedOut = new(
        WorklogFailureKind.Timeout, "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.", null);
    private static readonly WorklogOutcome BuildFailure = new(
        WorklogFailureKind.BuildErrors, "Derleme hata verdi.",
        "Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.");
    private static readonly WorklogOutcome TestFailure = new(
        WorklogFailureKind.TestFailures, "Testlerin bir kısmı başarısız oldu.", null);
    private static readonly WorklogOutcome DeliveryFailure = new(
        WorklogFailureKind.DeliveryGate,
        "Teslim kontrolü eksik kanıt nedeniyle gönderimi durdurdu.",
        "Kanıt dosyaları güncellenince tekrar denenebilir.");
    private static readonly WorklogOutcome ProtectedSecret = new(
        WorklogFailureKind.AccessDenied,
        "Bu konum gizli anahtarlara ayrılmış; Talvora burayı okumaz ve değiştirmez. Hiçbir şeye dokunulmadı.",
        null);
    private static readonly WorklogOutcome AccessDenied = new(
        WorklogFailureKind.AccessDenied, "Windows bu işleme izin vermedi.", null);
    private static readonly WorklogOutcome WorkspacePolicy = new(
        WorklogFailureKind.AccessDenied,
        "Bu dosya bir proje klasöründe; Talvora proje dosyalarını yalnızca kod değişikliği olarak değiştirir. Dosyaya dokunulmadı.",
        "Ajan aynı işi kod değişikliği olarak yeniden yapabilir.");
    private static readonly WorklogOutcome MissingAgents = new(
        WorklogFailureKind.NotFound, "Bu klasörde proje talimatı (AGENTS.md) yok.",
        "Ajan talimatın bulunduğu proje klasörünü seçip yeniden isteyebilir.");
    private static readonly WorklogOutcome NotFound = new(
        WorklogFailureKind.NotFound, "Aranan dosya ya da klasör bulunamadı.", null);
    private static readonly WorklogOutcome Fallback =
        new(WorklogFailureKind.Other, "Bu adım tamamlanamadı.", null);

    /// <summary>Returns the classified plain Turkish reason and next step (DESIGN-WORKLOG §5.3).</summary>
    internal static WorklogOutcome Describe(CallToolResult result, string normalizedToolName)
    {
        if (result.StructuredContent is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            if (TryGetErrorCode(json) is { } errorCode && CodeOutcome(errorCode) is { } codeOutcome)
            {
                return codeOutcome;
            }

            if (StructuredResultFailure.Describe(json) == StructuredResultFailure.TimedOut)
            {
                return TimedOut;
            }

            if (normalizedToolName == "dotnet_build")
            {
                return BuildFailure;
            }

            if (normalizedToolName == "dotnet_test")
            {
                return TestFailure;
            }

            if (ContainsText(json, "TALVORA_DELIVERY_GATE_BLOCKED") ||
                ContainsText(json, "WorkContract:"))
            {
                return DeliveryFailure;
            }

            if (!StructuredResultFailure.ReportsAnotherProcess(normalizedToolName) &&
                StructuredResultFailure.TryGetPropertyIgnoreCase(json, "exitCode", out var exitCode) &&
                exitCode.ValueKind == JsonValueKind.Number &&
                exitCode.TryGetInt32(out var code) && code != 0)
            {
                return new WorklogOutcome(
                    WorklogFailureKind.Other,
                    string.Create(CultureInfo.InvariantCulture, $"Komut {code} çıkış koduyla bitti."),
                    null);
            }
        }

        return Fallback;
    }

    internal static WorklogOutcome DescribeException(Exception exception, string normalizedToolName)
    {
        var reason = exception is AggregateException { InnerException: { } inner } ? inner : exception;
        return reason switch
        {
            UnauthorizedAccessException e when e.Message.StartsWith(
                "Bu konum gizli anahtarlar için ayrılmıştır", StringComparison.Ordinal) =>
                ProtectedSecret,
            UnauthorizedAccessException => AccessDenied,
            SourceEditDomainException { Code: "SOURCE_EDIT_POLICY_VIOLATION" } => WorkspacePolicy,
            FileNotFoundException when normalizedToolName == "agent_brief" => MissingAgents,
            FileNotFoundException or DirectoryNotFoundException => NotFound,
            TimeoutException => TimedOut,
            SourceEditDomainException e when CodeOutcome(e.Code) is { } o => o,
            _ => Fallback,
        };
    }

    private static WorklogOutcome? CodeOutcome(string code) => code switch
    {
        "EXPECTED_REVISION_MISMATCH" or "SOURCE_EDIT_CONFLICT" => StaleRevision,
        "PATCH_PARSE_ERROR" or "PATCH_CONTEXT_NOT_FOUND" or
        "PATCH_CONTEXT_AMBIGUOUS" or "PATCH_OVERLAPPING_EDITS" => PatchMismatch,
        "SOURCE_VALIDATION_FAILED" => SyntaxFailure,
        _ => null,
    };

    private static string? TryGetErrorCode(JsonElement json)
    {
        foreach (var property in json.EnumerateObject())
        {
            if (string.Equals(property.Name, "error", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var inner in property.Value.EnumerateObject())
                {
                    if (string.Equals(inner.Name, "code", StringComparison.OrdinalIgnoreCase) &&
                        inner.Value.ValueKind == JsonValueKind.String)
                    {
                        return inner.Value.GetString();
                    }
                }
            }
        }

        return null;
    }

    private static bool ContainsText(JsonElement json, string needle) =>
        json.GetRawText().Contains(needle, StringComparison.Ordinal);
}
