using System.ComponentModel;
using Microsoft.Win32;
using Talvora.Modules.Registry;

namespace Talvora.Platform.Windows;

public sealed class WindowsRegistryService : IRegistryService
{
    public ValueTask<RegistryValueData> ReadValueAsync(
        RegistryHiveId hive,
        string subKeyPath,
        string? valueName = null,
        RegistryViewId view = RegistryViewId.Default,
        bool expandEnvironmentStrings = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);

        using var baseKey = RegistryKey.OpenBaseKey(MapHive(hive), MapView(view));
        using var key = baseKey.OpenSubKey(subKeyPath, writable: false)
            ?? throw new KeyNotFoundException($"Registry key was not found: {hive}\\{subKeyPath}");

        var kind = key.GetValueKind(valueName);
        var options = expandEnvironmentStrings
            ? RegistryValueOptions.None
            : RegistryValueOptions.DoNotExpandEnvironmentNames;
        var rawValue = key.GetValue(valueName, defaultValue: null, options);

        var value = kind switch
        {
            RegistryValueKind.String => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.Text, StringValue: (string?)rawValue),
            RegistryValueKind.ExpandString => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.ExpandableText, StringValue: (string?)rawValue),
            RegistryValueKind.Binary => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.Binary, BinaryValue: (byte[]?)rawValue),
            RegistryValueKind.DWord => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.DWord, DWordValue: (int?)rawValue),
            RegistryValueKind.MultiString => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.MultiText, MultiStringValue: (string[]?)rawValue),
            RegistryValueKind.QWord => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.QWord, QWordValue: (long?)rawValue),
            RegistryValueKind.None => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.None, BinaryValue: rawValue as byte[]),
            _ => new RegistryValueData(
                hive, subKeyPath, valueName, RegistryValueType.Unknown, StringValue: rawValue?.ToString()),
        };

        return ValueTask.FromResult(value);
    }

    private static RegistryHive MapHive(RegistryHiveId hive) => hive switch
    {
        RegistryHiveId.ClassesRoot => RegistryHive.ClassesRoot,
        RegistryHiveId.CurrentUser => RegistryHive.CurrentUser,
        RegistryHiveId.LocalMachine => RegistryHive.LocalMachine,
        RegistryHiveId.Users => RegistryHive.Users,
        RegistryHiveId.CurrentConfig => RegistryHive.CurrentConfig,
        _ => throw new InvalidEnumArgumentException(nameof(hive), (int)hive, typeof(RegistryHiveId)),
    };

    private static RegistryView MapView(RegistryViewId view) => view switch
    {
        RegistryViewId.Default => RegistryView.Default,
        RegistryViewId.Registry32 => RegistryView.Registry32,
        RegistryViewId.Registry64 => RegistryView.Registry64,
        _ => throw new InvalidEnumArgumentException(nameof(view), (int)view, typeof(RegistryViewId)),
    };
}
