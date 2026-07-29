using System.Text.Json;
using YiShuHelper;

var failures = new List<string>();
await CoordinatorSelfTests.RunAsync(failures);


Check(Ipv4Prefix.Parse("172.29.16.6/24").ToString() == "172.29.16.0/24", "CIDR 应规范化网络地址");
Check(!Ipv4Prefix.TryParse("172.29.16.6/33", out _), "应拒绝无效前缀长度");

var minimized = Ipv4Prefix.Minimize([
    Ipv4Prefix.Parse("172.29.16.0/24"),
    Ipv4Prefix.Parse("172.29.16.6/32"),
    Ipv4Prefix.Parse("172.29.16.0/24")]);
Check(minimized.Count == 1 && minimized[0].ToString() == "172.29.16.0/24", "应去除重复和被覆盖前缀");

var temp = Path.Combine(Path.GetTempPath(), $"YiShuHelper-SelfTests-{Guid.NewGuid():N}");
Directory.CreateDirectory(temp);
try
{
    var path = Path.Combine(temp, "config.json");
    var config = new AppConfiguration
    {
        Routing = new RoutingConfiguration
        {
            MinimumPrefixLength = 8,
            InfrastructurePrefixes = ["1.2.3.4/32", "172.28.160.193/32"],
            ResourcePrefixes = ["172.27.103.0/24", "172.29.16.0/24", "172.29.16.6/32"]
        }
    };
    File.WriteAllText(path, JsonSerializer.Serialize(config, JsonDefaults.Options));
    var loaded = new ConfigurationLoader().Load(path);
    Check(loaded.DesiredPrefixes.Count == 4, "配置加载后应合并冗余前缀");

    var store = new AtomicJsonStore();
    var statePath = Path.Combine(temp, "state.json");
    store.Save(statePath, new ControlState(false));
    Check(store.Load<ControlState>(statePath)?.DesiredEnabled == false, "状态应可原子保存和读取");
}
finally
{
    Directory.Delete(temp, true);
}

if (failures.Count == 0)
{
    Console.WriteLine("全部自检通过。");
    return 0;
}

foreach (var failure in failures) Console.Error.WriteLine($"失败：{failure}");
return 1;

void Check(bool condition, string message)
{
    if (!condition) failures.Add(message);
}
