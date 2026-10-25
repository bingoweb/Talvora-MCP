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

        var approved = new[]
        {
            ("CodePreviewSetting", "Kod önizlemesini kartta göster", true),
            ("FailureAgentSetting", "Yalnızca hatalar ve ajan işleri için kart göster", false),
            ("QuietModeSetting", "Sessiz mod", false),
        };
        foreach (var (name, label, expected) in approved)
        {
            if (settings.FindName(name) is not Wpf.Ui.Controls.ToggleSwitch toggle ||
                toggle.IsChecked != expected ||
                AutomationProperties.GetName(toggle) != label)
                throw new InvalidOperationException("N8 notification control contract failed: " + name);
        }
        ((FrameworkElement)settings.FindName("QuietModeSetting")).BringIntoView();
        window.UpdateLayout();
        SaveVisualSnapshotIfRequested(window, "n8-notifications-" + theme);

        if (settings.FindName("AboutVersion") is not System.Windows.Controls.TextBlock aboutVersion ||
            settings.FindName("AboutCommit") is not System.Windows.Controls.TextBlock aboutCommit ||
            settings.FindName("ReadmeLink") is not Wpf.Ui.Controls.Anchor readme ||
            settings.FindName("SecurityLink") is not Wpf.Ui.Controls.Anchor security ||
            string.IsNullOrWhiteSpace(aboutVersion.Text) ||
            string.IsNullOrWhiteSpace(aboutCommit.Text) ||
            readme.NavigateUri?.ToString() != Views.SettingsView.ReadmeUrl ||
            security.NavigateUri?.ToString() != Views.SettingsView.SecurityUrl)
            throw new InvalidOperationException("UI7 Hakkında must show build metadata and working README/SECURITY links.");

        aboutCommit.BringIntoView();
        window.UpdateLayout();
        SaveVisualSnapshotIfRequested(window, "ui7-about-" + theme);
    }
}
