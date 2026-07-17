using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class DreamSkinPackageInstallerTests : IDisposable
{
    private readonly string _library = Path.Combine(Path.GetTempPath(), $"codex-dreamskin-{Guid.NewGuid():N}");

    [Fact]
    public void VerifiesAndInstallsSignedPackageForMacOs()
    {
        var package = Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-skin-sample-1.0.0.dreamskin");

        var result = DreamSkinPackageInstaller.ImportLocal(package, platform: "macos", libraryRoot: _library);
        var definition = ThemeDefinition.Load(result.ManifestPath);
        definition.ValidateAssets();

        Assert.Equal("codex-skin.jackson-sage-sample", result.Id);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal(64, result.PackageSha256.Length);
        Assert.Equal(result.Id, definition.CodeThemeId);
        Assert.Equal(4, definition.Home!["quickActions"]!.AsArray().Count);
        Assert.StartsWith(Path.GetFullPath(_library), Path.GetFullPath(result.ManifestPath), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsUnsupportedRuntimePlatform()
    {
        var package = Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-skin-sample-1.0.0.dreamskin");

        var error = Assert.Throws<InvalidOperationException>(() =>
            DreamSkinPackageInstaller.ImportLocal(package, platform: "linux", libraryRoot: _library));

        Assert.StartsWith("DSI_UNSUPPORTED_PLATFORM", error.Message, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(_library)) Directory.Delete(_library, true);
    }
}
