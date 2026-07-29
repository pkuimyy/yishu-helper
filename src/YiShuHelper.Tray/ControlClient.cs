using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using YiShuHelper;

namespace YiShuHelper.TrayApp;

internal sealed class ControlClient
{
    private const string PipeName = "YiShuHelper.Control.v1";

    public async Task<ControlResponse> SendAsync(string command, CancellationToken cancellationToken = default)
    {
        await using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            await client.ConnectAsync(timeout.Token);
            using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, true);
            using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
            await writer.WriteLineAsync(JsonSerializer.Serialize(new ControlRequest(command), JsonDefaults.CompactOptions).AsMemory(), timeout.Token);
            var response = await reader.ReadLineAsync(timeout.Token);
            return JsonSerializer.Deserialize<ControlResponse>(response ?? string.Empty, JsonDefaults.CompactOptions)
                   ?? new ControlResponse(false, "后台服务返回了空响应。");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ControlResponse(false, "连接后台服务超时。");
        }
        catch (Exception ex)
        {
            return new ControlResponse(false, $"无法连接后台服务：{ex.Message}");
        }
    }
}
