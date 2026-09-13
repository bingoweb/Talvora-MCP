using System.Runtime.InteropServices;
using Talvora.Abstractions;

namespace Talvora.Platform.Windows;

public sealed class WindowsPlatformInfoProvider : IPlatformInfoProvider
{
    public ValueTask<PlatformInfo> GetAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var info = new PlatformInfo(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.OSArchitecture.ToString(),
            Environment.MachineName,
            Environment.UserName,
            Environment.IsPrivilegedProcess,
            TimeSpan.FromMilliseconds(Environment.TickCount64));

        return ValueTask.FromResult(info);
    }
}
