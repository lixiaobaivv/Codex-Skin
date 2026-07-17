using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class ThemeInjectionPayloadTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"codex-theme-tests-{Guid.NewGuid():N}");

    [Fact]
    public void LoadsGeneratedThemeState()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "codex-theme.css"), "body { color: red; }");
        File.WriteAllText(Path.Combine(_directory, "codex-theme.js"), "globalThis.test = true;");
        File.WriteAllText(Path.Combine(_directory, "current-theme.json"), "{\"codeThemeId\":\"test-theme\"}");

        var payload = ThemeInjectionPayload.Load(_directory);

        Assert.Equal("body { color: red; }", payload.Css);
        Assert.Equal("globalThis.test = true;", payload.JavaScript);
        Assert.Equal("test-theme", payload.ThemeId);
    }

    [Fact]
    public void RequiresGeneratedCssAndJavaScript()
    {
        Directory.CreateDirectory(_directory);
        Assert.Throws<FileNotFoundException>(() => ThemeInjectionPayload.Load(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }
}
