using Talvora.Tray.ViewModels;
using CommunityToolkit.Mvvm.Input;

internal static partial class Program
{
    private static async Task AssertShellCommandsAsync()
    {
        var pendingRefresh = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new ShellViewModel(() => pendingRefresh.Task);
        var expected = new[] { "Genel bakış", "Hizmetler", "Çalışma günlüğü", "Bağlantı zinciri", "Olaylar", "Ayarlar ve kurulum" };
        if (!model.NavigationItems.Select(item => item.Label).SequenceEqual(expected))
            throw new InvalidOperationException("The shell rail must keep the approved Turkish order.");
        for (var shortcut = 1; shortcut <= 5; shortcut++)
        {
            if (!model.NavigateShortcut(shortcut) || model.SelectedIndex != shortcut - 1)
                throw new InvalidOperationException("Ctrl+1..5 must select the matching rail item.");
        }
        if (model.NavigateShortcut(6) || model.SelectedIndex != 4)
            throw new InvalidOperationException("The shell must not invent another numbered shortcut.");

        model.UpdateViewportWidth(1359);
        if (model.IsRailExpanded) throw new InvalidOperationException("The rail must collapse below 1360.");
        model.UpdateViewportWidth(1360);
        if (!model.IsRailExpanded) throw new InvalidOperationException("The rail must expand at 1360.");
        model.ToggleRailCommand.Execute(null);
        if (model.IsRailExpanded) throw new InvalidOperationException("The logo must toggle the rail.");

        var detailCalls = 0;
        model.SetServiceCommands([new ShellCommand("Talvora Dev — ayrıntılar", () =>
        {
            detailCalls++;
            return Task.CompletedTask;
        })]);
        model.PaletteQuery = "hzmt";
        if (!model.PaletteResults.Any(item => item.Label == "Hizmetler"))
            throw new InvalidOperationException("Palette search must support subsequence matching.");
        model.PaletteQuery = "dev";
        var detail = model.PaletteResults.Single();
        await ((IAsyncRelayCommand)detail.Command).ExecuteAsync(null);
        if (detailCalls != 1) throw new InvalidOperationException("The palette must call the existing service action once.");

        model.PaletteQuery = "no matching shell action";
        if (model.PaletteResults.Count != 0)
            throw new InvalidOperationException("An unmatched query must not execute a different action.");
        var refresh = model.RefreshCommand.ExecuteAsync(null);
        if (refresh.IsCompleted || !model.RefreshCommand.IsRunning)
            throw new InvalidOperationException("Refresh must remain asynchronous while its existing loader is pending.");
        pendingRefresh.SetResult();
        await refresh;
        if (model.RefreshCommand.IsRunning)
            throw new InvalidOperationException("Refresh must finish when the existing loader completes.");
    }
}
