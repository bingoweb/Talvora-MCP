using Microsoft.Win32;
using Talvora.Shared;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Reflection;
using System.Windows.Forms;

namespace Talvora.Installer;

internal enum SetupMode
{
    Install,
    Update,
    Uninstall,
}

/// <summary>The work the window runs: it reports progress, returns the closing detail line (or null) and throws on failure.</summary>
internal delegate Task<string?> SetupOperation(
    IProgress<InstallProgress> progress, bool option, CancellationToken cancellationToken);

/// <summary>KUR1: every word the setup window shows, per mode (plain Turkish, "sen" as in the Control Center).</summary>
internal sealed record SetupCopy(
    string WindowTitle,
    string WordmarkWord,
    string ReadyHeadline,
    string ReadyBody,
    string PrimaryReady,
    string WorkingHeadline,
    string DoneHeadline,
    string DoneBody,
    string FailedHeadline,
    string CloseBlocked,
    string ProgressName,
    string? OptionText,
    LampWords Words)
{
    internal const string Cancel = "Vazgeç";
    internal const string Close = "Kapat";
    internal const string Retry = "Yeniden dene";
    internal const string PackageTitle = "KURULUM PAKETİ";
    internal const string ComputerTitle = "BU BİLGİSAYAR";

    internal static SetupCopy For(SetupMode mode) => mode switch
    {
        SetupMode.Update => new SetupCopy(
            WindowTitle: "Talvora Kurulumu",
            WordmarkWord: "KURULUM",
            ReadyHeadline: "Talvora yeni sürüme geçecek.",
            ReadyBody: "Ayarların, bağlantıların ve anahtarların olduğu gibi korunur. Geçiş sırasında Talvora birkaç saniye yanıt vermez.",
            PrimaryReady: "Güncelle",
            WorkingHeadline: "Talvora güncelleniyor…",
            DoneHeadline: "Talvora hazır.",
            DoneBody: "Yeni sürüm çalışıyor. Kontrol Merkezi'ni görev çubuğundaki Talvora simgesinden açabilirsin.",
            FailedHeadline: "Güncelleme tamamlanamadı.",
            CloseBlocked: "Güncelleme bitene kadar bu pencere açık kalır.",
            ProgressName: "Güncelleme ilerlemesi",
            OptionText: null,
            Words: new LampWords("bekliyor", "güncelleniyor", "hazır", "güncellenemedi")),
        SetupMode.Uninstall => new SetupCopy(
            WindowTitle: "Talvora'yı kaldır",
            WordmarkWord: "KALDIRMA",
            ReadyHeadline: "Talvora bu bilgisayardan kaldırılacak.",
            ReadyBody: "Windows hizmeti, Kontrol Merkezi, Başlat menüsü kısayolu ve Codex kaydı silinir. Kontrol Merkezi'nden kurduğun ek bileşenler (Gitea, Penpot, ChatGPT tüneli) bu kaldırmaya dahil değildir.",
            PrimaryReady: "Kaldır",
            WorkingHeadline: "Talvora kaldırılıyor…",
            DoneHeadline: "Talvora kaldırıldı.",
            DoneBody: "Kurulum dosyası, bilgisayar yeniden başladığında silinecek.",
            FailedHeadline: "Kaldırma tamamlanamadı.",
            CloseBlocked: "Kaldırma bitene kadar bu pencere açık kalır.",
            ProgressName: "Kaldırma ilerlemesi",
            OptionText: @"Kayıtları ve ayarları da sil (C:\ProgramData\Talvora)",
            Words: new LampWords("kurulu", "kaldırılıyor", "kaldırıldı", "kaldırılamadı")),
        _ => new SetupCopy(
            WindowTitle: "Talvora Kurulumu",
            WordmarkWord: "KURULUM",
            ReadyHeadline: "Talvora bu bilgisayara kurulacak.",
            ReadyBody: "Talvora bir Windows hizmeti olarak çalışır, Kontrol Merkezi de görev çubuğuna yerleşir. PowerShell 7 yoksa o da indirilip kurulur.",
            PrimaryReady: "Kur",
            WorkingHeadline: "Talvora kuruluyor…",
            DoneHeadline: "Talvora hazır.",
            DoneBody: "Kontrol Merkezi'ni görev çubuğundaki Talvora simgesinden açabilirsin.",
            FailedHeadline: "Kurulum tamamlanamadı.",
            CloseBlocked: "Kurulum bitene kadar bu pencere açık kalır.",
            ProgressName: "Kurulum ilerlemesi",
            OptionText: null,
            Words: new LampWords("bekliyor", "kuruluyor", "hazır", "kurulamadı")),
    };

    /// <summary>"sürüm 0.0.1.0" for a release, "geliştirme sürümü" for a development build.</summary>
    internal static string VersionText(string? informationalVersion)
    {
        var value = informationalVersion?.Trim() ?? string.Empty;
        var plus = value.IndexOf('+', StringComparison.Ordinal);
        var version = plus >= 0 ? value[..plus] : value;
        return version.Length == 0 || version.StartsWith("0.0.0.0-dev", StringComparison.Ordinal)
            ? "geliştirme sürümü"
            : "sürüm " + version;
    }
}

/// <summary>
/// KUR1: Talvora's setup window in the Control Center's Santral language (docs/codex/DESIGN-SANTRAL.md):
/// a patch cord carries the real install progress from the setup package to this computer, success and
/// failure stay in the window, it cannot be closed half way, and the developer signs it with a small
/// brass maker plate (user request 2026-11-08).
/// </summary>
internal sealed class InstallerForm : Form, ISetupSurface
{
    internal const string DeveloperCredit = "Geliştiren: Taylan Soylu";

    private const int BaseWidth = 640;
    private const int BaseHeight = 480;
    private const int Edge = 32;
    private const int FooterRuleY = 410;

    private readonly SetupMode _mode;
    private readonly SetupOperation _operation;
    private readonly SetupCopy _copy;
    private readonly SwitchboardView _board;
    private readonly Label _headline;
    private readonly Label _body;
    private readonly Label _detail;
    private readonly SetupCheckBox? _option;
    private readonly MakerPlate _plate;
    private readonly SetupButton _primary;
    private readonly SetupButton _secondary;
    private SetupPalette _palette = SetupPalette.Gun;
    private SetupFonts? _fonts;
    private Bitmap? _backdrop;
    private float _scale = 1f;
    private SetupStage _stage = SetupStage.Ready;
    private int _percent;
    private bool _closeNotice;
    private string? _resultDetail;
    private string _failure = string.Empty;

    public InstallerForm()
        : this(DetectInstallMode(), RunInstallAsync)
    {
    }

    internal InstallerForm(SetupMode mode, SetupOperation operation)
    {
        _mode = mode;
        _operation = operation;
        _copy = SetupCopy.For(mode);

        Text = _copy.WindowTitle;
        AccessibleName = _copy.WindowTitle;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                Icon = Icon.ExtractAssociatedIcon(executable);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
        }

        _board = new SwitchboardView(mode == SetupMode.Uninstall, _copy.Words);
        _board.SetCaptions(
            SetupCopy.PackageTitle,
            SetupCopy.VersionText(typeof(InstallerForm).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion),
            SetupCopy.ComputerTitle,
            Environment.MachineName,
            _copy.ProgressName);
        _headline = CreateLabel();
        _body = CreateLabel();
        _detail = CreateLabel();
        _detail.AutoEllipsis = true;
        _plate = new MakerPlate();
        _primary = new SetupButton(primary: true) { TabIndex = 1 };
        _secondary = new SetupButton(primary: false) { TabIndex = 2 };
        _primary.Click += async (_, _) => await OnPrimaryAsync();
        _secondary.Click += (_, _) => Close();

        Controls.Add(_board);
        Controls.Add(_headline);
        Controls.Add(_body);
        Controls.Add(_detail);
        if (_copy.OptionText is { } optionText)
        {
            _option = new SetupCheckBox { Text = optionText, TabIndex = 0 };
            Controls.Add(_option);
        }

        Controls.Add(_plate);
        Controls.Add(_secondary);
        Controls.Add(_primary);

        ApplyScale(DeviceDpi);
        ShowStage(SetupStage.Ready);
        ActiveControl = _primary;
    }

    private enum SetupStage
    {
        Ready,
        Working,
        Done,
        Failed,
    }

    public Bitmap? Backdrop => _backdrop;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        SetupTheme.ApplyTitleBar(Handle, _palette);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyScale(e.DeviceDpiNew);
        SetupTheme.ApplyTitleBar(Handle, _palette);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_backdrop is null)
        {
            base.OnPaintBackground(e);
            return;
        }

        var g = e.Graphics;
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(_backdrop, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_fonts is null)
        {
            return;
        }

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        var x = S(Edge);
        var y = S(20);
        var tracking = 3.5f * _scale;
        var width = SetupArt.DrawTracked(g, "TALVORA", _fonts.Wordmark, _palette.Ink, x, y, tracking);
        SetupArt.DrawTracked(g, _copy.WordmarkWord, _fonts.WordmarkLight, _palette.Ink, x + width + (12f * _scale), y, tracking);

        using var rule = new Pen(SetupArt.Alpha(_palette.Rule, _palette.HighContrast ? 255 : 130), Math.Max(1f, _scale));
        g.DrawLine(rule, S(Edge), S(FooterRuleY), ClientSize.Width - S(Edge), S(FooterRuleY));
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_stage == SetupStage.Working && e.CloseReason == CloseReason.UserClosing)
        {
            // Closing half way would stop the installer between the old and the new version.
            e.Cancel = true;
            _closeNotice = true;
            ShowPercent();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fonts?.Dispose();
            _backdrop?.Dispose();
        }

        base.Dispose(disposing);
    }

    private static Label CreateLabel() => new()
    {
        AutoSize = false,
        BackColor = Color.Transparent,
        UseMnemonic = false,
        TabStop = false,
    };

    private static SetupMode DetectInstallMode()
    {
        try
        {
            using var service = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\" + TalvoraConstants.ServiceName);
            return service is null ? SetupMode.Install : SetupMode.Update;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return SetupMode.Install;
        }
    }

    private static async Task<string?> RunInstallAsync(
        IProgress<InstallProgress> progress, bool option, CancellationToken cancellationToken)
    {
        var result = await InstallerEngine.InstallAsync(progress, cancellationToken);
        return SetupCopy.VersionText(result.Version) + " · hizmet süreci " +
               result.ProcessId.ToString(CultureInfo.InvariantCulture);
    }

    private static string Tidy(string? message)
    {
        var text = (message ?? string.Empty).Replace("...", "…", StringComparison.Ordinal).Trim();
        return text.Length <= 240 ? text : text[..239].TrimEnd() + "…";
    }

    private float S(float value) => MathF.Round(value * _scale);

    private int Si(int value) => (int)MathF.Round(value * _scale);

    private void ApplyScale(int dpi)
    {
        _scale = Math.Max(1f, dpi / 96f);
        _palette = SetupTheme.Resolve();
        var animate = SetupTheme.AnimationsEnabled();
        var fonts = new SetupFonts(_scale);
        _fonts?.Dispose();
        _fonts = fonts;
        BackColor = _palette.Panel;
        ForeColor = _palette.Ink;
        ClientSize = new Size(Si(BaseWidth), Si(BaseHeight));
        var backdrop = SetupArt.CreateBackdrop(ClientSize, _palette, _scale);
        _backdrop?.Dispose();
        _backdrop = backdrop;

        _board.ApplyTheme(_palette, fonts, _scale, animate);
        _headline.Font = fonts.Title;
        _headline.ForeColor = _palette.Ink;
        _body.Font = fonts.Body;
        _body.ForeColor = _palette.InkMuted;
        _detail.Font = fonts.Mono;
        _detail.ForeColor = _palette.InkFaint;
        _option?.ApplyTheme(_palette, fonts.Body, _scale);
        _plate.ApplyTheme(_palette, fonts, _scale);
        _primary.ApplyTheme(_palette, fonts.Button, _scale);
        _secondary.ApplyTheme(_palette, fonts.Button, _scale);
        LayoutControls();
        Invalidate(true);
    }

    private void LayoutControls()
    {
        var margin = Si(Edge);
        var width = ClientSize.Width - (2 * margin);
        _board.Bounds = new Rectangle(margin, Si(62), width, Si(196));
        _headline.Bounds = new Rectangle(margin - Si(1), Si(268), width, Si(38));
        _body.Bounds = new Rectangle(margin, Si(308), width, Si(64));
        _detail.Bounds = new Rectangle(margin, Si(378), width, Si(22));
        if (_option is not null)
        {
            _option.Bounds = new Rectangle(margin - Si(3), Si(374), width, Si(28));
        }

        var buttonHeight = Si(44);
        var footerTop = Si(424);
        var inset = (int)MathF.Round(_primary.Inset);
        _primary.Size = new Size(_primary.PreferredWidth(), buttonHeight);
        _primary.Location = new Point(ClientSize.Width - margin + inset - _primary.Width, footerTop);
        _secondary.Size = new Size(_secondary.PreferredWidth(), buttonHeight);
        _secondary.Location = new Point(_primary.Left - Si(12) + (2 * inset) - _secondary.Width, footerTop);
        _plate.Location = new Point(margin, footerTop + ((buttonHeight - _plate.Height) / 2) + Si(1));
    }

    private void ShowStage(SetupStage stage)
    {
        _stage = stage;
        _closeNotice = false;
        UseWaitCursor = stage == SetupStage.Working;
        switch (stage)
        {
            case SetupStage.Working:
                _board.SetStage(CordStage.Working);
                _board.SetProgress(0d);
                _percent = 0;
                _headline.Text = _copy.WorkingHeadline;
                _body.Text = string.Empty;
                ShowPercent();
                _primary.Visible = false;
                _secondary.Visible = false;
                AcceptButton = null;
                CancelButton = null;
                break;
            case SetupStage.Done:
                _board.SetStage(CordStage.Done);
                _headline.Text = _copy.DoneHeadline;
                _body.Text = _copy.DoneBody;
                _detail.Text = _resultDetail ?? string.Empty;
                _primary.Text = SetupCopy.Close;
                _primary.Visible = true;
                _secondary.Visible = false;
                AcceptButton = _primary;
                CancelButton = _primary;
                break;
            case SetupStage.Failed:
                _board.SetStage(CordStage.Failed);
                _headline.Text = _copy.FailedHeadline;
                _body.Text = Tidy(_failure);
                _detail.Text = "Ayrıntılar: " + InstallerLog.PathName;
                _primary.Text = SetupCopy.Retry;
                _secondary.Text = SetupCopy.Close;
                _primary.Visible = true;
                _secondary.Visible = true;
                AcceptButton = _primary;
                CancelButton = _secondary;
                break;
            default:
                _board.SetStage(CordStage.Idle);
                _headline.Text = _copy.ReadyHeadline;
                _body.Text = _copy.ReadyBody;
                _detail.Text = string.Empty;
                _primary.Text = _copy.PrimaryReady;
                _secondary.Text = SetupCopy.Cancel;
                _primary.Visible = true;
                _secondary.Visible = true;
                AcceptButton = _primary;
                CancelButton = _secondary;
                break;
        }

        if (_option is not null)
        {
            _option.Visible = stage == SetupStage.Ready;
            _detail.Visible = stage != SetupStage.Ready;
        }

        LayoutControls();
        if (_primary.Visible && stage != SetupStage.Ready)
        {
            _primary.Focus();
        }
    }

    private void ShowPercent()
    {
        var text = "%" + _percent.ToString(CultureInfo.InvariantCulture);
        _detail.Text = _closeNotice ? text + " · " + _copy.CloseBlocked : text;
    }

    private void Report(InstallProgress update)
    {
        if (_stage != SetupStage.Working)
        {
            return;
        }

        _percent = Math.Clamp(update.Percent, 0, 100);
        _board.SetProgress(_percent / 100d);
        _body.Text = Tidy(update.Message);
        ShowPercent();
    }

    private async Task OnPrimaryAsync()
    {
        switch (_stage)
        {
            case SetupStage.Done:
                Close();
                break;
            case SetupStage.Ready:
            case SetupStage.Failed:
                await RunAsync();
                break;
        }
    }

    private async Task RunAsync()
    {
        ShowStage(SetupStage.Working);
        var option = _option?.Checked == true;
        var progress = new Progress<InstallProgress>(Report);
        try
        {
            _resultDetail = await Task.Run(() => _operation(progress, option, CancellationToken.None));
            ShowStage(SetupStage.Done);
        }
        catch (Exception ex)
        {
            InstallerLog.Write(
                _mode == SetupMode.Uninstall ? "Interactive uninstall failed" : "Interactive install failed", ex);
            _failure = ex.Message;
            ShowStage(SetupStage.Failed);
        }
    }
}
