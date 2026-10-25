using Talvora.Tray;
using Talvora.Tray.ViewModels;

internal static partial class Program
{
    private static void AssertUi5EventGroupingAndFilters()
    {
        var now = new DateTimeOffset(2026, 10, 24, 16, 0, 0, TimeSpan.FromHours(3));
        var records = new[]
        {
            Event(new DateTimeOffset(2026, 10, 24, 15, 5, 0, TimeSpan.FromHours(3)), "recovery",
                ControlCenterEventSeverity.Warning, "talvora-admin", "Tünel onarılıyor", "Tekrar bağlanılıyor"),
            Event(new DateTimeOffset(2026, 10, 24, 13, 0, 0, TimeSpan.FromHours(3)), "operation",
                ControlCenterEventSeverity.Info, "gitea", "Gitea başlatıldı", "Çalıştırma tamamlandı"),
            Event(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.FromHours(3)), "startup",
                ControlCenterEventSeverity.Info, "", "Talvora başlatıldı", "Başlangıç tamamlandı"),
            Event(new DateTimeOffset(2026, 10, 23, 12, 10, 0, TimeSpan.FromHours(3)), "connection",
                ControlCenterEventSeverity.Error, "talvora-dev", "Bağlantı kesildi", "Yetkilendirme hatası"),
            Event(new DateTimeOffset(2026, 10, 15, 9, 0, 0, TimeSpan.FromHours(3)), "operation",
                ControlCenterEventSeverity.Info, "talvora-admin", "Yeniden başlatıldı", "Önceki işlem"),
        };
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["talvora-admin"] = "Talvora Admin",
            ["talvora-dev"] = "Talvora Dev",
            ["gitea"] = "Gitea"
        };
        var vm = new EventsViewModel();
        if (!vm.IsLoading || vm.ShowEmptyState)
            throw new InvalidOperationException("UI5 must not show an empty result before the first read.");
        vm.ApplyEvents(records.Reverse().ToArray(), now, names);

        if (vm.Groups.Count != 3 ||
            vm.Groups[0].Title != "Bugün" || vm.Groups[0].Items.Count != 3 ||
            vm.Groups[0].Items[0].Title != "Tünel onarılıyor" ||
            vm.Groups[1].Title != "Dün" ||
            vm.Groups[2].Title != "15 Ekim Perşembe")
            throw new InvalidOperationException("UI5 events are not sorted and day-grouped correctly.");

        vm.SelectedCategory = "Sorunlar";
        if (vm.Groups.Sum(g => g.Items.Count) != 2)
            throw new InvalidOperationException("UI5 problem filter must include warning and error.");

        vm.SelectedCategory = "Onarımlar";
        if (vm.Groups.Count != 1 || vm.Groups[0].Items[0].Title != "Tünel onarılıyor")
            throw new InvalidOperationException("UI5 recovery filter included unrelated events.");

        vm.SelectedCategory = "Yaşam döngüsü";
        if (vm.Groups.Sum(g => g.Items.Count) != 3 ||
            !vm.Groups.SelectMany(g => g.Items).Any(item => item.Title == "Talvora başlatıldı"))
            throw new InvalidOperationException("UI5 lifecycle filter must include operation and startup events only.");
        if (vm.Groups.SelectMany(g => g.Items).Single(item => item.Title == "Talvora başlatıldı").HasService ||
            !vm.Groups.SelectMany(g => g.Items).Single(item => item.Title == "Gitea başlatıldı").HasService)
            throw new InvalidOperationException("UI5 must hide the pill only when the event has no service name.");

        vm.SelectedCategory = "Tümü";
        vm.SelectedServiceId = "talvora-admin";
        if (vm.Groups.Sum(g => g.Items.Count) != 2 ||
            vm.Groups.Any(g => g.Items.Any(x => x.ServiceName != "Talvora Admin")))
            throw new InvalidOperationException("UI5 service filter did not isolate one MCP.");

        vm.SelectedServiceId = null;
        vm.SearchText = "Yetkilendirme";
        if (vm.Groups.Count != 1 || vm.Groups[0].Title != "Dün" ||
            vm.Groups[0].Items[0].Title != "Bağlantı kesildi")
            throw new InvalidOperationException("UI5 search did not match event details.");

        vm.SearchText = "";
        var unsafeRecord = Event(now, "connection", ControlCenterEventSeverity.Info,
            "gitea", "Kayıt", "Authorization: Bearer fake-ui5-test-token");
        vm.ApplyEvents([unsafeRecord], now, names);
        if (vm.Groups.Single().Items.Single().Detail.Contains("fake-ui5-test-token", StringComparison.Ordinal))
            throw new InvalidOperationException("UI5 event detail was not redacted.");

        var empty = new EventsViewModel();
        var changed = new HashSet<string?>();
        empty.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        empty.ApplyEvents([], now, names);
        if (empty.IsLoading || !empty.ShowEmptyState ||
            empty.EmptyStateText != "Son 7 günde kaydedilmiş olay yok." ||
            !changed.Contains(nameof(EventsViewModel.ShowEmptyState)))
            throw new InvalidOperationException("UI5 must show the seven-day empty state after reading.");
        empty.SearchText = "eşleşmeyen";
        if (!empty.ShowEmptyState || empty.EmptyStateText != "Bu filtrelerle eşleşen olay yok." ||
            !changed.Contains(nameof(EventsViewModel.EmptyStateText)))
            throw new InvalidOperationException("UI5 empty search results need the filtered empty state.");
        empty.SearchText = " ";
        if (empty.EmptyStateText != "Son 7 günde kaydedilmiş olay yok.")
            throw new InvalidOperationException("UI5 whitespace is not an active search filter.");
        empty.SelectedCategory = "Sorunlar";
        if (empty.EmptyStateText != "Bu filtrelerle eşleşen olay yok.")
            throw new InvalidOperationException("UI5 empty category results need the filtered empty state.");
        empty.SelectedCategory = "Tümü";
        empty.SelectedServiceId = "gitea";
        if (empty.EmptyStateText != "Bu filtrelerle eşleşen olay yok.")
            throw new InvalidOperationException("UI5 empty service results need the filtered empty state.");
        empty.SelectedServiceId = null;
        empty.ApplyEvents([unsafeRecord], now, names);
        if (empty.ShowEmptyState)
            throw new InvalidOperationException("UI5 must hide the empty state when matching events arrive.");
        empty.ApplyEvents([], now, names);
        empty.IsLoading = true;
        if (empty.ShowEmptyState)
            throw new InvalidOperationException("UI5 loading must hide a previously visible empty state.");

        Console.WriteLine("UI5_EVENTS_GREEN");
    }

    private static ControlCenterEventRecord Event(
        DateTimeOffset time, string category, ControlCenterEventSeverity severity,
        string mcpId, string title, string detail) => new()
    {
        LastOccurredAtUtc = time.ToUniversalTime(),
        FirstOccurredAtUtc = time.ToUniversalTime(),
        Category = category,
        Severity = severity,
        McpId = mcpId,
        Title = title,
        Detail = detail,
        Count = 1
    };
}
