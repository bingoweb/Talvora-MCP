using System.IO;
using System.Text.Json;
using Talvora.Shared;

namespace Talvora.Tray;

internal sealed record TunnelProvisioningPendingRecord(
    int SchemaVersion,
    string RegistrationId,
    string RequestId,
    string ExpectedName,
    string ExpectedDescription,
    IReadOnlyList<string> OrganizationIds,
    IReadOnlyList<string> WorkspaceIds,
    string? TunnelId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? CreateAttemptedAtUtc,
    DateTimeOffset UpdatedAtUtc);

internal static partial class ManagedMcpTunnelProvisioningService
{
    private const int PendingProvisionSchemaVersion = 1;

    private static string PendingProvisionRoot =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Talvora",
            "ControlCenter",
            "pending-tunnel-provisions");

    private static string GetPendingProvisionPath(
        string registrationId) =>
        Path.Combine(
            PendingProvisionRoot,
            ManagedMcpIdentityKey.Create(registrationId) + ".json");

    private static TunnelProvisioningPendingRecord?
        ReadPendingProvision(
            string registrationId)
    {
        var path = GetPendingProvisionPath(registrationId);
        if (!File.Exists(path))
        {
            return null;
        }

        TunnelProvisioningPendingRecord pending;
        try
        {
            pending = JsonFileStore
                .ReadAsync<TunnelProvisioningPendingRecord>(
                    path,
                    ConfigJsonOptions,
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception ex) when (
            ex is IOException or
            JsonException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            throw new InvalidDataException(
                $"Pending tunnel provision journal could not be read safely: {path}",
                ex);
        }

        ValidatePendingProvision(
            pending,
            registrationId,
            path);
        return pending;
    }

    private static void ValidatePendingProvision(
        TunnelProvisioningPendingRecord pending,
        string registrationId,
        string path)
    {
        if (pending.SchemaVersion != PendingProvisionSchemaVersion ||
            !string.Equals(
                pending.RegistrationId,
                registrationId,
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(pending.RequestId) ||
            pending.RequestId.Length != 32 ||
            pending.RequestId.Any(character =>
                !Uri.IsHexDigit(character)) ||
            string.IsNullOrWhiteSpace(pending.ExpectedName) ||
            string.IsNullOrWhiteSpace(
                pending.ExpectedDescription) ||
            pending.OrganizationIds is null ||
            pending.WorkspaceIds is null ||
            pending.OrganizationIds.Any(
                string.IsNullOrWhiteSpace) ||
            pending.WorkspaceIds.Any(
                string.IsNullOrWhiteSpace) ||
            (pending.OrganizationIds.Count == 0 &&
             pending.WorkspaceIds.Count == 0) ||
            !pending.ExpectedDescription.Contains(
                $"Talvora request {pending.RequestId}.",
                StringComparison.Ordinal) ||
            (pending.TunnelId is not null &&
             !IsTunnelId(pending.TunnelId)))
        {
            throw new InvalidDataException(
                $"Pending tunnel provision journal is invalid: {path}");
        }
    }

    private static bool HasPendingRemoteTunnelId(
        string registrationId)
    {
        try
        {
            return IsTunnelId(
                ReadPendingProvision(
                    registrationId)?.TunnelId);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            TrayLog.Write(
                $"Pending tunnel journal could not contribute to provisioning assessment. MCP={registrationId}",
                ex);
            return false;
        }
    }

    private static TunnelProvisioningPendingRecord
        CreatePendingProvision(
            ManagedMcpRegistration registration,
            TunnelProvisioningScope scope)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;

        return new TunnelProvisioningPendingRecord(
            SchemaVersion: PendingProvisionSchemaVersion,
            RegistrationId: registration.Id,
            RequestId: requestId,
            ExpectedName:
                $"{registration.DisplayName} Tunnel",
            ExpectedDescription:
                $"Routes ChatGPT connector traffic to {registration.DisplayName}. Talvora request {requestId}.",
            OrganizationIds:
                scope.OrganizationIds
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            WorkspaceIds:
                scope.WorkspaceIds
                    .Distinct(
                        StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
            TunnelId: null,
            CreatedAtUtc: now,
            CreateAttemptedAtUtc: null,
            UpdatedAtUtc: now);
    }

    private static Task WritePendingProvisionAsync(
        TunnelProvisioningPendingRecord pending,
        CancellationToken cancellationToken) =>
        JsonFileStore.WriteAsync(
            GetPendingProvisionPath(
                pending.RegistrationId),
            pending,
            ConfigJsonOptions,
            createBackup: false,
            cancellationToken);

    private static void TryDeleteCommittedPendingProvision(
        ManagedMcpRegistration registration)
    {
        if (!IsTunnelId(registration.Tunnel?.TunnelId))
        {
            return;
        }

        try
        {
            var pending = ReadPendingProvision(
                registration.Id);
            if (pending is null ||
                !string.Equals(
                    pending.TunnelId,
                    registration.Tunnel!.TunnelId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Delete(
                GetPendingProvisionPath(
                    registration.Id));
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            TrayLog.Write(
                $"Committed tunnel pending journal cleanup deferred. MCP={registration.Id}",
                ex);
        }
    }

    private static async Task DeletePendingProvisionAfterCommitAsync(
        string registrationId,
        string tunnelId)
    {
        try
        {
            var pending = ReadPendingProvision(
                registrationId);
            if (pending is null ||
                !string.Equals(
                    pending.TunnelId,
                    tunnelId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            File.Delete(
                GetPendingProvisionPath(
                    registrationId));
            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is IOException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            TrayLog.Write(
                $"Committed tunnel pending journal cleanup deferred. MCP={registrationId}",
                ex);
        }
    }

    private static async Task<string>
        ResolveOrCreatePendingRemoteTunnelAsync(
            BusinessConfig clientConfig,
            ManagedMcpRegistration registration,
            TunnelProvisioningScope currentScope,
            string runtimeKey,
            string? adminKey,
            CancellationToken cancellationToken)
    {
        var pending = ReadPendingProvision(
            registration.Id);
        if (pending is null)
        {
            pending = CreatePendingProvision(
                registration,
                currentScope);
            await WritePendingProvisionAsync(
                pending,
                cancellationToken).ConfigureAwait(false);
        }

        if (IsTunnelId(pending.TunnelId))
        {
            await VerifyPendingRemoteTunnelAsync(
                clientConfig,
                pending,
                runtimeKey,
                cancellationToken).ConfigureAwait(false);
            return pending.TunnelId!;
        }

        if (pending.CreateAttemptedAtUtc is not null)
        {
            if (string.IsNullOrWhiteSpace(adminKey))
            {
                throw new InvalidOperationException(
                    "Belirsiz pending tünel create sonucunu reconcile etmek için OpenAI Admin API key gerekiyor.");
            }

            var reconciled = await FindPendingRemoteTunnelAsync(
                clientConfig,
                pending,
                adminKey!,
                cancellationToken).ConfigureAwait(false);
            if (IsTunnelId(reconciled))
            {
                pending = pending with
                {
                    TunnelId = reconciled,
                    UpdatedAtUtc = DateTimeOffset.UtcNow,
                };
                await WritePendingProvisionAsync(
                    pending,
                    CancellationToken.None).ConfigureAwait(false);
                return reconciled!;
            }

            throw new InvalidOperationException(
                $"Önceki tünel oluşturma denemesinin sonucu belirsiz. Otomatik ikinci create engellendi; pending journal korunuyor: {GetPendingProvisionPath(registration.Id)}");
        }

        if (string.IsNullOrWhiteSpace(adminKey))
        {
            throw new InvalidOperationException(
                "Yeni tünel oluşturmak için OpenAI Admin API key gerekiyor.");
        }

        pending = pending with
        {
            CreateAttemptedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await WritePendingProvisionAsync(
            pending,
            cancellationToken).ConfigureAwait(false);

        string tunnelId;
        try
        {
            tunnelId = await CreateRemoteTunnelAsync(
                clientConfig,
                pending.ExpectedName,
                pending.ExpectedDescription,
                new TunnelProvisioningScope(
                    pending.OrganizationIds,
                    pending.WorkspaceIds,
                    currentScope.ReferenceTunnelId,
                    currentScope.UpdatedAtUtc),
                adminKey!,
                cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            throw new InvalidOperationException(
                $"Tünel create isteğinin sonucu belirsiz; otomatik retry yapılmayacak. Pending journal korunuyor: {GetPendingProvisionPath(registration.Id)}",
                ex);
        }

        pending = pending with
        {
            TunnelId = tunnelId,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        };
        await WritePendingProvisionAsync(
            pending,
            CancellationToken.None).ConfigureAwait(false);
        return tunnelId;
    }

    private static async Task VerifyPendingRemoteTunnelAsync(
        BusinessConfig clientConfig,
        TunnelProvisioningPendingRecord pending,
        string runtimeKey,
        CancellationToken cancellationToken)
    {
        var result = await RunClientAsync(
            clientConfig.TunnelClient,
            clientConfig.StateRoot,
            [
                "admin",
                "--json",
                "tunnels",
                "get",
                pending.TunnelId!,
            ],
            runtimeKey,
            adminKey: null,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Pending tünel kaydı uzak control-plane üzerinde doğrulanamadı.");
        }

        using var document =
            ParseTunnelClientJson(
                result.StandardOutput);
        var actualId = FindFirstTunnelId(
            document.RootElement);
        if (!string.Equals(
                actualId,
                pending.TunnelId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Pending tünel kimliği uzak metadata ile eşleşmiyor.");
        }
    }

    private static async Task<string?>
        FindPendingRemoteTunnelAsync(
            BusinessConfig clientConfig,
            TunnelProvisioningPendingRecord pending,
            string adminKey,
            CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "admin",
            "--json",
            "tunnels",
            "list",
        };

        if (pending.WorkspaceIds.Count > 0)
        {
            arguments.Add("--workspace-id");
            arguments.Add(pending.WorkspaceIds[0]);
        }
        else
        {
            arguments.Add("--organization-id");
            arguments.Add(pending.OrganizationIds[0]);
        }

        var result = await RunClientAsync(
            clientConfig.TunnelClient,
            clientConfig.StateRoot,
            arguments,
            runtimeKey: null,
            adminKey,
            timeout: TimeSpan.FromSeconds(20),
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "Pending tünel create sonucu control-plane listesiyle reconcile edilemedi.");
        }

        using var document =
            ParseTunnelClientJson(
                result.StandardOutput);
        var matches = new List<string>();
        CollectPendingTunnelMatches(
            document.RootElement,
            pending,
            matches);
        var unique = matches
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return unique.Length switch
        {
            0 => null,
            1 => unique[0],
            _ => throw new InvalidOperationException(
                "Aynı pending provision request marker'ına sahip birden fazla uzak tünel bulundu; otomatik seçim engellendi."),
        };
    }

    private static void CollectPendingTunnelMatches(
        JsonElement element,
        TunnelProvisioningPendingRecord pending,
        ICollection<string> matches)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var name = TryGetStringProperty(
                element,
                "name");
            var description = TryGetStringProperty(
                element,
                "description");
            var requestMarker =
                $"Talvora request {pending.RequestId}.";
            if (string.Equals(
                    name,
                    pending.ExpectedName,
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(description) &&
                description.Contains(
                    requestMarker,
                    StringComparison.Ordinal))
            {
                var tunnelId = FindFirstTunnelId(
                    element);
                if (IsTunnelId(tunnelId))
                {
                    matches.Add(tunnelId!);
                }
            }

            foreach (var property in
                     element.EnumerateObject())
            {
                CollectPendingTunnelMatches(
                    property.Value,
                    pending,
                    matches);
            }
        }
        else if (element.ValueKind ==
                 JsonValueKind.Array)
        {
            foreach (var item in
                     element.EnumerateArray())
            {
                CollectPendingTunnelMatches(
                    item,
                    pending,
                    matches);
            }
        }
    }

    private static string? TryGetStringProperty(
        JsonElement element,
        string propertyName)
    {
        foreach (var property in
                 element.EnumerateObject())
        {
            if (property.Name.Equals(
                    propertyName,
                    StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind ==
                JsonValueKind.String)
            {
                return property.Value.GetString();
            }
        }

        return null;
    }

    private static JsonDocument ParseTunnelClientJson(
        string text)
    {
        var objectStart = text.IndexOf('{');
        var arrayStart = text.IndexOf('[');
        var start = objectStart < 0
            ? arrayStart
            : arrayStart < 0
                ? objectStart
                : Math.Min(
                    objectStart,
                    arrayStart);
        if (start < 0)
        {
            throw new InvalidDataException(
                "Tunnel-client response does not contain JSON.");
        }

        var opening = text[start];
        var closing = opening == '{'
            ? '}'
            : ']';
        var end = text.LastIndexOf(closing);
        if (end < start)
        {
            throw new InvalidDataException(
                "Tunnel-client JSON response is incomplete.");
        }

        return JsonDocument.Parse(
            text[start..(end + 1)]);
    }
}
