using System.IO.Compression;
using System.Text.Json.Nodes;
using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class ThemeCatalogTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-theme-catalog-{Guid.NewGuid():N}");

    [Fact]
    public void MergesInstalledPackagesAndKeepsHighestVersion()
    {
        var package = FixturePackage();
        var library = Path.Combine(_root, "packages");
        var installed = DreamSkinPackageInstaller.ImportLocal(package, platform: "macos", libraryRoot: library);
        var catalog = ExtractCatalogTheme(package, "0.9.0");

        var themes = ThemeCatalog.Load([catalog], library, "macos");

        var selected = Assert.Single(themes);
        Assert.Equal(installed.Id, selected.CodeThemeId);
        Assert.Equal("1.0.0", selected.Version);
        Assert.Equal(Path.GetFullPath(installed.ManifestPath), selected.SourcePath);
    }

    [Fact]
    public void CatalogCanSupersedeAnOlderInstalledPackage()
    {
        var package = FixturePackage();
        var library = Path.Combine(_root, "packages");
        DreamSkinPackageInstaller.ImportLocal(package, platform: "macos", libraryRoot: library);
        var catalog = ExtractCatalogTheme(package, "2.0.0");

        var selected = Assert.Single(ThemeCatalog.Load([catalog], library, "macos"));

        Assert.Equal("2.0.0", selected.Version);
        Assert.StartsWith(Path.GetFullPath(catalog), selected.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void CatalogWinsWhenAnInstalledCompatibilityPackageHasTheSameVersion()
    {
        var package = FixturePackage();
        var library = Path.Combine(_root, "packages");
        DreamSkinPackageInstaller.ImportLocal(package, platform: "macos", libraryRoot: library);
        var catalog = ExtractCatalogTheme(package, "1.0.0");

        var selected = Assert.Single(ThemeCatalog.Load([catalog], library, "macos"));

        Assert.StartsWith(Path.GetFullPath(catalog), selected.SourcePath, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnInstalledPackageWhoseAssetsWereModified()
    {
        var library = Path.Combine(_root, "packages");
        var installed = DreamSkinPackageInstaller.ImportLocal(FixturePackage(), platform: "macos", libraryRoot: library);
        var theme = ThemeDefinition.Load(installed.ManifestPath);
        File.AppendAllText(theme.ResolvePreviewImage(), "modified");

        Assert.Throws<DirectoryNotFoundException>(() => ThemeCatalog.Load([], library, "macos"));
    }

    [Fact]
    public void RejectsAnInstalledPackageMovedUnderTheWrongVersion()
    {
        var library = Path.Combine(_root, "packages");
        var installed = DreamSkinPackageInstaller.ImportLocal(FixturePackage(), platform: "macos", libraryRoot: library);
        var original = Path.GetDirectoryName(installed.ManifestPath)!;
        var moved = Path.Combine(Path.GetDirectoryName(original)!, "9.9.9");
        Directory.Move(original, moved);

        Assert.Throws<DirectoryNotFoundException>(() => ThemeCatalog.Load([], library, "macos"));
    }

    [Fact]
    public void AllowsAnEmptyCatalogForOnlineFirstRun()
    {
        var themes = ThemeCatalog.Load([], Path.Combine(_root, "missing-library"), "macos", allowEmpty: true);

        Assert.Empty(themes);
    }

    [Theory]
    [InlineData("1.0.0-alpha.2", "1.0.0-alpha.10", -1)]
    [InlineData("1.0.0-rc.1", "1.0.0", -1)]
    [InlineData("2.0.0", "1.99.99", 1)]
    public void ComparesSemanticVersions(string left, string right, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(ThemeCatalog.CompareVersions(left, right)));
    }

    [Fact]
    public void UsesTheConfiguredLibraryForImportAndDiscovery()
    {
        var configured = Path.Combine(_root, "configured");
        var previous = Environment.GetEnvironmentVariable("CODEX_THEME_LIBRARY_DIR");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_THEME_LIBRARY_DIR", configured);
            var imported = DreamSkinPackageInstaller.ImportLocal(FixturePackage(), platform: "macos");

            var selected = Assert.Single(ThemeCatalog.Load([], platform: "macos"));

            Assert.Equal(imported.ManifestPath, selected.SourcePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_THEME_LIBRARY_DIR", previous);
        }
    }

    private string ExtractCatalogTheme(string package, string version)
    {
        var catalog = Path.Combine(_root, $"catalog-{version}");
        Directory.CreateDirectory(catalog);
        ZipFile.ExtractToDirectory(package, catalog);
        var manifestPath = Path.Combine(catalog, "theme.json");
        var root = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        root["version"] = version;
        File.WriteAllText(manifestPath, root.ToJsonString());
        return catalog;
    }

    private static string FixturePackage() =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "codex-skin-sample-1.0.0.dreamskin");

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
