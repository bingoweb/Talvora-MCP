using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace Talvora.Tray;

internal sealed partial class ControlCenterWindow
{
    private readonly SemaphoreSlim _memoryMutationGate = new(1, 1);

    private UIElement BuildMemoryActionBar(ControlCenterMemoryItem item)
    {
        var panel = new WrapPanel
        {
            Margin = new Thickness(0, 16, 0, 0),
        };
        panel.Children.Add(BuildMemoryActionButton(
            "Düzenle",
            SymbolRegular.Wrench20,
            async () => await EditMemoryAsync(item)));
        panel.Children.Add(BuildMemoryActionButton(
            "Süresini bitir",
            SymbolRegular.History20,
            async () => await ExpireMemoryAsync(item)));
        panel.Children.Add(BuildMemoryActionButton(
            "Eski olarak işaretle",
            SymbolRegular.ArrowSync20,
            async () => await SupersedeMemoryAsync(item)));
        panel.Children.Add(BuildMemoryActionButton(
            "Unut",
            SymbolRegular.ErrorCircle20,
            async () => await ForgetMemoryAsync(item),
            danger: true));
        return panel;
    }

    private UiButton BuildMemoryActionButton(
        string text,
        SymbolRegular icon,
        Func<Task> action,
        bool danger = false)
    {
        var button = new UiButton
        {
            Content = text,
            Icon = new SymbolIcon { Symbol = icon },
            Margin = new Thickness(0, 0, 8, 8),
            Style = FindStyle("TalvoraSecondaryButtonStyle"),
            Appearance = danger
                ? ControlAppearance.Danger
                : ControlAppearance.Secondary,
        };
        button.Click += async (_, _) =>
        {
            if (!await _memoryMutationGate.WaitAsync(0))
            {
                SetMemoryStatus(
                    "Başka bir hafıza işlemi sürüyor; tamamlandığında yeniden deneyin.",
                    AttentionBrush);
                return;
            }

            button.IsEnabled = false;
            try
            {
                await action();
            }
            catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (IsExpectedMemoryUiFailure(ex))
            {
                TrayLog.Write("Control Center memory mutation failed", ex);
                SetMemoryStatus(
                    ControlCenterUserMessage.ForOperation(ex, "Hafıza işlemi"),
                    OfflineBrush);
            }
            finally
            {
                button.IsEnabled = true;
                _memoryMutationGate.Release();
            }
        };
        return button;
    }

    private async Task EditMemoryAsync(ControlCenterMemoryItem displayed)
    {
        var current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        var titleBox = new UiTextBox
        {
            Text = current.Title,
            MinWidth = 430,
        };
        var contentBox = new WpfTextBox
        {
            Text = current.Content,
            MinWidth = 430,
            MinHeight = 130,
            MaxHeight = 260,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = RaisedSurfaceBrush,
            Foreground = PrimaryTextBrush,
            BorderBrush = StrongBorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10),
        };
        var categoryBox = BuildMemoryEditCombo(
            [
                "decision", "preference", "lesson", "error", "solution",
                "architecture", "workflow", "todo", "fact",
            ],
            current.Category);
        var retentionBox = BuildMemoryEditCombo(
            ["ephemeral", "session", "short", "standard", "durable", "permanent"],
            current.RetentionClass);

        var importanceEditor = BuildScoreEditor(
            "Önem",
            current.Importance,
            out var importanceSlider);
        var confidenceEditor = BuildScoreEditor(
            "Güven",
            current.Confidence,
            out var confidenceSlider);

        var form = new StackPanel { MinWidth = 460 };
        form.Children.Add(BuildMemoryField("Başlık", titleBox));
        form.Children.Add(BuildMemoryField("İçerik", contentBox));

        var choices = new WrapPanel();
        choices.Children.Add(BuildMemoryField("Kategori", categoryBox));
        var retentionField = BuildMemoryField("Saklama", retentionBox);
        retentionField.Margin = new Thickness(12, 0, 0, 0);
        choices.Children.Add(retentionField);
        form.Children.Add(choices);
        form.Children.Add(importanceEditor);
        form.Children.Add(confidenceEditor);

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "Hafıza kaydını düzenle",
            Content = form,
            PrimaryButtonText = "Kaydet",
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonText = "Vazgeç",
            CloseButtonAppearance = ControlAppearance.Secondary,
        };
        if (await dialog.ShowDialogAsync(
                showAsDialog: true,
                cancellationToken: _lifetimeCts.Token) !=
            Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var title = titleBox.Text.Trim();
        var content = contentBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title) ||
            string.IsNullOrWhiteSpace(content))
        {
            SetMemoryStatus(
                "Başlık ve içerik boş bırakılamaz.",
                AttentionBrush);
            return;
        }

        current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        var updated = await ControlCenterMemoryService.UpdateAsync(
            current.Id,
            title,
            content,
            categoryBox.SelectedItem?.ToString(),
            importanceSlider.Value,
            confidenceSlider.Value,
            null,
            retentionBox.SelectedItem?.ToString(),
            _lifetimeCts.Token);
        if (!updated.Success)
        {
            throw new InvalidOperationException("Hafıza kaydı güncellenemedi.");
        }

        SetMemoryStatus("Hafıza kaydı güncellendi.", ReadyBrush);
        await RefreshMemoryAsync();
    }

    private async Task ExpireMemoryAsync(ControlCenterMemoryItem displayed)
    {
        var current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        if (!await ConfirmMemoryMutationAsync(
                "Bu kayıt normal aramadan çıkarılsın mı?",
                $"“{current.Title}” kaydının süresi şimdi bitecek. Kayıt denetim amacıyla veritabanında kalır.",
                "Süresini bitir",
                danger: false))
        {
            return;
        }

        current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        var updated = await ControlCenterMemoryService.UpdateAsync(
            current.Id,
            null,
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            null,
            _lifetimeCts.Token);
        if (!updated.Success)
        {
            throw new InvalidOperationException(
                "Hafıza kaydının süresi güncellenemedi.");
        }

        SetMemoryStatus(
            "Kayıt normal hafıza aramasından çıkarıldı.",
            ReadyBrush);
        await RefreshMemoryAsync();
    }

    private async Task ForgetMemoryAsync(ControlCenterMemoryItem displayed)
    {
        var current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        if (!await ConfirmMemoryMutationAsync(
                "Bu hafıza kalıcı olarak unutulsun mu?",
                $"“{current.Title}” kalıcı olarak silinecek. FTS ve semantic embedding kaydı da kaldırılır. Bu işlem geri alınamaz.",
                "Kalıcı olarak unut",
                danger: true))
        {
            return;
        }

        current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        var forgotten = await ControlCenterMemoryService.ForgetAsync(
            current.Id,
            _lifetimeCts.Token);
        if (!forgotten.Deleted)
        {
            throw new InvalidOperationException("Hafıza kaydı silinemedi.");
        }

        SetMemoryStatus("Hafıza kalıcı olarak unutuldu.", ReadyBrush);
        await RefreshMemoryAsync();
    }

    private async Task SupersedeMemoryAsync(ControlCenterMemoryItem displayed)
    {
        var current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        var replacementBox = new UiTextBox
        {
            MinWidth = 440,
            PlaceholderText = "Yeni/geçerli hafıza kaydının kimliği",
        };
        var form = new StackPanel();
        form.Children.Add(new TextBlock
        {
            Text =
                "Bu kayıt silinmez; daha güncel bir kayıt tarafından geçersiz kılındığı işaretlenir.",
            Foreground = SecondaryTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12),
        });
        form.Children.Add(replacementBox);

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = "Eski kaydı yeni kayıtla değiştir",
            Content = form,
            PrimaryButtonText = "Eski olarak işaretle",
            PrimaryButtonAppearance = ControlAppearance.Primary,
            CloseButtonText = "Vazgeç",
            CloseButtonAppearance = ControlAppearance.Secondary,
        };
        if (await dialog.ShowDialogAsync(
                true,
                _lifetimeCts.Token) != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var replacementId = replacementBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(replacementId) ||
            string.Equals(replacementId, current.Id, StringComparison.Ordinal))
        {
            SetMemoryStatus(
                "Yerine geçecek farklı bir hafıza kimliği girilmelidir.",
                AttentionBrush);
            return;
        }

        current = await EnsureMemoryCurrentAsync(displayed);
        if (current is null)
        {
            return;
        }

        var replacement = await ControlCenterMemoryService.GetAsync(
            replacementId,
            _lifetimeCts.Token);
        if (!replacement.Success || replacement.Item is null)
        {
            SetMemoryStatus(
                "Yerine geçecek hafıza kaydı bulunamadı.",
                AttentionBrush);
            return;
        }

        var result = await ControlCenterMemoryService.SupersedeAsync(
            current.Id,
            replacement.Item.Id,
            _lifetimeCts.Token);
        if (!result.Success)
        {
            throw new InvalidOperationException(
                "Eski hafıza kaydı güncel kayıtla ilişkilendirilemedi.");
        }

        SetMemoryStatus("Eski kayıt normal aramadan çıkarıldı.", ReadyBrush);
        await RefreshMemoryAsync();
    }

    private async Task<ControlCenterMemoryItem?> EnsureMemoryCurrentAsync(
        ControlCenterMemoryItem displayed)
    {
        var result = await ControlCenterMemoryService.GetAsync(
            displayed.Id,
            _lifetimeCts.Token);
        if (!result.Success || result.Item is null)
        {
            SetMemoryStatus(
                "Bu kayıt artık mevcut değil. Görünüm yenilendi.",
                AttentionBrush);
            await RefreshMemoryAsync();
            return null;
        }

        if (result.Item.UpdatedAtUtc != displayed.UpdatedAtUtc ||
            !string.Equals(
                result.Item.SupersededBy,
                displayed.SupersededBy,
                StringComparison.Ordinal))
        {
            SetMemoryStatus(
                "Bu kayıt siz görüntülerken değişti. Eski veriye işlem uygulanmadı; görünüm yenilendi.",
                AttentionBrush);
            await RefreshMemoryAsync();
            return null;
        }

        return result.Item;
    }

    private async Task<bool> ConfirmMemoryMutationAsync(
        string title,
        string content,
        string primaryText,
        bool danger)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            PrimaryButtonAppearance = danger
                ? ControlAppearance.Danger
                : ControlAppearance.Primary,
            CloseButtonText = "Vazgeç",
            CloseButtonAppearance = ControlAppearance.Secondary,
        };
        return await dialog.ShowDialogAsync(
                   true,
                   _lifetimeCts.Token) ==
               Wpf.Ui.Controls.MessageBoxResult.Primary;
    }

    private WpfComboBox BuildMemoryEditCombo(
        IEnumerable<string> items,
        string selected)
    {
        var combo = new WpfComboBox
        {
            Style = FindStyle("TalvoraFilterComboBoxStyle"),
            MinWidth = 180,
        };
        foreach (var item in items)
        {
            combo.Items.Add(item);
        }
        combo.SelectedItem = selected;
        return combo;
    }

    private StackPanel BuildScoreEditor(
        string label,
        double initialValue,
        out Slider slider)
    {
        var valueText = new TextBlock
        {
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            Text = initialValue.ToString("P0", CultureInfo.CurrentCulture),
        };
        slider = new Slider
        {
            Minimum = 0,
            Maximum = 1,
            Value = initialValue,
            TickFrequency = 0.05,
            SmallChange = 0.05,
            LargeChange = 0.1,
            MinWidth = 430,
        };
        slider.ValueChanged += (_, e) =>
            valueText.Text = e.NewValue.ToString(
                "P0",
                CultureInfo.CurrentCulture);

        var header = new Grid();
        header.ColumnDefinitions.Add(
            new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });
        header.ColumnDefinitions.Add(
            new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
        });
        Grid.SetColumn(valueText, 1);
        header.Children.Add(valueText);

        var body = new StackPanel
        {
            Margin = new Thickness(0, 12, 0, 0),
        };
        body.Children.Add(header);
        body.Children.Add(slider);
        return body;
    }

    private StackPanel BuildMemoryField(string label, UIElement input)
    {
        var field = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 12),
        };
        field.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = SecondaryTextBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5),
        });
        field.Children.Add(input);
        return field;
    }

    private void SetMemoryStatus(
        string message,
        System.Windows.Media.Brush brush)
    {
        _memoryStatusText.Text = message;
        _memoryStatusText.Foreground = brush;
    }

    private static bool IsExpectedMemoryUiFailure(Exception ex) =>
        ex is TimeoutException
            or InvalidOperationException
            or System.IO.InvalidDataException
            or HttpRequestException
            or ArgumentException;
}
