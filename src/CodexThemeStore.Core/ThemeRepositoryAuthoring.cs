using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexThemeStore.Core;

public sealed record ThemeRepositoryPackageResult(string Path, int ThemeCount, long Bytes, string Sha256);

public sealed class ThemeRepositoryAuthoring
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public int GenerateIndex(string rootDirectory, string? repositoryName = null, DateTimeOffset? updatedAt = null)
    {
        var root = Path.GetFullPath(rootDirectory);
        var themesDirectory = Path.Combine(root, "themes");
        if (!Directory.Exists(themesDirectory)) throw new DirectoryNotFoundException($"主题目录不存在: {themesDirectory}");

        var entries = new List<(string Id, string Manifest)>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(themesDirectory, "*.json", SearchOption.TopDirectoryOnly).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var definition = ThemeDefinition.Load(path);
            definition.ValidateAssets();
            var fileId = Path.GetFileNameWithoutExtension(path);
            if (!fileId.Equals(definition.CodeThemeId, StringComparison.Ordinal))
                throw new InvalidOperationException($"主题文件名必须与 codeThemeId 一致: {Path.GetFileName(path)}");
            if (!ids.Add(definition.CodeThemeId)) throw new InvalidOperationException($"主题 ID 重复: {definition.CodeThemeId}");
            entries.Add((definition.CodeThemeId, $"themes/{Path.GetFileName(path)}"));
        }
        if (entries.Count == 0) throw new InvalidOperationException("themes 目录中没有主题清单。");

        var indexPath = Path.Combine(root, "theme-repository.json");
        var name = string.IsNullOrWhiteSpace(repositoryName) ? ReadRepositoryName(indexPath) : repositoryName.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = "Codex Skin Theme Repository";
        if (name.Length > 80) throw new InvalidOperationException("主题仓库名称不能超过 80 个字符。");

        var themes = new JsonArray(entries.OrderBy(entry => entry.Id, StringComparer.Ordinal)
            .Select(entry => (JsonNode)new JsonObject
            {
                ["id"] = entry.Id,
                ["manifest"] = entry.Manifest,
            }).ToArray());
        var index = new JsonObject
        {
            ["$schema"] = "./schemas/theme-repository-v1.schema.json",
            ["schemaVersion"] = 1,
            ["name"] = name,
            ["updatedAt"] = (updatedAt ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
            ["themes"] = themes,
        };
        WriteTextAtomically(indexPath, index.ToJsonString(JsonOptions) + Environment.NewLine);
        return ThemeRepositoryClient.ValidateDirectory(root);
    }

    public ThemeRepositoryPackageResult Package(string rootDirectory, string destination)
    {
        var root = Path.GetFullPath(rootDirectory);
        var count = ThemeRepositoryClient.ValidateDirectory(root);
        var output = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var temporary = output + $".{Guid.NewGuid():N}.tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                             .Select(path => new { Path = path, Relative = Path.GetRelativePath(root, path).Replace('\\', '/') })
                             .Where(item => ThemeRepositoryClient.ShouldExtract(item.Relative))
                             .OrderBy(item => item.Relative, StringComparer.Ordinal))
                {
                    archive.CreateEntryFromFile(file.Path, file.Relative, CompressionLevel.Optimal);
                }
            }
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        using var stream = File.OpenRead(output);
        var hash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        return new ThemeRepositoryPackageResult(output, count, stream.Length, hash);
    }

    private static string? ReadRepositoryName(string indexPath)
    {
        if (!File.Exists(indexPath)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(indexPath, Encoding.UTF8))?["name"]?.GetValue<string>();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void WriteTextAtomically(string destination, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
