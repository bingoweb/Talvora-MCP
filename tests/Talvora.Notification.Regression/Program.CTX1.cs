using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Talvora;
using Talvora.Tools;

internal static partial class Program
{
    private static async Task AssertCtx1Context7Async()
    {
        var root = Path.Combine(Path.GetTempPath(), "talvora-ctx1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldFile = Path.Combine(root, $"lookups-{DateTime.UtcNow.AddDays(-15):yyyy-MM-dd}.jsonl");
        await File.WriteAllTextAsync(oldFile, "old\n");
        try
        {
            using var handler = new Context7FakeHandler();
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            await using var ledger = new Context7Ledger(root);
            const string query = "Enumerable.MaxBy key selector example";
            var result = await Context7Tools.FetchAsync("/dotnet/docs", query, client, ledger, "ChatGPT");
            await ledger.FlushAsync();
            var expected = "https://context7.com/api/v2/context?libraryId=%2Fdotnet%2Fdocs&query=Enumerable.MaxBy%20key%20selector%20example&type=txt";
            if (handler.Uris.Count != 1 || handler.Uris[0] != expected ||
                result.LibraryId != "/dotnet/docs" || result.Query != query ||
                result.Text != "Context7 fixture" || result.Bytes != Encoding.UTF8.GetByteCount(result.Text))
                throw new InvalidOperationException("CTX1: one escaped Context7 GET must return the body and its size.");
            var lines = await File.ReadAllLinesAsync(Path.Combine(root, $"lookups-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"));
            using (var doc = JsonDocument.Parse(lines.Single()))
            {
                var json = doc.RootElement;
                var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                    Encoding.UTF8.GetBytes(result.Text)))[..16].ToLowerInvariant();
                if (json.EnumerateObject().Count() != 7 ||
                    !json.GetProperty("ts").GetString()!.EndsWith('Z') ||
                    json.GetProperty("caller").GetString() != "ChatGPT" ||
                    json.GetProperty("libraryId").GetString() != "/dotnet/docs" ||
                    json.GetProperty("query").GetString() != query ||
                    json.GetProperty("status").GetInt32() != 200 ||
                    json.GetProperty("bytes").GetInt32() != result.Bytes ||
                    json.GetProperty("sha").GetString() != sha)
                    throw new InvalidOperationException("CTX1: the seven ledger fields must describe the exact response.");
            }
            handler.Status = HttpStatusCode.NotFound;
            try
            {
                await Context7Tools.FetchAsync("/talvora-smoke/does-not-exist", query, client, ledger, "ChatGPT");
                throw new InvalidOperationException("CTX1: a 404 must be a tool error.");
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Context7 does not know /talvora-smoke/does-not-exist"))
            {
            }
            await ledger.FlushAsync();
            var records = ledger.ReadSince(DateTimeOffset.UtcNow.AddMinutes(-2));
            if (records.Count != 2 || records[1].Status != 404)
                throw new InvalidOperationException("CTX1: a 404 must be recorded with status 404.");
            foreach (var (id, question) in new[] { ("dotnet/docs", query), ("/dotnet/docs", "docs") })
            {
                try
                {
                    await Context7Tools.FetchAsync(id, question, client, ledger, "ChatGPT");
                    throw new InvalidOperationException("CTX1: invalid arguments must fail.");
                }
                catch (ArgumentException)
                {
                }
            }
            if (handler.Uris.Count != 2)
                throw new InvalidOperationException("CTX1: invalid arguments must send no requests.");
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (File.Exists(oldFile) && DateTime.UtcNow < deadline)
                await Task.Delay(40);
            if (File.Exists(oldFile))
                throw new InvalidOperationException("CTX1: 15-day-old ledger files must be deleted.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        Console.WriteLine("CTX1_CONTEXT7_LOOKUP_GREEN");
    }

    private sealed class Context7FakeHandler : HttpMessageHandler
    {
        internal List<string> Uris { get; } = new();
        internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uris.Add(request.RequestUri!.AbsoluteUri);
            if (request.Method != HttpMethod.Get ||
                !request.Headers.UserAgent.ToString().Equals("Talvora", StringComparison.Ordinal) ||
                request.Headers.Count() != 1)
                throw new InvalidOperationException("CTX1: only a GET with User-Agent: Talvora is allowed.");
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("Context7 fixture"),
            });
        }
    }
}
