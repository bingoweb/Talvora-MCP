using System.Windows;
using Talvora.Tray.Controls;
using Talvora.Shared;

namespace Talvora.Tray.Views;

/// <summary>§5.2 full constellation and 360 px detail sheet.</summary>
internal sealed partial class ChainView : System.Windows.Controls.UserControl
{
    public ChainView()
    {
        InitializeComponent();
        FullChain.NodeSelected += SelectNode;
    }

    public void ApplySnapshot(ControlCenterDashboardSnapshot snapshot,
        IReadOnlyList<(ManagedMcpRegistration Owner, ManagedMcpComponentState State)> components) =>
        FullChain.Apply(snapshot, components, full: true);

    public void SelectNode(ChainNode node)
    {
        DetailsSheet.Visibility = Visibility.Visible;
        SelectedName.Text = node.Name;
        SelectedStatus.Text = node.StatusLabel;
        SelectedDetail.Text = node.PlainDetail;
        SelectedChecked.Text = "Son kontrol " + node.LastChecked.ToLocalTime()
            .ToString("HH:mm:ss", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

        // No layer-level repair entry point exists in ControlCenterRepairService.
        // The specification explicitly hides this action until such a path is supported.
        RepairLayer.Visibility = Visibility.Collapsed;
    }
}
