using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace YiShuHelper;

public sealed class WireGuardClient : IWireGuardClient
{
    public bool IsYiShuProcessRunning(YiShuConfiguration configuration)
    {
        try
        {
            return Process.GetProcessesByName(configuration.ProcessName).Any();
        }
        catch
        {
            return false;
        }
    }

    public bool WireGuardExecutableExists(YiShuConfiguration configuration) => File.Exists(GetWgPath(configuration));

    public async Task<WireGuardState> GetStateAsync(YiShuConfiguration configuration, CancellationToken cancellationToken)
    {
        var result = await RunAsync(configuration, ["show", configuration.Interface, "dump"], cancellationToken);
        var lines = result.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length < 2) throw new InvalidOperationException($"WireGuard 接口 {configuration.Interface} 没有有效 peer。");

        var peer = lines[1].Split('\t');
        if (peer.Length < 4 || string.IsNullOrWhiteSpace(peer[0]))
            throw new InvalidOperationException("WireGuard dump 格式异常。");

        var address = ReadTunnelAddress(configuration);
        var networkInterface = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(item =>
            item.Name.Equals(configuration.Interface, StringComparison.OrdinalIgnoreCase) ||
            item.GetIPProperties().UnicastAddresses.Any(ip => ip.Address.AddressFamily == AddressFamily.InterNetwork && ip.Address.ToString() == address));

        if (networkInterface is null) throw new InvalidOperationException($"找不到 WireGuard 接口：{configuration.Interface}");
        var properties = networkInterface.GetIPProperties().GetIPv4Properties()
                         ?? throw new InvalidOperationException("WireGuard 接口没有 IPv4 属性。");

        return new WireGuardState(peer[0], peer[3], (uint)properties.Index, networkInterface.Name, address);
    }

    public Task SetAllowedIPsAsync(YiShuConfiguration configuration, string peerPublicKey, IEnumerable<Ipv4Prefix> prefixes, CancellationToken cancellationToken) =>
        SetAllowedIPsAsync(configuration, peerPublicKey, string.Join(',', prefixes.Select(x => x.ToString())), cancellationToken);

    public async Task SetAllowedIPsAsync(YiShuConfiguration configuration, string peerPublicKey, string allowedIPs, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(allowedIPs)) throw new InvalidOperationException("AllowedIPs 不能为空。");
        await RunAsync(configuration, ["set", configuration.Interface, "peer", peerPublicKey, "allowed-ips", allowedIPs], cancellationToken);
    }

    private static string ReadTunnelAddress(YiShuConfiguration configuration)
    {
        var path = Path.Combine(Environment.ExpandEnvironmentVariables(configuration.Directory), "resources", "misas.conf");
        if (!File.Exists(path)) throw new FileNotFoundException("找不到翼枢隧道配置。", path);
        var line = File.ReadLines(path).FirstOrDefault(x => x.TrimStart().StartsWith("Address", StringComparison.OrdinalIgnoreCase));
        if (line is null || !line.Contains('=')) throw new InvalidOperationException("misas.conf 缺少 Address。");
        return line[(line.IndexOf('=') + 1)..].Trim().Split('/')[0];
    }

    private static string GetWgPath(YiShuConfiguration configuration) =>
        Path.Combine(Environment.ExpandEnvironmentVariables(configuration.Directory), "resources", "wg.exe");

    private static async Task<CommandResult> RunAsync(YiShuConfiguration configuration, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var executable = GetWgPath(configuration);
        if (!File.Exists(executable)) throw new FileNotFoundException("找不到翼枢 wg.exe。", executable);

        var start = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("无法启动 wg.exe。");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("wg.exe 执行超时。");
        }

        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"wg.exe 执行失败（{process.ExitCode}）：{error.Trim()}");
        return new CommandResult(output, error);
    }

    private sealed record CommandResult(string StandardOutput, string StandardError);
}
