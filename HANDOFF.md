# Talvora MCP — Living Handoff

## CURRENT — 2026-09-20

Bu dosya tek kanonik kesinti/devam belgesidir. Eski oturum kronolojisi tutulmaz. Ayrıntılı geçmiş: `BUG-AUDIT.md`; görev listesi: `MCP-CONTROL-CENTER-TODO.md`; Source Edit sözleşmesi: `SOURCE-EDIT-ENGINE-ARCHITECTURE.md`.

### Repo / remote / live durum

- Repository: `C:\Users\tayla\Talvora-MCP`
- Branch: `main`
- Last runtime-affecting fix commit: `43d2228738a32836f9a0a80b737be069d89fe59c` (`fix: allow HTTP mock reply retry after cancellation`).
- Gitea `origin/main` ve GitHub `github/main`: her bug closeout commit'inden sonra birlikte güncellenir.
- Canlı Talvora service: **Running / Automatic**.
- Exact-installed runtime source commit: `43d2228738a32836f9a0a80b737be069d89fe59c`.
- #178 tamamlandı: manual-stop persistence Windows SessionId bazlı dosyaya ayrıldı; targeted regression GREEN; fix commit Gitea + GitHub'a push edildi ve canonical live deploy exact-installed olarak doğrulandı.
- #179 tamamlandı: Control Center exit artık window-placement yazımını shutdown öncesi tamamlıyor; regression GREEN, fix iki remote'a push edildi ve canonical exact-installed live deploy doğrulandı.
- #180 tamamlandı: Control Center detay ekranındaki Gitea browser launch hatası artık beklenen shell exception'larını yakalayıp kullanıcıya bildiriyor; targeted regression GREEN, Tray Release build 0 warning / 0 error, fix iki remote'a push edildi ve canonical exact-installed live deploy doğrulandı.
- #181 tamamlandı: eşzamanlı window-placement kayıtları semaphore ile serialize ediliyor ve monoton save-version ile yalnız en yeni bekleyen snapshot yazılıyor; fix iki remote'a push edildi ve canonical exact-installed live gate GREEN.
- #182 tamamlandı: Control Center ve Tray Gitea browser launch yolları dönen `Process` nesnesini sahiplenip dispose ediyor; regression GREEN, fix iki remote'a push edildi ve canonical exact-installed live gate GREEN.
- #183 tamamlandı: isolated ast-grep ve semantic worker timeout/error cleanup yolları `Process.Kill` sonrasında bounded `WaitForExitAsync` ile parent process exit'ini bekliyor; targeted regression GREEN, Talvora Release build 0 warning / 0 error, fix iki remote'a push edildi ve canonical exact-installed live gate GREEN.
- #184 tamamlandı: HTTP mock runtime cancellation token'ı CTS dispose edilmeden önce cache'leniyor; in-flight handler'ların stop/dispose ile yarışırken dispose edilmiş `CancellationTokenSource.Token` getter'ına erişmesi engelleniyor; runtime-bounds regression GREEN, Talvora Release build 0 warning / 0 error, fix iki remote'a push edildi ve canonical exact-installed live gate GREEN.
- #185 tamamlandı: HTTP mock manual reply gönderimi iptal/hata ile tamamlanmazsa `Replied` claim atomik olarak serbest bırakılıyor; aynı pending isteğe retry mümkün. Targeted integration regression RED -> GREEN, Talvora Release build 0 warning / 0 error; fix iki remote'a push edildi ve canonical exact-installed live gate GREEN.
- #186 source fix hazır: HTTP mock listener lifetime cancellation artık pending-timeout ile aynı yol üzerinden default response göndermiyor; stop/lifetime cancellation pending isteği 503 ile kapatıyor. 64 eşzamanlı pending request regression RED -> GREEN; commit/push/live gate sırada.

## #178 — CLOSED / LIVE VERIFIED

Kök neden:
- Manual-stop state belgesi logon `SessionId`/`AuthenticationId` doğruluyordu ama tüm oturumlar aynı `%LOCALAPPDATA%\Talvora\ControlCenter\session-state.json` dosyasını kullanıyordu.
- Named mutex `Local\...` olduğundan farklı Windows session'ları birbirini kilitlemiyor; aynı kullanıcıyla ikinci session/RDP oturumu diğer session'ın state'ini mismatch görüp sıfırlayabiliyordu.

Düzeltme ve kanıt:
- State yolu `session-state.<SessionId>.json` oldu; aynı session içindeki process'ler mevcut Local mutex'i paylaşmaya devam ediyor, farklı session'ların dosyaları ayrışıyor.
- Eski `session-state.json` yalnız current SessionId/UserSid/AuthenticationId ile eşleşirse yeni session dosyasına migrate ediliyor; mismatch legacy state'e dokunmuyor.
- Runtime `AssertContract` farklı SessionId'lerin farklı dosya yoluna gittiğini doğruluyor.
- `NativeInstallerSourceRegression.ps1`: `ManualStopStateIsWindowsSessionScoped=True`; suite GREEN. Tray Release build **0 warning / 0 error**.
- Fix commit: `36b68b24e14a72b5cf44cd0ae2b91356e882733a`; Gitea `origin/main` ve GitHub `github/main` senkron.
- Canonical installer build GREEN; artifact SHA-256 `3A57702ECD153F71B744B871420D7E1626693B9D62246875E5ADF05B039203B1`.
- Self-update sırasında MCP bağlantısı servis değişimi nedeniyle kesildi; reconnect sonrası `system_info.sourceCommit=36b68b24e14a72b5cf44cd0ae2b91356e882733a` ve service Running doğrulandı.

### Aktif devam noktası

1. #186 source/test/docs değişikliklerini tek bug commit'i olarak commit et; Gitea ve GitHub `main` üzerine push et.
2. Temiz #186 HEAD'den canonical installer build + manifest-bound live deploy yap; exact-installed runtime commit'ini doğrula.
3. #186 live acceptance'ını docs-only closeout ile kapat; ardından deeper audit'e #187'den devam et.

## #179 — CLOSED / LIVE VERIFIED

Kök neden:
- `ControlCenterApplication.ExitFromTray()`, `PrepareForApplicationExit()` çağrısından hemen sonra window'u kapatıp WPF `Shutdown()` çağırıyor.
- `PrepareForApplicationExit()` içindeki `_ = SaveWindowPlacementAsync()` fire-and-forget olduğu için son `window-placement.json` yazımı process kapanışıyla yarışabiliyor ve son konum/boyut kaybolabiliyordu.

Düzeltme ve kanıt:
- Exit path artık `SaveWindowPlacementAsync().GetAwaiter().GetResult()` ile persist işlemini shutdown'dan önce tamamlıyor.
- Alt I/O await'i `ConfigureAwait(false)` kullanıyor; böylece WPF UI thread üzerinde sync-wait deadlock'u oluşturulmuyor.
- `NativeInstallerSourceRegression.ps1` önce RED (`ControlCenterExitPersistsWindowPlacementBeforeShutdown=False`), fix sonrası GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.
- Fix commit: `ded6cd52f692b3563b0edbfdaf1c6f392b8c77e9`; Gitea `origin/main` ve GitHub `github/main` senkron.
- Canonical installer build GREEN; artifact SHA-256 `19A18E87697FC75F8CC04B5E52464D8FAA8062FCCFB6059B2D96A1877D19756D`.
- Self-update sırasında MCP bağlantısı servis değişiminde kesildi; reconnect sonrası `system_info.sourceCommit=ded6cd52f692b3563b0edbfdaf1c6f392b8c77e9` doğrulandı.

## #180 — CLOSED / LIVE VERIFIED

Kök neden:
- Control Center detay ekranındaki Gitea birincil eylemi `OpenGiteaHome()` çağrısını doğrudan async WPF click zincirine bırakıyordu.
- Varsayılan browser/shell başlatma `InvalidOperationException` veya `Win32Exception` üretirse istisna dispatcher'a kadar çıkabiliyor; dispatcher handler yalnız logladığı için tray uygulamasının kapanma riski vardı.

Düzeltme ve kanıt:
- Detay Gitea açma yolu yalnız beklenen shell-launch exception'larını yakalıyor.
- Hata loglanıyor, Control Center event store'a operation failure olarak işleniyor ve kullanıcıya `ShowOperationErrorAsync` ile görünür hata veriliyor; beklenmeyen exception'lar gizlenmiyor.
- `NativeInstallerSourceRegression.ps1` önce RED (`ControlCenterDetailGiteaOpenHandlesShellFailure=False`), fix sonrası GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.
- Fix commit: `d8abbc25dc4e784bc12c7d08dcb4a20bf7e39190`; Gitea `origin/main` ve GitHub `github/main` senkron.
- Clean detached worktree üzerinden canonical installer build GREEN; artifact SHA-256 `D41A51195F8F44ED7D9C80DADAC2E66490CA5026C73EB9CDA5FA640D191713CD`.
- Manifest-bound independent SYSTEM deploy sonrası Talvora service **Running / Automatic** ve exact-installed `system_info.sourceCommit=d8abbc25dc4e784bc12c7d08dcb4a20bf7e39190`.

## #181 — CLOSED / LIVE VERIFIED

Kök neden:
- Debounce timer içindeki async `SaveWindowPlacementAsync()` dosya I/O sırasında UI thread'i serbest bırakıyor; bu sırada yeni hareket/resize veya exit save'i başlayabiliyor.
- Birden fazla kayıt eşzamanlı ilerlediğinde eski snapshot daha geç publish olup daha yeni pencere konumunu geri ezebilirdi.

Düzeltme ve kanıt:
- `_windowPlacementSaveGate` aynı dosyaya publication'ı serialize ediyor.
- `_windowPlacementSaveVersion` her snapshot'ta monoton artıyor; gate'i bekleyen eski snapshot, `Volatile.Read` ile artık en yeni değilse yazmadan çıkıyor.
- Exit save yolu #179'daki deterministik wait davranışını koruyor; gate await'i `ConfigureAwait(false)` ile UI context'e bağlı değil.
- `NativeInstallerSourceRegression.ps1`: `ControlCenterWindowPlacementSaveLatestWins=True`; suite GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.
- Fix commit: `86290c49abc14fe3ddfac791ccf6548c6b99c2c6`; Gitea `origin/main` ve GitHub `github/main` senkron.
- Canonical installer SHA-256 `67ECEC023E2892A0C09E906DA22A6DDC8BDC3BFD7BE0EC4D7AFDD9BE8967A585`.
- Self-update sonrası reconnect doğrulaması: `system_info.sourceCommit=86290c49abc14fe3ddfac791ccf6548c6b99c2c6`; service exact-installed GREEN.

## #182 — CLOSED / LIVE VERIFIED

Kök neden:
- Control Center `OpenGiteaHome()` ve Tray Gitea browser açma yolu shell launch için `Process.Start` çağırıyor ancak dönen nullable `Process` nesnesini sahiplenmeden bırakıyordu.
- Browser sürecini izlemiyoruz veya kontrol etmiyoruz; bu nedenle process component/handle kaynaklarını açık tutmanın işlevsel bir nedeni yok.

Düzeltme ve kanıt:
- Her iki launch yolu `using var launched = Process.Start(...)` kullanıyor; shell launch davranışı korunurken dönen disposable nesne scope sonunda serbest bırakılıyor.
- Güncel .NET disposable ownership rehberiyle doğrulandı.
- `NativeInstallerSourceRegression.ps1`: `GiteaBrowserLaunchDisposesProcessHandles=True`; suite GREEN.
- `Talvora.Tray` Release build: **0 warning / 0 error**.
- Fix commit: `47bd8430aaa71eafa4908b0de6697b88c99e647c`; Gitea `origin/main` ve GitHub `github/main` senkron.
- Canonical installer SHA-256 `C645B16606E3DD5BA944D7D717B390105B24285FCA755D3C800104D400C22EE3`.
- Self-update sonrası reconnect: `system_info.sourceCommit=47bd8430aaa71eafa4908b0de6697b88c99e647c`; service exact-installed GREEN.

## #183 — CLOSED / LIVE VERIFIED

Kök neden:
- Isolated ast-grep ve semantic worker timeout/error yollarında `Process.Kill(entireProcessTree: true)` çağrısından hemen sonra cleanup'a geçiliyordu.
- `Process.Kill` asenkron olduğundan ast-grep staging directory ve semantic response file cleanup'ı hâlâ çıkmakta olan process ile yarışabiliyordu.

Düzeltme ve kanıt:
- Her iki worker yolu kill sonrasında bağımsız 10 saniyelik bounded budget ile `WaitForExitAsync` çağırıyor; original cancellation/error sonucu korunuyor.
- Semantic worker response silme işlemi termination wait'ten sonra çalışıyor; ast-grep proposal yolu da termination wait tamamlanmadan outer staging cleanup'a dönmüyor.
- `NativeInstallerSourceRegression.ps1`: `SourceEditWorkerCleanupWaitsForExit=True`; suite GREEN.
- `Talvora` Release build: **0 warning / 0 error**.
- Fix commit: `4ab89d19b29927066464bc5fd6178d224552d51d`; Gitea `origin/main` ve GitHub `github/main` aynı commit'te.
- Clean detached worktree canonical installer SHA-256: `0DD13BEA0867B36096FF805978FACE4C272275F107BAD31EB6AC306335BDEA64`.
- Manifest-bound SYSTEM deploy sonrası service **Running / Automatic** ve exact-installed `system_info.sourceCommit=4ab89d19b29927066464bc5fd6178d224552d51d`.

## #184 — CLOSED / LIVE VERIFIED

Kök neden:
- `talvora_http_mock_stop`, runtime'ı registry'den çıkarıp `Dispose()` ederken fire-and-forget listener/request handler task'ları hâlâ devam edebiliyor.
- Eski handler kodu çeşitli await noktalarında yeniden `runtime.Cancellation.Token` okuyordu. CTS dispose edilmişse `Token` getter'ı `ObjectDisposedException` atabildiği için normal stop akışı handler hata yoluna düşebiliyordu.

Düzeltme ve kanıt:
- Runtime kendi CTS'sini özel alanda sahipleniyor ve `LifetimeToken` değerini constructor'da, dispose öncesi yalnız bir kez cache'liyor.
- Listener, pending-slot, response, linked-timeout ve request-body I/O yolları CTS getter yerine cache'lenmiş immutable token kopyasını kullanıyor.
- `--runtime-bounds-only`: watcher + HTTP mock resource-bounds regression GREEN; cached lifetime token dispose sonrasında cancelled olarak güvenle okunuyor.
- `Talvora` Release build: **0 warning / 0 error**.
- Fix commit: `cb68396a16b22f6829ea16f5d82751366f834e86`; Gitea `origin/main` ve GitHub `github/main` aynı commit'te.
- Clean detached worktree canonical installer SHA-256: `8216DCC27966C3CA87FDC5C206B7C2B727D87B8325D3F52713CE7F62411CB7FA`.
- Manifest-bound SYSTEM deploy sonrası service **Running / Automatic** ve exact-installed `system_info.sourceCommit=cb68396a16b22f6829ea16f5d82751366f834e86`.

## #185 — CLOSED / LIVE VERIFIED

Kök neden:
- `TrySendResponseAsync`, response I/O başlamadan önce `pending.Replied` için 0 -> 1 claim alıyor.
- Gönderim caller cancellation veya I/O hatasıyla tamamlanamazsa claim 1 olarak kalıyordu. Pending kayıt dictionary'de durmasına rağmen sonraki `talvora_http_mock_reply` çağrıları hemen `Replied=false` dönüyor ve request pending-timeout'a kadar yanıtlanamıyordu.

Düzeltme ve kanıt:
- Başarısız veya caller-cancelled send yolunda yalnız hâlâ bu attempt'e ait olan 1 claim'i `Interlocked.CompareExchange(..., 0, 1)` ile geri bırakılıyor; başarılı send claim'i koruyor.
- Gerçek loopback `HttpListener` integration regression önce RED: `HTTP mock reply cancellation permanently consumed the reply claim.`
- Fix sonrası aynı testte cancelled ilk attempt ardından ikinci reply `retry-ok` yanıtını client'a ulaştırıyor; `--runtime-bounds-only` GREEN.
- Regression ayrıca normal full-suite listesine `http-mock-reply-cancellation-retry` olarak kaydedildi.
- `Talvora` Release build: **0 warning / 0 error**.
- Fix commit: `43d2228738a32836f9a0a80b737be069d89fe59c`; Gitea `origin/main` ve GitHub `github/main` aynı commit'te.
- Canonical installer SHA-256: `C8371E97D3303F61402D9163896C7B261899072A2CA23D974C03C4EF07DF39CE`.
- Manifest-bound independent SYSTEM deploy sonrası exact-installed `system_info.sourceCommit=43d2228738a32836f9a0a80b737be069d89fe59c`.

## #186 — SOURCE FIXED / COMMIT + LIVE DEPLOY PENDING

Kök neden:
- Manual pending handler tek bir linked timeout token kullanıyordu; hem listener lifetime cancellation hem de gerçek pending timeout aynı `catch (OperationCanceledException)` bloğuna giriyordu.
- Bu blok default response'u `CancellationToken.None` ile gönderdiği için `talvora_http_mock_stop` ile başlayan lifetime cancellation yarışını handler kazanırsa Stop sözleşmesindeki 503 yerine default response dönebiliyordu.

Düzeltme ve kanıt:
- Lifetime cancellation ayrı filtered catch'e ayrıldı; reply claim'i handler kazanırsa `RejectContext(..., 503)` uygulanıyor.
- Yalnız gerçek pending timeout mevcut default-response davranışını kullanmaya devam ediyor.
- 64 eşzamanlı gerçek loopback pending request ile regression önce RED: `HTTP mock stop allowed a pending request to receive the timeout default instead of 503.`
- Fix sonrası `PASS http-mock-stop-pending-503` ve tüm `--runtime-bounds-only` gate GREEN.

## Sabit çalışma kuralları

- Source/config/repository-document editörü: **`talvora_apply_patch` PRIMARY/default**.
- Existing dosya editinden önce `talvora_read_source` revision al.
- Her bug: kanıt -> kök neden -> minimal patch -> targeted test -> docs -> commit -> Gitea push -> GitHub push -> gerekiyorsa live deploy.
- Git stage/commit için yalnız açık dosya listesi; `talvora_git_run explicitAdmin=true`.
- `git add .`, reset, clean, stash, revert kullanma.
- Gereksiz geniş testleri tekrar etme.
- Broad catch/fallback ile problemi gizleme.
- Çalışan capability'leri gereksiz yere kaldırma.
- Kesintide bu HANDOFF + `git status --short` + son commitlerden doğrudan ACTIVE maddeden devam et.

## Bitiş kriteri

Yeni audit turunda açık doğrulanmış bug kalmadığında, targeted testler GREEN, working tree temiz, iki remote senkron ve runtime-affecting son commit canlı olduğunda `C:\Users\tayla\Desktop\bitti.txt` oluştur. İçine tamamlanma zamanı, final HEAD, iki remote durumu, live source commit ve final test özetini yaz.
