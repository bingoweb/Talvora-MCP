namespace Talvora.Abstractions;

public interface IPlatformInfoProvider
{
    ValueTask<PlatformInfo> GetAsync(CancellationToken cancellationToken = default);
}
