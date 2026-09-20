# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu belge yalnız güncel çalışma durumunu taşır. Eski oturum kronolojisi burada tutulmaz; ayrıntılı bug geçmişi `BUG-AUDIT.md`, görev durumu `MCP-CONTROL-CENTER-TODO.md`, Source Edit mimarisi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

### Repository / live baseline

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Başlangıç HEAD: `27d13da1a58f2edeab24dd898972d53467ff841a`
- Başlangıçta working tree temizdi.
- Gitea remote: `origin = ssh://git@127.0.0.1:2222/taylan/Talvora-MCP.git`
- GitHub remote: `github = https://github.com/bingoweb/Talvora-MCP.git`
- Başlangıçta `origin/main` ve `github/main` HEAD ile senkrondu.
- Canlı Talvora service bağlantısı doğrulandı. `talvora_system_info.sourceCommit = e1ff681b6023b393b164e676a5c8d2b414bc5606`; service LocalSystem altında çalışıyor.

### Son tamamlanan düzeltmeler

En güncel bug-audit hattında #121–#172 aralığındaki tüm doğrulanmış maddeler, aşağıdaki iki açık madde hariç kapatıldı.

Özellikle son tamamlanan aileler:

- Source Edit transaction/WAL/idempotency/parent-identity/crash-recovery düzeltmeleri.
- Roslyn semantic edit resource bounds, graph fingerprint, analyzer/source-generator identity ve workspace-scoped MSBuild isolation.
- Installer immutable source snapshot, rollback state, artifact identity pinning ve canonical deploy doğrulamaları.
- ast-grep executable/provenance doğrulamaları ve npm singleton metadata envelope uyumluluğu.
- Tunnel create reconciliation ve client update rollback/recovery.
- Gitea gerçek MCP readiness ve full-chain stop doğrulaması.
- Shared process/output, job log/read continuation, source streaming read, watcher ve HTTP mock resource bounds.
- AtomicFile crash-durable publication.

Son doğrulanan Source Edit regression: **63/63 GREEN**.

## OPEN BUGS — sıradaki iş

### Son tamamlanan bug — #169

- **Kök neden:** provenance publish sonrasında mutable `obj\project.assets.json` okuyordu; sonraki restore published binary'den farklı graph raporlatabiliyordu.
- **Düzeltme:** explicit restore -> immutable assets snapshot + SHA-256 -> `--no-restore` Service/Tray publish -> published runtime dependency graph cross-check. Single-file Tray için pre-bundle `.deps.json` doğrulanıyor.
- **Test:** `INSTALLER_DEPENDENCY_PROVENANCE_GREEN`; `NATIVE_INSTALLER_SOURCE_GREEN`; canonical build GREEN.
- **Canlı:** installer 261,777,167 bytes / SHA-256 `D2FB078B0512E6AEB1507986ADA85CA3E3CC5E3AEEC6A82B6AD8E235B4E02985`; Talvora Running/Automatic, PID 2112, live source `17e1948bedc99e2e18a3e0397ff627dc07017826-dirty-8427300c024c`.

### #159 — Absolute transport/response budgets + continuation semantics — OPEN MEDIUM

Doğrulanmış kapsam:

- `talvora_read_bytes count=0`
- `talvora_tail_text lineCount=0`
- generic HTTP response reader `maxBytes=0`
- `talvora_tcp_exchange maxResponseBytes=0`
- `talvora_websocket_exchange maxMessageBytes=0`
- `talvora_sqlite_query maxRows=0`
- `talvora_find_files/search_text max*=0`

Sorun: `0=unlimited` tek invocation içinde sınırsız materialization / serialization belleği üretebiliyor.

Hedef çözüm:

1. Capability'yi kaldırmadan yüksek fakat mutlak server response ceiling'leri tanımla.
2. Uygun yüzeylerde continuation/pagination metadata ekle.
3. Network/read akışlarında streaming veya bounded buffering kullan.
4. Structured response yüzeylerinde deterministic truncation + continuation state tasarla.
5. Her tool için overflow/continuation regressions ekle; aynı kapsamlı suite'i gereksiz tekrar etme.
6. #159 tamamlanınca audit/TODO belgelerini güncelle.
7. Tek bug commit'i oluştur; `origin/main` ve `github/main` üzerine push et.
8. Runtime değişikliklerini canonical build/deploy ile canlıya al ve exact-installed tool behavior'ı doğrula.

## Çalışma kuralları

- Normal source/config/repository-document düzenlemesi: **`talvora_apply_patch` PRIMARY/default**.
- Mevcut dosyayı düzenlemeden önce `talvora_read_source` revision alınır; küçük, deterministic patch uygulanır.
- Aynı işi farklı mutatorlarla deneme-yanılma yapma.
- Her bug ayrı ele alınır: **kanıt -> kök neden -> patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa live deploy**.
- Aynı kapsamlı test paketi değişiklik gerektirmedikçe tekrar çalıştırılmaz.
- Güncel framework/API davranışı gerekiyorsa Context7 + resmi dokümanla doğrula; gerçek proje sürümünü esas al.
- Çalışan capability'leri güvenlik/kolaylık adına kırpma. Resource fix'lerinde TAM YETKI semantiği korunur; yalnız tek-response bellek/transport patlaması bounded hale getirilir.
- Broad catch/fallback ile semptom gizleme; kök nedeni düzelt.
- Yeni önemli bulgu çıkarsa önce `BUG-AUDIT.md` ve bu handoff'a ekle.

## Her bug sonrası zorunlu kayıt

Handoff'a şu dört satırlık kısa kayıt eklenir:

- Bulgu / kök neden
- Uygulanan düzeltme
- Targeted test sonucu
- Commit + `origin/main` + `github/main` + live deploy sonucu

## Bitiş kriteri

Tüm açık bug'lar kapanmış, ilgili targeted/regression testleri GREEN, working tree temiz, iki remote senkron ve gereken runtime değişiklikleri canlıya alınmış olmalı.

Bu koşulların tamamı sağlandıktan sonra Windows masaüstünde:

`C:\Users\tayla\Desktop\bitti.txt`

oluşturulacak ve içine final HEAD, iki remote senkron durumu, live source commit/fingerprint ve son test özeti yazılacak.
