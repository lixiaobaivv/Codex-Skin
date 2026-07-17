using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class ThemeStateCompilerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-theme-compiler-{Guid.NewGuid():N}");

    [Fact]
    public void CompilesV1ThemeIntoPortableStateFiles()
    {
        var themeDirectory = Path.Combine(_root, "themes");
        var previewDirectory = Path.Combine(_root, "previews");
        var backgroundDirectory = Path.Combine(_root, "backgrounds");
        var petDirectory = Path.Combine(_root, "pets");
        var stateDirectory = Path.Combine(_root, "state");
        Directory.CreateDirectory(themeDirectory);
        Directory.CreateDirectory(previewDirectory);
        Directory.CreateDirectory(backgroundDirectory);
        Directory.CreateDirectory(petDirectory);
        File.WriteAllBytes(Path.Combine(previewDirectory, "test.png"), PngBytes(1));
        File.WriteAllBytes(Path.Combine(backgroundDirectory, "test.png"), PngBytes(2));
        File.WriteAllBytes(Path.Combine(petDirectory, "test.png"), PngBytes(3));
        var themePath = Path.Combine(themeDirectory, "test-theme.json");
        File.WriteAllText(themePath, """
        {
          "schemaVersion": 1,
          "version": "1.0.0",
          "displayName": "Test Theme",
          "codeThemeId": "test-theme",
          "category": "极简",
          "description": "Test theme",
          "author": "Tests",
          "variant": "light",
          "previewImage": "../previews/test.png",
          "theme": {
            "accent": "#3366FF",
            "ink": "#111111",
            "surface": "#FFFFFF",
            "backgroundImage": "../backgrounds/test.png"
          },
          "home": {
            "brand": "Test",
            "title": "Build something",
            "pet": { "image": "../pets/test.png", "alt": "Test pet", "size": 96 },
            "quickActions": [
              { "title": "Explore", "prompt": "Explore the repository" },
              { "title": "Build", "prompt": "Build a feature" },
              { "title": "Review", "prompt": "Review the code" },
              { "title": "Fix", "prompt": "Fix a problem" }
            ]
          }
        }
        """);

        var payload = new ThemeStateCompiler().Compile(themePath, stateDirectory);

        Assert.Equal("test-theme", payload.ThemeId);
        Assert.Contains("data:image/png;base64,iVBOR", payload.Css);
        Assert.Contains("data:image/png;base64,iVBOR", payload.JavaScript);
        Assert.Contains("codex-theme-pet", payload.JavaScript);
        Assert.Contains("cancelAnimationFrame(frameId)", payload.JavaScript);
        Assert.Contains("function dispose()", payload.JavaScript);
        Assert.Contains("grid-template-columns: repeat(4, minmax(0, 1fr))", payload.Css);
        Assert.Contains("aria-label*=\"sidebar\" i", payload.Css);
        Assert.Contains("aria-label*=\"bottom panel\" i", payload.Css);
        Assert.Contains("display: inline-flex !important", payload.Css);
        Assert.Contains("transform: translateY(-36px)", payload.Css);
        Assert.DoesNotContain("repeat(auto-fit", payload.Css);
        Assert.Contains("backdrop-filter: none !important", payload.Css);
        Assert.Contains("function mutationNeedsApply", payload.JavaScript);
        Assert.Contains("mutations.some(mutationNeedsApply)", payload.JavaScript);
        Assert.Contains("getApplyCount", payload.JavaScript);
        Assert.Contains("}, 120);", payload.JavaScript);
        Assert.DoesNotContain("createTreeWalker", payload.JavaScript);
        Assert.DoesNotContain("characterData: true", payload.JavaScript);
        Assert.DoesNotContain("\"class\"", payload.JavaScript);
        Assert.Contains("test-theme", payload.JavaScript);
        Assert.DoesNotContain("createThemeSidebar", payload.JavaScript);
        Assert.DoesNotContain("#codex-theme-sidebar", payload.Css);
        Assert.True(File.Exists(Path.Combine(stateDirectory, "codex-theme.css")));
        Assert.True(File.Exists(Path.Combine(stateDirectory, "codex-theme.js")));
        Assert.True(File.Exists(Path.Combine(stateDirectory, "current-theme.json")));
    }

    [Fact]
    public void LoadsInstalledDreamSkinManifestIntoSharedCore()
    {
        var themeDirectory = Path.Combine(_root, "installed-dreamskin");
        Directory.CreateDirectory(themeDirectory);
        File.WriteAllBytes(Path.Combine(themeDirectory, "background.jpg"), [0xff, 0xd8, 0xff, 0xd9]);
        File.WriteAllBytes(Path.Combine(themeDirectory, "preview.png"), PngBytes(4));
        var themePath = Path.Combine(themeDirectory, "theme.json");
        File.WriteAllText(themePath, """
        {
          "schemaVersion": 1,
          "packageVersion": 1,
          "id": "codex-skin.test-signed",
          "name": "Signed Test Theme",
          "version": "1.0.0",
          "description": "Signed package compatibility fixture",
          "image": "background.jpg",
          "colors": {
            "background": "#102030",
            "panel": "#f4f5ef",
            "accent": "#336699",
            "text": "#112233"
          },
          "assets": {
            "background": { "path": "background.jpg" },
            "preview": { "path": "preview.png" }
          },
          "signature": { "algorithm": "Ed25519", "value": "verified-by-installer" }
        }
        """);

        var definition = ThemeDefinition.Load(themePath);
        definition.ValidateAssets();

        Assert.Equal("codex-skin.test-signed", definition.CodeThemeId);
        Assert.Equal("#336699", definition.Accent);
        Assert.Equal("#112233", definition.Ink);
        Assert.Equal("#f4f5ef", definition.Surface);
        Assert.Equal(4, definition.Home!["quickActions"]!.AsArray().Count);
        Assert.Equal(Path.Combine(themeDirectory, "background.jpg"), definition.ResolveBackgroundImage());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static byte[] PngBytes(byte marker) => [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, marker];
}
