using System.Drawing;
using VkoMonitoring.Agent.Setup.Activation;
using VkoMonitoring.Agent.Setup.Status;

namespace VkoMonitoring.Agent.Setup.UI;

public sealed class SetupForm : Form
{
    private static readonly Color PrimaryColor = Color.FromArgb(27, 94, 163);
    private static readonly Color SuccessColor = Color.FromArgb(31, 122, 78);
    private static readonly Color MutedColor = Color.FromArgb(92, 105, 121);
    private static readonly Color CardColor = Color.FromArgb(242, 247, 252);

    private readonly ActivationWorkflow workflow;
    private readonly LocalDeviceStatusService? statusService;
    private readonly TextBox serverAddress;
    private readonly TextBox activationCode = CreateTextBox();
    private readonly TextBox deviceName = CreateTextBox(Environment.MachineName);
    private readonly TextBox room = CreateTextBox();
    private readonly ComboBox connectionType = new();
    private readonly Button activateButton = new();
    private readonly Button editButton = new();
    private readonly Label statusLabel = new();
    private readonly Panel confirmationPanel = new();
    private readonly Label confirmationTitle = new();
    private readonly Label confirmationDetails = new();
    private CancellationTokenSource? activationCancellation;
    private ActivationInput? verifiedInput;

    public SetupForm(
        ActivationWorkflow workflow,
        Uri? defaultServerAddress = null,
        LocalDeviceStatusService? statusService = null)
    {
        this.workflow = workflow;
        this.statusService = statusService;
        serverAddress = CreateTextBox(
            (defaultServerAddress ?? new Uri("https://vko-internet-monitoring-api.onrender.com/")).AbsoluteUri);

        Text = "Настройка мониторинга интернета ВКО";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(720, 720);
        MinimumSize = new Size(700, 650);
        MaximizeBox = false;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.White;
        AutoScaleMode = AutoScaleMode.Dpi;

        Controls.Add(CreateContent());
        Controls.Add(CreateHeader());
        AcceptButton = activateButton;

        activationCode.CharacterCasing = CharacterCasing.Upper;
        activationCode.MaxLength = 32;
        connectionType.DropDownStyle = ComboBoxStyle.DropDownList;
        connectionType.Items.AddRange(["Ethernet", "Wi-Fi", "Другое"]);
        connectionType.SelectedIndex = 0;

        activateButton.Click += ActivateButtonClick;
        editButton.Click += (_, _) => ReturnToEditing();
        FormClosing += (_, _) => activationCancellation?.Cancel();
    }

    private static Panel CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 122,
            BackColor = PrimaryColor,
            Padding = new Padding(28, 22, 28, 16)
        };
        var title = new Label
        {
            AutoSize = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 18F),
            Text = "Подключение компьютера"
        };
        var subtitle = new Label
        {
            AutoSize = false,
            Location = new Point(31, 65),
            Size = new Size(650, 42),
            ForeColor = Color.FromArgb(225, 237, 250),
            Text = "Шаг 1 — проверка компьютера и кода. Шаг 2 — подтверждение школы и запуск службы."
        };
        panel.Controls.Add(title);
        panel.Controls.Add(subtitle);
        return panel;
    }

    private Control CreateContent()
    {
        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 20, 28, 22),
            AutoScroll = true
        };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 8
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(fields, 0, "Адрес сервера", serverAddress,
            "Адрес уже заполнен для рабочего сервера. Изменяйте его только по инструкции администратора.");
        AddField(fields, 1, "Код активации", activationCode,
            "Одноразовый код выдаётся в веб-панели и проверяется без расходования.");
        AddField(fields, 2, "Название компьютера", deviceName,
            "Например: Кабинет 205 — компьютер учителя.");
        AddField(fields, 3, "Кабинет", room, "Можно оставить пустым.");
        AddField(fields, 4, "Тип подключения", connectionType, null);

        ConfigureConfirmationPanel();
        fields.Controls.Add(confirmationPanel, 0, 5);
        fields.SetColumnSpan(confirmationPanel, 2);

        statusLabel.AutoSize = false;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Height = 52;
        statusLabel.ForeColor = MutedColor;
        statusLabel.Text = "Сначала проверим компьютер, сервер и код. Код будет использован только после подтверждения.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        fields.Controls.Add(statusLabel, 0, 6);
        fields.SetColumnSpan(statusLabel, 2);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };
        activateButton.Text = "Проверить и продолжить";
        activateButton.Size = new Size(245, 46);
        activateButton.BackColor = PrimaryColor;
        activateButton.ForeColor = Color.White;
        activateButton.FlatStyle = FlatStyle.Flat;
        activateButton.FlatAppearance.BorderSize = 0;
        activateButton.Cursor = Cursors.Hand;
        editButton.Text = "Изменить данные";
        editButton.Size = new Size(165, 46);
        editButton.BackColor = Color.White;
        editButton.ForeColor = PrimaryColor;
        editButton.FlatStyle = FlatStyle.Flat;
        editButton.FlatAppearance.BorderColor = PrimaryColor;
        editButton.Visible = false;
        actions.Controls.Add(activateButton);
        actions.Controls.Add(editButton);
        fields.Controls.Add(actions, 1, 7);

        content.Controls.Add(fields);
        return content;
    }

    private void ConfigureConfirmationPanel()
    {
        confirmationPanel.AutoSize = true;
        confirmationPanel.Dock = DockStyle.Top;
        confirmationPanel.BackColor = CardColor;
        confirmationPanel.Padding = new Padding(18, 14, 18, 14);
        confirmationPanel.Margin = new Padding(0, 6, 0, 8);
        confirmationPanel.Visible = false;

        confirmationTitle.AutoSize = true;
        confirmationTitle.Dock = DockStyle.Top;
        confirmationTitle.Font = new Font("Segoe UI Semibold", 12F);
        confirmationTitle.ForeColor = SuccessColor;
        confirmationTitle.Text = "Проверка пройдена";
        confirmationDetails.AutoSize = true;
        confirmationDetails.Dock = DockStyle.Top;
        confirmationDetails.ForeColor = Color.FromArgb(45, 65, 82);
        confirmationDetails.Padding = new Padding(0, 8, 0, 0);
        confirmationPanel.Controls.Add(confirmationDetails);
        confirmationPanel.Controls.Add(confirmationTitle);
    }

    private static void AddField(TableLayoutPanel layout, int row, string labelText, Control input, string? hint)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var label = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 10, 14, 0),
            Text = labelText
        };
        input.Dock = DockStyle.Top;
        input.Margin = new Padding(0, 6, 0, hint is null ? 14 : 0);

        layout.Controls.Add(label, 0, row);
        if (hint is null)
        {
            layout.Controls.Add(input, 1, row);
            return;
        }

        var valuePanel = new TableLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        var hintLabel = new Label
        {
            AutoSize = true,
            ForeColor = MutedColor,
            Margin = new Padding(0, 4, 0, 12),
            Text = hint
        };
        valuePanel.Controls.Add(input, 0, 0);
        valuePanel.Controls.Add(hintLabel, 0, 1);
        layout.Controls.Add(valuePanel, 1, row);
    }

    private async void ActivateButtonClick(object? sender, EventArgs eventArgs)
    {
        if (verifiedInput is null)
        {
            await CheckAndContinueAsync();
            return;
        }

        await ActivateAsync(verifiedInput);
    }

    private async Task CheckAndContinueAsync()
    {
        try
        {
            var input = ReadInput();
            SetBusy(true, "Проверяем Windows, службу, сервер и код…");
            activationCancellation = new CancellationTokenSource();
            var preview = await workflow.PreflightAsync(input, activationCancellation.Token);

            verifiedInput = input;
            confirmationDetails.Text =
                $"Школа: {preview.SchoolName}\n" +
                $"Линия: {preview.LineName}\n" +
                $"Поставщик: {preview.ProviderName ?? "не указан"}\n" +
                $"Тип линии: {preview.ConnectionType ?? "не указан"}\n" +
                $"Код действует до: {preview.ExpiresAtUtc.ToLocalTime():dd.MM.yyyy HH:mm}" +
                (preview.IsRecovery ? "\n\nБудет восстановлена незавершённая активация этого компьютера." : string.Empty);
            if (preview.IsReconfiguration)
            {
                confirmationDetails.Text += "\n\nЭтот компьютер уже зарегистрирован. Будет обновлена привязка к указанной линии и выпущен новый токен.";
            }
            confirmationPanel.Visible = true;
            editButton.Visible = true;
            activateButton.Text = "Подтвердить и запустить";
            statusLabel.ForeColor = SuccessColor;
            statusLabel.Text = "Проверка пройдена. Убедитесь, что школа и линия выбраны верно.";
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Проверка отменена.";
            statusLabel.ForeColor = MutedColor;
        }
        catch (Exception exception)
        {
            ShowError("Не удалось пройти проверку.", exception);
        }
        finally
        {
            DisposeCancellation();
            SetBusy(false);
        }
    }

    private async Task ActivateAsync(ActivationInput input)
    {
        try
        {
            SetBusy(true, "Регистрируем компьютер, защищаем токен и запускаем службу…");
            activationCancellation = new CancellationTokenSource();
            var result = await workflow.ExecuteAsync(input, activationCancellation.Token);
            statusLabel.Text = "Служба запущена. Проверяем первую связь с сервером…";
            var heartbeatConfirmed = await WaitForHeartbeatAsync(activationCancellation.Token);
            statusLabel.ForeColor = heartbeatConfirmed ? SuccessColor : Color.FromArgb(191, 112, 0);
            statusLabel.Text = heartbeatConfirmed
                ? "Готово: служба запущена, сервер получил первую связь."
                : "Служба запущена. Первая связь пока не подтверждена; проверьте локальное состояние через минуту.";

            MessageBox.Show(
                (result.Reconfigured
                    ? "Компьютер успешно переподключён к выбранной линии.\n\n"
                    : result.Recovered ? "Активация компьютера успешно восстановлена.\n\n" : "Компьютер успешно подключён.\n\n") +
                $"Устройство: {input.DeviceName}\n" +
                $"Идентификатор: {result.DeviceId:D}\n\n" +
                (heartbeatConfirmed
                    ? "Служба работает, первая связь с сервером подтверждена."
                    : "Служба работает. Первая связь ещё не подтверждена; откройте локальное состояние через минуту."),
                "Компьютер подключён",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Активация отменена. Повторите её на этом компьютере с тем же кодом в течение 24 часов.";
            statusLabel.ForeColor = MutedColor;
        }
        catch (Exception exception)
        {
            ReturnToEditing();
            ShowError("Не удалось завершить активацию.", exception);
        }
        finally
        {
            DisposeCancellation();
            if (!IsDisposed)
            {
                SetBusy(false);
            }
        }
    }

    private async Task<bool> WaitForHeartbeatAsync(CancellationToken cancellationToken)
    {
        if (statusService is null)
        {
            return false;
        }

        using var heartbeatCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        heartbeatCancellation.CancelAfter(TimeSpan.FromSeconds(15));
        while (!heartbeatCancellation.IsCancellationRequested)
        {
            try
            {
                var view = await statusService.GetAsync(heartbeatCancellation.Token);
                if (view.IsServiceRunning && view.ServerStatus.Device.LastSeenAtUtc is not null)
                {
                    return true;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (HttpRequestException)
            {
            }
            catch (InvalidOperationException)
            {
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(1), heartbeatCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return false;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return false;
    }

    private ActivationInput ReadInput()
    {
        if (!Uri.TryCreate(serverAddress.Text.Trim(), UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps &&
             !(serverUri.Scheme == Uri.UriSchemeHttp && serverUri.IsLoopback)))
        {
            throw new InvalidOperationException(
                "Введите корректный HTTPS-адрес сервера. HTTP разрешён только для localhost.");
        }

        var code = activationCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("Введите одноразовый код активации.");
        }
        var normalizedCode = new string(code
            .Where(character => character is not ('-' or ' '))
            .Select(char.ToUpperInvariant)
            .ToArray());
        const string activationAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        if (normalizedCode.Length != 12 ||
            normalizedCode.Any(character => !activationAlphabet.Contains(character, StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                "Код должен содержать 12 символов в формате XXXX-XXXX-XXXX. Буквы I и O в кодах не используются.");
        }

        var name = deviceName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Введите понятное название компьютера.");
        }

        return new ActivationInput(
            serverUri,
            normalizedCode,
            name,
            NullIfEmpty(room.Text),
            connectionType.SelectedItem?.ToString() ?? "Ethernet");
    }

    private void ReturnToEditing()
    {
        verifiedInput = null;
        confirmationPanel.Visible = false;
        editButton.Visible = false;
        activateButton.Text = "Проверить и продолжить";
        statusLabel.ForeColor = MutedColor;
        statusLabel.Text = "Измените данные и повторите проверку. Если сервер уже принял код, этот компьютер восстановит активацию автоматически.";
        SetBusy(false);
        activationCode.Focus();
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        var canEdit = !isBusy && verifiedInput is null;
        serverAddress.Enabled = canEdit;
        activationCode.Enabled = canEdit;
        deviceName.Enabled = canEdit;
        room.Enabled = canEdit;
        connectionType.Enabled = canEdit;
        activateButton.Enabled = !isBusy;
        editButton.Enabled = !isBusy;
        UseWaitCursor = isBusy;
        if (message is not null)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = PrimaryColor;
        }
    }

    private void ShowError(string summary, Exception exception)
    {
        statusLabel.Text = summary + " Выполните указанное действие и повторите.";
        statusLabel.ForeColor = Color.Firebrick;
        MessageBox.Show(
            exception.Message,
            "Ошибка настройки",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void DisposeCancellation()
    {
        activationCancellation?.Dispose();
        activationCancellation = null;
    }

    private static TextBox CreateTextBox(string text = "") => new()
    {
        Text = text,
        BorderStyle = BorderStyle.FixedSingle,
        Height = 30
    };

    private static string? NullIfEmpty(string value)
    {
        var trimmed = value.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
