using System.Text.RegularExpressions;

namespace CodexThemeStore.Core;

public static class ThemeCatalog
{
    private static readonly Regex SemVerPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static IReadOnlyList<ThemeDefinition> Load(
        IEnumerable<string> catalogDirectories,
        string? installedPackageDirectory = null,
        string? platform = null)
    {
        var themes = new Dictionary<string, ThemeDefinition>(StringComparer.Ordinal);

        foreach (var directory in catalogDirectories.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(directory)) continue;
            var files = Directory.GetFiles(directory, "*.json");
            if (files.Length == 0) continue;
            try
            {
                var catalogThemes = files.Select(LoadCatalogTheme).ToList();
                foreach (var theme in catalogThemes) AddNewest(themes, theme);
                break;
            }
            catch
            {
                // A partial cache must not prevent fallback to the bundled catalog.
            }
        }

        var packageRoot = DreamSkinPackageInstaller.ResolveLibraryRoot(installedPackageDirectory);
        if (Directory.Exists(packageRoot))
        {
            foreach (var manifestPath in EnumerateInstalledManifests(packageRoot))
            {
                try
                {
                    AddNewest(themes, DreamSkinPackageInstaller.LoadInstalledTheme(manifestPath, platform));
                }
                catch (Exception ex)
                {
                    // Ignore damaged or manually modified packages. They can be repaired by importing again.
                    if (Environment.GetEnvironmentVariable("CODEX_THEME_CATALOG_DIAGNOSTICS") == "1")
                        Console.Error.WriteLine($"跳过无效的已安装主题 {manifestPath}: {ex.Message}");
                }
            }
        }

        if (themes.Count == 0) throw new DirectoryNotFoundException("找不到可用的本地主题。");
        return themes.Values
            .OrderBy(theme => theme.Category, StringComparer.Ordinal)
            .ThenBy(theme => theme.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }

    public static ThemeDefinition? FindById(
        string id,
        IEnumerable<string> catalogDirectories,
        string? installedPackageDirectory = null,
        string? platform = null) =>
        Load(catalogDirectories, installedPackageDirectory, platform)
            .FirstOrDefault(theme => theme.CodeThemeId.Equals(id, StringComparison.Ordinal));

    public static int CompareVersions(string left, string right)
    {
        var leftMatch = SemVerPattern.Match(left);
        var rightMatch = SemVerPattern.Match(right);
        if (!leftMatch.Success || !rightMatch.Success) return string.CompareOrdinal(left, right);

        for (var index = 1; index <= 3; index++)
        {
            var comparison = long.Parse(leftMatch.Groups[index].Value).CompareTo(long.Parse(rightMatch.Groups[index].Value));
            if (comparison != 0) return comparison;
        }

        var leftPre = leftMatch.Groups[4].Value;
        var rightPre = rightMatch.Groups[4].Value;
        if (leftPre.Length == 0) return rightPre.Length == 0 ? 0 : 1;
        if (rightPre.Length == 0) return -1;

        var leftParts = leftPre.Split('.');
        var rightParts = rightPre.Split('.');
        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var leftNumeric = long.TryParse(leftParts[index], out var leftNumber);
            var rightNumeric = long.TryParse(rightParts[index], out var rightNumber);
            var comparison = leftNumeric && rightNumeric
                ? leftNumber.CompareTo(rightNumber)
                : leftNumeric != rightNumeric
                    ? leftNumeric ? -1 : 1
                    : string.CompareOrdinal(leftParts[index], rightParts[index]);
            if (comparison != 0) return comparison;
        }
        return leftParts.Length.CompareTo(rightParts.Length);
    }

    private static ThemeDefinition LoadCatalogTheme(string path)
    {
        var theme = ThemeDefinition.Load(path);
        theme.ValidateAssets();
        return theme;
    }

    private static IEnumerable<string> EnumerateInstalledManifests(string root)
    {
        foreach (var path in Directory.EnumerateFiles(root, "theme.json", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length == 3)
                yield return path;
        }
    }

    private static void AddNewest(IDictionary<string, ThemeDefinition> themes, ThemeDefinition candidate)
    {
        if (!themes.TryGetValue(candidate.CodeThemeId, out var current) ||
            CompareVersions(candidate.Version, current.Version) >= 0)
        {
            themes[candidate.CodeThemeId] = candidate;
        }
    }
}
