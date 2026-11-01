using System.Drawing.Drawing2D;

namespace BuiltInKeyboardManager;

internal sealed class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ComboBox _devices = new();
    private readonly Label _status = new();
    private readonly Label _deviceId = new();
    private readonly Button _disable = new();
    private readonly Button _enable = new();
    private readonly Button _refresh = new();
    private bool _loading = true;

    public MainForm()
    {
        Text = "内置键盘管理器 1.2";
        Width = 620;
        Height = 350;
        MinimumSize = new Size(590, 330);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Color.FromArgb(246, 248, 252);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        BuildInterface();
        Shown += OnShown;
    }

    private async void OnShown(object? sender, EventArgs e)
    {
        await RefreshDevicesAsync();
        _loading = false;

        if (!StartupTaskService.Exists()) return;
        var answer = MessageBox.Show(this,
            "检测到 1.1 或更早版本创建的开机自启动任务。\n\n当前版本改为按需运行，是否删除这个旧任务？",
            "清理旧版自启动任务", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        var result = StartupTaskService.RemoveLegacyTask();
        _status.Text = result.Message;
        if (!result.Success)
            MessageBox.Show(this, result.Message, "清理失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    private void BuildInterface()
    {
        var title = new Label
        {
            Text = "内置键盘管理器",
            Font = new Font(Font.FontFamily, 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(28, 39, 55),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        var subtitle = new Label
        {
            Text = "按需启用或禁用指定的笔记本内置键盘 · 关闭窗口即退出",
            ForeColor = Color.FromArgb(94, 105, 122),
            AutoSize = true,
            Location = new Point(31, 60)
        };
        Controls.Add(title);
        Controls.Add(subtitle);

        var card = new Panel
        {
            Location = new Point(28, 95),
            Size = new Size(548, 160),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            BackColor = Color.White,
            Padding = new Padding(20)
        };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(223, 228, 236));
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };
        Controls.Add(card);

        var selectLabel = new Label
        {
            Text = "管理的键盘设备",
            AutoSize = true,
            Location = new Point(20, 18),
            ForeColor = Color.FromArgb(55, 65, 81)
        };
        _devices.DropDownStyle = ComboBoxStyle.DropDownList;
        _devices.Location = new Point(20, 43);
        _devices.Width = 420;
        _devices.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _devices.SelectedIndexChanged += (_, _) => DeviceSelectionChanged();

        _refresh.Text = "刷新";
        _refresh.Location = new Point(452, 42);
        _refresh.Size = new Size(74, 31);
        _refresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _refresh.Click += async (_, _) => await RefreshDevicesAsync();

        _deviceId.Location = new Point(20, 79);
        _deviceId.Size = new Size(506, 22);
        _deviceId.AutoEllipsis = true;
        _deviceId.ForeColor = Color.FromArgb(112, 122, 138);

        _disable.Text = "禁用内置键盘";
        _disable.Location = new Point(20, 111);
        _disable.Size = new Size(150, 34);
        _disable.BackColor = Color.FromArgb(205, 55, 68);
        _disable.ForeColor = Color.White;
        _disable.FlatStyle = FlatStyle.Flat;
        _disable.FlatAppearance.BorderSize = 0;
        _disable.Click += async (_, _) => await ChangeStateAsync(false);

        _enable.Text = "启用内置键盘";
        _enable.Location = new Point(180, 111);
        _enable.Size = new Size(150, 34);
        _enable.BackColor = Color.FromArgb(42, 126, 91);
        _enable.ForeColor = Color.White;
        _enable.FlatStyle = FlatStyle.Flat;
        _enable.FlatAppearance.BorderSize = 0;
        _enable.Click += async (_, _) => await ChangeStateAsync(true);

        card.Controls.AddRange([selectLabel, _devices, _refresh, _deviceId, _disable, _enable]);

        _status.Location = new Point(28, 270);
        _status.Size = new Size(548, 42);
        _status.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _status.ForeColor = Color.FromArgb(65, 76, 92);
        _status.Text = "正在读取键盘设备…";
        Controls.Add(_status);
    }

    private async Task RefreshDevicesAsync()
    {
        ToggleControls(false);
        _status.Text = "正在读取键盘设备…";
        try
        {
            var devices = await Task.Run(KeyboardDeviceService.Enumerate);
            var previousId = (_devices.SelectedItem as KeyboardDevice)?.InstanceId ?? _settings.SelectedDeviceId;
            _devices.Items.Clear();
            foreach (var device in devices) _devices.Items.Add(device);

            var selected = devices.FirstOrDefault(d => d.InstanceId.Equals(previousId, StringComparison.OrdinalIgnoreCase))
                           ?? devices.FirstOrDefault(d => d.IsRecommended);
            if (selected is not null)
            {
                _devices.SelectedItem = selected;
                _settings.SelectedDeviceId = selected.InstanceId;
                _settings.Save();
            }

            if (devices.Count == 0)
                _status.Text = "没有检测到键盘设备。";
            else if (selected is not null)
                ShowCurrentStatus(selected);
            else
                _status.Text = "请选择标记为“内置候选”的设备。";
        }
        catch (Exception ex)
        {
            _status.Text = $"读取设备失败：{ex.Message}";
        }
        finally
        {
            ToggleControls(true);
        }
    }

    private void DeviceSelectionChanged()
    {
        if (_devices.SelectedItem is not KeyboardDevice device) return;
        _deviceId.Text = $"设备实例：{device.InstanceId}";
        if (!_loading)
        {
            _settings.SelectedDeviceId = device.InstanceId;
            _settings.Save();
        }
        ShowCurrentStatus(device);
    }

    private void ShowCurrentStatus(KeyboardDevice device)
    {
        var enabled = KeyboardDeviceService.IsDeviceEnabled(device.InstanceId);
        _status.Text = enabled ? "当前状态：内置键盘已启用。" : "当前状态：内置键盘已禁用。";
        _disable.Enabled = enabled;
        _enable.Enabled = !enabled;
    }

    private async Task ChangeStateAsync(bool enabled)
    {
        if (_devices.SelectedItem is not KeyboardDevice selected)
        {
            MessageBox.Show(this, "请先选择内置键盘设备。", "尚未选择",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!enabled && !selected.IsRecommended)
        {
            var answer = MessageBox.Show(this,
                "该设备不像典型的内置 PS/2/ACPI 键盘，可能是 USB 或蓝牙外接键盘。\n\n仍要禁用这个设备吗？",
                "确认设备", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        ToggleControls(false);
        _status.Text = enabled ? "正在启用键盘…" : "正在禁用键盘…";
        var result = await KeyboardDeviceService.SetEnabledAsync(selected.InstanceId, enabled);
        await RefreshDevicesAsync();
        _status.Text = result.Message;

        if (!result.Success)
        {
            MessageBox.Show(this, result.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        else if (result.RestartRequired)
        {
            MessageBox.Show(this, result.Message, "操作成功，需要重启",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void ToggleControls(bool enabled)
    {
        _devices.Enabled = enabled;
        _refresh.Enabled = enabled;
        if (!enabled)
        {
            _disable.Enabled = false;
            _enable.Enabled = false;
        }
        else if (_devices.SelectedItem is KeyboardDevice device)
        {
            var isEnabled = KeyboardDeviceService.IsDeviceEnabled(device.InstanceId);
            _disable.Enabled = isEnabled;
            _enable.Enabled = !isEnabled;
        }
    }
}
