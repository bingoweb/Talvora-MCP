using System.Text.Json;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private sealed record OperationNarrative(
        string Subject,
        string Action,
        string Reason);

    private static OperationNarrative BuildNarrative(
        string? toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        var normalized = NormalizeToolName(toolName);
        var fallback = GetFriendlyToolName(toolName);

        if (normalized == "run_powershell")
        {
            var script = GetArgumentText(arguments, "script");
            if (script.Contains("Build-Windows-Installer.ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Talvora kurulum paketi hazırlanıyor",
                    "Güncel kaynak koddan Windows installer paketi üretiliyor.",
                    "Tamamlanan geliştirmeleri güvenli biçimde canlı Talvora sürümüne taşımak için.");
            }

            if (script.Contains("Install.ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Talvora yeni sürüme geçiriliyor",
                    "Hazırlanan installer çalıştırılıyor; servis ve tray yeni sürüme geçiriliyor.",
                    "Kod değişikliklerinin çalışan Talvora ortamında etkinleşmesi için.");
            }

            if (script.Contains("Get-Content", StringComparison.OrdinalIgnoreCase) &&
                script.Contains("log", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Talvora günlükleri inceleniyor",
                    "Son çalışma ve hata kayıtları okunarak canlı durum doğrulanıyor.",
                    "Sessiz hata, gereksiz tekrar veya başarısız kurtarma döngüsü olmadığını kontrol etmek için.");
            }

            return new(
                "Talvora sistem görevi yürütüyor",
                SummarizeScript(script),
                "Geliştirme veya sistem yönetimi adımını tamamlamak için.");
        }

        if (normalized is "dotnet_build" or "dotnet_test" or "dotnet_restore")
        {
            var target = GetArgumentText(arguments, "target");
            return new(
                normalized == "dotnet_test"
                    ? "Talvora testleri çalıştırıyor"
                    : "Talvora derleme doğrulaması yapıyor",
                string.IsNullOrWhiteSpace(target)
                    ? "Güncel .NET kaynakları derleniyor ve teknik doğrulama yapılıyor."
                    : $"{target} hedefi derleniyor ve doğrulanıyor.",
                "Yapılan değişikliklerin derleme hatası veya regresyon üretmediğini doğrulamak için.");
        }

        if (normalized == "apply_patch")
        {
            var transaction = GetArgumentText(arguments, "transactionId");
            return new(
                "Talvora kaynak kodu güncelliyor",
                string.IsNullOrWhiteSpace(transaction)
                    ? "Planlanan kod ve yapılandırma değişiklikleri uygulanıyor."
                    : $"{HumanizeIdentifier(transaction)} değişikliği kaynak dosyalara uygulanıyor.",
                "Modernizasyon kapsamında belirlenen davranışı kalıcı ve sürüm kontrollü hale getirmek için.");
        }

        if (normalized == "git_run")
        {
            var gitArgs = GetArgumentArray(arguments, "arguments");
            var joined = string.Join(' ', gitArgs);
            if (gitArgs.Count > 0 &&
                string.Equals(gitArgs[0], "push", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Talvora değişiklikleri uzak depoya gönderiyor",
                    $"Git {joined} çalıştırılarak doğrulanmış commitler yayınlanıyor.",
                    "Gitea/GitHub ana dalını yerel doğrulanmış durumla senkron tutmak için.");
            }

            return new(
                "Talvora Git işlemi yürütüyor",
                string.IsNullOrWhiteSpace(joined)
                    ? "Git çalışma alanı güncelleniyor."
                    : $"Git {joined} çalıştırılıyor.",
                "Kaynak kod geçmişini ve sürüm durumunu yönetmek için.");
        }

        return new(
            fallback,
            $"{fallback} yürütülüyor.",
            "Talvora modernizasyon çalışmasının mevcut adımını tamamlamak için.");
    }

    private static string BuildCompletionMessage<T>(
        OperationNarrative narrative,
        T result,
        TimeSpan elapsed)
    {
        return
            $"Sonuç: {SummarizeResult(result)}\n" +
            $"Ne değişti: {DescribeChange(narrative)}\n" +
            $"Neden: {narrative.Reason}\n" +
            $"Süre: {FormatElapsed(elapsed)}";
    }

    private static string DescribeChange(OperationNarrative narrative) =>
        narrative.Subject switch
        {
            "Talvora kurulum paketi hazırlanıyor" =>
                "Yeni installer paketi ve manifest üretildi; çalışan servis henüz değiştirilmedi.",
            "Talvora yeni sürüme geçiriliyor" =>
                "Talvora Windows servisi ve tray hazırlanan yeni sürüme geçirildi.",
            "Talvora günlükleri inceleniyor" =>
                "Sistemde değişiklik yapılmadı; yalnız günlükler okunup durum doğrulandı.",
            "Talvora derleme doğrulaması yapıyor" or
            "Talvora testleri çalıştırıyor" =>
                "Kaynak dosyalarda değişiklik yapılmadı; derleme/test sonucu doğrulandı.",
            "Talvora kaynak kodu güncelliyor" =>
                "Planlanan kaynak dosyaları sürüm kontrollü patch ile güncellendi.",
            "Talvora değişiklikleri uzak depoya gönderiyor" =>
                "Uzak Git deposu doğrulanmış yerel commitlerle güncellendi.",
            _ => narrative.Action,
        };

    private static string SummarizeResult<T>(T result)
    {
        try
        {
            var json = JsonSerializer.SerializeToElement(result);
            if (json.ValueKind == JsonValueKind.Object)
            {
                if (json.TryGetProperty("exitCode", out var exitCode))
                {
                    return exitCode.GetInt32() == 0
                        ? "İşlem başarıyla tamamlandı."
                        : $"İşlem {exitCode.GetInt32()} çıkış koduyla tamamlandı.";
                }

                if (json.TryGetProperty("success", out var success) &&
                    success.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return success.GetBoolean()
                        ? "Planlanan değişiklikler başarıyla uygulandı."
                        : "Planlanan değişikliklerin tamamı uygulanamadı.";
                }
            }
        }
        catch
        {
        }

        return "İşlem başarıyla tamamlandı.";
    }

    private static string SummarizeScript(string script)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return "Yerel sistem görevi çalıştırılıyor.";
        }

        var first = script
            .Split(new[] { '\r', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Length > 0)
            ?? "Yerel sistem görevi çalıştırılıyor.";

        return first.Length <= 180
            ? first
            : first[..177] + "...";
    }

    private static string HumanizeIdentifier(string value) =>
        value.Replace('-', ' ').Replace('_', ' ').Trim();

    private static string GetArgumentText(
        IDictionary<string, JsonElement>? arguments,
        string key)
    {
        if (arguments is null ||
            !arguments.TryGetValue(key, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static IReadOnlyList<string> GetArgumentArray(
        IDictionary<string, JsonElement>? arguments,
        string key)
    {
        if (arguments is null ||
            !arguments.TryGetValue(key, out var value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(static item => item.ToString())
            .ToArray();
    }
}
