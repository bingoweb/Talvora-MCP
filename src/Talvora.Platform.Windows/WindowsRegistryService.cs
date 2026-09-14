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

    public ValueTask WriteValueAsync(
        RegistryValueData value,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(value.SubKeyPath);

        (object RawValue, RegistryValueKind Kind) writableValue = value.Type switch
        {
            RegistryValueType.Text => (
                value.StringValue ?? throw MissingPayload(value.Type, nameof(value.StringValue)),
                RegistryValueKind.String),
            RegistryValueType.ExpandableText => (
                value.StringValue ?? throw MissingPayload(value.Type, nameof(value.StringValue)),
                RegistryValueKind.ExpandString),
            RegistryValueType.Binary => (
                value.BinaryValue ?? throw MissingPayload(value.Type, nameof(value.BinaryValue)),
                RegistryValueKind.Binary),
            RegistryValueType.DWord => (
                value.DWordValue ?? throw MissingPayload(value.Type, nameof(value.DWordValue)),
                RegistryValueKind.DWord),
            RegistryValueType.MultiText => (
                value.MultiStringValue?.ToArray()
                    ?? throw MissingPayload(value.Type, nameof(value.MultiStringValue)),
                RegistryValueKind.MultiString),
            RegistryValueType.QWord => (
                value.QWordValue ?? throw MissingPayload(value.Type, nameof(value.QWordValue)),
                RegistryValueKind.QWord),
            RegistryValueType.None => (
                value.BinaryValue ?? throw MissingPayload(value.Type, nameof(value.BinaryValue)),
                RegistryValueKind.None),
            _ => throw new InvalidEnumArgumentException(
                nameof(value.Type),
                (int)value.Type,
                typeof(RegistryValueType)),
        };

        using var baseKey = RegistryKey.OpenBaseKey(MapHive(value.Hive), MapView(view));
        using var key = baseKey.OpenSubKey(value.SubKeyPath, writable: true)
            ?? throw new KeyNotFoundException($"Registry key was not found: {value.Hive}\\{value.SubKeyPath}");

        key.SetValue(value.ValueName, writableValue.RawValue, writableValue.Kind);
        return ValueTask.CompletedTask;
    }

    public ValueTask DeleteValueAsync(
        RegistryHiveId hive,
        string subKeyPath,
        string? valueName = null,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);

        using var baseKey = RegistryKey.OpenBaseKey(MapHive(hive), MapView(view));
        using var key = baseKey.OpenSubKey(subKeyPath, writable: true)
            ?? throw new KeyNotFoundException($"Registry key was not found: {hive}\\{subKeyPath}");

        key.DeleteValue(valueName ?? string.Empty, throwOnMissingValue: true);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<string>> ListSubKeyNamesAsync(
        RegistryHiveId hive,
        string subKeyPath,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);

        using var baseKey = RegistryKey.OpenBaseKey(MapHive(hive), MapView(view));
        using var key = baseKey.OpenSubKey(subKeyPath, writable: false)
            ?? throw new KeyNotFoundException($"Registry key was not found: {hive}\\{subKeyPath}");

        var names = key.GetSubKeyNames();
        Array.Sort(names, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyList<string>>(names);
    }

    public ValueTask<IReadOnlyList<string>> ListValueNamesAsync(
        RegistryHiveId hive,
        string subKeyPath,
        RegistryViewId view = RegistryViewId.Default,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(subKeyPath);

        using var baseKey = RegistryKey.OpenBaseKey(MapHive(hive), MapView(view));
        using var key = baseKey.OpenSubKey(subKeyPath, writable: false)
            ?? throw new KeyNotFoundException($"Registry key was not found: {hive}\\{subKeyPath}");

        var names = key.GetValueNames();
        Array.Sort(names, StringComparer.Ordinal);

        return ValueTask.FromResult<IReadOnlyList<string>>(names);
    }

    private static ArgumentException MissingPayload(RegistryValueType type, string propertyName) =>
        new($"Registry value type {type} requires {propertyName}.", propertyName);

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
