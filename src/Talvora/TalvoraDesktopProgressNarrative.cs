using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace Talvora;

internal sealed partial class TalvoraDesktopProgressNotifier
{
    private enum OperationCategory
    {
        SourceEdit,
        Verification,
        PackageBuild,
        Deployment,
        VersionControl,
        ServiceOperation,
        PackageOperation,
        FileMutation,
        NetworkOperation,
        ProcessOperation,
        Inspection,
        GenericExecution,
    }

    private sealed record OperationNarrative(
        OperationCategory Category,
        string Subject,
        string Action,
        string Reason,
        string Completion);

    private static OperationNarrative BuildNarrative(
        string? toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        var normalized = NormalizeToolName(toolName);

        if (normalized is
            "read_source" or
            "read_text" or
            "read_text_range" or
            "read_bytes" or
            "tail_text" or
            "search_text" or
            "knowledge_search" or
            "find_files")
        {
            return new(
                OperationCategory.Inspection,
                "İlgili kaynakları inceliyorum",
                "İstenen işi doğru yapmak için ilgili dosya, metin ve eşleşmeleri kontrol ediyorum.",
                "Değişiklik veya karar vermeden önce gerçek kaynak verisini görmek için.",
                "Kaynak incelemesini tamamladım.");
        }

        if (normalized is "git_diff" or "git_log")
        {
            return new(
                OperationCategory.Inspection,
                "Değişiklik geçmişini inceliyorum",
                "Yapılan değişikliklerin farkını ve geçmişini kontrol ediyorum.",
                "Gerçek değişiklik durumunu doğrulamak için.",
                "Değişiklik incelemesini tamamladım.");
        }

        if (normalized is "file_hash" or "path_info")
        {
            return new(
                OperationCategory.Inspection,
                "Dosya bilgisini doğruluyorum",
                "Dosyanın gerçek konum, boyut veya bütünlük bilgisini kontrol ediyorum.",
                "Sonucun varsayıma değil gerçek dosya verisine dayanması için.",
                "Dosya doğrulamasını tamamladım.");
        }

        if (normalized == "http_request")
        {
            return new(
                OperationCategory.Inspection,
                "Bağlantıyı doğruluyorum",
                "İlgili HTTP uç noktasının gerçek yanıtını kontrol ediyorum.",
                "Bağlantı durumunu gerçek yanıt üzerinden doğrulamak için.",
                "Bağlantı kontrolünü tamamladım.");
        }

        if (normalized == "run_powershell")
        {
            var script = GetArgumentText(arguments, "script");
            if (IsCanonicalPackageBuildScript(script))
            {
                return new(
                    OperationCategory.PackageBuild,
                    "Yaptığım değişiklikleri kullanıma hazırlıyorum",
                    "Yaptığım son düzenlemeleri bilgisayarında kullanılabilecek hale getiriyorum.",
                    "Yeni sürüm paketinin gerçekten üretildiğini doğrulamak için.",
                    "Yeni sürüm paketini hazırladım.");
            }

            if (script.Contains("Install.ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    OperationCategory.Deployment,
                    "Yeni hali bilgisayarında etkinleştiriyorum",
                    "Az önce hazırladığım değişiklikleri çalışan Talvora'ya uyguluyorum.",
                    "Bilgisayarında çalışan sürümün yeni hale geçmesi için.",
                    "Kurulum adımını tamamladım; çalışan sürüm ayrıca doğrulanmalı.");
            }

            if (script.Contains("Get-Content", StringComparison.OrdinalIgnoreCase) &&
                script.Contains("log", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    OperationCategory.Verification,
                    "Çalışma kaydını kontrol ediyorum",
                    "Programın son çalışmasındaki gerçek günlük verisini inceliyorum.",
                    "Sonucun varsayıma değil gerçek çalışma kaydına dayanması için.",
                    "Çalışma kaydı kontrolünü tamamladım.");
            }

            return new(
                OperationCategory.GenericExecution,
                "Yerel bir sistem adımı çalıştırıyorum",
                "Talvora'nın istediğin işi tamamlamak için gereken yerel komutu çalıştırıyorum.",
                "Komutun gerçek sonucunu almak için.",
                "Yerel sistem adımı tamamlandı.");
        }

        if (normalized is "dotnet_build" or "dotnet_test" or "dotnet_restore")
        {
            return new(
                OperationCategory.Verification,
                normalized == "dotnet_test"
                    ? "Davranış testlerini çalıştırıyorum"
                    : "Teknik kontrolü çalıştırıyorum",
                normalized == "dotnet_test"
                    ? "Gerçek testlerin beklenen davranışı koruduğunu kontrol ediyorum."
                    : "Programın bu değişiklikle hatasız hazırlanabildiğini kontrol ediyorum.",
                "Değişikliğin gerçek teknik kontrollerden geçtiğini görmek için.",
                normalized == "dotnet_test"
                    ? "Davranış testleri tamamlandı."
                    : "Teknik kontrol tamamlandı.");
        }

        if (normalized is
            "apply_patch" or
            "apply_edits" or
            "structural_edit" or
            "semantic_edit")
        {
            return new(
                OperationCategory.SourceEdit,
                "İstediğin değişikliği uyguluyorum",
                "Şu an istediğin davranışı programın içine yerleştiriyorum.",
                "İstenen davranışın gerçekten programda yer alması için.",
                "Program değişikliğini uyguladım.");
        }

        if (normalized == "git_run")
        {
            var gitArgs = GetArgumentArray(arguments, "arguments");
            if (gitArgs.Count > 0 &&
                string.Equals(gitArgs[0], "push", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    OperationCategory.VersionControl,
                    "Tamamlanan çalışmayı uzak depoya gönderiyorum",
                    "Kaydettiğim değişiklikleri uzak güvenli depoya gönderiyorum.",
                    "Yerel çalışma ile uzak kopyanın eşitlenmesi için.",
                    "Uzak depo güncellemesini tamamladım.");
            }

            if (gitArgs.Count > 0 &&
                string.Equals(gitArgs[0], "commit", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    OperationCategory.VersionControl,
                    "Doğrulanan değişiklikleri kaydediyorum",
                    "Yalnız seçilmiş dosyaları yeni çalışma kaydına ekliyorum.",
                    "Çalışmanın izlenebilir ve geri takip edilebilir olması için.",
                    "Değişiklik kaydı oluşturuldu.");
            }

            return new(
                OperationCategory.VersionControl,
                "Çalışma kaydını güncelliyorum",
                "Değişiklik geçmişinde gerekli kayıt adımını yürütüyorum.",
                "Yapılan işin izlenebilir kalması için.",
                "Çalışma kaydı güncellendi.");
        }

        if (normalized.Contains("service_", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                OperationCategory.ServiceOperation,
                "Windows servisini güncelliyorum",
                "İstenen servis yaşam döngüsü işlemini uyguluyorum.",
                "Çalışan bileşenin istenen duruma geçmesi için.",
                "Servis işlemi tamamlandı.");
        }

        if (normalized.Contains("choco_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("package_", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                OperationCategory.PackageOperation,
                "Paket durumunu değiştiriyorum",
                "Gerekli yazılım paketini kuruyor, güncelliyor veya kaldırıyorum.",
                "Çalışma ortamının gereken bileşene sahip olması için.",
                "Paket işlemi tamamlandı.");
        }

        if (normalized.Contains("file_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("directory_", StringComparison.OrdinalIgnoreCase) ||
            normalized is "write_text" or "append_text" or "move" or "copy" or "delete")
        {
            return new(
                OperationCategory.FileMutation,
                "Dosya sisteminde değişiklik yapıyorum",
                "İstenen dosya veya klasör değişikliğini uyguluyorum.",
                "Yerel çalışma alanının istenen duruma gelmesi için.",
                "Dosya sistemi işlemi tamamlandı.");
        }

        if (normalized.Contains("tunnel", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("network", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("http_", StringComparison.OrdinalIgnoreCase))
        {
            return new(
                OperationCategory.NetworkOperation,
                "Bağlantı katmanını güncelliyorum",
                "İstenen bağlantı ayarını uyguluyorum.",
                "Yerel ve uzak bileşenlerin doğru iletişim kurması için.",
                "Bağlantı işlemi tamamlandı.");
        }

        if (normalized.Contains("process_", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("job_", StringComparison.OrdinalIgnoreCase) ||
            normalized is "run_process" or "user_process_start")
        {
            return new(
                OperationCategory.ProcessOperation,
                "Yerel işlemi yürütüyorum",
                "İstenen arka plan işlemini başlatıyor, izliyor veya sonlandırıyorum.",
                "İşin kontrollü biçimde çalışması veya sonlanması için.",
                "Yerel işlem tamamlandı.");
        }

        return new(
            OperationCategory.GenericExecution,
            "Talvora üzerinde bir işlem yürütüyorum",
            "İstenen görevin gerekli teknik adımını çalıştırıyorum.",
            "İşlemin gerçek sonucunu almak için.",
            "Teknik işlem tamamlandı.");
    }

    private static string BuildPlainCompletion(OperationNarrative narrative) =>
        narrative.Completion;

    private static bool TryBuildDeferredCompletion<T>(
        OperationNarrative narrative,
        T result,
        out string title,
        out string message)
    {
        if (!string.Equals(
                narrative.Subject,
                "Yeni hali bilgisayarında etkinleştiriyorum",
                StringComparison.Ordinal) ||
            !ResultContainsText(result, "TALVORA_UPDATE_DETACHED"))
        {
            title = string.Empty;
            message = string.Empty;
            return false;
        }

        title = "Kurucuya devrettim";
        message =
            "Güncellemeyi bağımsız kurucuya aktardım. " +
            "Talvora servisi güvenli biçimde yeniden başlatılıyor.\n\n" +
            "Bu mesaj yeni sürümün hazır olduğu anlamına gelmiyor. " +
            "Yeni çalışan sürüm ancak ayrı sağlık doğrulaması başarılı olduğunda hazır sayılacak.";
        return true;
    }

    private static bool ResultContainsText<T>(
        T result,
        string expected)
    {
        try
        {
            JsonElement json;
            if (result is CallToolResult callToolResult &&
                callToolResult.StructuredContent is JsonElement structured)
            {
                json = structured;
            }
            else
            {
                json = JsonSerializer.SerializeToElement(result);
            }

            return TryGetPropertyIgnoreCase(
                       json,
                       "standardOutput",
                       out var output) &&
                   output.ValueKind == JsonValueKind.String &&
                   (output.GetString() ?? string.Empty).Contains(
                       expected,
                       StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

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
