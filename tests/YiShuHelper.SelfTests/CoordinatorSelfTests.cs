using System.Text.Json;
using YiShuHelper;

internal static class CoordinatorSelfTests
{
    public static async Task RunAsync(List<string> failures)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"YiShuHelper-Coordinator-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            var paths = new ApplicationPaths(temp);
            var configuration = new AppConfiguration
            {
                Routing = new RoutingConfiguration
                {
                    InfrastructurePrefixes = ["1.2.3.4/32", "172.28.160.193/32"],
                    ResourcePrefixes = ["172.27.103.0/24", "172.28.72.0/24", "172.29.16.0/24"]
                }
            };
            File.WriteAllText(paths.ConfigurationFile, JsonSerializer.Serialize(configuration, JsonDefaults.Options));

            var wireGuard = new FakeWireGuardClient();
            var routes = new FakeRouteManager();
            routes.Items.Add(new RouteEntry("0.0.0.0/0", 65, "0.0.0.0", 0));
            var coordinator = new SplitCoordinator(paths, new ConfigurationLoader(), new AtomicJsonStore(), wireGuard, routes, new NullLogger());

            var enabled = await coordinator.HeartbeatAsync(CancellationToken.None);
            Check(enabled.State == SplitRuntimeState.Enabled, "默认期望启用时应完成启用事务", failures);
            Check(enabled.EffectivePrefixCount == 5, "应得到五个有效路由前缀", failures);
            Check(routes.Items.Count == 5 && routes.Items.All(x => x.DestinationPrefix != "0.0.0.0/0"), "启用后应仅保留目标路由", failures);
            Check(File.Exists(paths.TransactionFile), "启用后应保留事务快照", failures);

            var disabled = await coordinator.SetDesiredAsync(false, CancellationToken.None);
            Check(disabled.State == SplitRuntimeState.Disabled, "关闭请求应完成关闭事务", failures);
            Check(wireGuard.State.AllowedIPs == "0.0.0.0/0", "关闭后应恢复原始 AllowedIPs", failures);
            Check(routes.Items.Count == 1 && routes.Items[0].DestinationPrefix == "0.0.0.0/0", "关闭后应恢复原始路由", failures);
            Check(!File.Exists(paths.TransactionFile), "关闭成功后应删除事务快照", failures);

            wireGuard.ProcessAvailable = false;
            var waiting = await coordinator.SetDesiredAsync(true, CancellationToken.None);
            Check(waiting.State == SplitRuntimeState.WaitingForYiShu, "翼枢离线时应进入等待状态", failures);
        }
        finally
        {
            Directory.Delete(temp, true);
        }
    }

    private static void Check(bool condition, string message, List<string> failures)
    {
        if (!condition) failures.Add(message);
    }

    private sealed class FakeWireGuardClient : IWireGuardClient
    {
        public bool ProcessAvailable { get; set; } = true;
        public WireGuardState State { get; private set; } = new("peer", "0.0.0.0/0", 65, "misas", "10.1.12.87");

        public bool IsYiShuProcessRunning(YiShuConfiguration configuration) => ProcessAvailable;
        public bool WireGuardExecutableExists(YiShuConfiguration configuration) => true;
        public Task<WireGuardState> GetStateAsync(YiShuConfiguration configuration, CancellationToken cancellationToken) => Task.FromResult(State);
        public Task SetAllowedIPsAsync(YiShuConfiguration configuration, string peerPublicKey, IEnumerable<Ipv4Prefix> prefixes, CancellationToken cancellationToken) =>
            SetAllowedIPsAsync(configuration, peerPublicKey, string.Join(',', prefixes), cancellationToken);
        public Task SetAllowedIPsAsync(YiShuConfiguration configuration, string peerPublicKey, string allowedIPs, CancellationToken cancellationToken)
        {
            State = State with { AllowedIPs = allowedIPs };
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRouteManager : IRouteManager
    {
        public List<RouteEntry> Items { get; } = [];
        public IReadOnlyList<RouteEntry> GetRoutes(uint interfaceIndex) => Items.Where(x => x.InterfaceIndex == interfaceIndex).ToList();
        public bool Exists(uint interfaceIndex, Ipv4Prefix prefix) => Items.Any(x => x.InterfaceIndex == interfaceIndex && x.DestinationPrefix == prefix.ToString());
        public bool Create(uint interfaceIndex, Ipv4Prefix prefix, uint metric = 5)
        {
            if (Exists(interfaceIndex, prefix)) return false;
            Items.Add(new RouteEntry(prefix.ToString(), interfaceIndex, "0.0.0.0", metric));
            return true;
        }
        public bool Create(RouteEntry route)
        {
            if (Exists(route.InterfaceIndex, Ipv4Prefix.Parse(route.DestinationPrefix))) return false;
            Items.Add(route);
            return true;
        }
        public bool Delete(uint interfaceIndex, Ipv4Prefix prefix) =>
            Items.RemoveAll(x => x.InterfaceIndex == interfaceIndex && x.DestinationPrefix == prefix.ToString()) > 0;
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Information(string message) { }
        public void Warning(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
