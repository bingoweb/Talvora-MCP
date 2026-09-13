using Talvora.Abstractions;
using Talvora.Adapter.Mcp;
using Talvora.Application;
using Talvora.Core;
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

        var automaticInvocation = method.Invoke(tools, CreateShellArguments(parameters, runAsAdministrator: false));
        var automaticTask = automaticInvocation as Task<ToolEnvelope<ShellExecutionResult>>;
        Assert.IsNotNull(automaticTask);
        var automaticEnvelope = await automaticTask;
        Assert.IsTrue(automaticEnvelope.Ok, automaticEnvelope.Error?.Message);
        Assert.AreEqual(ExecutionPrivilege.Auto, router.LastShellPrivilege);

        var administratorInvocation = method.Invoke(tools, CreateShellArguments(parameters, runAsAdministrator: true));
        var administratorTask = administratorInvocation as Task<ToolEnvelope<ShellExecutionResult>>;
        Assert.IsNotNull(administratorTask);
        var administratorEnvelope = await administratorTask;
        Assert.IsTrue(administratorEnvelope.Ok, administratorEnvelope.Error?.Message);
        Assert.AreEqual(ExecutionPrivilege.Elevated, router.LastShellPrivilege);
    }

    [TestMethod]
    public async Task StartProcessDefaultsToAutoAndUsesAdministratorOverride()
    {
        var router = new RecordingExecutionRouter();
        var tools = new ProcessTools(
            new UnusedProcessService(),
            new OperationExecutor(new DefaultErrorMapper()),
            router);
        var method = typeof(ProcessTools).GetMethod(nameof(ProcessTools.StartProcess));
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

        var automaticInvocation = method.Invoke(tools, CreateProcessArguments(parameters, runAsAdministrator: false));
        var automaticTask = automaticInvocation as Task<ToolEnvelope<ProcessStartResult>>;
        Assert.IsNotNull(automaticTask);
        var automaticEnvelope = await automaticTask;
        Assert.IsTrue(automaticEnvelope.Ok, automaticEnvelope.Error?.Message);
        Assert.AreEqual(ExecutionPrivilege.Auto, router.LastProcessPrivilege);

        var administratorInvocation = method.Invoke(tools, CreateProcessArguments(parameters, runAsAdministrator: true));
        var administratorTask = administratorInvocation as Task<ToolEnvelope<ProcessStartResult>>;
        Assert.IsNotNull(administratorTask);
        var administratorEnvelope = await administratorTask;
        Assert.IsTrue(administratorEnvelope.Ok, administratorEnvelope.Error?.Message);
        Assert.AreEqual(ExecutionPrivilege.Elevated, router.LastProcessPrivilege);
    }

    private static object?[] CreateShellArguments(
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

    private static object?[] CreateProcessArguments(
        System.Reflection.ParameterInfo[] parameters,
        bool runAsAdministrator) =>
        parameters.Select(parameter => parameter.Name switch
        {
            "fileName" => (object?)"notepad.exe",
            "arguments" => null,
            "workingDirectory" => null,
            "runAsAdministrator" => runAsAdministrator,
            "cancellationToken" => CancellationToken.None,
            _ => throw new InvalidOperationException($"Unexpected StartProcess parameter '{parameter.Name}'."),
        }).ToArray();

    private sealed class RecordingExecutionRouter : IExecutionRouter
    {
        public ExecutionPrivilege? LastShellPrivilege { get; private set; }

        public ExecutionPrivilege? LastProcessPrivilege { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            LastProcessPrivilege = privilege;
            return ValueTask.FromResult(TalvoraResult.Success(new ProcessStartResult(7002)));
        }
    }

    private sealed class UnusedProcessService : IProcessService
    {
        public ValueTask<IReadOnlyList<ProcessSnapshot>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<ProcessStartResult> StartAsync(
            StartProcessRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask StopAsync(
            int processId,
            bool entireProcessTree = true,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
