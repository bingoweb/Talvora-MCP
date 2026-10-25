using System.Drawing;
using System.Windows.Forms;
using Talvora.Tray;
using Talvora.Tray.Views;

internal static partial class Program
{
    private static void AssertUi7TrayAndAbout()
    {
        var opened = 0;
        var refreshed = 0;
        var exited = 0;
        var open = new ToolStripMenuItem("Kontrol Merkezini aç");
        open.Click += (_, _) => opened++;
        var status = new ToolStripMenuItem("Durum: Her şey yolunda.");
        var refresh = new ToolStripMenuItem("Yenile");
        refresh.Click += (_, _) => refreshed++;
        var exit = new ToolStripMenuItem("Çıkış");
        exit.Click += (_, _) => exited++;
        using var menu = TrayApplicationContext.BuildUi7Menu(open, status, refresh, exit);
        if (menu.Items.Count != 6 ||
            !ReferenceEquals(menu.Items[0], open) ||
            !ReferenceEquals(menu.Items[1], status) ||
            menu.Items[2] is not ToolStripSeparator ||
            !ReferenceEquals(menu.Items[3], refresh) ||
            menu.Items[4] is not ToolStripSeparator ||
            !ReferenceEquals(menu.Items[5], exit) ||
            !open.Font.Bold ||
            status.Enabled)
            throw new InvalidOperationException("UI7 tray menu must have the approved order, default action and disabled status.");
        open.PerformClick();
        refresh.PerformClick();
        exit.PerformClick();
        if (opened != 1 || refreshed != 1 || exited != 1)
            throw new InvalidOperationException("UI7 tray menu actions are not reachable.");
        // Audit 21: the full §9 sentence fits; only text beyond 127 characters ends at a word with "…".
        var attention = TrayApplicationContext.BuildUi7Tooltip("Talvora Admin dikkat istiyor: Yanıt gecikiyor.");
        if (attention != "Talvora Kontrol Merkezi — Talvora Admin dikkat istiyor: Yanıt gecikiyor.")
            throw new InvalidOperationException("UI7 tooltip cut the status sentence: " + attention);
        var longTip = TrayApplicationContext.BuildUi7Tooltip(string.Join(' ', Enumerable.Repeat("bileşen", 30)));
        if (longTip.Length > 127 || !longTip.EndsWith("…", StringComparison.Ordinal) || longTip.Contains("bileş…", StringComparison.Ordinal))
            throw new InvalidOperationException("UI7 tooltip must end at a word within 127 characters: " + longTip);
        if (TrayApplicationContext.BuildUi7Tooltip("Her şey yolunda.") !=
            "Talvora Kontrol Merkezi — Her şey yolunda.")
            throw new InvalidOperationException("UI7 tray tooltip must use the approved status text.");

        var about = SettingsView.GetUi7AboutMetadata("3.0.0-dev+1234567890abcdef");
        if (about.Version != "3.0.0-dev+1234567890abcdef" ||
            about.ShortCommit != "1234567" ||
            SettingsView.ReadmeUrl != "https://github.com/bingoweb/Talvora-MCP/blob/main/README.md" ||
            SettingsView.SecurityUrl != "https://github.com/bingoweb/Talvora-MCP/blob/main/SECURITY.md")
            throw new InvalidOperationException("UI7 About must expose version, short source commit and approved links.");
        Console.WriteLine("UI7_TRAY_ABOUT_GREEN");
    }
}
