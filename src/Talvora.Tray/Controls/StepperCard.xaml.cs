using System.Windows;
using System.Windows.Controls;
using Talvora.Tray.ViewModels;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using UiPasswordBox = Wpf.Ui.Controls.PasswordBox;

namespace Talvora.Tray.Controls;

internal sealed partial class StepperCard : System.Windows.Controls.UserControl
{
    internal event EventHandler? RetryRequested;
    internal event EventHandler? SetupRequested;

    internal StepperCard(SettingsViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }

    internal UiTextBox DevIdBox => DevTunnelInput;
    internal UiTextBox AdminIdBox => AdminTunnelInput;
    internal UiPasswordBox AdminPasswordBox => AdminKeyInput;
    internal UiButton TunnelAction => TunnelSubmitButton;
    internal UiButton AdminAction => AdminSubmitButton;
    internal TextBlock Details => TechnicalDetailText;

    internal void ShowTunnelIds(bool needsDev, bool needsAdmin)
    {
        DevTunnelLabel.Visibility = DevTunnelInput.Visibility =
            needsDev ? Visibility.Visible : Visibility.Collapsed;
        AdminTunnelLabel.Visibility = AdminTunnelInput.Visibility =
            needsAdmin ? Visibility.Visible : Visibility.Collapsed;
        if (!needsDev) DevTunnelInput.Text = string.Empty;
        if (!needsAdmin) AdminTunnelInput.Text = string.Empty;
    }

    private void OnRetryCore(object sender, RoutedEventArgs e) =>
        RetryRequested?.Invoke(this, EventArgs.Empty);

    private void OnSubmit(object sender, RoutedEventArgs e) =>
        SetupRequested?.Invoke(this, EventArgs.Empty);
}
