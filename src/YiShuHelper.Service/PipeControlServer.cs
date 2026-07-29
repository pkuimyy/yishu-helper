using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using YiShuHelper;

namespace YiShuHelper.ServiceHost;

internal sealed class PipeControlServer(SplitCoordinator coordinator, IAppLogger logger)
{
    public const string PipeName = "YiShuHelper.Control.v1";

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = CreateServer();
                await server.WaitForConnectionAsync(cancellationToken);
                await HandleClientAsync(server, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.Error("命名管道处理失败。", ex);
                try { await Task.Delay(1000, cancellationToken); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private static NamedPipeServerStream CreateServer()
    {
        var security = new PipeSecurity();
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.InOut, 4,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, new UTF8Encoding(false), false, 4096, true);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
        var line = await reader.ReadLineAsync(cancellationToken);
        ControlResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<ControlRequest>(line ?? string.Empty, JsonDefaults.CompactOptions)
                          ?? throw new InvalidOperationException("控制请求为空。");
            response = request.Command.ToLowerInvariant() switch
            {
                "status" => new ControlResponse(true, "状态已更新。", coordinator.Status),
                "enable" => new ControlResponse(true, "启用请求已处理。", await coordinator.SetDesiredAsync(true, cancellationToken)),
                "disable" => new ControlResponse(true, "关闭请求已处理。", await coordinator.SetDesiredAsync(false, cancellationToken)),
                "reload" => new ControlResponse(true, "配置已重新加载。", coordinator.ReloadConfiguration()),
                _ => new ControlResponse(false, $"未知命令：{request.Command}", coordinator.Status)
            };
        }
        catch (Exception ex)
        {
            response = new ControlResponse(false, ex.Message, coordinator.Status);
        }
        await writer.WriteLineAsync(JsonSerializer.Serialize(response, JsonDefaults.CompactOptions).AsMemory(), cancellationToken);
    }
}
