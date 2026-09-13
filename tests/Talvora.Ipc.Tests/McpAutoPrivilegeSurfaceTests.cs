using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Modules.Processes;
using Talvora.Modules.Shell;

namespace Talvora.Ipc.Tests;

[TestClass]
public sealed class McpAutoPrivilegeSurfaceTests
{
    [TestMethod]
    public async Task RunShellDefaultsToAutoAndUsesAdministratorOverride()
    {
        var router = new RecordingExecutionRouter();
        var tools = new ShellTools(router);
        var method = typeof(ShellTools).GetMethod(nameof(ShellTools.RunShell));
        Assert.IsNotNull(method);

        var parameters = method.GetParameters();
        Assert.IsNull(
            parameters.SingleOrDefault(parameter => string.Equals(parameter.Name, "elevated", StringComparison.Ordinal)),
            "The MCP surface must expose Windows administrator intent, not an internal elevation switch.");

        var administratorParameter = parameters.SingleOrDefault(parameter =>
            string.Equals(parameter.Name, "runAsAdministrator", StringComparison.Ordinal) &&
            parameter.ParameterType == typeof(bool));
        Assert.IsNotNull(administratorParameter);
        Assert.AreEqual(false, administratorParameter.DefaultValue);

        var automaticInvocation = method.Invoke(tools, CreateArguments(parameters, runAsAdministrator: false));
        var automaticTask = automaticInvocation as Task<ToolEnvelope<ShellExecutionResult>>;
        Assert.IsNotNull(automaticTask);
        var automaticEnvelope = await automaticTask;
        Assert.IsTrue(automaticEnvelope.Ok, automaticEnvelope.Error?.Message);
        Assert.AreEqual(ExecutionPrivilege.Auto, router.LastShellPrivilege);

        var administratorInvocation = method.Invoke(tools, CreateArguments(parameters, runAsAdministrator: true));
        var administratorTask = administratorInvocation as Task<ToolEnvelope<ShellExecutionResult>>;
        Assert.IsNotNull(administratorTask);
        var administratorEnvelope = await administratorTask;
        Assert.IsTrue(administratorEnvelope.Ok, administratorEnvelope.Error?.Message);
        Assert.AreEqual(ExecutionPrivilege.Elevated, router.LastShellPrivilege);
    }

    private static object?[] CreateArguments(
        System.Reflection.ParameterInfo[] parameters,
        bool runAsAdministrator) =>
        parameters.Select(parameter => parameter.Name switch
        {
            "command" => (object?)"echo privilege-surface",
            "shell" => ShellKind.Cmd,
            "workingDirectory" => null,
            "loadProfile" => false,
            "runAsAdministrator" => runAsAdministrator,
            "cancellationToken" => CancellationToken.None,
            _ => throw new InvalidOperationException($"Unexpected RunShell parameter '{parameter.Name}'."),
        }).ToArray();

    private sealed class RecordingExecutionRouter : IExecutionRouter
    {
        public ExecutionPrivilege? LastShellPrivilege { get; private set; }

        public ValueTask<TalvoraResult<ShellExecutionResult>> ExecuteShellAsync(
            ShellExecutionRequest request,
            ExecutionPrivilege privilege,
            CancellationToken cancellationToken = default)
        {
            LastShellPrivilege = privilege;
            return ValueTask.FromResult(TalvoraResult.Success(new ShellExecutionResult(
                7001,
                0,
                "ok",
                string.Empty,
                TimeSpan.Zero)));
        }

        public ValueTask<TalvoraResult<ProcessStartResult>> StartProcessAsync(
            StartProcessRequest request,
            ExecutionPrivilege privilege,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
