using System.Text;
using Talvora.Shared;
using Talvora.SourceEditing;
using Talvora.Tools;

internal static partial class SourceEditRegressionRunner
{
    // W1: console output is read as UTF-8 when it is valid UTF-8, otherwise through the OEM code page.
    public static Task RunConsoleOutputEncodingAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var console = ConsoleOutputEncoding.Create(857);
        AssertEqual("arşiv düz ğİıÇÖÜ", console.GetString(Encoding.GetEncoding(857).GetBytes("arşiv düz ğİıÇÖÜ")),
            "W1: OEM 857 console output must read as Turkish.");
        AssertEqual("Proje dosyası yok", console.GetString(Encoding.UTF8.GetBytes("Proje dosyası yok")),
            "W1: UTF-8 console output must stay UTF-8.");
        var mixed = Encoding.GetEncoding(857).GetBytes("dir: arşiv\r\n").Concat(Encoding.UTF8.GetBytes("utf8: ğüş\r\n")).ToArray();
        using var reader = new StreamReader(new MemoryStream(mixed), console, detectEncodingFromByteOrderMarks: false, bufferSize: 1);
        AssertEqual("dir: arşiv\r\nutf8: ğüş\r\n", reader.ReadToEnd(), "W1: streamed mixed output must decode per byte run.");
        return Task.CompletedTask;
    }
}
