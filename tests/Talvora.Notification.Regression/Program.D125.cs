using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using Talvora;
using Talvora.Shared;
using Talvora.SourceEditing;

internal static partial class Program
{
    private static async Task AssertD125FailureOutcomesAsync()
    {
        var observed = new ConcurrentQueue<DesktopProgressMessage>();
        await using var notifier = CreateNotifier(observed);

        static CallToolResult Failure(object structured, bool isError = false) => new()
        {
            IsError = isError,
            StructuredContent = JsonSerializer.SerializeToElement(structured),
        };

        (string Tool, Func<CancellationToken, ValueTask<CallToolResult>> Run, WorklogOutcome Expected)[] cases =
        [
            ("talvora_run_process",
                _ => ValueTask.FromResult(Failure(new { exitCode = 1, timedOut = false, standardError = "x" })),
                new(WorklogFailureKind.Other, "Komut 1 çıkış koduyla bitti.", null)),
            ("talvora_user_run",
                _ => ValueTask.FromResult(Failure(new { success = false, exitCode = 2, timedOut = false })),
                new(WorklogFailureKind.Other, "Komut 2 çıkış koduyla bitti.", null)),
            ("talvora_user_run",
                _ => ValueTask.FromResult(Failure(new { success = false, exitCode = 0, timedOut = true })),
                new(WorklogFailureKind.Timeout, "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.", null)),
            ("talvora_apply_patch",
                _ => ValueTask.FromResult(Failure(new
                {
                    success = false,
                    error = new { code = "EXPECTED_REVISION_MISMATCH" },
                })),
                new(WorklogFailureKind.StaleRevision,
                    "Dosya bu arada değişmişti; eski hâline göre değişiklik yapılmadı. Kodunuz değişmedi.",
                    "Ajan güncel hâli okuyup yeniden deneyecek.")),
            ("talvora_apply_patch",
                _ => ValueTask.FromResult(Failure(new { error = new { code = "PATCH_PARSE_ERROR" } }, true)),
                new(WorklogFailureKind.InvalidArguments,
                    "Değişiklik isteği dosyanın güncel hâliyle eşleşmedi. Kodunuz değişmedi.",
                    "Ajan isteği düzeltip yeniden deneyebilir.")),
            ("talvora_apply_patch",
                _ => ValueTask.FromResult(Failure(new { error = new { code = "SOURCE_VALIDATION_FAILED" } }, true)),
                new(WorklogFailureKind.Other,
                    "Değişiklik sözdizimi kontrolünden geçmedi. Kodunuz değişmedi.",
                    "Ajan hatayı düzeltip yeniden deneyebilir.")),
            ("talvora_dotnet_build",
                _ => ValueTask.FromResult(Failure(new { success = false })),
                new(WorklogFailureKind.BuildErrors, "Derleme hata verdi.",
                    "Ajan hataları düzeltebilir; kodunuz kaydedildiği hâliyle duruyor.")),
            ("talvora_dotnet_test",
                _ => ValueTask.FromResult(Failure(new { success = false })),
                new(WorklogFailureKind.TestFailures, "Testlerin bir kısmı başarısız oldu.", null)),
            ("talvora_git_run",
                _ => ValueTask.FromResult(Failure(new
                {
                    success = false,
                    standardError = "TALVORA_DELIVERY_GATE_BLOCKED",
                })),
                new(WorklogFailureKind.DeliveryGate,
                    "Teslim kontrolü eksik kanıt nedeniyle gönderimi durdurdu.",
                    "Kanıt dosyaları güncellenince tekrar denenebilir.")),
            ("talvora_delete",
                _ => throw new UnauthorizedAccessException(
                    "Bu konum gizli anahtarlar için ayrılmıştır; Talvora dosya araçları burayı okumaz ve değiştirmez."),
                new(WorklogFailureKind.AccessDenied,
                    "Bu konum gizli anahtarlara ayrılmış; Talvora burayı okumaz ve değiştirmez. Hiçbir şeye dokunulmadı.",
                    null)),
            ("talvora_delete",
                _ => throw new UnauthorizedAccessException("Access denied"),
                new(WorklogFailureKind.AccessDenied, "Windows bu işleme izin vermedi.", null)),
            ("talvora_delete",
                _ => throw new SourceEditDomainException(
                    "SOURCE_EDIT_POLICY_VIOLATION", "SOURCE_EDIT_POLICY_VIOLATION: x"),
                new(WorklogFailureKind.AccessDenied,
                    "Bu dosya bir proje klasöründe; Talvora proje dosyalarını yalnızca kod değişikliği olarak değiştirir. Dosyaya dokunulmadı.",
                    "Ajan aynı işi kod değişikliği olarak yeniden yapabilir.")),
            ("talvora_agent_brief",
                _ => throw new FileNotFoundException("No AGENTS.md in C:\\x."),
                new(WorklogFailureKind.NotFound,
                    "Bu klasörde proje talimatı (AGENTS.md) yok.",
                    "Ajan talimatın bulunduğu proje klasörünü seçip yeniden isteyebilir.")),
            ("talvora_move",
                _ => throw new FileNotFoundException("x"),
                new(WorklogFailureKind.NotFound, "Aranan dosya ya da klasör bulunamadı.", null)),
            ("talvora_git_run",
                _ => throw new InvalidOperationException("x"),
                new(WorklogFailureKind.Other, "Bu adım tamamlanamadı.", null)),
            ("talvora_user_run",
                _ => throw new TimeoutException("x"),
                new(WorklogFailureKind.Timeout, "İşlem zaman sınırını aştı ve tamamlanmış sayılmıyor.", null)),
            ("talvora_delete",
                _ => throw new SourceEditDomainException("SOURCE_EDIT_CONFLICT", "SOURCE_EDIT_CONFLICT: x"),
                new(WorklogFailureKind.StaleRevision,
                    "Dosya bu arada değişmişti; eski hâline göre değişiklik yapılmadı. Kodunuz değişmedi.",
                    "Ajan güncel hâli okuyup yeniden deneyecek.")),
            ("talvora_delete",
                _ => throw new AggregateException(new UnauthorizedAccessException("Access denied")),
                new(WorklogFailureKind.AccessDenied, "Windows bu işleme izin vermedi.", null)),
            ("talvora_apply_patch",
                _ => ValueTask.FromResult(Failure(new
                {
                    success = false, error = new { code = "UNKNOWN_ERROR_CODE" },
                })),
                new(WorklogFailureKind.Other, "Bu adım tamamlanamadı.", null)),
        ];

        foreach (var test in cases)
        {
            observed.Clear();
            try
            {
                _ = await notifier.RunToolCallAsync<CallToolResult>(
                    test.Tool, null, test.Run, CancellationToken.None);
            }
            catch (UnauthorizedAccessException) when (test.Tool == "talvora_delete") { }
            catch (SourceEditDomainException) when (test.Tool == "talvora_delete") { }
            catch (AggregateException) when (test.Tool == "talvora_delete") { }
            catch (FileNotFoundException) when (test.Tool is "talvora_agent_brief" or "talvora_move") { }
            catch (InvalidOperationException) when (test.Tool == "talvora_git_run") { }
            catch (TimeoutException) when (test.Tool == "talvora_user_run") { }

            var timeout = Stopwatch.StartNew();
            DesktopProgressMessage[] frames;
            do
            {
                frames = observed.Where(message =>
                    message.WorklogFrame?.Current.State == WorklogStepState.Failed).ToArray();
                if (frames.Length > 0)
                    break;
                await Task.Delay(20);
            }
            while (timeout.Elapsed < TimeSpan.FromSeconds(4));

            if (frames.Length == 0)
                throw new InvalidOperationException($"D125 {test.Tool} did not publish a failed worklog frame.");
            if (!frames.All(f => f.WorklogFrame!.Current.Outcome == test.Expected))
            {
                throw new InvalidOperationException(
                    $"D125 {test.Tool}: expected {test.Expected}; actual: " +
                    string.Join(", ", frames.Select(f => f.WorklogFrame!.Current.Outcome?.ToString() ?? "null")));
            }
        }

        Console.WriteLine("D125_FAILURE_OUTCOMES_GREEN");
    }
}
