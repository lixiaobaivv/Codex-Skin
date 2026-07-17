using System.Text;
using System.Text.Json;

namespace CodexThemeStore.Core;

public sealed record CodexInstallation(string AppPath, string ExecutablePath, string DisplayName);

public sealed record ThemeInjectionPayload(string Css, string JavaScript, string? ThemeId)
{
    public static ThemeInjectionPayload Load(string stateDirectory)
    {
        var cssPath = Path.Combine(stateDirectory, "codex-theme.css");
        var scriptPath = Path.Combine(stateDirectory, "codex-theme.js");
        if (!File.Exists(cssPath) || !File.Exists(scriptPath))
            throw new FileNotFoundException("主题状态目录缺少 codex-theme.css 或 codex-theme.js。");

        string? themeId = null;
        var metadataPath = Path.Combine(stateDirectory, "current-theme.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(metadataPath, Encoding.UTF8));
                if (document.RootElement.TryGetProperty("codeThemeId", out var node) && node.ValueKind == JsonValueKind.String)
                    themeId = node.GetString();
            }
            catch (JsonException)
            {
                // CSS and JavaScript remain usable when optional metadata is damaged.
            }
        }

        return new ThemeInjectionPayload(
            File.ReadAllText(cssPath, Encoding.UTF8),
            File.ReadAllText(scriptPath, Encoding.UTF8),
            string.IsNullOrWhiteSpace(themeId) ? null : themeId);
    }
}

public interface ICodexPlatformAdapter
{
    string PlatformName { get; }
    bool IsSupported { get; }
    Task<CodexInstallation?> DiscoverAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync(CodexInstallation installation, CancellationToken cancellationToken = default);
    Task StopAsync(CodexInstallation installation, CancellationToken cancellationToken = default);
    Task StartAsync(CodexInstallation installation, bool enableCdp, CancellationToken cancellationToken = default);
    Task<bool> InjectAsync(ThemeInjectionPayload payload, TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<bool> RollbackAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
