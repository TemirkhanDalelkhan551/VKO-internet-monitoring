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
    private readonly Button reportProblemButton = new();
    private readonly Button setupButton = new();
    private readonly LocalTrendChart trendChart = new();
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
        reportProblemButton.Click += async (_, _) => await ReportProblemAsync();
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
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CreateSummaryPanel(), 0, 0);
        content.Controls.Add(CreateMetricsPanel(), 0, 1);
        content.Controls.Add(CreateHistoryHeader(), 0, 2);
        content.Controls.Add(CreateHistoryAndChart(), 0, 3);
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
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

    private Control CreateHistoryAndChart()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(history, 0, 0);
        var trend = new Panel { Dock = DockStyle.Fill, BackColor = CardColor, Margin = new Padding(0, 14, 0, 0), Padding = new Padding(12, 8, 12, 8) };
        trendChart.Dock = DockStyle.Fill;
        trend.Controls.Add(trendChart);
        trend.Controls.Add(new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 9F),
            ForeColor = MutedColor,
            Text = "Динамика за последние 24 часа · Download и Ping"
        });
        layout.Controls.Add(trend, 0, 1);
        return layout;
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
        var title = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = new Font("Segoe UI Semibold", 12F),
            Text = "Последние измерения"
        };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 38,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 2, 0, 0)
        };
        refreshButton.Text = "Обновить";
        refreshButton.Width = 120;
        refreshButton.Height = 32;
        refreshButton.FlatStyle = FlatStyle.Flat;
        refreshButton.FlatAppearance.BorderColor = PrimaryColor;
        refreshButton.ForeColor = PrimaryColor;
        measureNowButton.Text = "Проверить интернет сейчас";
        measureNowButton.Width = 215;
        measureNowButton.Height = 32;
        measureNowButton.Margin = new Padding(0, 0, 8, 0);
        measureNowButton.FlatStyle = FlatStyle.Flat;
        measureNowButton.FlatAppearance.BorderColor = PrimaryColor;
        measureNowButton.BackColor = PrimaryColor;
        measureNowButton.ForeColor = Color.White;
        diagnosticsButton.Text = "Скопировать диагностику";
        diagnosticsButton.Width = 185;
        diagnosticsButton.Height = 32;
        diagnosticsButton.Margin = new Padding(0, 0, 8, 0);
        diagnosticsButton.FlatStyle = FlatStyle.Flat;
        diagnosticsButton.FlatAppearance.BorderColor = Color.FromArgb(160, 173, 185);
        diagnosticsButton.ForeColor = Color.FromArgb(55, 75, 92);
        reportProblemButton.Text = "Сообщить о проблеме";
        reportProblemButton.Width = 230;
        reportProblemButton.Height = 32;
        reportProblemButton.Font = new Font("Segoe UI", 9F);
        reportProblemButton.Margin = new Padding(0, 0, 8, 0);
        reportProblemButton.FlatStyle = FlatStyle.Flat;
        reportProblemButton.FlatAppearance.BorderColor = Color.FromArgb(183, 98, 0);
        reportProblemButton.ForeColor = Color.FromArgb(148, 78, 0);
        actions.Controls.Add(reportProblemButton);
        actions.Controls.Add(diagnosticsButton);
        actions.Controls.Add(measureNowButton);
        actions.Controls.Add(refreshButton);
        panel.Controls.Add(actions);
        panel.Controls.Add(title);
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
            var result = await measurementRequestService.RequestAsync(CancellationToken.None);
            measuredAt.Text =
                "Тестовый замер запрошен. Обычно результат появляется через 1–3 минуты; " +
                "при высокой нагрузке — позднее.";
            MessageBox.Show(
                (result.ServiceWasStarted
                    ? "Остановленная служба запущена автоматически. "
                    : string.Empty) +
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
        connectionState.Text = $"● {TranslateStatus(device.Status)}";
        connectionState.ForeColor = StatusColor(device.Status);
        serviceState.Text = view.IsServiceRunning ? "Работает" : "Остановлена";
        serviceState.ForeColor = view.IsServiceRunning ? Color.FromArgb(31, 122, 78) : Color.Firebrick;
        lastSeen.Text = FormatDate(device.LastSeenAtUtc);
        serverAddress.Text = view.ServerAddress.ToString();
        var measurement = device.LatestMeasurement;
        var latestDetails = view.ServerStatus.RecentMeasurements.FirstOrDefault();
        var issue = DescribeIssue(device.Status, measurement, latestDetails?.FailureReason, view.ServerStatus.QualityThresholds);
        deviceDetails.Text = $"{issue} · {device.Name} · кабинет {device.Room ?? "не указан"}";

        downloadValue.Text = FormatMetric(measurement?.DownloadMbps, "Мбит/с");
        uploadValue.Text = FormatMetric(measurement?.UploadMbps, "Мбит/с");
        pingValue.Text = FormatMetric(measurement?.PingMilliseconds, "мс");
        jitterValue.Text = FormatMetric(measurement?.JitterMilliseconds, "мс");
        lossValue.Text = FormatMetric(measurement?.PacketLossPercent, "%");
        measuredAt.Text = measurement is null
            ? "Данных измерения пока нет"
            : $"Последнее измерение: {measurement.MeasuredAtUtc.ToLocalTime():dd.MM.yyyy HH:mm:ss}" +
              $" · {FormatDuration(latestDetails?.DurationMilliseconds)}" +
              $" · {TranslateConnectionType(latestDetails?.NetworkConnectionType)}" +
              $" · сервер {latestDetails?.MeasurementServer ?? "—"}" +
              $" · внешний IP {latestDetails?.ExternalIpAddress ?? "—"}" +
              $" · {FormatNextMeasurement(view.MeasurementWindows)}";
        trendChart.SetMeasurements(view.ServerStatus.RecentMeasurements);

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
            $"Служба: {FormatServiceState(view)}",
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

    private async Task ReportProblemAsync()
    {
        if (lastView?.ServerStatus.Device.LatestMeasurement is null)
        {
            MessageBox.Show("Сначала дождитесь хотя бы одного измерения, чтобы приложить фактические показатели.",
                "Нет измерений", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new ProblemReportDialog(lastView.ServerStatus.Device.LatestMeasurement);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        reportProblemButton.Enabled = false;
        reportProblemButton.Text = "Отправляем…";
        try
        {
            var result = await statusService.SubmitProblemReportAsync(dialog.Comment, CancellationToken.None);
            MessageBox.Show(result.Created
                    ? "Обращение зарегистрировано и передано в веб-панель со статусом «Новое». Оно не отправлялось провайдеру автоматически."
                    : "По этой линии уже есть активный инцидент. Ваше обращение и фактические показатели добавлены в его журнал.",
                "Обращение принято", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "Не удалось отправить обращение", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            reportProblemButton.Enabled = true;
            reportProblemButton.Text = "Сообщить о проблеме";
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

    private static string FormatServiceState(LocalStatusView? view) => view switch
    {
        null => "не удалось проверить",
        { IsServiceRunning: true } => "работает",
        _ => "остановлена"
    };

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

    private static string DescribeIssue(
        string status,
        LocalMeasurementSnapshot? measurement,
        string? failureReason,
        LocalQualityThresholds? thresholds)
    {
        if (!string.IsNullOrWhiteSpace(failureReason)) return $"Причина: {failureReason}";
        if (measurement is not null && thresholds is not null)
        {
            var issues = new List<string>();
            if (measurement.DownloadMbps < (double)thresholds.MinimumDownloadMbps)
                issues.Add($"Download ниже порога ({FormatMetric(measurement.DownloadMbps, "Мбит/с")} при норме от {thresholds.MinimumDownloadMbps:0.##})");
            if (measurement.UploadMbps < (double)thresholds.MinimumUploadMbps)
                issues.Add($"Upload ниже порога ({FormatMetric(measurement.UploadMbps, "Мбит/с")} при норме от {thresholds.MinimumUploadMbps:0.##})");
            if (measurement.PingMilliseconds > (double)thresholds.MaximumPingMilliseconds)
                issues.Add($"Ping выше порога ({FormatMetric(measurement.PingMilliseconds, "мс")} при норме до {thresholds.MaximumPingMilliseconds:0.##})");
            if (measurement.JitterMilliseconds > (double)thresholds.MaximumJitterMilliseconds)
                issues.Add($"Jitter выше порога ({FormatMetric(measurement.JitterMilliseconds, "мс")} при норме до {thresholds.MaximumJitterMilliseconds:0.##})");
            if (measurement.PacketLossPercent > (double)thresholds.MaximumPacketLossPercent)
                issues.Add($"Потери выше порога ({FormatMetric(measurement.PacketLossPercent, "%")} при норме до {thresholds.MaximumPacketLossPercent:0.##}%)");
            if (issues.Count > 0) return $"Причина: {string.Join("; ", issues)}";
        }
        return status switch
        {
            "Normal" or "Online" => "Причина: показатели в норме",
            "NoConnection" or "Offline" => "Причина: нет соединения с интернетом",
            "Critical" => "Причина: критичное отклонение одного или нескольких показателей",
            "Unstable" or "Degraded" => "Причина: один или несколько показателей вне нормы",
            _ => "Причина: ожидается анализ следующего измерения"
        };
    }

    private static string FormatNextMeasurement(IEnumerable<string> windows)
    {
        var now = DateTime.Now;
        var starts = windows.Select(window => window.Split('-', 2))
            .Where(parts => parts.Length == 2 && TimeOnly.TryParse(parts[0], out _))
            .Select(parts => TimeOnly.Parse(parts[0]))
            .Select(time => now.Date.Add(time.ToTimeSpan()))
            .Select(start => start > now ? start : start.AddDays(1))
            .OrderBy(start => start)
            .FirstOrDefault();
        if (starts == default) return "Автопроверка: по расписанию службы";
        var remaining = starts - now;
        var wait = remaining.TotalHours >= 1
            ? $"через {(int)remaining.TotalHours} ч {remaining.Minutes} мин"
            : $"через {Math.Max(1, remaining.Minutes)} мин";
        return $"Следующая автопроверка {wait} ({starts:HH:mm})";
    }
}

internal sealed class ProblemReportDialog : Form
{
    private readonly TextBox comment = new() { Multiline = true, MaxLength = 4000, ScrollBars = ScrollBars.Vertical };
    public string Comment => comment.Text.Trim();

    public ProblemReportDialog(LocalMeasurementSnapshot measurement)
    {
        Text = "Сообщить о проблеме";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 330);
        MinimumSize = new Size(520, 300);
        Font = new Font("Segoe UI", 10F);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { AutoSize = true, Font = new Font("Segoe UI Semibold", 12F), Text = "Обращение будет передано администратору" }, 0, 0);
        layout.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(570, 0),
            ForeColor = Color.FromArgb(83, 96, 110),
            Text = $"К обращению будут приложены фактические показатели: Download {Metric(measurement.DownloadMbps, "Мбит/с")}, Ping {Metric(measurement.PingMilliseconds, "мс")}, Jitter {Metric(measurement.JitterMilliseconds, "мс")}, потери {Metric(measurement.PacketLossPercent, "%")}. Провайдеру оно автоматически не отправляется."
        }, 0, 1);
        comment.Dock = DockStyle.Fill;
        comment.Margin = new Padding(0, 14, 0, 10);
        comment.PlaceholderText = "Опишите, что наблюдаете: когда началась проблема, какие сервисы недоступны, что уже пробовали сделать…";
        layout.Controls.Add(comment, 0, 2);
        layout.Controls.Add(new Label { AutoSize = true, ForeColor = Color.FromArgb(83, 96, 110), Text = "Обращение появится в разделе «Инциденты» веб-панели." }, 0, 3);
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 10, 0, 0) };
        var send = new Button { Text = "Отправить на рассмотрение", AutoSize = true };
        var cancel = new Button { Text = "Отмена", AutoSize = true, DialogResult = DialogResult.Cancel };
        send.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(Comment))
            {
                MessageBox.Show("Опишите проблему перед отправкой.", "Нужно описание", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        buttons.Controls.Add(send);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);
        AcceptButton = send;
        CancelButton = cancel;
    }

    private static string Metric(double? value, string unit) => value is null ? "—" : $"{value:0.##} {unit}";
}

internal sealed class LocalTrendChart : Control
{
    private IReadOnlyList<LocalMeasurement> values = [];

    public LocalTrendChart()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
    }

    public void SetMeasurements(IReadOnlyList<LocalMeasurement> measurements)
    {
        values = measurements.Take(12).Reverse().ToArray();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (values.Count < 2)
        {
            TextRenderer.DrawText(e.Graphics, "Нужно минимум два измерения для построения динамики", Font,
                ClientRectangle, Color.FromArgb(110, 120, 130), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        var area = Rectangle.Inflate(ClientRectangle, -6, -5);
        var download = values.Select(value => value.DownloadMbps).ToArray();
        var ping = values.Select(value => value.PingMilliseconds).ToArray();
        DrawSeries(e.Graphics, area, download, Color.FromArgb(31, 122, 78));
        DrawSeries(e.Graphics, area, ping, Color.FromArgb(27, 94, 163));
        TextRenderer.DrawText(e.Graphics, "● Download", Font, new Point(area.Left, area.Top), Color.FromArgb(31, 122, 78));
        TextRenderer.DrawText(e.Graphics, "● Ping", Font, new Point(area.Left + 95, area.Top), Color.FromArgb(27, 94, 163));
    }

    private static void DrawSeries(Graphics graphics, Rectangle area, IReadOnlyList<double?> series, Color color)
    {
        var points = series.Where(value => value is not null).Select(value => value!.Value).ToArray();
        if (points.Length < 2) return;
        var min = points.Min();
        var range = Math.Max(0.01, points.Max() - min);
        var drawArea = Rectangle.FromLTRB(area.Left, area.Top + 18, area.Right, area.Bottom);
        using var pen = new Pen(color, 2F);
        PointF? previous = null;
        for (var index = 0; index < series.Count; index++)
        {
            if (series[index] is not double value) { previous = null; continue; }
            var x = drawArea.Left + index * drawArea.Width / Math.Max(1, series.Count - 1);
            var y = drawArea.Bottom - (float)((value - min) / range * drawArea.Height);
            var current = new PointF(x, y);
            if (previous is not null) graphics.DrawLine(pen, previous.Value, current);
            previous = current;
        }
    }
}
