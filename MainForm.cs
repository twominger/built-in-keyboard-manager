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
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _autoDisable = new();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _trayState = new("正在读取键盘状态…") { Enabled = false };
    private readonly EventWaitHandle _showEvent;
    private readonly CancellationTokenSource _showListenerCancellation = new();
    private bool _reallyExit;
    private bool _loading = true;

    public MainForm(bool startedAutomatically, string showEventName)
    {
        Text = "内置键盘管理器 1.1";
        Width = 620;
        Height = 430;
        MinimumSize = new Size(590, 410);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Color.FromArgb(246, 248, 252);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        BuildInterface();

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开管理器", null, (_, _) => ShowWindow());
        menu.Items.Add(_trayState);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("启用内置键盘", null, async (_, _) => await ChangeStateAsync(true));
        menu.Items.Add("禁用内置键盘", null, async (_, _) => await ChangeStateAsync(false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出（不改变键盘状态）", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Icon = Icon,
            Text = "内置键盘管理器 1.1",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowWindow();

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        _ = ListenForShowRequestAsync(_showListenerCancellation.Token);

        Shown += async (_, _) =>
        {
            await RefreshDevicesAsync();
            var startupExists = StartupTaskService.Exists();
            if (startupExists)
                StartupTaskService.SetEnabled(true); // 将旧版本任务安全迁移到当前可执行文件路径。
            _startWithWindows.Checked = startupExists;
            _settings.StartWithWindows = _startWithWindows.Checked;
            _autoDisable.Checked = _settings.AutoDisable;
            _loading = false;

            if (startedAutomatically)
            {
                Hide();
                ShowInTaskbar = false;
                if (_settings.AutoDisable && !string.IsNullOrWhiteSpace(_settings.SelectedDeviceId))
                {
                    await Task.Delay(1500);
                    await ChangeStateAsync(false, quietSuccess: true);
                }
            }
        };
        FormClosing += OnFormClosing;
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
            Text = "安全地启用或禁用指定的笔记本内置键盘",
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

        var selectLabel = new Label { Text = "管理的键盘设备", AutoSize = true, Location = new Point(20, 18), ForeColor = Color.FromArgb(55, 65, 81) };
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

        _startWithWindows.Text = "登录 Windows 后自动启动（使用最高权限计划任务）";
        _startWithWindows.AutoSize = true;
        _startWithWindows.Location = new Point(32, 275);
        _startWithWindows.CheckedChanged += (_, _) => StartupChanged();
        _autoDisable.Text = "程序自动启动后，立即禁用上面选定的内置键盘";
        _autoDisable.AutoSize = true;
        _autoDisable.Location = new Point(52, 307);
        _autoDisable.CheckedChanged += (_, _) =>
        {
            if (_loading) return;
            _settings.AutoDisable = _autoDisable.Checked;
            _settings.Save();
        };
        Controls.Add(_startWithWindows);
        Controls.Add(_autoDisable);

        _status.Location = new Point(28, 346);
        _status.Size = new Size(548, 38);
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
            _status.Text = devices.Count == 0 ? "没有检测到键盘设备。" : "请选择标记为“内置候选”的设备。";
            UpdateStatusDisplay();
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
        UpdateStatusDisplay();
    }

    private void UpdateStatusDisplay()
    {
        if (_devices.SelectedItem is not KeyboardDevice device)
        {
            _trayState.Text = "未选择键盘设备";
            return;
        }
        var enabled = KeyboardDeviceService.IsDeviceEnabled(device.InstanceId);
        _trayState.Text = enabled ? "内置键盘：已启用" : "内置键盘：已禁用";
        _tray.Text = _trayState.Text;
        _disable.Enabled = enabled;
        _enable.Enabled = !enabled;
    }

    private async Task ChangeStateAsync(bool enabled, bool quietSuccess = false)
    {
        var id = (_devices.SelectedItem as KeyboardDevice)?.InstanceId ?? _settings.SelectedDeviceId;
        var selected = _devices.SelectedItem as KeyboardDevice;
        if (string.IsNullOrWhiteSpace(id))
        {
            if (!quietSuccess) MessageBox.Show(this, "请先选择内置键盘设备。", "尚未选择", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (!enabled && selected is { IsRecommended: false })
        {
            if (quietSuccess)
            {
                _status.Text = "自动禁用已跳过：保存的设备不像内置键盘，请打开程序重新确认。";
                _tray.ShowBalloonTip(4000, "自动禁用已跳过", "保存的设备可能是外接键盘，请打开管理器重新选择。", ToolTipIcon.Warning);
                return;
            }
            var answer = MessageBox.Show(this,
                "该设备不像典型的内置 PS/2/ACPI 键盘，可能是 USB 或蓝牙外接键盘。\n\n仍要禁用这个设备吗？",
                "确认设备", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer != DialogResult.Yes) return;
        }

        ToggleControls(false);
        _status.Text = enabled ? "正在启用键盘…" : "正在禁用键盘…";
        var result = await KeyboardDeviceService.SetEnabledAsync(id, enabled);
        _status.Text = result.Message;
        await RefreshDevicesAsync();
        if (result.Success)
        {
            if (result.RestartRequired)
            {
                _status.Text = result.Message;
                _trayState.Text = enabled ? "内置键盘：等待重启后启用" : "内置键盘：等待重启后禁用";
                _tray.ShowBalloonTip(5000, "需要重启 Windows", result.Message, ToolTipIcon.Warning);
                if (!quietSuccess)
                    MessageBox.Show(this, result.Message, "操作成功，需要重启", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (!quietSuccess)
            {
                _tray.ShowBalloonTip(2500, "内置键盘管理器", result.Message, ToolTipIcon.Info);
            }
        }
        else
        {
            MessageBox.Show(this, result.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        ToggleControls(true);
    }

    private void StartupChanged()
    {
        if (_loading) return;
        var result = StartupTaskService.SetEnabled(_startWithWindows.Checked);
        if (!result.Success)
        {
            _loading = true;
            _startWithWindows.Checked = !_startWithWindows.Checked;
            _loading = false;
            MessageBox.Show(this, result.Message, "无法修改自启动设置", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        _settings.StartWithWindows = _startWithWindows.Checked;
        _settings.Save();
        _status.Text = result.Message;
    }

    private void ToggleControls(bool enabled)
    {
        _devices.Enabled = enabled;
        _refresh.Enabled = enabled;
        if (enabled) UpdateStatusDisplay();
        else
        {
            _disable.Enabled = false;
            _enable.Enabled = false;
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_reallyExit) return;
        e.Cancel = true;
        Hide();
        ShowInTaskbar = false;
        if (!_settings.CloseHintShown)
        {
            _settings.CloseHintShown = true;
            _settings.Save();
            _tray.ShowBalloonTip(3500, "内置键盘管理器仍在运行", "双击托盘图标可重新打开；右键可以启用键盘或退出。", ToolTipIcon.Info);
        }
    }

    private void ShowWindow()
    {
        if (InvokeRequired) { BeginInvoke(ShowWindow); return; }
        ShowInTaskbar = true;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        _ = RefreshDevicesAsync();
    }

    private async Task ListenForShowRequestAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Run(() => _showEvent.WaitOne(), cancellationToken);
                if (!cancellationToken.IsCancellationRequested) BeginInvoke(ShowWindow);
            }
            catch (OperationCanceledException) { break; }
            catch { break; }
        }
    }

    private void ExitApplication()
    {
        _reallyExit = true;
        _showListenerCancellation.Cancel();
        _tray.Visible = false;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _showListenerCancellation.Cancel();
            _showListenerCancellation.Dispose();
            _showEvent.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
