namespace YiShuHelper;

public sealed class SplitCoordinator
{
    private readonly ApplicationPaths _paths;
    private readonly ConfigurationLoader _configurationLoader;
    private readonly AtomicJsonStore _store;
    private readonly IWireGuardClient _wireGuard;
    private readonly IRouteManager _routes;
    private readonly IAppLogger _logger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private volatile StatusSnapshot _status = new();

    public SplitCoordinator(ApplicationPaths paths, ConfigurationLoader configurationLoader, AtomicJsonStore store,
        IWireGuardClient wireGuard, IRouteManager routes, IAppLogger logger)
    {
        _paths = paths;
        _configurationLoader = configurationLoader;
        _store = store;
        _wireGuard = wireGuard;
        _routes = routes;
        _logger = logger;
        Directory.CreateDirectory(paths.DataDirectory);
        var control = _store.Load<ControlState>(_paths.ControlFile) ?? new ControlState(true);
        _store.Save(_paths.ControlFile, control);
        _status = _status with { DesiredEnabled = control.DesiredEnabled, State = control.DesiredEnabled ? SplitRuntimeState.WaitingForYiShu : SplitRuntimeState.Disabled };
    }

    public StatusSnapshot Status => _status;

    public async Task<StatusSnapshot> SetDesiredAsync(bool enabled, CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            _store.Save(_paths.ControlFile, new ControlState(enabled));
            _status = _status with { DesiredEnabled = enabled, LastError = null };
            if (enabled) await ReconcileEnabledAsync(cancellationToken);
            else await DisableInternalAsync(cancellationToken);
            return _status;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<StatusSnapshot> HeartbeatAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            var desired = (_store.Load<ControlState>(_paths.ControlFile) ?? new ControlState(true)).DesiredEnabled;
            _status = _status with { DesiredEnabled = desired, LastHeartbeat = DateTimeOffset.Now };
            if (desired) await ReconcileEnabledAsync(cancellationToken);
            else if (_store.Load<TransactionSnapshot>(_paths.TransactionFile) is not null) await DisableInternalAsync(cancellationToken);
            else _status = _status with { State = SplitRuntimeState.Disabled, LastError = null };
            return _status;
        }
        catch (Exception ex)
        {
            _logger.Error("健康检查失败。", ex);
            _status = _status with { State = SplitRuntimeState.Faulted, LastError = ex.Message, LastHeartbeat = DateTimeOffset.Now };
            return _status;
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await DisableInternalAsync(cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public StatusSnapshot ReloadConfiguration()
    {
        if (_status.State != SplitRuntimeState.Disabled) throw new InvalidOperationException("只能在分流关闭时重新加载配置。");
        var configuration = _configurationLoader.Load(_paths.ConfigurationFile);
        _status = _status with
        {
            RoutingConfiguration = ComponentStatus.Ok($"{configuration.DesiredPrefixes.Count} 个有效 CIDR"),
            EffectivePrefixCount = configuration.DesiredPrefixes.Count,
            LastError = null
        };
        return _status;
    }

    private async Task ReconcileEnabledAsync(CancellationToken cancellationToken)
    {
        ValidatedConfiguration configuration;
        try
        {
            configuration = _configurationLoader.Load(_paths.ConfigurationFile);
            _status = _status with
            {
                RoutingConfiguration = ComponentStatus.Ok($"{configuration.DesiredPrefixes.Count} 个有效 CIDR"),
                EffectivePrefixCount = configuration.DesiredPrefixes.Count
            };
        }
        catch (Exception ex)
        {
            _status = _status with { RoutingConfiguration = ComponentStatus.Fail(ex.Message), State = SplitRuntimeState.Faulted, LastError = ex.Message };
            return;
        }

        var processAvailable = _wireGuard.IsYiShuProcessRunning(configuration.Value.YiShu);
        var executableAvailable = _wireGuard.WireGuardExecutableExists(configuration.Value.YiShu);
        _status = _status with
        {
            YiShuProcess = processAvailable ? ComponentStatus.Ok("运行中") : ComponentStatus.Fail("未运行"),
            WireGuardExecutable = executableAvailable ? ComponentStatus.Ok("可用") : ComponentStatus.Fail("不存在")
        };
        if (!processAvailable || !executableAvailable)
        {
            _status = _status with { State = SplitRuntimeState.WaitingForYiShu, TunnelInterface = ComponentStatus.Fail("不可用"), WireGuardPeer = ComponentStatus.Fail("不可用"), LastError = null };
            return;
        }

        WireGuardState wireGuard;
        try
        {
            wireGuard = await _wireGuard.GetStateAsync(configuration.Value.YiShu, cancellationToken);
            _status = _status with { TunnelInterface = ComponentStatus.Ok($"{wireGuard.InterfaceAlias} / {wireGuard.InterfaceIndex}"), WireGuardPeer = ComponentStatus.Ok("可读取") };
        }
        catch (Exception ex)
        {
            _status = _status with { State = SplitRuntimeState.WaitingForYiShu, TunnelInterface = ComponentStatus.Fail(ex.Message), WireGuardPeer = ComponentStatus.Fail("不可读取"), LastError = null };
            return;
        }

        var transaction = _store.Load<TransactionSnapshot>(_paths.TransactionFile);
        if (transaction is not null && TransactionMatches(transaction, wireGuard, configuration.DesiredPrefixes))
        {
            _status = _status with { State = SplitRuntimeState.Enabled, LastError = null };
            return;
        }

        if (transaction is not null)
        {
            if (transaction.PeerPublicKey.Equals(wireGuard.PeerPublicKey, StringComparison.Ordinal) && transaction.InterfaceIndex == wireGuard.InterfaceIndex)
                await RestoreTransactionAsync(transaction, TransactionConfiguration(transaction), wireGuard, cancellationToken);
            else
            {
                CleanupRecordedRoutes(transaction);
                _store.Delete(_paths.TransactionFile);
            }
        }
        await EnableInternalAsync(configuration, wireGuard, cancellationToken);
    }

    private bool TransactionMatches(TransactionSnapshot transaction, WireGuardState wireGuard, IReadOnlyList<Ipv4Prefix> desired)
    {
        if (!transaction.PeerPublicKey.Equals(wireGuard.PeerPublicKey, StringComparison.Ordinal)) return false;
        if (!PrefixSetsEqual(wireGuard.AllowedIPs, desired)) return false;
        return desired.All(prefix => _routes.Exists(wireGuard.InterfaceIndex, prefix));
    }

    private async Task EnableInternalAsync(ValidatedConfiguration configuration, WireGuardState wireGuard, CancellationToken cancellationToken)
    {
        _status = _status with { State = SplitRuntimeState.Enabling, LastError = null };
        var originalPrefixes = ParseAllowedIPv4(wireGuard.AllowedIPs);
        var originalSet = originalPrefixes.Select(x => x.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var originalRoutes = _routes.GetRoutes(wireGuard.InterfaceIndex)
            .Where(x => originalSet.Contains(Ipv4Prefix.Parse(x.DestinationPrefix).ToString()) && x.NextHop == "0.0.0.0")
            .ToList();
        var transaction = new TransactionSnapshot
        {
            CreatedAt = DateTimeOffset.Now,
            PeerPublicKey = wireGuard.PeerPublicKey,
            OriginalAllowedIPs = wireGuard.AllowedIPs,
            InterfaceIndex = wireGuard.InterfaceIndex,
            YiShuDirectory = configuration.Value.YiShu.Directory,
            InterfaceName = configuration.Value.YiShu.Interface,
            ProcessName = configuration.Value.YiShu.ProcessName,
            InterfaceAlias = wireGuard.InterfaceAlias,
            OriginalRoutes = originalRoutes,
            DesiredPrefixes = configuration.DesiredPrefixes.Select(x => x.ToString()).ToList()
        };
        _store.Save(_paths.TransactionFile, transaction);

        try
        {
            foreach (var prefix in configuration.DesiredPrefixes)
            {
                if (_routes.Create(wireGuard.InterfaceIndex, prefix))
                {
                    transaction.CreatedRoutes.Add(prefix.ToString());
                    _store.Save(_paths.TransactionFile, transaction);
                }
            }

            await _wireGuard.SetAllowedIPsAsync(configuration.Value.YiShu, wireGuard.PeerPublicKey, configuration.DesiredPrefixes, cancellationToken);
            var desiredSet = configuration.DesiredPrefixes.Select(x => x.ToString()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var route in originalRoutes.Where(x => !desiredSet.Contains(Ipv4Prefix.Parse(x.DestinationPrefix).ToString())))
                _routes.Delete(route.InterfaceIndex, Ipv4Prefix.Parse(route.DestinationPrefix));

            var verified = await _wireGuard.GetStateAsync(configuration.Value.YiShu, cancellationToken);
            if (!PrefixSetsEqual(verified.AllowedIPs, configuration.DesiredPrefixes) || configuration.DesiredPrefixes.Any(x => !_routes.Exists(verified.InterfaceIndex, x)))
                throw new InvalidOperationException("启用后的 WireGuard 或路由核验失败。");

            _logger.Information($"分流已启用：{configuration.DesiredPrefixes.Count} 个 CIDR。");
            _status = _status with { State = SplitRuntimeState.Enabled, LastError = null };
        }
        catch (Exception ex)
        {
            _logger.Error("启用事务失败，开始回滚。", ex);
            try { await RestoreTransactionAsync(transaction, configuration.Value.YiShu, wireGuard, cancellationToken); }
            catch (Exception rollback)
            {
                _logger.Error("启用事务回滚失败。", rollback);
                _status = _status with { State = SplitRuntimeState.Faulted, LastError = $"启用失败：{ex.Message}；回滚失败：{rollback.Message}" };
                return;
            }
            _status = _status with { State = SplitRuntimeState.Faulted, LastError = ex.Message };
        }
    }

    private async Task DisableInternalAsync(CancellationToken cancellationToken)
    {
        var transaction = _store.Load<TransactionSnapshot>(_paths.TransactionFile);
        if (transaction is null)
        {
            _status = _status with { State = SplitRuntimeState.Disabled, LastError = null };
            return;
        }

        _status = _status with { State = SplitRuntimeState.Disabling };
        try
        {
            var transactionConfiguration = TransactionConfiguration(transaction);
            WireGuardState? wireGuard = null;
            try { wireGuard = await _wireGuard.GetStateAsync(transactionConfiguration, cancellationToken); } catch { }
            if (wireGuard is null)
            {
                var managedPrefixes = transaction.CreatedRoutes.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var remainingManagedRoutes = _routes.GetRoutes(transaction.InterfaceIndex)
                    .Any(route => managedPrefixes.Contains(Ipv4Prefix.Parse(route.DestinationPrefix).ToString()));
                if (remainingManagedRoutes)
                    throw new InvalidOperationException("WireGuard 暂时不可读，但受管路由仍然存在；已保留事务快照等待恢复。");

                _store.Delete(_paths.TransactionFile);
            }
            else
            {
                await RestoreTransactionAsync(transaction, transactionConfiguration, wireGuard, cancellationToken);
            }
            _logger.Information("分流已关闭并恢复原始状态。");
            _status = _status with { State = SplitRuntimeState.Disabled, LastError = null };
        }
        catch (Exception ex)
        {
            _logger.Error("关闭事务失败。", ex);
            _status = _status with { State = SplitRuntimeState.Faulted, LastError = ex.Message };
        }
    }

    private async Task RestoreTransactionAsync(TransactionSnapshot transaction, YiShuConfiguration configuration, WireGuardState wireGuard, CancellationToken cancellationToken)
    {
        if (transaction.PeerPublicKey.Equals(wireGuard.PeerPublicKey, StringComparison.Ordinal))
            await _wireGuard.SetAllowedIPsAsync(configuration, transaction.PeerPublicKey, transaction.OriginalAllowedIPs, cancellationToken);

        foreach (var prefix in transaction.CreatedRoutes.Select(Ipv4Prefix.Parse))
            _routes.Delete(transaction.InterfaceIndex, prefix);
        foreach (var route in transaction.OriginalRoutes)
            _routes.Create(route);

        _store.Delete(_paths.TransactionFile);
    }

    private void CleanupRecordedRoutes(TransactionSnapshot transaction)
    {
        foreach (var prefix in transaction.CreatedRoutes.Select(Ipv4Prefix.Parse))
        {
            try { _routes.Delete(transaction.InterfaceIndex, prefix); } catch { }
        }
    }

    private static YiShuConfiguration TransactionConfiguration(TransactionSnapshot transaction) => new()
    {
        Directory = transaction.YiShuDirectory,
        Interface = transaction.InterfaceName,
        ProcessName = transaction.ProcessName
    };

    private static IReadOnlyList<Ipv4Prefix> ParseAllowedIPv4(string allowedIPs) =>
        allowedIPs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(x => Ipv4Prefix.TryParse(x, out _)).Select(Ipv4Prefix.Parse).ToList();

    private static bool PrefixSetsEqual(string allowedIPs, IEnumerable<Ipv4Prefix> desired)
    {
        var left = ParseAllowedIPv4(allowedIPs).Select(x => x.ToString()).Order(StringComparer.OrdinalIgnoreCase);
        var right = desired.Select(x => x.ToString()).Order(StringComparer.OrdinalIgnoreCase);
        return left.SequenceEqual(right, StringComparer.OrdinalIgnoreCase);
    }
}
