using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;

namespace Talvora.Tools;

/// <summary>Read-only Windows PnP inventory. No device or driver state is changed.</summary>
[McpServerToolType]
public static class DeviceInspectionTools
{
    [McpServerTool(Name = "talvora_device_inspect", ReadOnly = true, Destructive = false,
        Idempotent = true, OpenWorld = false, UseStructuredContent = true,
        OutputSchemaType = typeof(TalvoraPowerShellResult)),
     Description("Inspect connected Windows PnP devices by name, USB VID/PID or instance ID. Returns device identities, hardware IDs, driver details, errors and connection paths without changing settings. Pass e.g. 05E3 or microscope. Empty query lists cameras and imaging devices.")]
    public static Task<TalvoraPowerShellResult> Inspect(
        string query = "", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Length > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(query), "Query is limited to 256 characters.");
        }

        // Base64 data is used only as a literal filter value, never executed as code.
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(query));
        var script = $$"""
            $ErrorActionPreference = 'Stop'
            $query = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{{encoded}}'))
            $devices = @(Get-PnpDevice -PresentOnly | Where-Object {
                if ($query.Length -eq 0) { $_.Class -in @('Camera', 'Image') }
                else { ([string]$_.FriendlyName).Contains($query, [StringComparison]::OrdinalIgnoreCase) -or
                    $_.InstanceId.Contains($query, [StringComparison]::OrdinalIgnoreCase) }
            } | Select-Object -First 80)
            $results = foreach ($device in $devices) {
                $props = @{}
                foreach ($p in (Get-PnpDeviceProperty -InstanceId $device.InstanceId -ErrorAction SilentlyContinue)) {
                    if ($p.KeyName -match 'HardwareIds|CompatibleIds|BusReportedDeviceDesc|DriverProvider|DriverVersion|DriverDate|DriverInfPath|DriverDesc|Parent$|Children$|LocationPaths|ProblemCode|Service$') {
                        $props[$p.KeyName] = @($p.Data | ForEach-Object { [string]$_ })
                    }
                }
                [pscustomobject]@{name=$device.FriendlyName; class=$device.Class;
                    status=$device.Status; instanceId=$device.InstanceId; properties=$props}
            }
            ConvertTo-Json -InputObject @($results) -Depth 8 -Compress
            """;
        return PowerShellTools.RunPowerShell(script, timeoutSeconds: 35,
            maxCapturedCharactersPerStream: 128 * 1024,
            cancellationToken: cancellationToken);
    }
}
