using Talvora.Shared;

internal static partial class SourceEditRegressionRunner
{
    /// <summary>
    /// Audit 2026-10-25 (18): text is often redacted twice (protocol, then a view). A second pass must leave an
    /// existing "[REDACTED]" alone instead of turning it into "[REDACTED]]", must still hide a real value
    /// that merely starts with the placeholder, and must hide a secret that contains "]" to its end.
    /// </summary>
    private static Task RedactionIsIdempotentAsync()
    {
        var bearer = "Authorization: Bearer " + "TalvoraIdempotentBearer0123456789";
        var named = "api_key=" + "TalvoraIdempotentKey0123456789" + "; password=\"" + "TalvoraIdempotentPassword" + "\"";
        foreach (var input in new[] { bearer, named, bearer + "; " + named })
        {
            var once = FileLog.RedactSensitiveData(input);
            var twice = FileLog.RedactSensitiveData(once);
            Assert(once.Contains("[REDACTED]", StringComparison.Ordinal) && !once.Contains("Idempotent", StringComparison.Ordinal),
                "Redaction must hide the fixture secrets: " + once);
            AssertEqual(once, twice, "A second redaction pass must not change already redacted text.");
            Assert(!twice.Contains("[REDACTED]]", StringComparison.Ordinal),
                "Redaction must never produce \"[REDACTED]]\": " + twice);
        }

        var disguised = FileLog.RedactSensitiveData("password=[REDACTED]" + "TalvoraDisguisedSecret0123");
        Assert(!disguised.Contains("TalvoraDisguisedSecret0123", StringComparison.Ordinal),
            "A value that only starts with the placeholder must still be redacted: " + disguised);
        var bracketed = FileLog.RedactSensitiveData("password=Ab]" + "c9TalvoraBracketTail");
        Assert(!bracketed.Contains("c9TalvoraBracketTail", StringComparison.Ordinal),
            "A secret containing \"]\" must be redacted to its end: " + bracketed);
        var structured = FileLog.RedactSensitiveData("[password=" + "TalvoraInBrackets" + "] done");
        Assert(!structured.Contains("TalvoraInBrackets", StringComparison.Ordinal) && structured.EndsWith("] done", StringComparison.Ordinal),
            "A closing bracket after the value must stay outside the redaction: " + structured);
        return Task.CompletedTask;
    }
}
