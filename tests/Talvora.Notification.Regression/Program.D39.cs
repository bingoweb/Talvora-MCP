using Talvora.Tools;

internal static partial class Program
{
    /// <summary>D39: Penpot error text goes through the shared secret masking too.</summary>
    private static void AssertD39PenpotRedaction()
    {
        var text = PenpotMcpTools.RedactSensitiveText(
            "Penpot failed: userToken=abc123 Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIn0.c2lnbmF0dXJl");
        if (text.Contains("abc123", StringComparison.Ordinal) ||
            text.Contains("c2lnbmF0dXJl", StringComparison.Ordinal))
            throw new InvalidOperationException("D39: Penpot error text still shows a secret.");
        Console.WriteLine("D39_PENPOT_REDACTION_GREEN");
    }
}
