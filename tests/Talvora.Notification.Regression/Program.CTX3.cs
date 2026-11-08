using System.Net;
using System.Net.Http;
using Talvora;
using Talvora.Tools;

internal static partial class Program
{
    private static async Task AssertCtx3Context7KeyAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-ctx3-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            const string key = "ctx7sk-fixture_0123456789abcdef";
            var keyFile = Path.Combine(root, "api-key");
            if (Context7Tools.ReadApiKey(keyFile) is not null)
                throw new InvalidOperationException("CTX3: a missing key file means no key.");
            await File.WriteAllTextAsync(keyFile, "﻿  " + key + "  \r\n");
            if (Context7Tools.ReadApiKey(keyFile) != key)
                throw new InvalidOperationException("CTX3: the key file is read without BOM, spaces and line break.");
            await File.WriteAllTextAsync(keyFile, "not a context7 key");
            if (Context7Tools.ReadApiKey(keyFile) is not null)
                throw new InvalidOperationException("CTX3: text that is not a Context7 key is ignored.");
            await File.WriteAllTextAsync(keyFile, key + new string('x', 600));
            if (Context7Tools.ReadApiKey(keyFile) is not null)
                throw new InvalidOperationException("CTX3: an oversized key file is ignored.");
            if (!Context7Tools.ApiKeyPath.EndsWith(@"\Talvora\Context7\api-key", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("CTX3: the service reads its key from ProgramData\\Talvora\\Context7\\api-key.");

            using var handler = new Context7KeyHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            await using var ledger = new Context7Ledger(root);
            const string query = "HttpRequestMessage Authorization header Bearer scheme";
            var result = await Context7Tools.FetchAsync("/dotnet/docs", query, client, ledger, "ChatGPT", key);
            if (handler.Authorization != "Bearer " + key || handler.UserAgent != "Talvora" ||
                handler.HeaderCount != 2 || result.Text != "Context7 fixture")
                throw new InvalidOperationException("CTX3: a lookup with a key sends User-Agent: Talvora and Authorization: Bearer <key>.");

            handler.Status = HttpStatusCode.Unauthorized;
            var refused = await Ctx3ToolErrorAsync(() =>
                Context7Tools.FetchAsync("/dotnet/docs", query, client, ledger, "ChatGPT", key));
            if (!refused.Contains("refused the API key", StringComparison.Ordinal) ||
                !refused.Contains(Context7Tools.ApiKeyPath, StringComparison.Ordinal) ||
                refused.Contains(key, StringComparison.Ordinal))
                throw new InvalidOperationException("CTX3: a refused key is named by its file, never by its value.");

            handler.Status = HttpStatusCode.TooManyRequests;
            var quota = await Ctx3ToolErrorAsync(() =>
                Context7Tools.FetchAsync("/dotnet/docs", query, client, ledger, "ChatGPT", null));
            if (!quota.StartsWith("Context7 HTTP 429", StringComparison.Ordinal) ||
                !quota.Contains(Context7Tools.ApiKeyPath, StringComparison.Ordinal) ||
                handler.Authorization is not null || handler.HeaderCount != 1)
                throw new InvalidOperationException("CTX3: an anonymous 429 sends no key and names the quota and the key file.");

            await ledger.FlushAsync();
            var records = ledger.ReadSince(DateTimeOffset.UtcNow.AddMinutes(-2));
            if (records.Count != 3 || records[0].Status != 200 || records[1].Status != 401 || records[2].Status != 429)
                throw new InvalidOperationException("CTX3: every keyed or anonymous lookup is recorded with its status.");
            foreach (var file in Directory.EnumerateFiles(root, "lookups-*.jsonl"))
            {
                if ((await File.ReadAllTextAsync(file)).Contains(key, StringComparison.Ordinal))
                    throw new InvalidOperationException("CTX3: the ledger never stores the key.");
            }
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Console.WriteLine("CTX3_CONTEXT7_KEY_GREEN");
    }

    private static async Task<string> Ctx3ToolErrorAsync(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
        throw new InvalidOperationException("CTX3: the lookup must fail with a tool error.");
    }

    private sealed class Context7KeyHandler : HttpMessageHandler
    {
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        internal string? Authorization { get; private set; }
        internal string? UserAgent { get; private set; }
        internal int HeaderCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            UserAgent = request.Headers.UserAgent.ToString();
            HeaderCount = request.Headers.Count();
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("Context7 fixture"),
            });
        }
    }
}
