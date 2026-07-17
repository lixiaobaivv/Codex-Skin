using System.Diagnostics;

namespace CodexThemeStore.Core;

public sealed class MacOsCodexAdapter : ICodexPlatformAdapter
{
    private static readonly string[] ExecutableNames = ["Codex", "ChatGPT", "OpenAI Codex"];
    private readonly CdpThemeInjector _injector;

    public MacOsCodexAdapter(CdpThemeInjector? injector = null)
    {
        _injector = injector ?? new CdpThemeInjector();
    }

    public string PlatformName => "macOS";
    public bool IsSupported => OperatingSystem.IsMacOS();

    public Task<CodexInstallation?> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (!IsSupported) return Task.FromResult<CodexInstallation?>(null);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var candidate in GetAppCandidates())
        {
            var installation = CreateInstallation(candidate);
            if (installation is not null) return Task.FromResult<CodexInstallation?>(installation);
        }
        return Task.FromResult<CodexInstallation?>(null);
    }

    public Task<bool> IsRunningAsync(CodexInstallation installation, CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var process in FindProcesses(installation))
        {
            process.Dispose();
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public async Task StopAsync(CodexInstallation installation, CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var processes = FindProcesses(installation).ToList();
            if (processes.Count == 0) return;
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && !process.CloseMainWindow()) process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            await Task.Delay(250, cancellationToken);
        }
        foreach (var process in FindProcesses(installation))
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            finally { process.Dispose(); }
        }
    }

    public Task StartAsync(CodexInstallation installation, bool enableCdp, CancellationToken cancellationToken = default)
    {
        EnsureSupported();
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(installation.ExecutablePath)) throw new FileNotFoundException("Codex 可执行文件不存在。", installation.ExecutablePath);
        var startInfo = new ProcessStartInfo
        {
            FileName = "/usr/bin/open",
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-n");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add(installation.AppPath);
        if (enableCdp)
        {
            startInfo.ArgumentList.Add("--args");
            startInfo.ArgumentList.Add("--remote-debugging-address=127.0.0.1");
            startInfo.ArgumentList.Add($"--remote-debugging-port={CdpThemeInjector.DebugPort}");
        }
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("无法通过 LaunchServices 启动 Codex。");
        return process.WaitForExitAsync(cancellationToken);
    }

    public async Task<bool> InjectAsync(ThemeInjectionPayload payload, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        await _injector.InjectAsync(payload, timeout, cancellationToken) > 0;

    public async Task<bool> RollbackAsync(TimeSpan timeout, CancellationToken cancellationToken = default) =>
        await _injector.RemoveAsync(timeout, cancellationToken) > 0;

    private static IEnumerable<string> GetAppCandidates()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_APP_PATH");
        if (!string.IsNullOrWhiteSpace(configured)) yield return configured;
        yield return "/Applications/Codex.app";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "Codex.app");
        yield return "/Applications/ChatGPT.app";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications", "ChatGPT.app");
    }

    private static CodexInstallation? CreateInstallation(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)) return new CodexInstallation(FindAppRoot(fullPath), fullPath, "Codex");
        if (!Directory.Exists(fullPath)) return null;
        var executableDirectory = Path.Combine(fullPath, "Contents", "MacOS");
        if (!Directory.Exists(executableDirectory)) return null;
        foreach (var name in ExecutableNames)
        {
            var candidate = Path.Combine(executableDirectory, name);
            if (File.Exists(candidate)) return new CodexInstallation(fullPath, candidate, "Codex");
        }
        var fallback = Directory.EnumerateFiles(executableDirectory, "*", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return fallback is null ? null : new CodexInstallation(fullPath, fallback, "Codex");
    }

    private static string FindAppRoot(string executable)
    {
        var marker = $".app{Path.DirectorySeparatorChar}";
        var index = executable.IndexOf(marker, StringComparison.Ordinal);
        return index < 0 ? Path.GetDirectoryName(executable)! : executable[..(index + 4)];
    }

    private static IEnumerable<Process> FindProcesses(CodexInstallation installation)
    {
        var executable = Path.GetFullPath(installation.ExecutablePath);
        var executableName = Path.GetFileNameWithoutExtension(executable);
        foreach (var process in Process.GetProcesses())
        {
            var matches = false;
            try
            {
                var path = process.MainModule?.FileName;
                if (path is not null && (Path.GetFullPath(path).Equals(executable, StringComparison.Ordinal) || path.StartsWith(installation.AppPath + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
                {
                    matches = true;
                }
                else if (path is null && process.ProcessName.Equals(executableName, StringComparison.OrdinalIgnoreCase))
                {
                    matches = true;
                }
            }
            catch
            {
                // macOS can deny MainModule access for unrelated processes.
            }
            if (matches) yield return process;
            else process.Dispose();
        }
    }

    private void EnsureSupported()
    {
        if (!IsSupported) throw new PlatformNotSupportedException("MacOsCodexAdapter 只能在 macOS 上运行。");
    }
}
