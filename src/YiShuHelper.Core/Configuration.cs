using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace YiShuHelper;

public sealed class AppConfiguration
{
    public int Version { get; set; } = 1;
    public YiShuConfiguration YiShu { get; set; } = new();
    public RoutingConfiguration Routing { get; set; } = new();
}

public sealed class YiShuConfiguration
{
    public string Directory { get; set; } = @"C:\Program Files\YiShu";
    public string Interface { get; set; } = "misas";
    public string ProcessName { get; set; } = "翼枢";
}

public sealed class RoutingConfiguration
{
    public int MinimumPrefixLength { get; set; } = 8;
    public List<string> InfrastructurePrefixes { get; set; } = [];
    public List<string> ResourcePrefixes { get; set; } = [];
}

public sealed record ValidatedConfiguration(
    AppConfiguration Value,
    IReadOnlyList<Ipv4Prefix> InfrastructurePrefixes,
    IReadOnlyList<Ipv4Prefix> ResourcePrefixes,
    IReadOnlyList<Ipv4Prefix> DesiredPrefixes);

public sealed class ConfigurationException(string message) : Exception(message);

public sealed class ConfigurationLoader
{
    public ValidatedConfiguration Load(string path)
    {
        if (!File.Exists(path))
            throw new ConfigurationException($"配置文件不存在：{path}");

        AppConfiguration? value;
        try
        {
            value = JsonSerializer.Deserialize<AppConfiguration>(File.ReadAllText(path), JsonDefaults.Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            throw new ConfigurationException($"配置文件无法读取：{ex.Message}");
        }

        if (value is null) throw new ConfigurationException("配置内容为空。");
        if (value.Version != 1) throw new ConfigurationException($"不支持配置版本：{value.Version}");
        if (string.IsNullOrWhiteSpace(value.YiShu.Directory)) throw new ConfigurationException("yiShu.directory 不能为空。");
        if (string.IsNullOrWhiteSpace(value.YiShu.Interface)) throw new ConfigurationException("yiShu.interface 不能为空。");
        if (string.IsNullOrWhiteSpace(value.YiShu.ProcessName)) throw new ConfigurationException("yiShu.processName 不能为空。");
        if (value.Routing.MinimumPrefixLength is < 0 or > 32) throw new ConfigurationException("minimumPrefixLength 必须介于 0 和 32 之间。");
        if (value.Routing.InfrastructurePrefixes.Count == 0) throw new ConfigurationException("infrastructurePrefixes 不能为空。");
        if (value.Routing.ResourcePrefixes.Count == 0) throw new ConfigurationException("resourcePrefixes 不能为空。");

        var infrastructure = ParseGroup("infrastructurePrefixes", value.Routing.InfrastructurePrefixes, value.Routing.MinimumPrefixLength);
        var resources = ParseGroup("resourcePrefixes", value.Routing.ResourcePrefixes, value.Routing.MinimumPrefixLength);
        var desired = Ipv4Prefix.Minimize(infrastructure.Concat(resources));
        return new ValidatedConfiguration(value, infrastructure, resources, desired);
    }

    private static IReadOnlyList<Ipv4Prefix> ParseGroup(string name, IEnumerable<string> values, int minimumLength)
    {
        var parsed = new List<Ipv4Prefix>();
        foreach (var text in values)
        {
            if (!Ipv4Prefix.TryParse(text, out var prefix))
                throw new ConfigurationException($"{name} 包含无效 IPv4 CIDR：{text}");
            if (prefix.PrefixLength < minimumLength)
                throw new ConfigurationException($"拒绝宽于 /{minimumLength} 的路由：{prefix}");
            parsed.Add(prefix);
        }
        return Ipv4Prefix.Minimize(parsed);
    }
}

public readonly record struct Ipv4Prefix(uint Network, byte PrefixLength) : IComparable<Ipv4Prefix>
{
    public static bool TryParse(string? text, out Ipv4Prefix prefix)
    {
        prefix = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var parts = text.Trim().Split('/', StringSplitOptions.TrimEntries);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var address) ||
            address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork ||
            !byte.TryParse(parts[1], out var length) || length > 32) return false;

        var value = ToUInt32(address);
        var mask = length == 0 ? 0u : uint.MaxValue << (32 - length);
        prefix = new Ipv4Prefix(value & mask, length);
        return true;
    }

    public static Ipv4Prefix Parse(string text) =>
        TryParse(text, out var prefix) ? prefix : throw new FormatException($"无效 IPv4 CIDR：{text}");

    public bool Contains(Ipv4Prefix other)
    {
        if (PrefixLength > other.PrefixLength) return false;
        var mask = PrefixLength == 0 ? 0u : uint.MaxValue << (32 - PrefixLength);
        return (Network & mask) == (other.Network & mask);
    }

    public IPAddress NetworkAddress => new([
        (byte)(Network >> 24),
        (byte)(Network >> 16),
        (byte)(Network >> 8),
        (byte)Network]);

    public int CompareTo(Ipv4Prefix other)
    {
        var networkOrder = Network.CompareTo(other.Network);
        return networkOrder != 0 ? networkOrder : PrefixLength.CompareTo(other.PrefixLength);
    }

    public override string ToString() => $"{NetworkAddress}/{PrefixLength}";

    public static IReadOnlyList<Ipv4Prefix> Minimize(IEnumerable<Ipv4Prefix> source)
    {
        var ordered = source.Distinct().OrderBy(x => x.PrefixLength).ThenBy(x => x.Network).ToList();
        var result = new List<Ipv4Prefix>();
        foreach (var item in ordered)
        {
            if (result.Any(existing => existing.Contains(item))) continue;
            result.Add(item);
        }
        result.Sort();
        return result;
    }

    internal static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }
}

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static JsonSerializerOptions CompactOptions { get; } = new(Options) { WriteIndented = false };
}
