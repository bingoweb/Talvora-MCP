using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace Talvora.Tray;

internal static class ControlCenterUserMessage
{
    public static string ForOperation(
        Exception exception,
        string action = "İşlem")
    {
        var prefix = string.IsNullOrWhiteSpace(action)
            ? "İşlem"
            : action.Trim();

        return exception switch
        {
            UnauthorizedAccessException =>
                $"{prefix} için gerekli Windows izni alınamadı. " +
                "Sonraki adım: Talvora servis ve kurulum izinleri kontrol edilmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",

            TimeoutException or OperationCanceledException =>
                $"{prefix} beklenen sürede tamamlanmadı. " +
                "Sonraki adım: ilgili bileşenin durumu yenilenmeli veya işlem yeniden denenmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",

            HttpRequestException =>
                $"{prefix} sırasında yerel bağlantıya ulaşılamadı. " +
                "Sonraki adım: ilgili bileşenin çalışıp çalışmadığı kontrol edilmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",

            IOException =>
                $"{prefix} sırasında gerekli yerel dosyaya veya çalışma alanına erişilemedi. " +
                "Sonraki adım: dosya yolu ve erişim izinleri kontrol edilmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",

            JsonException =>
                $"{prefix} için alınan durum bilgisi okunamadı. " +
                "Sonraki adım: durum bilgisi yenilenmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",

            Win32Exception =>
                $"{prefix} Windows tarafından başlatılamadı. " +
                "Sonraki adım: ilgili programın kurulu ve çalıştırılabilir olduğu kontrol edilmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",

            ManagedMcpOperationInProgressException =>
                $"{prefix} şu anda tamamlanamıyor çünkü aynı bileşen üzerinde başka bir işlem sürüyor. " +
                "Devam eden işlem bittiğinde durum yeniden kontrol edilecek.",

            _ =>
                $"{prefix} tamamlanamadı. " +
                "Sonraki adım: durum yenilenmeli ve işlem gerekirse yeniden denenmeli. " +
                "Teknik ayrıntılar günlüğe kaydedildi.",
        };
    }
}
