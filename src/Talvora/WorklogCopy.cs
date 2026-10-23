namespace Talvora;

internal sealed record WorklogTitles(string Running, string Succeeded, string Failed);

/// <summary>Approved Turkish copy (DESIGN-WORKLOG §6, decisions P-4/P-10). Do not edit without Claude.</summary>
internal static class WorklogCopy
{
    internal const string RunningFallback = "Devam ediyor.";
    internal const string SucceededFallback = "Adım başarıyla tamamlandı.";
    internal const string CancelledFallback = "Adım durduruldu.";
    internal const string CancelledTitle = "İşlem durduruldu";

    internal static readonly WorklogTitles SourceEdit = new("Kod değişikliği uygulanıyor", "Kod değişikliği uygulandı", "Kod değişikliği uygulanamadı");
    internal static readonly WorklogTitles Build = new("Derleniyor", "Derleme başarılı", "Derleme başarısız");
    internal static readonly WorklogTitles Tests = new("Testler çalışıyor", "Testler geçti", "Testler başarısız");
    internal static readonly WorklogTitles Publish = new("Yayın paketi hazırlanıyor", "Yayın paketi hazırlandı", "Yayın paketi hazırlanamadı");
    internal static readonly WorklogTitles Restore = new("Paketler geri yükleniyor", "Paketler hazır", "Paketler geri yüklenemedi");
    internal static readonly WorklogTitles PackageBuild = new("Kurulum paketi hazırlanıyor", "Kurulum paketi hazırlandı", "Kurulum paketi hazırlanamadı");
    internal static readonly WorklogTitles Deploy = new("Yeni sürüm kuruluyor", "Kurulum devredildi · doğrulama bekleniyor", "Yeni sürüm kurulamadı");
    internal static readonly WorklogTitles Commit = new("Değişiklikler kaydediliyor", "Değişiklikler kaydedildi", "Kayıt oluşturulamadı");
    internal static readonly WorklogTitles Push = new("Uzak depoya gönderiliyor", "Uzak depoya gönderildi", "Gönderim yapılamadı");
    internal static readonly WorklogTitles OtherGit = new("Git işlemi yapılıyor", "Git işlemi tamamlandı", "Git işlemi tamamlanamadı");
    internal static readonly WorklogTitles Service = new("Servis işlemi yapılıyor", "Servis işlemi tamamlandı", "Servis istenen duruma geçmedi");
    internal static readonly WorklogTitles Package = new("Paket işlemi yapılıyor", "Paket işlemi tamamlandı", "Paket işlemi tamamlanamadı");
    internal static readonly WorklogTitles FileMutation = new("Dosyalar düzenleniyor", "Dosyalar düzenlendi", "Dosya işlemi tamamlanamadı");
    internal static readonly WorklogTitles Network = new("Bağlantı denetleniyor", "Bağlantı denetlendi", "Bağlantı kurulamadı");
    internal static readonly WorklogTitles Process = new("Program çalışıyor", "Program bitti", "Program hata ile bitti");
    internal static readonly WorklogTitles Codex = new("Codex çalışıyor", "Codex görevi tamamlandı", "Codex görevi tamamlanamadı");
    internal static readonly WorklogTitles Claude = new("Claude çalışıyor", "Claude görevi tamamlandı", "Claude görevi tamamlanamadı");
    internal static readonly WorklogTitles RemotionCreate = new("Animasyon projesi hazırlanıyor", "Animasyon projesi hazır", "Animasyon projesi hazırlanamadı");
    internal static readonly WorklogTitles RemotionCheck = new("Animasyon denetleniyor", "Animasyon denetlendi", "Animasyon denetlenemedi");
    internal static readonly WorklogTitles RemotionStill = new("Önizleme karesi hazırlanıyor", "Önizleme karesi hazır", "Önizleme karesi hazırlanamadı");
    internal static readonly WorklogTitles RemotionRender = new("Animasyon oluşturuluyor", "Animasyon hazır", "Animasyon oluşturulamadı");
    internal static readonly WorklogTitles Generic = new("İşlem yürütülüyor", "İşlem tamamlandı", "İşlem tamamlanamadı");
}
