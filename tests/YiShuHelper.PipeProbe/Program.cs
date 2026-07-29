using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using YiShuHelper;

await using var client = new NamedPipeClientStream(".", "YiShuHelper.Control.v1", PipeDirection.InOut, PipeOptions.Asynchronous);
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
await client.ConnectAsync(timeout.Token);
using var reader = new StreamReader(client, new UTF8Encoding(false), false, 4096, true);
using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
await writer.WriteLineAsync(JsonSerializer.Serialize(new ControlRequest("status"), JsonDefaults.CompactOptions).AsMemory(), timeout.Token);
var line = await reader.ReadLineAsync(timeout.Token);
var response = JsonSerializer.Deserialize<ControlResponse>(line ?? string.Empty, JsonDefaults.CompactOptions)
               ?? throw new InvalidOperationException("服务返回空响应。");
Console.WriteLine($"成功={response.Success}；状态={response.Status?.State}；期望启用={response.Status?.DesiredEnabled}");
return response.Success && response.Status?.State == SplitRuntimeState.WaitingForYiShu ? 0 : 1;
