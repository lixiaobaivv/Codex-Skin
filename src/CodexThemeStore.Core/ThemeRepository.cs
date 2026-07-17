using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexThemeStore.Core;

public sealed record ThemeRepositorySource(string Id, string Name, string Prefix)
{
    public override string ToString() => Name;
}

public sealed record ThemeRepositorySettings(string Repository, string Branch, string SourceId)
{
    public const string OfficialRepository = "lixiaobaivv/Codex-Skin-Store";
    public const string OfficialBranch = "main";

    public static ThemeRepositorySettings Default { get; } = new(
        OfficialRepository,
        OfficialBranch,
        "github");
}

public sealed record ThemeSyncResult(int ThemeCount, string SourceId, string SourceName);

public sealed class ThemeRepositoryClient
{
    private const long MaxArchiveBytes = 200L * 1024 * 1024;
    private const long MaxFileBytes = 20L * 1024 * 1024;
    private const int MaxFiles = 2000;
    private static readonly TimeSpan SourceTimeout = TimeSpan.FromSeconds(45);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly string StoreDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexThemeStore");

    public static string CacheDirectory { get; } = Path.Combine(StoreDirectory, "ThemeCatalog");
    public static string CacheThemeDirectory { get; } = Path.Combine(CacheDirectory, "themes");
    public static string SettingsPath { get; } = Path.Combine(StoreDirectory, "repository-settings.json");
    public static IReadOnlyList<ThemeRepositorySource> Sources { get; } =
    [
        new("github", "GitHub 官方", ""),
        new("gh-proxy", "GH Proxy", "https://gh-proxy.com/"),
        new("ghfast", "GHFast", "https://ghfast.top/"),
    ];

    public static ThemeRepositorySettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return ThemeRepositorySettings.Default;
            var settings = JsonSerializer.Deserialize<ThemeRepositorySettings>(File.ReadAllText(SettingsPath, Encoding.UTF8));
            return NormalizeSettings(settings);
        }
        catch
        {
            return ThemeRepositorySettings.Default;
        }
    }

    public static void SaveSettings(ThemeRepositorySettings settings)
    {
        Directory.CreateDirectory(StoreDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(NormalizeSettings(settings), new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static ThemeRepositorySettings NormalizeSettings(ThemeRepositorySettings? settings)
    {
        var sourceId = Sources.Any(source => source.Id == settings?.SourceId) ? settings!.SourceId : Sources[0].Id;
        return new ThemeRepositorySettings(ThemeRepositorySettings.OfficialRepository, ThemeRepositorySettings.OfficialBranch, sourceId);
    }

    public static IReadOnlyList<ThemeRepositorySource> GetSourceCandidates(string? preferredSourceId)
    {
        var direct = Sources.First(source => source.Id == "github");
        var preferred = Sources.FirstOrDefault(source => source.Id == preferredSourceId) ?? direct;
        return new[] { preferred, direct }
            .Concat(Sources)
            .DistinctBy(source => source.Id)
            .ToList();
    }

    public async Task<ThemeSyncResult> SyncAsync(ThemeRepositorySettings settings, CancellationToken cancellationToken = default)
    {
        settings = NormalizeSettings(settings);

        var upstream = $"https://github.com/{settings.Repository}/archive/refs/heads/{settings.Branch}.zip";
        var tempArchive = Path.Combine(Path.GetTempPath(), $"codex-theme-{Guid.NewGuid():N}.zip");
        var tempDirectory = Path.Combine(StoreDirectory, $"ThemeCatalog-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(StoreDirectory);
            Exception? lastError = null;
            foreach (var source in GetSourceCandidates(settings.SourceId))
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeleteTemporaryFiles(tempArchive, tempDirectory);
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(SourceTimeout);
                    await DownloadAsync(source.Prefix + upstream, tempArchive, timeout.Token);
                    Directory.CreateDirectory(tempDirectory);
                    ExtractArchive(tempArchive, tempDirectory);
                    var count = ValidateDirectory(tempDirectory);
                    ReplaceCache(tempDirectory);
                    SaveSettings(settings with { SourceId = source.Id });
                    return new ThemeSyncResult(count, source.Id, source.Name);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastError = new InvalidOperationException($"通过 {source.Name} 同步主题超时。");
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException)
                {
                    lastError = ex;
                }
            }

            throw new InvalidOperationException(
                lastError is null ? "没有可用的主题同步线路。" : $"所有主题同步线路均失败：{lastError.Message}",
                lastError);
        }
        finally
        {
            DeleteTemporaryFiles(tempArchive, tempDirectory);
        }
    }

    private static void DeleteTemporaryFiles(string archivePath, string directoryPath)
    {
        if (File.Exists(archivePath)) File.Delete(archivePath);
        if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, true);
    }

    private static async Task DownloadAsync(string url, string destination, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CodexThemeStore/1.0");
        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxArchiveBytes)
            throw new InvalidOperationException("主题仓库归档超过 200 MB 限制。");

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var output = File.Create(destination);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
        {
            total += read;
            if (total > MaxArchiveBytes) throw new InvalidOperationException("主题仓库归档超过 200 MB 限制。");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static void ExtractArchive(string archivePath, string destination)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxFiles) throw new InvalidOperationException($"主题仓库文件数超过 {MaxFiles} 个限制。");
        var manifestEntry = archive.Entries.FirstOrDefault(entry => entry.FullName.EndsWith("/theme-repository.json", StringComparison.Ordinal))
                            ?? archive.GetEntry("theme-repository.json")
                            ?? throw new InvalidOperationException("归档根目录缺少 theme-repository.json。");
        var prefix = manifestEntry.FullName[..^"theme-repository.json".Length];
        var extracted = 0;
        long extractedBytes = 0;
        var outputPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var relative = entry.FullName[prefix.Length..].Replace('\\', '/');
            if (string.IsNullOrEmpty(relative) || relative.EndsWith('/')) continue;
            if (!ShouldExtract(relative)) continue;
            if (++extracted > MaxFiles) throw new InvalidOperationException($"主题仓库文件数超过 {MaxFiles} 个限制。");
            if (entry.Length > MaxFileBytes) throw new InvalidOperationException($"主题文件超过 20 MB: {relative}");
            extractedBytes += entry.Length;
            if (extractedBytes > MaxArchiveBytes) throw new InvalidOperationException("解压后的主题资源超过 200 MB 限制。");

            var outputPath = Path.GetFullPath(Path.Combine(destination, relative));
            var destinationRoot = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!outputPath.StartsWith(destinationRoot, StringComparison.Ordinal))
                throw new InvalidOperationException($"主题归档包含不安全路径: {relative}");
            if (!outputPaths.Add(outputPath)) throw new InvalidOperationException($"主题归档包含重复路径: {relative}");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            entry.ExtractToFile(outputPath, true);
        }
    }

    internal static bool ShouldExtract(string relative)
    {
        if (relative.Equals("theme-repository.json", StringComparison.Ordinal)) return true;
        var slash = relative.IndexOf('/');
        if (slash <= 0) return false;
        var directory = relative[..slash];
        var extension = Path.GetExtension(relative).ToLowerInvariant();
        return directory switch
        {
            "themes" or "schemas" => extension == ".json",
            "backgrounds" or "logos" or "previews" or "pets" => extension is ".jpg" or ".jpeg" or ".png" or ".webp" or ".avif",
            _ => false,
        };
    }

    public static int ValidateDirectory(string rootDirectory)
    {
        rootDirectory = Path.GetFullPath(rootDirectory);
        if (!Directory.Exists(rootDirectory)) throw new DirectoryNotFoundException($"主题仓库目录不存在: {rootDirectory}");
        foreach (var schema in new[] { "theme-v1.schema.json", "theme-repository-v1.schema.json" })
        {
            var schemaPath = Path.Combine(rootDirectory, "schemas", schema);
            if (!File.Exists(schemaPath)) throw new FileNotFoundException($"主题仓库缺少 Schema: schemas/{schema}", schemaPath);
        }
        foreach (var directory in new[] { "themes", "schemas", "backgrounds", "logos", "previews", "pets" })
        {
            var path = Path.Combine(rootDirectory, directory);
            if (Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null)
                throw new InvalidOperationException($"主题仓库目录不能是符号链接: {directory}");
        }
        var publishableFiles = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Where(path => ShouldExtract(Path.GetRelativePath(rootDirectory, path).Replace('\\', '/')))
            .ToList();
        if (publishableFiles.Count > MaxFiles) throw new InvalidOperationException($"主题仓库文件数超过 {MaxFiles} 个限制。");
        long totalBytes = 0;
        foreach (var file in publishableFiles)
        {
            if (new FileInfo(file).LinkTarget is not null)
                throw new InvalidOperationException($"主题文件不能是符号链接: {Path.GetRelativePath(rootDirectory, file)}");
            var length = new FileInfo(file).Length;
            if (length > MaxFileBytes) throw new InvalidOperationException($"主题文件超过 20 MB: {Path.GetRelativePath(rootDirectory, file)}");
            totalBytes += length;
        }
        if (totalBytes > MaxArchiveBytes) throw new InvalidOperationException("主题仓库资源超过 200 MB 限制。");

        var manifestPath = Path.Combine(rootDirectory, "theme-repository.json");
        if (!File.Exists(manifestPath)) throw new InvalidOperationException("主题仓库根目录缺少 theme-repository.json。");
        var root = JsonNode.Parse(File.ReadAllText(manifestPath, Encoding.UTF8)) as JsonObject
                   ?? throw new InvalidOperationException("theme-repository.json 格式无效。");
        EnsureRepositoryKeys(root, "$schema", "schemaVersion", "name", "updatedAt", "themes");
        if (root["schemaVersion"]?.GetValue<int>() != 1) throw new InvalidOperationException("只支持主题仓库标准 v1。");
        var name = root["name"]?.GetValue<string>() ?? "";
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidOperationException("主题仓库 name 必须包含 1 到 80 个字符。");
        var updatedAt = root["updatedAt"]?.GetValue<string>() ?? "";
        if (!Regex.IsMatch(updatedAt, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$") ||
            !DateTimeOffset.TryParse(updatedAt, out _))
            throw new InvalidOperationException("主题仓库 updatedAt 必须是 UTC 时间，例如 2026-07-17T00:00:00Z。");
        if (root["themes"] is not JsonArray themes || themes.Count == 0 || themes.Count > 500)
            throw new InvalidOperationException("主题仓库 themes 数量必须在 1 到 500 之间。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var manifests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in themes)
        {
            if (item is not JsonObject entry) throw new InvalidOperationException("主题仓库条目格式无效。");
            EnsureRepositoryKeys(entry, "id", "manifest");
            var id = entry["id"]?.GetValue<string>() ?? "";
            var relative = entry["manifest"]?.GetValue<string>() ?? "";
            if (!Regex.IsMatch(id, "^[a-z0-9][a-z0-9-]{1,63}$") ||
                !Regex.IsMatch(relative, "^themes/[a-z0-9][a-z0-9-]*\\.json$") || !ids.Add(id) || !manifests.Add(relative))
                throw new InvalidOperationException($"主题仓库条目无效或 ID 重复: {id}");
            var themePath = Path.Combine(rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(themePath)) throw new FileNotFoundException($"主题清单不存在: {relative}", themePath);
            var themeRoot = JsonNode.Parse(File.ReadAllText(themePath, Encoding.UTF8));
            if (themeRoot?["schemaVersion"]?.GetValue<int>() != 1)
                throw new InvalidOperationException($"远程主题必须使用 schemaVersion 1: {id}");
            var definition = ThemeDefinition.Load(themePath);
            if (!definition.CodeThemeId.Equals(id, StringComparison.Ordinal))
                throw new InvalidOperationException($"主题 ID 与仓库清单不一致: {id}");
            if (!Path.GetFileNameWithoutExtension(themePath).Equals(id, StringComparison.Ordinal))
                throw new InvalidOperationException($"主题文件名必须与 ID 一致: {relative}");
            definition.ValidateAssets();
        }

        var actualManifests = Directory.EnumerateFiles(Path.Combine(rootDirectory, "themes"), "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => $"themes/{Path.GetFileName(path)}")
            .ToHashSet(StringComparer.Ordinal);
        if (!actualManifests.SetEquals(manifests))
        {
            var missing = actualManifests.Except(manifests, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
            var stale = manifests.Except(actualManifests, StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal);
            throw new InvalidOperationException($"主题索引与 themes 目录不一致。未索引: {string.Join(", ", missing)}；不存在: {string.Join(", ", stale)}");
        }
        return ids.Count;
    }

    private static void EnsureRepositoryKeys(JsonObject value, params string[] keys)
    {
        var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
        var unknown = value.Select(item => item.Key).FirstOrDefault(key => !allowed.Contains(key));
        if (unknown is not null) throw new InvalidOperationException($"主题仓库包含不允许的字段: {unknown}");
    }

    private static void ReplaceCache(string sourceDirectory)
    {
        var backup = CacheDirectory + ".previous";
        if (Directory.Exists(backup)) Directory.Delete(backup, true);
        if (Directory.Exists(CacheDirectory)) Directory.Move(CacheDirectory, backup);
        try
        {
            Directory.Move(sourceDirectory, CacheDirectory);
            if (Directory.Exists(backup)) Directory.Delete(backup, true);
        }
        catch
        {
            if (Directory.Exists(CacheDirectory)) Directory.Delete(CacheDirectory, true);
            if (Directory.Exists(backup)) Directory.Move(backup, CacheDirectory);
            throw;
        }
    }
}
