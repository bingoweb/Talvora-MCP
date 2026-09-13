using Talvora.Core;

namespace Talvora.Core.Tests;

[TestClass]
public sealed class OperationExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsyncReturnsValueWhenOperationSucceeds()
    {
        var executor = new OperationExecutor(new DefaultErrorMapper());

        var result = await executor.ExecuteAsync(
            "test.success",
            _ => ValueTask.FromResult("ok"));

        Assert.IsTrue(result.IsSuccess);
        Assert.AreEqual("ok", result.Value);
        Assert.IsNull(result.Error);
    }

    [TestMethod]
    public async Task ExecuteAsyncNormalizesUnauthorizedAccessErrors()
    {
        var executor = new OperationExecutor(new DefaultErrorMapper());

        var result = await executor.ExecuteAsync<string>(
            "filesystem.read",
            _ => throw new UnauthorizedAccessException("denied"));

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual("access_denied", result.Error.Code);
        Assert.AreEqual("filesystem.read", result.Error.Operation);
    }

    [TestMethod]
    public async Task ExecuteAsyncNormalizesCancellation()
    {
        var executor = new OperationExecutor(new DefaultErrorMapper());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await executor.ExecuteAsync<string>(
            "shell.execute",
            cancellationToken => ValueTask.FromCanceled<string>(cancellationToken),
            cts.Token);

        Assert.IsFalse(result.IsSuccess);
        Assert.IsNotNull(result.Error);
        Assert.AreEqual("operation_cancelled", result.Error.Code);
    }
}
