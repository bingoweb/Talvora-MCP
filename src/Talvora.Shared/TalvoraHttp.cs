namespace Talvora.Shared;

public static class TalvoraHttp
{
    public static HttpClient CreateClient(
        bool ignoreTlsErrors = false,
        bool allowAutoRedirect = true,
        TimeSpan? timeout = null)
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = allowAutoRedirect,
        };

        if (ignoreTlsErrors)
        {
            handler.ServerCertificateCustomValidationCallback =
                HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        return new HttpClient(handler)
        {
            Timeout = timeout ?? Timeout.InfiniteTimeSpan,
        };
    }
}
