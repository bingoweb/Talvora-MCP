using System.Collections.Concurrent;
using System.Text.Json;
using Talvora.Shared;

internal static partial class Program
{
    private static async Task AssertD95CommandErrorLinesAsync()
    {
        const string powerShellColored =
            "\u001B[31;1mException: \u001B[0mC:\\repo\\tests\\Throw-Test.ps1:3\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1mLine |\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m   3 | \u001B[0m \u001B[36;1mthrow 'D95 RED: örnek hata'\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m     | \u001B[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mD95 RED: örnek hata\u001B[0m\r\n";
        const string powerShellPlain =
            "Exception: C:\\repo\\tests\\Throw-Test.ps1:3\r\n" +
            "Line |\r\n" +
            "   3 |  throw 'D95 RED: örnek hata'\r\n" +
            "     |  ~~~~~~~~~~~~~~~~~~~~~~~~~~~\r\n" +
            "     | D95 RED: örnek hata\r\n";
        const string powerShellLong =
            "\u001B[31;1mException: \u001B[0mC:\\repo\\tests\\Long.ps1:1\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1mLine |\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m   1 | \u001B[0m \u001B[36;1mthrow \"Bu uzun hata iletisi bilerek yazildi: derleme adiminda ikinci \u001B[0m \u0007\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m     | \u001B[31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mBu uzun hata iletisi bilerek yazildi: derleme adiminda ikinci proje bulunamadi ve kurulum paketi olusturulamadi;\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mlutfen proje yolunu, surum numarasini ve calisma klasorunu yeniden kontrol edin, ardindan komutu tekrar\u001B[0m\r\n" +
            "\u001B[31;1m\u001B[0m\u001B[36;1m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[0m\u001B[36;1m\u001B[31;1m\u001B[31;1m\u001B[36;1m\u001B[31;1m\u001B[36;1m\u001B[31;1m\u001B[36;1m     | \u001B[31;1mcalistirin.\u001B[0m\r\n";
        const string pythonUnittest =
            "test_a_ok (__main__.T.test_a_ok) ... ok\n" +
            "test_b_stock (__main__.T.test_b_stock) ... FAIL\n\n" +
            "======================================================================\n" +
            "FAIL: test_b_stock (__main__.T.test_b_stock)\n" +
            "----------------------------------------------------------------------\n" +
            "Traceback (most recent call last):\n" +
            "  File \"<string>\", line 4, in test_b_stock\n" +
            "AssertionError: -1 != 0\n\n" +
            "----------------------------------------------------------------------\n" +
            "Ran 2 tests in 0.001s\n\n" +
            "FAILED (failures=1)\n";
        const string compilerOutput =
            "   Compiling demo v0.1.0 (C:\\repo)\n" +
            "error[E0425]: cannot find value `x` in this scope\n" +
            "error: could not compile `demo` (bin \"demo\") due to 1 previous error\n";

        var cases = new (string Name, string StandardError, string ExpectedResult)[]
        {
            ("PowerShell ANSI", powerShellColored, "Sonuç: D95 RED: örnek hata"),
            ("PowerShell plain", powerShellPlain, "Sonuç: D95 RED: örnek hata"),
            ("PowerShell wrapped ANSI", powerShellLong,
                "Sonuç: Bu uzun hata iletisi bilerek yazildi: derleme adiminda ikinci proje bulunamadi ve kurulum paketi olusturulamadi; lutfen proje yolunu, surum numarasini ve calisma klasorunu yeniden kontrol edin, ardindan komutu tekrar calistirin."),
            ("Python unittest", pythonUnittest,
                "Sonuç: test_b_stock (__main__.T.test_b_stock) ... FAIL"),
            ("Python then PowerShell", pythonUnittest + powerShellColored,
                "Sonuç: D95 RED: örnek hata"),
            ("compiler progress", compilerOutput,
                "Sonuç: error[E0425]: cannot find value `x` in this scope"),
            ("unclassified progress", "step one\nstep two\n", "Sonuç: step two"),
        };

        foreach (var (name, standardError, expectedResult) in cases)
        {
            var recorded = new ConcurrentQueue<DesktopProgressMessage>();
            await using var notifier = CreateNotifier(recorded);
            var arguments = new Dictionary<string, JsonElement>
            {
                ["executable"] = JsonSerializer.SerializeToElement("pwsh.exe"),
                ["arguments"] = JsonSerializer.SerializeToElement(
                    new[] { "-NoProfile", "-File", "tests/Throw-Test.ps1" }),
            };
            await notifier.RunToolCallAsync<object>(
                "talvora_user_run", arguments,
                _ => ValueTask.FromResult<object>(
                    new { exitCode = 1, standardOutput = "", standardError }),
                CancellationToken.None, "/mcp/dev", "ChatGPT");

            var ended = recorded.LastOrDefault(m => m.Kind == DesktopProgressKind.Failed);
            if (ended?.Title != "Test betiği başarısız · Throw-Test.ps1" ||
                ended.Evidence?.Result != expectedResult ||
                ended.Evidence.Summary is null ||
                ended.Evidence.Result.Contains('\u001B') ||
                ended.Evidence.Summary.Contains('\u001B'))
            {
                throw new InvalidOperationException(
                    $"D95 RED {name}: expected '{expectedResult}', got " +
                    $"'{ended?.Evidence?.Result}', title '{ended?.Title}', " +
                    $"summary '{ended?.Evidence?.Summary}'.");
            }
        }

        Console.WriteLine("D95_COMMAND_FAILURE_EVIDENCE_GREEN");
    }
}
