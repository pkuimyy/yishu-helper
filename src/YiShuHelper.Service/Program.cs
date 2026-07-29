using System.ServiceProcess;
using YiShuHelper;
using YiShuHelper.ServiceHost;

var dataArgument = Array.FindIndex(args, x => x.Equals("--data", StringComparison.OrdinalIgnoreCase));
var paths = dataArgument >= 0 && dataArgument + 1 < args.Length
    ? new ApplicationPaths(Path.GetFullPath(args[dataArgument + 1]))
    : ApplicationPaths.ForMachine();
var logger = new FileAppLogger(paths.LogDirectory, "service");
var coordinator = new SplitCoordinator(paths, new ConfigurationLoader(), new AtomicJsonStore(),
    new WireGuardClient(), new WindowsRouteManager(), logger);
var runtime = new ServiceRuntime(coordinator, logger);

if (args.Contains("--console", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine($"翼枢分流服务调试模式，数据目录：{paths.DataDirectory}");
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    await runtime.RunConsoleAsync(cancellation.Token);
    return;
}

ServiceBase.Run(new YiShuWindowsService(runtime, logger));
