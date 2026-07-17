using System.Text;

namespace CodexThemeStore.Core;

public sealed class ThemeStateCompiler
{
    public ThemeInjectionPayload Compile(string themePath, string stateDirectory)
    {
        var theme = ThemeDefinition.Load(Path.GetFullPath(themePath));
        theme.ValidateAssets();
        var backgroundUrl = theme.BackgroundImage is null ? null : ToDataUrl(theme.ResolveBackgroundImage());
        var logoUrl = theme.LogoImage is null ? null : ToDataUrl(theme.ResolveLogoImage());
        var petUrl = theme.PetImage is null ? null : ToDataUrl(theme.ResolvePetImage());
        var css = CssBuilder.Build(theme, backgroundUrl);
        var script = JsBuilder.Build(theme.Copy, theme.Home, theme.CodeThemeId, logoUrl, petUrl);
        var payload = new ThemeInjectionPayload(css, script, theme.CodeThemeId);
        WriteStateAtomically(stateDirectory, theme.RawJson, payload);
        return payload;
    }

    private static string? ToDataUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return path;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("v1 主题不允许引用远程运行时资源。");
        if (!File.Exists(path)) throw new FileNotFoundException("主题图片不存在。", path);
        var mime = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".avif" => "image/avif",
            _ => throw new InvalidOperationException($"不支持的主题图片格式: {Path.GetExtension(path)}"),
        };
        return $"data:{mime};base64,{Convert.ToBase64String(File.ReadAllBytes(path))}";
    }

    private static void WriteStateAtomically(string stateDirectory, string themeJson, ThemeInjectionPayload payload)
    {
        Directory.CreateDirectory(stateDirectory);
        WriteFileAtomically(Path.Combine(stateDirectory, "codex-theme.css"), payload.Css);
        WriteFileAtomically(Path.Combine(stateDirectory, "codex-theme.js"), payload.JavaScript);
        WriteFileAtomically(Path.Combine(stateDirectory, "current-theme.json"), themeJson);
    }

    private static void WriteFileAtomically(string destination, string content)
    {
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
