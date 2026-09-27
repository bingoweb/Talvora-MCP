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

        if (normalized == "run_powershell")
        {
            var script = GetArgumentText(arguments, "script");
            if (script.Contains("Build-Windows-Installer.ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Yaptığım değişiklikleri kullanıma hazırlıyorum",
                    "Yaptığım son düzenlemeleri bilgisayarında kullanılabilecek hale getiriyorum.",
                    "Birazdan yeni halini doğrudan deneyebilmen için.");
            }

            if (script.Contains("Install.ps1", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Yeni hali bilgisayarında etkinleştiriyorum",
                    "Az önce hazırladığım değişiklikleri çalışan Talvora'ya uyguluyorum.",
                    "Yaptığım düzeltmeleri hemen kullanabilmen için.");
            }

            if (script.Contains("Get-Content", StringComparison.OrdinalIgnoreCase) &&
                script.Contains("log", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Son yaptığım değişikliği kontrol ediyorum",
                    "Programın az önceki değişiklikten sonra düzgün çalışıp çalışmadığına bakıyorum.",
                    "Sana tamamlandı demeden önce gerçekten sorunsuz olduğundan emin olmak için.");
            }

            return new(
                "Bilgisayarında gerekli kontrolü yapıyorum",
                "Şu an yaptığım işin doğru ilerlediğini kontrol ediyorum.",
                "Bir sonraki adıma güvenle geçebilmek için.");
        }

        if (normalized is "dotnet_build" or "dotnet_test" or "dotnet_restore")
        {
            return new(
                "Yaptığım değişikliği kontrol ediyorum",
                "Az önce yaptığım düzenlemenin programı bozmadığını kontrol ediyorum.",
                "Sorun varsa sana ulaşmadan önce yakalayıp düzeltmek için.");
        }

        if (normalized == "apply_patch")
        {
            return new(
                "İstediğin değişikliği uyguluyorum",
                "Şu an istediğin davranışı programın içine yerleştiriyorum.",
                "İstediğin şey sadece anlatılmış değil gerçekten çalışıyor olsun diye.");
        }

        if (normalized == "git_run")
        {
            var gitArgs = GetArgumentArray(arguments, "arguments");
            if (gitArgs.Count > 0 &&
                string.Equals(gitArgs[0], "push", StringComparison.OrdinalIgnoreCase))
            {
                return new(
                    "Yaptığım çalışmayı güvene alıyorum",
                    "Tamamladığım değişikliklerin güvenli bir kopyasını kaydediyorum.",
                    "Bir sorun olursa yapılan işi kaybetmemek için.");
            }

            return new(
                "Yaptığım değişiklikleri toparlıyorum",
                "Bu çalışma sırasında yaptığım düzenlemeleri kontrol edip toparlıyorum.",
                "Bir sonraki adıma temiz ve güvenli şekilde geçmek için.");
        }

        return new(
            "Şu an sıradaki işi yapıyorum",
            "İstediğin geliştirme üzerinde çalışmaya devam ediyorum.",
            "Talvora'yı daha düzgün ve kullanışlı hale getirmek için.");
    }

    private static string BuildPlainCompletion(OperationNarrative narrative) =>
        narrative.Subject switch
        {
            "Yaptığım değişiklikleri kullanıma hazırlıyorum" =>
                "Yaptığım son düzenlemeler kullanıma hazır.",
            "Yeni hali bilgisayarında etkinleştiriyorum" =>
                "Yeni hali bilgisayarında etkinleştirdim.",
            "Son yaptığım değişikliği kontrol ediyorum" =>
                "Son yaptığım değişikliği kontrol ettim; bu adım tamamlandı.",
            "Bilgisayarında gerekli kontrolü yapıyorum" =>
                "Gerekli kontrolü tamamladım.",
            "Yaptığım değişikliği kontrol ediyorum" =>
                "Yaptığım değişikliğin bu kontrolünü tamamladım.",
            "İstediğin değişikliği uyguluyorum" =>
                "İstediğin değişikliği uyguladım.",
            "Yaptığım çalışmayı güvene alıyorum" =>
                "Yaptığım çalışmanın güvenli kopyasını kaydettim.",
            "Yaptığım değişiklikleri toparlıyorum" =>
                "Yaptığım değişiklikleri toparladım.",
            _ =>
                "Bu adımı tamamladım.",
        };

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
