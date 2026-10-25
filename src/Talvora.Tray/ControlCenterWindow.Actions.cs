using System.Windows;
using Talvora.Shared;
using Wpf.Ui.Controls;

namespace Talvora.Tray;

/// <summary>Operation records and dialogs shared by the UI3/UI4 views (UI9 removed the legacy detail actions).</summary>
internal sealed partial class ShellWindow
{
    private static void RecordLifecycleSuccess(
        ManagedMcpRegistration registration,
        ManagedMcpLifecycleResult result)
    {
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Info,
            "operation",
            result.Summary,
            result.Detail,
            registration.Id,
            $"operation:{registration.Id}:{result.Operation}:success");
    }

    private static void RecordRepairSuccess(
        ManagedMcpRegistration registration,
        ManagedMcpRepairResult repair)
    {
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Info,
            "recovery",
            repair.Summary,
            $"{repair.Detail} Strateji: {repair.Strategy}.",
            registration.Id,
            $"recovery:{registration.Id}:manual-success");
    }

    private static void RecordOperationFailure(
        ManagedMcpRegistration registration,
        string title,
        string detail)
    {
        ControlCenterEventStore.Record(
            ControlCenterEventSeverity.Error,
            "operation",
            $"{registration.DisplayName}: {title}",
            detail,
            registration.Id,
            $"operation:{registration.Id}:{title}:failed");
    }

    private async Task<bool> ConfirmStopAsync(
        ManagedMcpRegistration registration)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = $"{registration.DisplayName} durdurulsun mu?",
            Content =
                BuildStopConfirmationText(registration) + " " +
                "Bu Windows oturumunda siz yeniden Başlat seçeneğini kullanana kadar " +
                "otomatik kurtarma bu bileşeni yeniden başlatmayacak.",
            PrimaryButtonText = "Durdur",
            PrimaryButtonAppearance = ControlAppearance.Danger,
            CloseButtonText = "Vazgeç",
            CloseButtonAppearance = ControlAppearance.Secondary,
        };

        var result = await dialog.ShowDialogAsync(
            showAsDialog: true,
            cancellationToken: _lifetimeCts.Token);

        return result == Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private async Task ShowOperationErrorAsync(
        string title,
        string message)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title,
            Content = message,
            PrimaryButtonText = "Tamam",
            PrimaryButtonAppearance = ControlAppearance.Primary,
        };

        _ = await dialog.ShowDialogAsync(
            showAsDialog: true,
            cancellationToken: _lifetimeCts.Token);
    }

    private static string BuildStopConfirmationText(
        ManagedMcpRegistration registration)
    {
        if (string.Equals(
                registration.Id,
                "talvora",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Talvora Core Service ile Dev ve Admin ChatGPT bağlantıları birlikte durdurulacak.";
        }

        if (string.Equals(
                registration.Id,
                "gitea",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Gitea MCP zinciri ve güvenli bağlantısı birlikte durdurulacak.";
        }

        return registration.Tunnel is not null
            ? $"{registration.DisplayName} ve güvenli bağlantısı birlikte durdurulacak."
            : $"{registration.DisplayName} çalışma bileşenleri durdurulacak.";
    }

    private static string GetFriendlyOperationError(Exception exception)
        => ControlCenterUserMessage.ForOperation(exception);
}
