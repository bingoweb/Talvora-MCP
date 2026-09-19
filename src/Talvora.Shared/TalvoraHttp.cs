namespace Talvora.Shared;

public static class TalvoraHttp
{
    public static HttpClient CreateClient(
        bool ignoreTlsErrors = false,
        bool allowAutoRedirect = true,
        TimeSpan? timeout = null)
    {
        HttpClientHandler? handler = null;
        HttpClient? client = null;

        try
        {
            handler = new HttpClientHandler
            {
                AllowAutoRedirect = allowAutoRedirect,
            };

            if (ignoreTlsErrors)
            {
                handler.ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            }

            client = new HttpClient(handler, disposeHandler: true);
            handler = null; // Ownership transferred to HttpClient.
            client.Timeout = timeout ?? Timeout.InfiniteTimeSpan;
            return client;
        }
        catch
        {
            client?.Dispose();
            handler?.Dispose();
            throw;
        }
    }
}
