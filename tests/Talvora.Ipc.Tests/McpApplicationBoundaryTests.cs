using Talvora.Adapter.Mcp;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class McpApplicationBoundaryTests
{
    [TestMethod]
    public void McpAdapterDoesNotReferenceIpcClient()
    {
        var references = typeof(ShellTools)
            .Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .ToArray();

        Assert.IsFalse(
            references.Any(reference => string.Equals(reference, "Talvora.Ipc.Client", StringComparison.Ordinal)),
            $"MCP adapter must depend on the application execution abstraction, not the broker client directly. References: {string.Join(", ", references)}");
    }
}
