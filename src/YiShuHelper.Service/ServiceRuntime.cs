using System.ServiceProcess;
using YiShuHelper;

namespace YiShuHelper.ServiceHost;

internal sealed class ServiceRuntime(SplitCoordinator coordinator, IAppLogger logger)
{
    private CancellationTokenSource? _cancellation;
    private Task? _heartbeat;
    private Task? _controlServer;

    public void Start()
    {
        if (_cancellation is not null) return;
        _cancellation = new CancellationTokenSource();
        _heartbeat = Task.Run(() => HeartbeatLoopAsync(_cancellation.Token));
        _controlServer = Task.Run(() => new PipeControlServer(coordinator, logger).RunAsync(_cancellation.Token));
        logger.Information("后台服务运行循环已启动。");
    }

    public async Task StopAsync()
    {
        var cancellation = _cancellation;
        if (cancellation is null) return;
        cancellation.Cancel();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await coordinator.ShutdownAsync(timeout.Token);
        }
        catch (Exception ex)
        {
            logger.Error("服务停止时恢复分流失败。", ex);
        }

        var tasks = new[] { _heartbeat, _controlServer }.Where(x => x is not null).Cast<Task>();
        try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
        cancellation.Dispose();
        _cancellation = null;
        logger.Information("后台服务运行循环已停止。");
    }

    public async Task RunConsoleAsync(CancellationToken cancellationToken)
    {
        Start();
        try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
        catch (OperationCanceledException) { }
        await StopAsync();
    }

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        var retryIndex = 0;
        var retrySeconds = new[] { 2, 5, 10, 30 };
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var status = await coordinator.HeartbeatAsync(cancellationToken);
                retryIndex = status.State == SplitRuntimeState.Enabled || status.State == SplitRuntimeState.Disabled
                    ? 0
                    : Math.Min(retryIndex + 1, retrySeconds.Length - 1);
                var delay = status.State == SplitRuntimeState.Enabled ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(retrySeconds[retryIndex]);
                await Task.Delay(delay, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.Error("服务心跳循环异常。", ex);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }
}

internal sealed class YiShuWindowsService : ServiceBase
{
    private readonly ServiceRuntime _runtime;
    private readonly IAppLogger _logger;

    public const string InternalName = "YiShuHelper";

    public YiShuWindowsService(ServiceRuntime runtime, IAppLogger logger)
    {
        _runtime = runtime;
        _logger = logger;
        ServiceName = InternalName;
        CanStop = true;
        CanShutdown = true;
        AutoLog = true;
    }

    protected override void OnStart(string[] args)
    {
        _logger.Information("收到 Windows 服务启动请求。");
        _runtime.Start();
    }

    protected override void OnStop() => _runtime.StopAsync().GetAwaiter().GetResult();
    protected override void OnShutdown() => _runtime.StopAsync().GetAwaiter().GetResult();
}
