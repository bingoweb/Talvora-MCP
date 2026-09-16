using Talvora.Gateway.Tools;
using Xunit;

namespace Talvora.Foundation.Tests;

public sealed class ProcessToolTests
{
    [Fact]
    public async Task Run_process_executes_dotnet_and_captures_output()
    {
        var result = await ProcessTools.RunProcess("dotnet", ["--version"], timeoutSeconds: 30);

        Assert.False(result.TimedOut);
        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StandardOutput));
    }
}
