using System.Drawing;
using System.Runtime.InteropServices;
using VkoMonitoring.Agent.Setup.Activation;
using VkoMonitoring.Agent.Setup.Status;

namespace VkoMonitoring.Agent.Setup.UI;

public sealed class DeviceStatusForm : Form
{
    private static readonly Color PrimaryColor = Color.FromArgb(27, 94, 163);
    private static readonly Color MutedColor = Color.FromArgb(92, 105, 121);
    private static readonly Color CardColor = Color.FromArgb(246, 248, 251);

    private readonly LocalDeviceStatusService statusService;
    private readonly ActivationWorkflow activationWorkflow;
    private readonly LocalMeasurementRequestService measurementRequestService;
    private readonly Label connectionState = CreateValueLabel("Загрузка…", 18F);
    private readonly Label deviceDetails = CreateValueLabel("—", 10F);
    private readonly Label serviceState = CreateValueLabel("—", 11F);
    private readonly Label lastSeen = CreateValueLabel("—", 11F);
    private readonly Label serverAddress = CreateValueLabel("—", 10F);
    private readonly Label downloadValue = CreateValueLabel("—", 17F);
    private readonly Label uploadValue = CreateValueLabel("—", 17F);
    private readonly Label pingValue = CreateValueLabel("—", 17F);
    private readonly Label jitterValue = CreateValueLabel("—", 17F);
    private readonly Label lossValue = CreateValueLabel("—", 17F);
    private readonly Label measuredAt = CreateValueLabel("Данных измерения пока нет", 9F);
    private readonly DataGridView history = new();
    private readonly Button refreshButton = new();
    private readonly Button measureNowButton = new();
    private readonly Button diagnosticsButton = new();
    private readonly Button setupButton = new();
    private readonly System.Windows.Forms.Timer refreshTimer = new() { Interval = 60_000 };
    private CancellationTokenSource? refreshCancellation;
    private bool refreshInProgress;
    private LocalStatusView? lastView;
    private string? lastError;

    public DeviceStatusForm(
        LocalDeviceStatusService statusService,
        ActivationWorkflow activationWorkflow,
        LocalMeasurementRequestService measurementRequestService)
    {
        this.statusService = statusService;
        this.activationWorkflow = activationWorkflow;
        this.measurementRequestService = measurementRequestService;

        Text = "Мониторинг интернета ВКО — состояние компьютера";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(940, 720);
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(CreateContent());
        Controls.Add(CreateHeader());
        ConfigureHistory();

        Shown += async (_, _) =>
        {
            await RefreshStatusAsync();
            refreshTimer.Start();
        };
        refreshTimer.Tick += async (_, _) => await RefreshStatusAsync();
        refreshButton.Click += async (_, _) => await RefreshStatusAsync();
        measureNowButton.Click += async (_, _) => await RequestMeasurementAsync();
        diagnosticsButton.Click += (_, _) => CopyDiagnostics();
        setupButton.Click += OpenSetup;
        Load += (_, _) => FitToWorkingArea();
        FormClosing += (_, _) =>
        {
            refreshTimer.Stop();
            refreshCancellation?.Cancel();
        };
    }

    private Panel CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 118,
            BackColor = PrimaryColor,
            Padding = new Padding(28, 20, 28, 16)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = PrimaryColor,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Text = "Состояние этого компьютера"
        }, 0, 0);
        layout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(225, 237, 250),
            Text = "Здесь отображаются только локальное устройство и его последние измерения."
        }, 0, 1);

        setupButton.Text = "Повторная настройка";
        setupButton.Dock = DockStyle.Fill;
        setupButton.Margin = new Padding(10, 12, 0, 12);
        setupButton.FlatStyle = FlatStyle.Flat;
        setupButton.FlatAppearance.BorderColor = Color.FromArgb(180, 211, 242);
        setupButton.ForeColor = Color.White;
        layout.Controls.Add(setupButton, 1, 0);
        layout.SetRowSpan(setupButton, 2);
        panel.Controls.Add(layout);
        return panel;
    }

    private Control CreateContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(26, 20, 26, 22),
            ColumnCount = 1,
            RowCount = 4,
            AutoScroll = true
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 144));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CreateSummaryPanel(), 0, 0);
        content.Controls.Add(CreateMetricsPanel(), 0, 1);
        content.Controls.Add(CreateHistoryHeader(), 0, 2);
        content.Controls.Add(history, 0, 3);
        return content;
    }

    private Control CreateSummaryPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardColor,
            Padding = new Padding(18, 12, 18, 12),
            ColumnCount = 3,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 14)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        AddSummary(panel, 0, "Состояние соединения", connectionState);
        AddSummary(panel, 1, "Служба Windows", serviceState);
        AddSummary(panel, 2, "Последняя связь", lastSeen);
        deviceDetails.ForeColor = MutedColor;
        deviceDetails.AutoSize = false;
        deviceDetails.AutoEllipsis = true;
        deviceDetails.Dock = DockStyle.Fill;
        panel.Controls.Add(deviceDetails, 0, 1);
        panel.SetColumnSpan(deviceDetails, 2);
        serverAddress.ForeColor = MutedColor;
        serverAddress.AutoSize = false;
        serverAddress.AutoEllipsis = true;
        serverAddress.TextAlign = ContentAlignment.MiddleRight;
        serverAddress.Dock = DockStyle.Fill;
        panel.Controls.Add(serverAddress, 2, 1);
        return panel;
    }

    private static void AddSummary(TableLayoutPanel panel, int column, string title, Label value)
    {
        var box = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty
        };
        box.Controls.Add(CreateCaption(title));
        box.Controls.Add(value);
        panel.Controls.Add(box, column, 0);
    }

    private Control CreateMetricsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 5,
            RowCount = 2,
            Margin = Padding.Empty
        };
        for (var index = 0; index < 5; index++)
        {
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        }
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(CreateMetricCard("Download", downloadValue), 0, 0);
        panel.Controls.Add(CreateMetricCard("Upload", uploadValue), 1, 0);
        panel.Controls.Add(CreateMetricCard("Ping", pingValue), 2, 0);
        panel.Controls.Add(CreateMetricCard("Jitter", jitterValue), 3, 0);
        panel.Controls.Add(CreateMetricCard("Потери", lossValue), 4, 0);
        measuredAt.ForeColor = MutedColor;
        measuredAt.AutoSize = false;
        measuredAt.Dock = DockStyle.Fill;
        measuredAt.TextAlign = ContentAlignment.MiddleLeft;
        panel.Controls.Add(measuredAt, 0, 1);
        panel.SetColumnSpan(measuredAt, 5);
        return panel;
    }

    private static Control CreateMetricCard(string title, Label value)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = CardColor,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(13, 9, 8, 8),
            Margin = new Padding(0, 0, 8, 8)
        };
        panel.Controls.Add(CreateCaption(title));
        panel.Controls.Add(value);
        return panel;
    }

    private Control CreateHistoryHeader()
    {
        var panel = new Panel { Dock = DockStyle.Fill };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Left,
            Font = new Font("Segoe UI Semibold", 12F),
            Text = "Последние измерения"
        });
        refreshButton.Text = "Обновить";
        refreshButton.Dock = DockStyle.Right;
        refreshButton.Width = 120;
        refreshButton.FlatStyle = FlatStyle.Flat;
        refreshButton.FlatAppearance.BorderColor = PrimaryColor;
        refreshButton.ForeColor = PrimaryColor;
        measureNowButton.Text = "Проверить интернет сейчас";
        measureNowButton.Dock = DockStyle.Right;
        measureNowButton.Width = 215;
        measureNowButton.Margin = new Padding(0, 0, 10, 0);
        measureNowButton.FlatStyle = FlatStyle.Flat;
        measureNowButton.FlatAppearance.BorderColor = PrimaryColor;
        measureNowButton.BackColor = PrimaryColor;
        measureNowButton.ForeColor = Color.White;
        diagnosticsButton.Text = "Скопировать диагностику";
        diagnosticsButton.Dock = DockStyle.Right;
        diagnosticsButton.Width = 205;
        diagnosticsButton.Margin = new Padding(0, 0, 10, 0);
        diagnosticsButton.FlatStyle = FlatStyle.Flat;
        diagnosticsButton.FlatAppearance.BorderColor = Color.FromArgb(160, 173, 185);
        diagnosticsButton.ForeColor = Color.FromArgb(55, 75, 92);
        panel.Controls.Add(refreshButton);
        panel.Controls.Add(measureNowButton);
        panel.Controls.Add(diagnosticsButton);
        return panel;
    }

    private async Task RequestMeasurementAsync()
    {
        var choice = MessageBox.Show(
            "Будет выполнена полная проверка Download, Upload, Ping, Jitter и потерь пакетов. " +
            "Она использует около 7 МБ трафика и может быть отложена при высокой нагрузке компьютера. Продолжить?",
            "Проверить интернет сейчас",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (choice != DialogResult.Yes)
        {
            return;
        }

        measureNowButton.Enabled = false;
        measureNowButton.Text = "Передаём запрос…";
        try
        {
            await measurementRequestService.RequestAsync(CancellationToken.None);
            measuredAt.Text =
                "Тестовый замер запрошен. Обычно результат появляется через 1–3 минуты; " +
                "при высокой нагрузке — позднее.";
            MessageBox.Show(
                "Запрос передан службе. Окно можно закрыть: измерение и доставка продолжатся в фоне. " +
                "Нажмите «Обновить» через 1–3 минуты.",
                "Тестовый замер запущен",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                exception.Message,
                "Не удалось запустить тестовый замер",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            measureNowButton.Enabled = true;
            measureNowButton.Text = "Проверить интернет сейчас";
        }
    }

    private void ConfigureHistory()
    {
        history.Dock = DockStyle.Fill;
        history.ReadOnly = true;
        history.AllowUserToAddRows = false;
        history.AllowUserToDeleteRows = false;
        history.AllowUserToResizeRows = false;
        history.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        history.ScrollBars = ScrollBars.Both;
        history.BackgroundColor = Color.White;
        history.BorderStyle = BorderStyle.FixedSingle;
        history.RowHeadersVisible = false;
        history.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        AddHistoryColumn("time", "Дата и время", 130, 1.2F);
        AddHistoryColumn("status", "Статус", 105, 1F);
        AddHistoryColumn("download", "Download", 110, 1F);
        AddHistoryColumn("upload", "Upload", 110, 1F);
        AddHistoryColumn("ping", "Ping", 90, 0.8F);
        AddHistoryColumn("loss", "Потери", 90, 0.8F);
    }

    private void AddHistoryColumn(string name, string title, int minimumWidth, float fillWeight)
    {
        var index = history.Columns.Add(name, title);
        history.Columns[index].MinimumWidth = minimumWidth;
        history.Columns[index].FillWeight = fillWeight;
    }

    private void FitToWorkingArea()
    {
        var workingArea = Screen.FromControl(this).WorkingArea;
        var maximumWidth = Math.Max(640, workingArea.Width - 24);
        var maximumHeight = Math.Max(520, workingArea.Height - 24);

        Size = new Size(
            Math.Min(Width, maximumWidth),
            Math.Min(Height, maximumHeight));
        MinimumSize = new Size(
            Math.Min(760, maximumWidth),
            Math.Min(560, maximumHeight));
        CenterToScreen();
    }

    private async Task RefreshStatusAsync()
    {
        if (refreshInProgress)
        {
            return;
        }

        refreshInProgress = true;
        refreshButton.Enabled = false;
        refreshCancellation?.Dispose();
        refreshCancellation = new CancellationTokenSource();
        try
        {
            connectionState.Text = "Обновление…";
            connectionState.ForeColor = PrimaryColor;
            var view = await statusService.GetAsync(refreshCancellation.Token);
            Render(view);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            connectionState.Text = "Нет связи";
            connectionState.ForeColor = Color.Firebrick;
            deviceDetails.Text = exception.Message;
            lastError = exception.Message;
        }
        finally
        {
            refreshButton.Enabled = true;
            refreshInProgress = false;
        }
    }

    private void Render(LocalStatusView view)
    {
        lastView = view;
        lastError = null;
        var device = view.ServerStatus.Device;
        connectionState.Text = TranslateStatus(device.Status);
        connectionState.ForeColor = StatusColor(device.Status);
        serviceState.Text = view.IsServiceRunning ? "Работает" : "Остановлена";
        serviceState.ForeColor = view.IsServiceRunning ? Color.FromArgb(31, 122, 78) : Color.Firebrick;
        lastSeen.Text = FormatDate(device.LastSeenAtUtc);
        serverAddress.Text = view.ServerAddress.ToString();
        deviceDetails.Text = $"{device.Name} · кабинет {device.Room ?? "не указан"} · {device.ConnectionType ?? "тип не указан"}";

        var measurement = device.LatestMeasurement;
        downloadValue.Text = FormatMetric(measurement?.DownloadMbps, "Мбит/с");
        uploadValue.Text = FormatMetric(measurement?.UploadMbps, "Мбит/с");
        pingValue.Text = FormatMetric(measurement?.PingMilliseconds, "мс");
        jitterValue.Text = FormatMetric(measurement?.JitterMilliseconds, "мс");
        lossValue.Text = FormatMetric(measurement?.PacketLossPercent, "%");
        var latestDetails = view.ServerStatus.RecentMeasurements.FirstOrDefault();
        measuredAt.Text = measurement is null
            ? "Данных измерения пока нет"
            : $"Последнее измерение: {measurement.MeasuredAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}" +
              $" · {FormatDuration(latestDetails?.DurationMilliseconds)}" +
              $" · {TranslateConnectionType(latestDetails?.NetworkConnectionType)}" +
              $" · сервер {latestDetails?.MeasurementServer ?? "—"}" +
              $" · внешний IP {latestDetails?.ExternalIpAddress ?? "—"}";

        history.Rows.Clear();
        foreach (var item in view.ServerStatus.RecentMeasurements)
        {
            history.Rows.Add(
                item.MeasuredAtUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm"),
                TranslateStatus(item.ConnectionStatus),
                FormatMetric(item.DownloadMbps, "Мбит/с"),
                FormatMetric(item.UploadMbps, "Мбит/с"),
                FormatMetric(item.PingMilliseconds, "мс"),
                FormatMetric(item.PacketLossPercent, "%"));
        }
    }

    private void CopyDiagnostics()
    {
        var view = lastView;
        var device = view?.ServerStatus.Device;
        var report = string.Join(Environment.NewLine,
        [
            "Диагностика агента мониторинга интернета ВКО",
            $"Сформировано: {DateTimeOffset.Now:dd.MM.yyyy HH:mm:ss zzz}",
            $"Версия: {Application.ProductVersion}",
            $"Windows: {RuntimeInformation.OSDescription}",
            $"Архитектура: {RuntimeInformation.OSArchitecture}",
            $"Компьютер: {Environment.MachineName}",
            $"Служба: {(view?.IsServiceRunning == true ? "работает" : "не подтверждена")}",
            $"Сервер: {view?.ServerAddress.ToString() ?? TryGetServerAddress()}",
            $"Устройство: {device?.Name ?? "нет данных"}",
            $"Device ID: {device?.DeviceId.ToString("D") ?? "нет данных"}",
            $"Линия ID: {device?.LineId.ToString("D") ?? "нет данных"}",
            $"Последняя связь: {FormatDate(device?.LastSeenAtUtc)}",
            $"Статус: {(device is null ? "нет данных" : TranslateStatus(device.Status))}",
            $"Последняя ошибка: {lastError ?? "нет"}",
            @"Журнал: C:\ProgramData\VkoInternetMonitoringAgent\logs",
            "Секреты и токен устройства в этот отчёт не включены."
        ]);

        try
        {
            Clipboard.SetText(report);
            MessageBox.Show(
                "Диагностический отчёт скопирован в буфер обмена. Его можно отправить техническому специалисту.",
                "Диагностика скопирована",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (ExternalException)
        {
            MessageBox.Show(
                report,
                "Диагностика агента",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }

    private string TryGetServerAddress()
    {
        try
        {
            return statusService.GetConfiguredServerAddress().ToString();
        }
        catch
        {
            return "не удалось прочитать";
        }
    }

    private void OpenSetup(object? sender, EventArgs eventArgs)
    {
        var choice = MessageBox.Show(
            "Повторная настройка потребует новый одноразовый код и изменит регистрацию этого компьютера. Продолжить?",
            "Повторная настройка",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (choice != DialogResult.Yes)
        {
            return;
        }

        using var form = new SetupForm(
            activationWorkflow,
            statusService.GetConfiguredServerAddress(),
            statusService);
        form.ShowDialog(this);
        _ = RefreshStatusAsync();
    }

    private static Label CreateCaption(string text) => new()
    {
        AutoSize = true,
        ForeColor = MutedColor,
        Font = new Font("Segoe UI", 9F),
        Text = text
    };

    private static Label CreateValueLabel(string text, float fontSize) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", fontSize),
        Text = text
    };

    private static string FormatMetric(double? value, string unit) =>
        value is null ? "—" : $"{value.Value:0.##} {unit}";

    private static string FormatDate(DateTimeOffset? value) =>
        value is null ? "Нет данных" : value.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

    private static string FormatDuration(long? milliseconds) =>
        milliseconds is null or <= 0 ? "длительность —" : $"{milliseconds.Value / 1000d:0.##} с";

    private static string TranslateConnectionType(string? connectionType) => connectionType switch
    {
        "Ethernet" => "Ethernet",
        "WiFi" => "Wi-Fi",
        "Mobile" => "мобильная сеть",
        "Other" => "другое подключение",
        _ => "тип подключения неизвестен"
    };

    private static string TranslateStatus(string status) => status switch
    {
        "Normal" or "Online" => "Норма",
        "Unstable" or "Degraded" => "Нестабильно",
        "Critical" => "Критично",
        "NoConnection" or "Offline" => "Нет соединения",
        _ => "Нет данных"
    };

    private static Color StatusColor(string status) => status switch
    {
        "Normal" or "Online" => Color.FromArgb(31, 122, 78),
        "Unstable" or "Degraded" => Color.FromArgb(191, 112, 0),
        "Critical" or "NoConnection" or "Offline" => Color.Firebrick,
        _ => MutedColor
    };
}
