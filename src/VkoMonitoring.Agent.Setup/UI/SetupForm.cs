using System.Drawing;
using VkoMonitoring.Agent.Setup.Activation;

namespace VkoMonitoring.Agent.Setup.UI;

public sealed class SetupForm : Form
{
    private static readonly Color PrimaryColor = Color.FromArgb(27, 94, 163);
    private static readonly Color SuccessColor = Color.FromArgb(31, 122, 78);
    private static readonly Color MutedColor = Color.FromArgb(92, 105, 121);

    private readonly ActivationWorkflow workflow;
    private readonly TextBox serverAddress = CreateTextBox("http://localhost:5080");
    private readonly TextBox activationCode = CreateTextBox();
    private readonly TextBox deviceName = CreateTextBox(Environment.MachineName);
    private readonly TextBox room = CreateTextBox();
    private readonly ComboBox connectionType = new();
    private readonly Button activateButton = new();
    private readonly Label statusLabel = new();
    private CancellationTokenSource? activationCancellation;

    public SetupForm(ActivationWorkflow workflow)
    {
        this.workflow = workflow;

        Text = "Настройка мониторинга интернета ВКО";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(680, 650);
        MinimumSize = new Size(680, 650);
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
        FormClosing += (_, _) => activationCancellation?.Cancel();
    }

    private static Panel CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 118,
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
            Size = new Size(615, 38),
            ForeColor = Color.FromArgb(225, 237, 250),
            Text = "Введите данные рабочего места. Школа, провайдер и линия связи определятся по одноразовому коду."
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
            Padding = new Padding(28, 22, 28, 22),
            AutoScroll = true
        };
        var fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 2,
            RowCount = 7
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddField(fields, 0, "Адрес сервера", serverAddress, "В рабочей среде используется защищённый адрес HTTPS.");
        AddField(fields, 1, "Код активации", activationCode, "Код выдаёт администратор системы. Он действует только один раз.");
        AddField(fields, 2, "Название компьютера", deviceName, "Например: Кабинет 205 — компьютер учителя.");
        AddField(fields, 3, "Кабинет", room, "Можно оставить пустым.");
        AddField(fields, 4, "Тип подключения", connectionType, null);

        statusLabel.AutoSize = false;
        statusLabel.Dock = DockStyle.Fill;
        statusLabel.Height = 48;
        statusLabel.ForeColor = MutedColor;
        statusLabel.Text = "После активации служба будет работать автоматически в фоне.";
        statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        fields.Controls.Add(statusLabel, 0, 5);
        fields.SetColumnSpan(statusLabel, 2);

        activateButton.Text = "Активировать и запустить";
        activateButton.AutoSize = false;
        activateButton.Dock = DockStyle.Right;
        activateButton.Size = new Size(235, 46);
        activateButton.BackColor = PrimaryColor;
        activateButton.ForeColor = Color.White;
        activateButton.FlatStyle = FlatStyle.Flat;
        activateButton.FlatAppearance.BorderSize = 0;
        activateButton.Cursor = Cursors.Hand;
        fields.Controls.Add(activateButton, 1, 6);

        content.Controls.Add(fields);
        return content;
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
        try
        {
            var input = ReadInput();
            SetBusy(true, "Подключаем компьютер к серверу…");
            activationCancellation = new CancellationTokenSource();

            var result = await workflow.ExecuteAsync(input, activationCancellation.Token);
            statusLabel.ForeColor = SuccessColor;
            statusLabel.Text = "Готово: компьютер зарегистрирован, защита включена, служба запущена.";
            activateButton.Text = "Готово";

            MessageBox.Show(
                $"Настройка завершена.\n\nИдентификатор устройства: {result.DeviceId:D}\nСлужба мониторинга уже работает в фоне.",
                "Компьютер подключён",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            Close();
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Операция отменена.";
            statusLabel.ForeColor = MutedColor;
            SetBusy(false);
        }
        catch (Exception exception)
        {
            statusLabel.Text = "Не удалось завершить настройку. Проверьте введённые данные.";
            statusLabel.ForeColor = Color.Firebrick;
            SetBusy(false);
            MessageBox.Show(
                exception.Message,
                "Ошибка настройки",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            activationCancellation?.Dispose();
            activationCancellation = null;
        }
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

        var name = deviceName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Введите понятное название компьютера.");
        }

        return new ActivationInput(
            serverUri,
            code,
            name,
            NullIfEmpty(room.Text),
            connectionType.SelectedItem?.ToString() ?? "Ethernet");
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        serverAddress.Enabled = !isBusy;
        activationCode.Enabled = !isBusy;
        deviceName.Enabled = !isBusy;
        room.Enabled = !isBusy;
        connectionType.Enabled = !isBusy;
        activateButton.Enabled = !isBusy;
        UseWaitCursor = isBusy;
        if (message is not null)
        {
            statusLabel.Text = message;
            statusLabel.ForeColor = PrimaryColor;
        }
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
