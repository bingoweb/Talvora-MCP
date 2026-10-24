using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Talvora.Tray.ViewModels;

internal sealed record ShellNavigationItem(int Index, string Label, string Symbol);

internal sealed class ShellCommand
{
    public ShellCommand(string label, Func<Task> execute)
    {
        Label = label;
        Command = new AsyncRelayCommand(execute);
    }

    public string Label { get; }
    public ICommand Command { get; }
}

internal sealed class ShellViewModel : ObservableObject
{
    private int _selectedIndex;
    private bool _isRailExpanded;
    private bool? _manualExpanded;
    private string _paletteQuery = string.Empty;
    private int? _attentionCount;
    private IReadOnlyList<ShellCommand> _serviceCommands = [];

    public ShellViewModel(Func<Task> refresh)
    {
        NavigationItems =
        [
            new(0, "Genel bakış", "Home24"),
            new(1, "Hizmetler", "Server24"),
            new(2, "Çalışma günlüğü", "Notebook24"),
            new(3, "Bağlantı zinciri", "Organization24"),
            new(4, "Olaylar", "TextBulletListLtr24"),
            new(5, "Ayarlar ve kurulum", "Settings24"),
        ];
        NavigateCommand = new RelayCommand<int>(SelectPage);
        ToggleRailCommand = new RelayCommand(() =>
        {
            _manualExpanded = !IsRailExpanded;
            IsRailExpanded = _manualExpanded.Value;
        });
        RefreshCommand = new AsyncRelayCommand(refresh);
        FilterPalette();
    }

    public IReadOnlyList<ShellNavigationItem> NavigationItems { get; }
    public IRelayCommand<int> NavigateCommand { get; }
    public IRelayCommand ToggleRailCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public ObservableCollection<ShellCommand> PaletteResults { get; } = [];

    public int SelectedIndex
    {
        get => _selectedIndex;
        private set
        {
            if (SetProperty(ref _selectedIndex, value))
                OnPropertyChanged(nameof(SelectedLabel));
        }
    }

    public string SelectedLabel => NavigationItems[SelectedIndex].Label;

    public bool IsRailExpanded
    {
        get => _isRailExpanded;
        private set => SetProperty(ref _isRailExpanded, value);
    }

    public int? AttentionCount
    {
        get => _attentionCount;
        set
        {
            if (SetProperty(ref _attentionCount, value))
            {
                OnPropertyChanged(nameof(HasHealth));
                OnPropertyChanged(nameof(HasAttention));
            }
        }
    }

    public bool HasHealth => AttentionCount.HasValue;
    public bool HasAttention => AttentionCount > 0;

    public string PaletteQuery
    {
        get => _paletteQuery;
        set
        {
            if (SetProperty(ref _paletteQuery, value))
                FilterPalette();
        }
    }

    public void SelectPage(int index)
    {
        if (index >= 0 && index < NavigationItems.Count)
            SelectedIndex = index;
    }

    public bool NavigateShortcut(int number)
    {
        if (number is < 1 or > 5) return false;
        SelectPage(number - 1);
        return true;
    }

    public void UpdateViewportWidth(double width) =>
        IsRailExpanded = _manualExpanded ?? width >= 1360;

    public void SetServiceCommands(IEnumerable<ShellCommand> commands)
    {
        _serviceCommands = commands.ToArray();
        FilterPalette();
    }

    private void FilterPalette()
    {
        var commands = NavigationItems.Select(item =>
            new ShellCommand(item.Label, () =>
            {
                SelectPage(item.Index);
                return Task.CompletedTask;
            })).Concat(_serviceCommands).ToArray();
        var query = PaletteQuery.Trim();
        var matches = query.Length == 0 ? commands :
            commands.Select((item, order) => new { Item = item, Order = order, Score = Score(item.Label, query) })
                .Where(item => item.Score != int.MaxValue)
                .OrderBy(item => item.Score).ThenBy(item => item.Order)
                .Select(item => item.Item).ToArray();
        PaletteResults.Clear();
        foreach (var item in matches) PaletteResults.Add(item);
    }

    private static int Score(string label, string query)
    {
        var culture = CultureInfo.GetCultureInfo("tr-TR");
        label = label.ToUpper(culture);
        query = query.ToUpper(culture);
        var position = -1;
        var score = label.Length;
        foreach (var letter in query)
        {
            var next = label.IndexOf(letter, position + 1);
            if (next < 0) return int.MaxValue;
            score += next - position - 1;
            position = next;
        }
        return score;
    }
}
