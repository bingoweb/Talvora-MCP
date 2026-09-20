using Microsoft.Build.Locator;

namespace Talvora.SourceEditing;

internal sealed record RoslynMsBuildRegistration(
    string Version,
    string MsBuildPath);

internal static class RoslynMsBuildBootstrap
{
    private static readonly object Gate = new();
    private static RoslynMsBuildRegistration? registration;

    public static RoslynMsBuildRegistration EnsureRegistered(
        string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            workingDirectory);
        var canonicalWorkingDirectory =
            Path.GetFullPath(
                workingDirectory);

        lock (Gate)
        {
            if (registration is not null)
            {
                return registration;
            }

            if (MSBuildLocator.IsRegistered)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.SemanticMsBuildUnavailable,
                    $"{SourceEditCodes.SemanticMsBuildUnavailable}: MSBuildLocator was registered by another component before Talvora could select and record a deterministic instance.");
            }

            VisualStudioInstance[] instances;
            try
            {
                instances =
                    MSBuildLocator
                        .QueryVisualStudioInstances(
                            new VisualStudioInstanceQueryOptions
                            {
                                DiscoveryTypes =
                                    DiscoveryType.DotNetSdk |
                                    DiscoveryType.VisualStudioSetup |
                                    DiscoveryType.DeveloperConsole,
                                WorkingDirectory =
                                    canonicalWorkingDirectory,
                            })
                        .OrderByDescending(instance => instance.Version)
                        .ThenBy(
                            instance => instance.MSBuildPath,
                            StringComparer.OrdinalIgnoreCase)
                        .ToArray();
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                    IOException or
                    UnauthorizedAccessException)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.SemanticMsBuildUnavailable,
                    $"{SourceEditCodes.SemanticMsBuildUnavailable}: compatible MSBuild discovery failed.",
                    innerException: ex);
            }

            if (instances.Length == 0)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.SemanticMsBuildUnavailable,
                    $"{SourceEditCodes.SemanticMsBuildUnavailable}: no compatible MSBuild instance was discovered for workspace '{canonicalWorkingDirectory}'. Check global.json SDK selection/roll-forward and installed SDK/MSBuild versions.",
                    canonicalWorkingDirectory);
            }

            var selected = instances[0];
            try
            {
                MSBuildLocator.RegisterInstance(selected);
            }
            catch (Exception ex) when (
                ex is InvalidOperationException or
                    IOException or
                    UnauthorizedAccessException)
            {
                throw new SourceEditDomainException(
                    SourceEditCodes.SemanticMsBuildUnavailable,
                    $"{SourceEditCodes.SemanticMsBuildUnavailable}: MSBuild instance registration failed.",
                    selected.MSBuildPath,
                    innerException: ex);
            }

            registration =
                new RoslynMsBuildRegistration(
                    selected.Version.ToString(),
                    selected.MSBuildPath);
            return registration;
        }
    }
}
