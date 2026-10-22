using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Protocol;

namespace Talvora;

internal static partial class WorklogFailureClassifier
{
    /// <summary>Returns the plain Turkish reason (DESIGN-WORKLOG §5.3) for a failed step.</summary>
    internal static string Describe(CallToolResult result, string normalizedToolName)
    {
        if (result.StructuredContent is JsonElement json && json.ValueKind == JsonValueKind.Object)
        {
            if (TryGetErrorCode(json) is { } code)
            {
                return code switch
                {
                    "EXPECTED_REVISION_MISMATCH" or "SOURCE_EDIT_CONFLICT" =>
                        "Dosya bu arada değişmişti; eski hâline göre değişiklik yapılmadı. Kodunuz değişmedi. Ajan güncel hâli okuyup yeniden deneyecek.",
                    "PATCH_PARSE_ERROR" or "PATCH_CONTEXT_NOT_FOUND" or "PATCH_CONTEXT_AMBIGUOUS" or "PATCH_OVERLAPPING_EDITS" =>
                        "Değişiklik isteği dosyanın güncel hâliyle eşleşmedi. Kodunuz değişmedi. Ajan isteği düzeltip yeniden deneyebilir.",
                    "SOURCE_VALIDATION_FAILED" =>
                        "Değişiklik sözdizimi kontrolünden geçmedi. Kodunuz değişmedi. Ajan hatayı düzeltip yeniden deneyebilir.",
                    _ => "Bu adım tamamlanamadı.",
                };
            }

            if (StructuredResultFailure.Describe(json) == StructuredResultFailure.TimedOut)
            {
                return "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.";
            }

            if (normalizedToolName == "dotnet_build")
            {
                return "Derleme hata verdi. Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.";
            }

            if (normalizedToolName == "dotnet_test")
            {
                return "Testlerin bir kısmı başarısız oldu.";
            }

            if (ContainsText(json, "TALVORA_DELIVERY_GATE_BLOCKED") || ContainsText(json, "WorkContract:"))
            {
                return "Teslim kontrolü eksik kanıt nedeniyle gönderimi durdurdu. Kanıt dosyaları güncellenince tekrar denenebilir.";
            }
        }

        var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? string.Empty;
        return CategoryPrefix().Match(text) is { Success: true } match
            ? match.Groups["c"].Value switch
            {
                "InvalidArguments" => "İstek eksik ya da hatalı bilgi içeriyordu. Ajan isteği düzeltip yeniden deneyebilir.",
                "NotFound" => "Aranan dosya ya da kayıt bulunamadı.",
                "AccessDenied" => "Windows bu işleme izin vermedi.",
                "Timeout" => "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.",
                _ => "Bu adım tamamlanamadı.",
            }
            : "Bu adım tamamlanamadı.";
    }

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

    [GeneratedRegex(@"^(?<c>InvalidArguments|NotFound|AccessDenied|Timeout|IO|Failed):")]
    private static partial Regex CategoryPrefix();
}
