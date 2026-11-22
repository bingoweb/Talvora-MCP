#nullable enable
using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Talvora.Installer;

internal static partial class InstallerEngine
{
    private const string MachineEnvironmentRegistryPath =
        @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    private const uint WmSettingChange = 0x001A;
    private const uint SmtoAbortIfHung = 0x0002;

    internal static bool AddMachinePathEntry(string entry)
    {
        using var key = Registry.LocalMachine.OpenSubKey(MachineEnvironmentRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Machine environment registry key is missing.");
        var rawPath = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string ?? "";
        var updated = AppendPathEntry(rawPath, entry);
        if (updated is null)
            return false;
        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        BroadcastMachineEnvironmentChange();
        return true;
    }

    internal static void RemoveMachinePathEntry(string entry)
    {
        using var key = Registry.LocalMachine.OpenSubKey(MachineEnvironmentRegistryPath, writable: true)
            ?? throw new InvalidOperationException("Machine environment registry key is missing.");
        var rawPath = key.GetValue("Path", "", RegistryValueOptions.DoNotExpandEnvironmentNames)
            as string ?? "";
        var updated = RemovePathEntry(rawPath, entry);
        if (updated is null)
            return;
        key.SetValue("Path", updated, RegistryValueKind.ExpandString);
        BroadcastMachineEnvironmentChange();
    }

    private static void BroadcastMachineEnvironmentChange()
    {
        if (SendMessageTimeoutW((nint)0xFFFF, WmSettingChange, 0, "Environment",
                SmtoAbortIfHung, 5000, out _) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial nint SendMessageTimeoutW(
        nint hWnd, uint message, nint wParam, string lParam,
        uint flags, uint timeoutMilliseconds, out nint result);
}
