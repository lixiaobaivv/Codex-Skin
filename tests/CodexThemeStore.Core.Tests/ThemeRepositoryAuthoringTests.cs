using System.IO.Compression;
using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class ThemeRepositoryAuthoringTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"codex-theme-authoring-{Guid.NewGuid():N}");

    [Fact]
    public void GeneratesValidIndexAndPublishablePackage()
    {
        CreateRepository();
        File.WriteAllText(Path.Combine(_root, ".env"), "TOKEN=must-not-ship");
        var authoring = new ThemeRepositoryAuthoring();

        var count = authoring.GenerateIndex(_root, "Test Catalog", DateTimeOffset.Parse("2026-07-17T00:00:00Z"));
        var output = Path.Combine(Path.GetTempPath(), $"codex-theme-package-{Guid.NewGuid():N}.zip");
        try
        {
            var package = authoring.Package(_root, output);
            Assert.Equal(1, count);
            Assert.Equal(1, package.ThemeCount);
            Assert.Equal(64, package.Sha256.Length);
            using var archive = ZipFile.OpenRead(output);
            var names = archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
            Assert.Contains("theme-repository.json", names);
            Assert.Contains("themes/test-theme.json", names);
            Assert.Contains("previews/test-theme.png", names);
            Assert.DoesNotContain(".env", names);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void RejectsThemeManifestMissingFromIndex()
    {
        CreateRepository();
        var authoring = new ThemeRepositoryAuthoring();
        authoring.GenerateIndex(_root, "Test Catalog", DateTimeOffset.Parse("2026-07-17T00:00:00Z"));
        WriteTheme("unlisted-theme");

        var error = Assert.Throws<InvalidOperationException>(() => ThemeRepositoryClient.ValidateDirectory(_root));
        Assert.Contains("未索引", error.Message);
    }

    [Fact]
    public void RejectsImageWhoseContentDoesNotMatchExtension()
    {
        CreateRepository();
        File.WriteAllBytes(Path.Combine(_root, "previews", "test-theme.png"), [1, 2, 3, 4]);

        var theme = ThemeDefinition.Load(Path.Combine(_root, "themes", "test-theme.json"));
        Assert.Throws<InvalidDataException>(theme.ValidateAssets);
    }

    private void CreateRepository()
    {
        Directory.CreateDirectory(Path.Combine(_root, "themes"));
        Directory.CreateDirectory(Path.Combine(_root, "previews"));
        Directory.CreateDirectory(Path.Combine(_root, "schemas"));
        File.WriteAllText(Path.Combine(_root, "schemas", "theme-v1.schema.json"), "{}");
        File.WriteAllText(Path.Combine(_root, "schemas", "theme-repository-v1.schema.json"), "{}");
        WriteTheme("test-theme");
    }

    private void WriteTheme(string id)
    {
        File.WriteAllBytes(Path.Combine(_root, "previews", $"{id}.png"), [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        File.WriteAllText(Path.Combine(_root, "themes", $"{id}.json"), $$"""
        {
          "schemaVersion": 1,
          "version": "1.0.0",
          "displayName": "{{id}}",
          "codeThemeId": "{{id}}",
          "category": "其他",
          "description": "Test theme",
          "author": "Tests",
          "variant": "light",
          "previewImage": "../previews/{{id}}.png",
          "theme": {
            "accent": "#3366FF",
            "ink": "#111111",
            "surface": "#FFFFFF"
          },
          "home": {
            "brand": "Test",
            "title": "Build something",
            "quickActions": [
              { "title": "Explore", "prompt": "Explore the repository" },
              { "title": "Build", "prompt": "Build a feature" },
              { "title": "Review", "prompt": "Review the code" },
              { "title": "Fix", "prompt": "Fix a problem" }
            ]
          }
        }
        """);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
