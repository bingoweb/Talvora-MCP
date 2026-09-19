using Microsoft.Win32;
using Talvora.Shared;
using System.Diagnostics;
using System.Drawing;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace Talvora.Installer;

internal sealed class InstallerForm : Form
{
    private readonly Label _statusLabel;
    private readonly ProgressBar _progressBar;
    private readonly Button _installButton;
    private readonly Button _closeButton;

    public InstallerForm()
    {
        Text = "Talvora Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(620, 360);
        BackColor = Color.FromArgb(246, 248, 251);

        try
        {
            var executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable))
            {
                Icon = Icon.ExtractAssociatedIcon(executable);
            }
        }
        catch
        {
        }

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 112,
            BackColor = Color.FromArgb(18, 42, 62),
        };

        var iconBox = new PictureBox
        {
            Location = new Point(28, 24),
            Size = new Size(64, 64),
            SizeMode = PictureBoxSizeMode.Zoom,
            Image = Icon?.ToBitmap(),
        };

        var title = new Label
        {
            AutoSize = true,
            Location = new Point(112, 25),
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 24, FontStyle.Bold),
            Text = "Talvora",
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Location = new Point(116, 70),
            ForeColor = Color.FromArgb(188, 223, 232),
            Font = new Font("Segoe UI", 10),
            Text = "Yerel MCP + ChatGPT Business bağlantısı",
        };

        header.Controls.Add(iconBox);
        header.Controls.Add(title);
        header.Controls.Add(subtitle);
        Controls.Add(header);

        var description = new Label
        {
            Location = new Point(32, 136),
            Size = new Size(556, 48),
            Font = new Font("Segoe UI", 10),
            ForeColor = Color.FromArgb(38, 50, 61),
            Text = "Talvora Windows servisini ve sistem tepsisi uygulamasını kurar veya günceller. " +
                   "Mevcut ChatGPT Business tunnel kimliği ve Runtime API key korunur.",
        };
        Controls.Add(description);

        _statusLabel = new Label
        {
            Location = new Point(32, 205),
            Size = new Size(556, 38),
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(62, 74, 84),
            Text = "Kuruluma hazır.",
        };
        Controls.Add(_statusLabel);

        _progressBar = new ProgressBar
        {
            Location = new Point(32, 250),
            Size = new Size(556, 18),
            Minimum = 0,
            Maximum = 100,
            Value = 0,
        };
        Controls.Add(_progressBar);

        _installButton = new Button
        {
            Location = new Point(326, 296),
            Size = new Size(126, 38),
            Text = "Kur / Güncelle",
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
        };
        _installButton.Click += async (_, _) => await InstallAsync();
        Controls.Add(_installButton);

        _closeButton = new Button
        {
            Location = new Point(462, 296),
            Size = new Size(126, 38),
            Text = "Kapat",
            Font = new Font("Segoe UI", 9.5f),
        };
        _closeButton.Click += (_, _) => Close();
        Controls.Add(_closeButton);
    }

    private async Task InstallAsync()
    {
        _installButton.Enabled = false;
        _closeButton.Enabled = false;
        UseWaitCursor = true;

        var progress = new Progress<InstallProgress>(update =>
        {
            _progressBar.Value = Math.Clamp(update.Percent, 0, 100);
            _statusLabel.Text = update.Message;
        });

        try
        {
            var result = await InstallerEngine.InstallAsync(progress, CancellationToken.None);
            _progressBar.Value = 100;
            _statusLabel.Text =
                $"Talvora hazır. Servis PID: {result.ProcessId}. Sistem tepsisi uygulaması başlatıldı.";
            MessageBox.Show(
                "Talvora başarıyla kuruldu ve çalışıyor.\n\n" +
                "Saat yanındaki Talvora ikonundan ChatGPT Business bağlantısını yeniden kurabilirsiniz.",
                "Talvora hazır",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            InstallerLog.Write("Interactive install failed", ex);
            _statusLabel.Text = ex.Message;
            MessageBox.Show(
                ex.Message,
                "Talvora kurulumu başarısız",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            _installButton.Enabled = true;
            _closeButton.Enabled = true;
        }
    }
}
