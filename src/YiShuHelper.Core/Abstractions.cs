namespace YiShuHelper;

public interface IWireGuardClient
{
    bool IsYiShuProcessRunning(YiShuConfiguration configuration);
    bool WireGuardExecutableExists(YiShuConfiguration configuration);
    Task<WireGuardState> GetStateAsync(YiShuConfiguration configuration, CancellationToken cancellationToken);
    Task SetAllowedIPsAsync(YiShuConfiguration configuration, string peerPublicKey, IEnumerable<Ipv4Prefix> prefixes, CancellationToken cancellationToken);
    Task SetAllowedIPsAsync(YiShuConfiguration configuration, string peerPublicKey, string allowedIPs, CancellationToken cancellationToken);
}

public interface IRouteManager
{
    IReadOnlyList<RouteEntry> GetRoutes(uint interfaceIndex);
    bool Exists(uint interfaceIndex, Ipv4Prefix prefix);
    bool Create(uint interfaceIndex, Ipv4Prefix prefix, uint metric = 5);
    bool Create(RouteEntry route);
    bool Delete(uint interfaceIndex, Ipv4Prefix prefix);
}

public interface IAppLogger
{
    void Information(string message);
    void Warning(string message);
    void Error(string message, Exception? exception = null);
}

public sealed class FileAppLogger(string directory, string component) : IAppLogger
{
    private readonly object _sync = new();

    public void Information(string message) => Write("信息", message);
    public void Warning(string message) => Write("警告", message);
    public void Error(string message, Exception? exception = null) =>
        Write("错误", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private void Write(string level, string message)
    {
        lock (_sync)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, $"{component}-{DateTime.Now:yyyyMMdd}.log");
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} [{level}] {message}{Environment.NewLine}");
                foreach (var old in Directory.GetFiles(directory, $"{component}-*.log")
                             .Select(x => new FileInfo(x)).OrderByDescending(x => x.Name).Skip(14))
                    old.Delete();
            }
            catch
            {
                // 日志失败不能改变路由事务结果。
            }
        }
    }
}
