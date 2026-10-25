using System.Windows;
using System.Windows.Automation;

namespace Talvora.Tray;

internal static partial class ControlCenterSmoke
{
    private static void AssertUi6VisualFixture(ShellWindow window, string theme)
    {
        window.ShellModel.SelectPage(5);
        window.UpdateLayout();
        if (window.SettingsContentHost.Content is not Views.SettingsView settings ||
            window.SettingsContentHost.Visibility != Visibility.Visible ||
            window.LegacyContentHost.Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("UI6 settings route did not open its XAML view.");

        var model = settings.Model;
        var statuses = model.Steps.Select(step => step.StateText).ToArray();
        if (statuses.Length != 3 ||
            statuses.Any(status => status is not ("Tamam" or "Sırada" or "Bekliyor")) ||
            statuses.Count(status => status == "Sırada") != (model.AllComplete ? 0 : 1))
            throw new InvalidOperationException("UI6 stepper states violate the P-37 contract.");

        var card = settings.SetupCard;
        if (!card.IsVisible || card.ActualWidth < 32 ||
            AutomationProperties.GetName(card.DevIdBox) != "Talvora Dev tunnel ID" ||
            AutomationProperties.GetName(card.AdminPasswordBox) != "OpenAI Admin API key")
            throw new InvalidOperationException("UI6 setup inputs are not accessible.");

        SaveVisualSnapshotIfRequested(window, "ui6-settings-" + theme);
    }
}
