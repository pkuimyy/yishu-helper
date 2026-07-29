using System.Diagnostics;
using YiShuHelper;

namespace YiShuHelper.TrayApp;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly ControlClient _client = new();
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem = new("分流状态：正在连接服务……") { Enabled = false };
    private readonly ToolStripMenuItem _enableItem = new("启用分流");
    private readonly ToolStripMenuItem _disableItem = new("关闭分流");
    private readonly ToolStripMenuItem _reloadItem = new("重新加载配置");
    private readonly ToolStripMenuItem _componentsItem = new("组件状态");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 3000 };
    private bool _busy;

    public TrayApplicationContext()
    {
        _enableItem.Click += async (_, _) => await ExecuteAsync("enable", "分流启用请求");
        _disableItem.Click += async (_, _) => await ExecuteAsync("disable", "分流关闭请求");
        _reloadItem.Click += async (_, _) => await ExecuteAsync("reload", "配置重新加载");

        var openConfiguration = new ToolStripMenuItem("打开配置目录");
        openConfiguration.Click += (_, _) => OpenDirectory(ApplicationPaths.ForMachine().DataDirectory);
        var openLogs = new ToolStripMenuItem("查看日志");
        openLogs.Click += (_, _) => OpenDirectory(ApplicationPaths.ForMachine().LogDirectory);
        var exit = new ToolStripMenuItem("退出托盘");
        exit.Click += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([
            _statusItem,
            new ToolStripSeparator(),
            _enableItem,
            _disableItem,
            _componentsItem,
            new ToolStripSeparator(),
            _reloadItem,
            openConfiguration,
            openLogs,
            new ToolStripSeparator(),
            exit]);

        _icon = new NotifyIcon
        {
            Text = "翼枢分流助手：正在连接服务",
            Icon = SystemIcons.Information,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => _icon.ContextMenuStrip?.Show(Cursor.Position);
        _timer.Tick += async (_, _) => await RefreshAsync();
        _timer.Start();
        _ = RefreshAsync();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _timer.Dispose();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }

    private async Task RefreshAsync()
    {
        if (_busy) return;
        var response = await _client.SendAsync("status");
        if (response.Success && response.Status is not null) ApplyStatus(response.Status);
        else ApplyUnavailable(response.Message);
    }

    private async Task ExecuteAsync(string command, string operation)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            var response = await _client.SendAsync(command);
            if (response.Status is not null) ApplyStatus(response.Status);
            _icon.ShowBalloonTip(3000, "翼枢分流助手", response.Success ? response.Message : $"{operation}失败：{response.Message}",
                response.Success ? ToolTipIcon.Info : ToolTipIcon.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ApplyStatus(StatusSnapshot status)
    {
        var stateText = status.State switch
        {
            SplitRuntimeState.Disabled => "已关闭",
            SplitRuntimeState.WaitingForYiShu => "等待翼枢",
            SplitRuntimeState.Enabling => "正在启用",
            SplitRuntimeState.Enabled => "已启用",
            SplitRuntimeState.Disabling => "正在关闭",
            SplitRuntimeState.Faulted => "故障",
            _ => "未知"
        };
        _statusItem.Text = $"分流状态：{stateText}";
        _icon.Text = TrimTooltip($"翼枢分流助手：{stateText}");
        _icon.Icon = status.State switch
        {
            SplitRuntimeState.Enabled => SystemIcons.Shield,
            SplitRuntimeState.Faulted => SystemIcons.Error,
            SplitRuntimeState.WaitingForYiShu => SystemIcons.Warning,
            _ => SystemIcons.Information
        };
        _enableItem.Enabled = !_busy && !status.DesiredEnabled;
        _disableItem.Enabled = !_busy && status.DesiredEnabled;
        _reloadItem.Enabled = !_busy && status.State == SplitRuntimeState.Disabled;

        _componentsItem.DropDownItems.Clear();
        AddComponent("后台服务", status.Service);
        AddComponent("翼枢进程", status.YiShuProcess);
        AddComponent("wg.exe", status.WireGuardExecutable);
        AddComponent("misas 接口", status.TunnelInterface);
        AddComponent("WireGuard peer", status.WireGuardPeer);
        AddComponent("路由配置", status.RoutingConfiguration);
        _componentsItem.DropDownItems.Add(new ToolStripSeparator());
        _componentsItem.DropDownItems.Add(new ToolStripMenuItem($"最近心跳：{status.LastHeartbeat.LocalDateTime:yyyy-MM-dd HH:mm:ss}") { Enabled = false });
        if (!string.IsNullOrWhiteSpace(status.LastError))
            _componentsItem.DropDownItems.Add(new ToolStripMenuItem($"最近错误：{status.LastError}") { Enabled = false });
    }

    private void AddComponent(string name, ComponentStatus component) =>
        _componentsItem.DropDownItems.Add(new ToolStripMenuItem($"{(component.Available ? "正常" : "异常")} · {name}：{component.Detail}") { Enabled = false });

    private void ApplyUnavailable(string message)
    {
        _statusItem.Text = "分流状态：后台服务不可用";
        _icon.Text = "翼枢分流助手：服务不可用";
        _icon.Icon = SystemIcons.Error;
        _enableItem.Enabled = false;
        _disableItem.Enabled = false;
        _reloadItem.Enabled = false;
        _componentsItem.DropDownItems.Clear();
        _componentsItem.DropDownItems.Add(new ToolStripMenuItem($"异常 · 后台服务：{message}") { Enabled = false });
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        if (busy)
        {
            _enableItem.Enabled = false;
            _disableItem.Enabled = false;
            _reloadItem.Enabled = false;
        }
    }

    private static void OpenDirectory(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    private static string TrimTooltip(string text) => text.Length <= 63 ? text : text[..63];
}
