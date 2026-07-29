using System.Text.Json;

namespace YiShuHelper;

public enum SplitRuntimeState
{
    Disabled,
    WaitingForYiShu,
    Enabling,
    Enabled,
    Disabling,
    Faulted
}

public sealed record ComponentStatus(bool Available, string Detail)
{
    public static ComponentStatus Ok(string detail = "正常") => new(true, detail);
    public static ComponentStatus Fail(string detail) => new(false, detail);
}

public sealed record StatusSnapshot
{
    public bool DesiredEnabled { get; init; }
    public SplitRuntimeState State { get; init; } = SplitRuntimeState.WaitingForYiShu;
    public ComponentStatus Service { get; init; } = ComponentStatus.Ok("运行中");
    public ComponentStatus YiShuProcess { get; init; } = ComponentStatus.Fail("未检查");
    public ComponentStatus WireGuardExecutable { get; init; } = ComponentStatus.Fail("未检查");
    public ComponentStatus TunnelInterface { get; init; } = ComponentStatus.Fail("未检查");
    public ComponentStatus WireGuardPeer { get; init; } = ComponentStatus.Fail("未检查");
    public ComponentStatus RoutingConfiguration { get; init; } = ComponentStatus.Fail("未检查");
    public DateTimeOffset LastHeartbeat { get; init; } = DateTimeOffset.Now;
    public string? LastError { get; init; }
    public int EffectivePrefixCount { get; init; }
}

public sealed record RouteEntry(string DestinationPrefix, uint InterfaceIndex, string NextHop, uint Metric);

public sealed record WireGuardState(
    string PeerPublicKey,
    string AllowedIPs,
    uint InterfaceIndex,
    string InterfaceAlias,
    string Address);

public sealed record TransactionSnapshot
{
    public required DateTimeOffset CreatedAt { get; init; }
    public required string PeerPublicKey { get; init; }
    public required string OriginalAllowedIPs { get; init; }
    public required uint InterfaceIndex { get; init; }
    public required string InterfaceAlias { get; init; }
    public required string YiShuDirectory { get; init; }
    public required string InterfaceName { get; init; }
    public required string ProcessName { get; init; }
    public List<RouteEntry> OriginalRoutes { get; init; } = [];
    public List<string> CreatedRoutes { get; init; } = [];
    public List<string> DesiredPrefixes { get; init; } = [];
}

public sealed record ControlState(bool DesiredEnabled = true);

public sealed record ControlRequest(string Command);
public sealed record ControlResponse(bool Success, string Message, StatusSnapshot? Status = null);

public sealed record ApplicationPaths(string DataDirectory)
{
    public string ConfigurationFile => Path.Combine(DataDirectory, "yishu-split-config.json");
    public string ControlFile => Path.Combine(DataDirectory, "control-state.json");
    public string TransactionFile => Path.Combine(DataDirectory, "transaction-state.json");
    public string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static ApplicationPaths ForMachine() =>
        new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "YiShuHelper"));
}

public sealed class AtomicJsonStore
{
    public T? Load<T>(string path)
    {
        if (!File.Exists(path)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), JsonDefaults.Options);
    }

    public void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonDefaults.Options));
        File.Move(temporary, path, true);
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
