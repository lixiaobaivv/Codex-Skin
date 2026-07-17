namespace CodexThemeStore.Core;

public sealed class CodexThemeRuntime
{
    private readonly CdpThemeInjector _injector;

    public CodexThemeRuntime(CdpThemeInjector? injector = null)
    {
        _injector = injector ?? new CdpThemeInjector();
    }

    public async Task<CodexInstallation> RestartAndInjectAsync(
        ICodexPlatformAdapter adapter,
        ThemeInjectionPayload payload,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!adapter.IsSupported) throw new PlatformNotSupportedException($"当前系统不支持 {adapter.PlatformName} 适配器。");
        var installation = await adapter.DiscoverAsync(cancellationToken)
                           ?? throw new FileNotFoundException($"未找到 {adapter.PlatformName} Codex 安装。可通过 CODEX_APP_PATH 指定位置。");
        await adapter.StopAsync(installation, cancellationToken);
        await adapter.StartAsync(installation, enableCdp: true, cancellationToken);

        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _injector.IsReadyAsync(cancellationToken))
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero && await adapter.InjectAsync(payload, Min(remaining, TimeSpan.FromSeconds(10)), cancellationToken))
                    return installation;
            }
            await Task.Delay(500, cancellationToken);
        }
        throw new TimeoutException("Codex 已启动，但主题未能在规定时间内完成 CDP 注入。");
    }

    private static TimeSpan Min(TimeSpan left, TimeSpan right) => left <= right ? left : right;
}
