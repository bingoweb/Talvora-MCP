# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20 16:33 TRT

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi burada tutulmaz. Ayrıntılı geçmiş `BUG-AUDIT.md`, görev listesi `MCP-CONTROL-CENTER-TODO.md`, Source Edit sözleşmesi `SOURCE-EDIT-ENGINE-ARCHITECTURE.md` içindedir.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Son committed HEAD: `07dcf042fc85837eb9ec3a1506674fac88e6a95b`
- `origin/main` (Gitea) ve `github/main` aynı HEAD'de.
- Working tree şu anda yalnız #174 üzerinde yarım kalan üç source değişikliği nedeniyle dirty:
  - `src/Talvora/Tools/ConfigFormatTools.cs`
  - `src/Talvora/Tools/ConfigFormatTools.Xml.cs`
  - `src/Talvora/Tools/ConfigFormatTools.TestReports.cs`
- Canlı Windows service: `Talvora` = Running / Automatic.
- Exact-installed source commit: `642c2158aebbe91e99e9349e92262c94b9b17e43`.
- Son canlı kabul: #159 response-bounds düzeltmeleri installed ve doğrulandı.

## Son tamamlanan düzeltme hattı

#121–#173 kapsamındaki doğrulanmış buglar kapalı. Son önemli aileler: Source Edit transaction/WAL/idempotency ve recovery; semantic edit toolchain isolation; installer immutable source/dependency provenance; tunnel/Gitea lifecycle; process/job/watch/HTTP resource bounds; AtomicFile durability; #159 response bounds; #173 installer artifacts-path fix.

Tek açık doğrulanmış madde: **#174**.

## ACTIVE — #174 Residual structured/list 0=unlimited responses

Durum: **IN PROGRESS / MEDIUM**.

Kök neden: bazı structured/list tool yüzeylerinde caller limitinin `0` olması hâlâ tek response içinde gerçek limitsiz materialization/list üretimine izin veriyor. #159 bu davranışı transport/read yüzeylerinde kapattı ancak aşağıdaki residual yüzeyler ayrı kaldı.

Doğrulanan kapsam:
- `talvora_test_report_summary maxFailures=0`
- `talvora_xml_query maxResults=0`
- `talvora_project_discover maxResults=0`
- `talvora_workspace_inspect maxProjects=0`
- `talvora_workspace_commands maxCommands=0`
- `talvora_dev_server_list maxResults=0`
- `talvora_job_list maxResults=0`
- `talvora_git_log maxCount=0`
- quality artifact/diagnostic list yolları

Şu anda yarım kalmış uygulama:
- XML query için finite absolute ceiling + `resultOffset/nextResultOffset` başlanmış.
- Test report için finite absolute ceiling + `failureOffset/nextFailureOffset` başlanmış.
- Bu üç source dosyası henüz commitlenmedi; önce tamamlanıp targeted test ile doğrulanacak.

#174 kabul kriteri:
1. Her list/structured yüzeyde `0` = finite server maximum.
2. Caller büyük pozitif limit verse bile absolute ceiling aşılmıyor.
3. Deterministic continuation/offset/cursor sağlanıyor veya continuation desteklenmiyorsa response bunu açıkça bildiriyor.
4. Response schema truncation + next-state bilgisini taşıyor.
5. Tool description yeni semantiği doğru anlatıyor.
6. Targeted regression tüm kapsanan yüzeylerde GREEN.
7. Talvora Release build 0 warning / 0 error.
8. `BUG-AUDIT.md`, `MCP-CONTROL-CENTER-TODO.md`, bu HANDOFF güncelleniyor.
9. Tek bug commit'i Gitea + GitHub'a push ediliyor.
10. Canonical mevcut deploy/update yolu ile canlıya alınıp exact-installed source commit doğrulanıyor.

## Sabit çalışma kuralları

- Normal source/config/repository-document editörü: **`talvora_apply_patch` PRIMARY/default**.
- Existing dosya editinden önce `talvora_read_source` revision alınır.
- Her bug: kanıt -> kök neden -> küçük patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa live deploy.
- `git add .` yok; yalnız ilgili dosyalar stage edilir.
- reset/clean/stash/revert yok.
- Aynı geniş test paketi gereksiz yere tekrar edilmez.
- Geniş catch/fallback ile hata gizlenmez; kök neden düzeltilir.
- Çalışan capability'ler gereksiz yere kaldırılmaz.
- Güncel API/framework davranışı gerekiyorsa kurulu sürüm + Context7/resmi doküman doğrulanır.
- Kesintiden sonra bu dosya + `git status --short` + son commitler okunur ve doğrudan ACTIVE maddeden devam edilir.

## Her bug sonrası HANDOFF kaydı

- Bulgu / kök neden
- Düzeltme
- Targeted test sonucu
- Commit hash
- `origin/main` push
- `github/main` push
- Live deploy / exact-installed source sonucu

## Bitiş

Tüm açık doğrulanmış buglar kapalı, targeted testler GREEN, working tree temiz, iki remote senkron ve gerekli runtime değişiklikleri canlıya alınmış olduğunda:

`C:\Users\tayla\Desktop\bitti.txt`

oluşturulacak. İçerik: tamamlanma zamanı, final HEAD, iki remote durumu, live source commit ve son test özeti.
