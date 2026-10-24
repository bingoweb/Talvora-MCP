using System.Windows.Controls;
using Talvora.Tray.ViewModels;

namespace Talvora.Tray.Views;

internal sealed partial class WorklogHistoryView : System.Windows.Controls.UserControl
{
    internal WorklogHistoryView(WorklogHistoryStore store)
    {
        InitializeComponent();
        Model = new WorklogHistoryViewModel(store);
        DataContext = Model;
    }

    internal WorklogHistoryViewModel Model { get; }

    internal void Reload() => Model.Reload();

    internal void FocusSearch()
    {
        HistorySearch.Focus();
        HistorySearch.SelectAll();
    }
}
