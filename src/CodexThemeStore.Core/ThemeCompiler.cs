using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace CodexThemeStore.Core;

public sealed class ThemeDefinition
{
    private readonly JsonNode _root;

    private ThemeDefinition(JsonNode root, string rawJson, string sourcePath)
    {
        _root = root;
        RawJson = rawJson;
        SourcePath = sourcePath;
    }

    public string RawJson { get; }
    public string SourcePath { get; }
    public JsonNode? Copy => _root["copy"];
    public JsonNode? Home => _root["home"] ?? BuildDreamSkinHome();
    public string DisplayName => GetString("displayName", GetString("name", GetString("codeThemeId", "theme")));
    public string CodeThemeId => GetString("codeThemeId", GetString("id", "absolutely"));
    public string Category => GetString("category", "其他");
    public string Description => GetString("description", Variant == "dark" ? "深色主题" : "浅色主题");
    public string Version => GetString("version", "0.0.0");
    public string Variant => _root["variant"] is not null
        ? GetString("variant", "light").Equals("dark", StringComparison.OrdinalIgnoreCase) ? "dark" : "light"
        : ColorUtil.Luminance(GetStoreColor("background", "#f5f4ee")) < 0.35 ? "dark" : "light";
    public string Accent => GetThemeString("accent", GetStoreColor("accent", "#da7756"));
    public string Ink => GetThemeString("ink", GetStoreColor("text", "#141413"));
    public string Surface => GetThemeString("surface", GetStoreColor("panel", GetStoreColor("background", "#f5f4ee")));
    public string DiffAdded => GetSemanticString("diffAdded", GetStoreColor("highlight", "#00c853"));
    public string DiffRemoved => GetSemanticString("diffRemoved", GetStoreColor("accentAlt", "#ff5f38"));
    public string Skill => GetSemanticString("skill", GetStoreColor("secondary", "#cc7d5e"));
    public string UiFont => GetFontString("ui", "ui-serif, Georgia, Cambria, \"Times New Roman\", Times, \"Noto Serif SC\", serif");
    public string DisplayFont => GetFontString("display", "ui-serif, Georgia, Cambria, \"Times New Roman\", Times, \"Noto Serif SC\", serif");
    public string CodeFont => GetFontString("code", "JetBrainsMono NFM");
    public bool OpaqueWindows => GetThemeBool("opaqueWindows", true);
    public double Contrast => Clamp(GetThemeDouble("contrast", 45), 0, 100);
    public string? BackgroundImage => GetThemeNullableString("backgroundImage") ?? _root["image"]?.GetValue<string>();
    public string? LogoImage => GetThemeNullableString("logoImage");
    public string? PetImage => _root["home"]?["pet"]?["image"]?.GetValue<string>();
    public string? PreviewImage => _root["previewImage"]?.GetValue<string>() ?? _root["assets"]?["preview"]?["path"]?.GetValue<string>();
    public double BackgroundImageOpacity => Clamp(GetThemeDouble("backgroundImageOpacity", 0.18), 0, 1);
    public double BackgroundImageBlur => Clamp(GetThemeDouble("backgroundImageBlur", 0), 0, 24);

    public string ResolveBackgroundImage()
    {
        return ResolveAsset(BackgroundImage);
    }

    public string ResolveLogoImage()
    {
        return ResolveAsset(LogoImage);
    }

    public string ResolvePetImage()
    {
        return ResolveAsset(PetImage);
    }

    public string ResolvePreviewImage()
    {
        return ResolveAsset(PreviewImage ?? BackgroundImage);
    }

    private string ResolveAsset(string? value)
    {
        var source = value?.Trim() ?? "";
        if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }
        if (Path.IsPathRooted(source)) return Path.GetFullPath(source);
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(SourcePath)!, source));
    }

    public static ThemeDefinition Load(string path)
    {
        var raw = File.ReadAllText(path, Encoding.UTF8);
        var root = JsonNode.Parse(raw) ?? throw new InvalidOperationException($"Invalid JSON: {path}");
        var definition = new ThemeDefinition(root, raw, Path.GetFullPath(path));
        if (root["schemaVersion"]?.GetValue<int>() == 1)
        {
            if (root["packageVersion"] is null) definition.ValidateV1();
            else definition.ValidateDreamSkinPackage();
        }
        return definition;
    }

    private void ValidateDreamSkinPackage()
    {
        if (_root["packageVersion"]?.GetValue<int>() != 1) throw new InvalidOperationException("只支持 DreamSkin packageVersion 1。");
        foreach (var key in new[] { "id", "name", "version", "description", "image", "colors", "assets", "signature" })
            if (_root[key] is null) throw new InvalidOperationException($"DreamSkin 清单缺少字段: {key}");
        RequireLength(CodeThemeId, 3, 128, "id");
        RequireLength(DisplayName, 1, 80, "name");
        RequirePattern(Version, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", "version");
        if (Path.GetFileName(BackgroundImage) != BackgroundImage || Path.GetFileName(PreviewImage) != PreviewImage)
            throw new InvalidOperationException("DreamSkin 图片必须位于已安装主题目录根部。");
    }

    private void ValidateV1()
    {
        if (_root is not JsonObject root) throw new InvalidOperationException($"主题清单必须是对象: {SourcePath}");
        EnsureOnlyKeys(root, "$schema", "schemaVersion", "version", "displayName", "codeThemeId", "category", "description", "author", "variant", "previewImage", "theme", "home", "copy");
        RequireKeys(root, "schemaVersion", "version", "displayName", "codeThemeId", "category", "description", "author", "variant", "previewImage", "theme", "home");
        RequirePattern(CodeThemeId, "^[a-z0-9][a-z0-9-]{1,63}$", "codeThemeId");
        RequirePattern(Version, "^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[0-9A-Za-z.-]+)?$", "version");
        RequireLength(DisplayName, 1, 60, "displayName");
        RequireLength(root["description"]!.GetValue<string>(), 1, 120, "description");
        RequireLength(root["author"]!.GetValue<string>(), 1, 60, "author");
        if (!new[] { "人物", "动漫", "游戏", "风景", "极简", "节日", "其他" }.Contains(Category))
            throw new InvalidOperationException($"不支持的主题分类: {Category}");
        var rawVariant = root["variant"]?.GetValue<string>();
        if (rawVariant is not ("light" or "dark")) throw new InvalidOperationException($"不支持的主题模式: {rawVariant}");

        if (root["theme"] is not JsonObject theme) throw new InvalidOperationException("主题缺少 theme 对象。");
        EnsureOnlyKeys(theme, "accent", "contrast", "fonts", "ink", "opaqueWindows", "semanticColors", "surface", "backgroundImage", "logoImage", "backgroundImageOpacity", "backgroundImageBlur");
        RequireKeys(theme, "accent", "ink", "surface");
        foreach (var colorKey in new[] { "accent", "ink", "surface" }) RequirePattern(theme[colorKey]!.GetValue<string>(), "^#[0-9A-Fa-f]{6}$", colorKey);
        RequireOptionalRange(theme["contrast"], 0, 100, "theme.contrast");
        RequireOptionalRange(theme["backgroundImageOpacity"], 0, 1, "theme.backgroundImageOpacity");
        RequireOptionalRange(theme["backgroundImageBlur"], 0, 24, "theme.backgroundImageBlur");
        if (theme["fonts"] is JsonObject fonts)
        {
            EnsureOnlyKeys(fonts, "code", "ui", "display");
            RequireOptionalLength(fonts["code"], 200, "theme.fonts.code");
            RequireOptionalLength(fonts["ui"], 300, "theme.fonts.ui");
            RequireOptionalLength(fonts["display"], 300, "theme.fonts.display");
        }
        else if (theme["fonts"] is not null) throw new InvalidOperationException("theme.fonts 必须是对象。");
        if (theme["semanticColors"] is JsonObject semantic)
        {
            EnsureOnlyKeys(semantic, "diffAdded", "diffRemoved", "skill");
            foreach (var key in new[] { "diffAdded", "diffRemoved", "skill" })
                if (semantic[key] is JsonNode value) RequirePattern(value.GetValue<string>(), "^#[0-9A-Fa-f]{6}$", $"theme.semanticColors.{key}");
        }
        else if (theme["semanticColors"] is not null) throw new InvalidOperationException("theme.semanticColors 必须是对象。");

        if (root["home"] is not JsonObject home) throw new InvalidOperationException("主题缺少 home 对象。");
        EnsureOnlyKeys(home, "brand", "eyebrow", "badge", "title", "subtitle", "footerNote", "composerHint", "tags", "sidebarLabels", "quickActions", "pet");
        RequireKeys(home, "brand", "title", "quickActions");
        RequireLength(home["brand"]!.GetValue<string>(), 1, 200, "home.brand");
        RequireLength(home["title"]!.GetValue<string>(), 1, 200, "home.title");
        foreach (var key in new[] { "eyebrow", "badge", "subtitle", "footerNote", "composerHint" })
            RequireOptionalLength(home[key], 200, $"home.{key}");
        if (home["tags"] is JsonArray tags)
        {
            if (tags.Count > 6) throw new InvalidOperationException("home.tags 最多包含 6 项。");
            foreach (var tag in tags) RequireLength(tag?.GetValue<string>() ?? "", 0, 40, "home.tags[]");
        }
        else if (home["tags"] is not null) throw new InvalidOperationException("home.tags 必须是数组。");
        if (home["sidebarLabels"] is JsonObject labels)
        {
            EnsureOnlyKeys(labels, "newTask", "scheduled", "plugins", "settings");
            foreach (var label in labels) RequireOptionalLength(label.Value, 40, $"home.sidebarLabels.{label.Key}");
        }
        else if (home["sidebarLabels"] is not null) throw new InvalidOperationException("home.sidebarLabels 必须是对象。");
        if (home["pet"] is not null && home["pet"] is not JsonObject)
            throw new InvalidOperationException("pet 必须是对象。");
        if (home["pet"] is JsonObject pet)
        {
            EnsureOnlyKeys(pet, "image", "alt", "size");
            RequireKeys(pet, "image");
            RequireOptionalLength(pet["alt"], 40, "home.pet.alt");
            if (pet["size"] is JsonNode sizeNode && (sizeNode.GetValue<double>() < 48 || sizeNode.GetValue<double>() > 220))
                throw new InvalidOperationException("pet.size 必须在 48 到 220 之间。");
        }
        if (home["quickActions"] is not JsonArray actions || actions.Count != 4)
            throw new InvalidOperationException("quickActions 必须包含 4 个引导项。");
        foreach (var action in actions)
        {
            if (action is not JsonObject actionObject) throw new InvalidOperationException("quickActions 项必须是对象。");
            EnsureOnlyKeys(actionObject, "icon", "title", "description", "prompt");
            var title = actionObject["title"]?.GetValue<string>() ?? "";
            var prompt = actionObject["prompt"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(prompt))
                throw new InvalidOperationException("quickActions 项缺少 title 或 prompt。");
            RequireLength(title, 1, 200, "home.quickActions[].title");
            RequireLength(prompt, 1, 1000, "home.quickActions[].prompt");
            RequireOptionalLength(actionObject["description"], 200, "home.quickActions[].description");
            RequireOptionalLength(actionObject["icon"], 12, "home.quickActions[].icon");
        }

        if (root["copy"] is JsonObject copy)
        {
            EnsureOnlyKeys(copy, "title", "replacePlaceholders");
            RequireOptionalLength(copy["title"], 200, "copy.title");
            if (copy["replacePlaceholders"] is JsonObject placeholders)
            {
                if (placeholders.Count > 20) throw new InvalidOperationException("copy.replacePlaceholders 最多包含 20 项。");
                foreach (var placeholder in placeholders)
                {
                    RequireLength(placeholder.Key, 0, 100, "copy.replacePlaceholders key");
                    RequireOptionalLength(placeholder.Value, 200, $"copy.replacePlaceholders.{placeholder.Key}");
                }
            }
            else if (copy["replacePlaceholders"] is not null) throw new InvalidOperationException("copy.replacePlaceholders 必须是对象。");
        }
        else if (root["copy"] is not null) throw new InvalidOperationException("copy 必须是对象。");
        RequireAssetPath(PreviewImage, "previews", "previewImage");
        RequireAssetPath(BackgroundImage, "backgrounds", "theme.backgroundImage");
        RequireAssetPath(LogoImage, "logos", "theme.logoImage");
        RequireAssetPath(PetImage, "pets", "home.pet.image");
    }

    public void ValidateAssets()
    {
        foreach (var asset in new[] { PreviewImage, BackgroundImage, LogoImage, PetImage }.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            var resolved = ResolveAsset(asset);
            if (!File.Exists(resolved)) throw new FileNotFoundException($"主题资源不存在: {asset}", resolved);
            ValidateImageSignature(resolved);
        }
    }

    private static void ValidateImageSignature(string path)
    {
        Span<byte> header = stackalloc byte[32];
        using var stream = File.OpenRead(path);
        var read = stream.Read(header);
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var valid = extension switch
        {
            ".png" => read >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }),
            ".jpg" or ".jpeg" => read >= 3 && header[0] == 0xff && header[1] == 0xd8 && header[2] == 0xff,
            ".webp" => read >= 12 && header[..4].SequenceEqual("RIFF"u8) && header.Slice(8, 4).SequenceEqual("WEBP"u8),
            ".avif" => read >= 12 && header.Slice(4, 4).SequenceEqual("ftyp"u8) &&
                       (header[..read].IndexOf("avif"u8) >= 0 || header[..read].IndexOf("avis"u8) >= 0),
            _ => false,
        };
        if (!valid) throw new InvalidDataException($"主题图片格式与扩展名不匹配: {path}");
    }

    private static void EnsureOnlyKeys(JsonObject value, params string[] keys)
    {
        var allowed = new HashSet<string>(keys, StringComparer.Ordinal);
        var unknown = value.Select(item => item.Key).FirstOrDefault(key => !allowed.Contains(key));
        if (unknown is not null) throw new InvalidOperationException($"主题包含 v1 不允许的字段: {unknown}");
    }

    private static void RequireKeys(JsonObject value, params string[] keys)
    {
        var missing = keys.FirstOrDefault(key => value[key] is null);
        if (missing is not null) throw new InvalidOperationException($"主题缺少必填字段: {missing}");
    }

    private static void RequirePattern(string value, string pattern, string field)
    {
        if (!Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant))
            throw new InvalidOperationException($"主题字段 {field} 格式无效: {value}");
    }

    private static void RequireAssetPath(string? value, string directory, string field)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        RequirePattern(value, $"^\\.\\./{directory}/[A-Za-z0-9._-]+\\.(?:png|jpe?g|webp|avif)$", field);
    }

    private static void RequireLength(string value, int min, int max, string field)
    {
        if (value.Length < min || value.Length > max)
            throw new InvalidOperationException($"主题字段 {field} 长度必须在 {min} 到 {max} 之间。");
    }

    private static void RequireOptionalLength(JsonNode? node, int max, string field)
    {
        if (node is null) return;
        RequireLength(node.GetValue<string>(), 0, max, field);
    }

    private static void RequireOptionalRange(JsonNode? node, double min, double max, string field)
    {
        if (node is null) return;
        var value = node.GetValue<double>();
        if (value < min || value > max) throw new InvalidOperationException($"主题字段 {field} 必须在 {min} 到 {max} 之间。");
    }

    private string GetString(string key, string fallback) => _root[key]?.GetValue<string>() ?? fallback;
    private string GetThemeString(string key, string fallback) => _root["theme"]?[key]?.GetValue<string>() ?? fallback;
    private string GetStoreColor(string key, string fallback)
    {
        var value = _root["colors"]?[key]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        if (Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")) return value;
        var rgba = Regex.Match(value, "^rgba\\((\\d{1,3}),[ ]*(\\d{1,3}),[ ]*(\\d{1,3}),[ ]*(?:0|1|0?\\.\\d{1,3}|1\\.0{1,3})\\)$");
        return rgba.Success
            ? $"#{int.Parse(rgba.Groups[1].Value):x2}{int.Parse(rgba.Groups[2].Value):x2}{int.Parse(rgba.Groups[3].Value):x2}"
            : fallback;
    }

    private JsonNode? BuildDreamSkinHome()
    {
        if (_root["packageVersion"] is null) return null;
        return new JsonObject
        {
            ["brand"] = GetString("name", CodeThemeId),
            ["eyebrow"] = GetString("brandSubtitle", "Dream Skin"),
            ["badge"] = GetString("statusText", "已验证主题"),
            ["title"] = GetString("tagline", "我们该构建什么？"),
            ["subtitle"] = GetString("description", ""),
            ["footerNote"] = GetString("quote", GetString("brandSubtitle", "Dream Skin")),
            ["tags"] = new JsonArray(GetString("projectLabel", "签名主题"), "DreamSkin", "Ed25519"),
            ["quickActions"] = new JsonArray(
                DreamSkinAction("理解代码", "梳理结构与关键流程", "请概览当前代码库的结构、关键模块和主要流程。"),
                DreamSkinAction("构建功能", "实现可运行的新能力", "请基于当前项目实现一个新功能，并完成必要验证。"),
                DreamSkinAction("审查代码", "查找缺陷和回归风险", "请审查当前代码，优先指出缺陷、风险和缺失测试。"),
                DreamSkinAction("修复问题", "定位根因并验证修复", "请诊断当前问题，修复根因并验证结果。"))
        };
    }

    private static JsonObject DreamSkinAction(string title, string description, string prompt) => new()
    {
        ["title"] = title,
        ["description"] = description,
        ["prompt"] = prompt,
    };
    private string? GetThemeNullableString(string key)
    {
        var node = _root["theme"]?[key];
        if (node is null) return null;
        return node.GetValueKind() == JsonValueKind.Null ? null : node.GetValue<string>();
    }
    private string GetFontString(string key, string fallback) => _root["theme"]?["fonts"]?[key]?.GetValue<string>() ?? fallback;
    private string GetSemanticString(string key, string fallback) => _root["theme"]?["semanticColors"]?[key]?.GetValue<string>() ?? fallback;
    private bool GetThemeBool(string key, bool fallback) => _root["theme"]?[key]?.GetValue<bool>() ?? fallback;
    private double GetThemeDouble(string key, double fallback) => _root["theme"]?[key]?.GetValue<double>() ?? fallback;
    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);
}

public static class CssBuilder
{
    public static string Build(ThemeDefinition theme, string? backgroundUrl)
    {
        var dark = theme.Variant == "dark" || ColorUtil.Luminance(theme.Surface) < 0.35;
        var contrast = theme.Contrast / 100.0;
        var baseMix = dark ? "#000000" : "#ffffff";
        var under = ColorUtil.Mix(theme.Surface, dark ? "#000000" : "#d9d4c6", dark ? 0.18 : 0.22);
        var elevated = ColorUtil.Mix(theme.Surface, baseMix, dark ? 0.10 : 0.44);
        var elevated2 = ColorUtil.Mix(theme.Surface, baseMix, dark ? 0.16 : 0.64);
        var softAccent = ColorUtil.Mix(theme.Surface, theme.Accent, dark ? 0.22 : 0.12);
        var activeAccent = ColorUtil.Mix(theme.Accent, dark ? "#ffffff" : "#000000", dark ? 0.18 : 0.08);
        var secondaryText = ColorUtil.Alpha(theme.Ink, dark ? 0.80 : 0.76);
        var tertiaryText = ColorUtil.Alpha(theme.Ink, dark ? 0.60 : 0.56);
        var border = ColorUtil.Alpha(theme.Ink, dark ? 0.20 + contrast * 0.10 : 0.12 + contrast * 0.08);
        var borderHeavy = ColorUtil.Alpha(theme.Ink, dark ? 0.30 + contrast * 0.12 : 0.18 + contrast * 0.10);
        var hover = ColorUtil.Alpha(theme.Accent, dark ? 0.20 : 0.12);
        var buttonText = ColorUtil.ReadableText(theme.Accent);
        var dangerBg = ColorUtil.Mix(theme.Surface, theme.DiffRemoved, dark ? 0.20 : 0.11);
        var successBg = ColorUtil.Mix(theme.Surface, theme.DiffAdded, dark ? 0.17 : 0.10);

        var backgroundCss = "";
        if (!string.IsNullOrWhiteSpace(backgroundUrl))
        {
            var backgroundFilter = theme.BackgroundImageBlur > 0
                ? $"blur({CssNumber(theme.BackgroundImageBlur)}px) saturate(1.05)"
                : "none";
            backgroundCss = $@"
:root {{
  --codex-theme-background-image: url(""{CssEscape(backgroundUrl)}"");
}}

html[data-codex-window-type=""electron""] .main-surface,
html[data-codex-window-type=""electron""] .browser-main-surface {{
  position: relative;
}}

html[data-codex-window-type=""electron""] .main-surface::before,
html[data-codex-window-type=""electron""] .browser-main-surface::before {{
  content: """";
  position: absolute;
  inset: 0;
  z-index: 0;
  pointer-events: none;
  background-image: var(--codex-theme-background-image);
  background-size: cover;
  background-position: center top;
  background-repeat: no-repeat;
  opacity: {CssNumber(theme.BackgroundImageOpacity)};
  -webkit-mask-image: linear-gradient(to bottom, #000 0%, #000 50%, rgba(0, 0, 0, 0.82) 64%, rgba(0, 0, 0, 0.34) 82%, transparent 100%);
  mask-image: linear-gradient(to bottom, #000 0%, #000 50%, rgba(0, 0, 0, 0.82) 64%, rgba(0, 0, 0, 0.34) 82%, transparent 100%);
  -webkit-mask-size: 100% 100%;
  mask-size: 100% 100%;
  filter: {backgroundFilter};
  transform: {(theme.BackgroundImageBlur > 0 ? "scale(1.03)" : "none")};
  transition: opacity 180ms ease;
}}

html[data-codex-window-type=""electron""] .main-surface > *,
html[data-codex-window-type=""electron""] .browser-main-surface > * {{
  position: relative;
  z-index: 1;
}}

html[data-codex-window-type=""electron""] .main-surface.codex-theme-home-active::before,
html[data-codex-window-type=""electron""] .browser-main-surface.codex-theme-home-active::before {{
  opacity: 0;
}}
";
        }

        var baseCss = $@"/* Generated by CodexThemeStore.exe. */
:root {{
  --codex-theme-id: ""{CssEscape(theme.CodeThemeId)}"";
  --codex-theme-variant: {theme.Variant};
  --codex-theme-accent: {theme.Accent};
  --codex-theme-surface: {theme.Surface};
  --codex-theme-ink: {theme.Ink};
  --codex-theme-skill: {theme.Skill};
}}

:root,
html[data-codex-window-type=""electron""],
html[data-codex-window-type=""electron""] body {{
  color-scheme: {(dark ? "dark" : "light")};
  --vscode-font-family: {theme.UiFont};
  --vscode-editor-font-family: {QuoteFont(theme.CodeFont)}, ui-monospace, ""SFMono-Regular"", ""SF Mono"", Menlo, Consolas, ""Liberation Mono"", monospace;
  --font-sans-default: {theme.UiFont};
  --font-mono-default: {QuoteFont(theme.CodeFont)}, ui-monospace, ""SFMono-Regular"", ""SF Mono"", Menlo, Consolas, ""Liberation Mono"", monospace;

  --color-background-surface: {theme.Surface};
  --color-background-surface-under: {under};
  --color-background-panel: {ColorUtil.Mix(theme.Surface, under, 0.46)};
  --color-background-elevated-primary: {elevated};
  --color-background-elevated-primary-opaque: {elevated};
  --color-background-elevated-secondary: {elevated2};
  --color-background-elevated-secondary-opaque: {elevated2};
  --color-background-control: {softAccent};
  --color-background-control-opaque: {softAccent};
  --color-background-accent: {softAccent};
  --color-background-accent-hover: {hover};
  --color-background-accent-active: {ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)};
  --color-background-button-primary: {theme.Accent};
  --color-background-button-primary-hover: {activeAccent};
  --color-background-button-primary-active: {ColorUtil.Mix(theme.Accent, "#000000", dark ? 0.05 : 0.16)};
  --color-background-button-secondary: {elevated2};
  --color-background-button-secondary-hover: {ColorUtil.Mix(elevated2, theme.Accent, dark ? 0.16 : 0.08)};
  --color-background-button-tertiary-hover: {hover};
  --color-background-status-error: {dangerBg};
  --color-background-status-success: {successBg};

  --color-text-foreground: {theme.Ink};
  --color-text-foreground-secondary: {secondaryText};
  --color-text-foreground-tertiary: {tertiaryText};
  --color-text-accent: {theme.Accent};
  --color-text-button-primary: {buttonText};
  --color-text-button-secondary: {theme.Ink};
  --color-text-button-tertiary: {theme.Ink};
  --color-text-error: {theme.DiffRemoved};
  --color-text-success: {theme.DiffAdded};
  --color-border: {border};
  --color-border-light: {ColorUtil.Alpha(theme.Ink, dark ? 0.12 : 0.07)};
  --color-border-heavy: {borderHeavy};
  --color-border-focus: {theme.Accent};
  --color-accent-blue: {theme.Accent};
  --color-accent-green: {theme.DiffAdded};
  --color-accent-orange: {theme.DiffRemoved};
  --color-accent-purple: {theme.Skill};
  --color-accent-red: {theme.DiffRemoved};
  --color-accent-yellow: #c59b21;
  --color-icon-primary: {theme.Ink};
  --color-icon-secondary: {secondaryText};
  --color-icon-tertiary: {tertiaryText};
  --color-icon-accent: {theme.Accent};
  --color-icon-error: {theme.DiffRemoved};
  --color-icon-success: {theme.DiffAdded};

  --color-decoration-added: {theme.DiffAdded};
  --color-decoration-deleted: {theme.DiffRemoved};
  --color-decoration-modified: {theme.Accent};
  --color-decoration-unchanged: {tertiaryText};
  --color-editor-added: {successBg};
  --color-editor-deleted: {dangerBg};

  --vscode-foreground: {theme.Ink};
  --vscode-disabledForeground: {tertiaryText};
  --vscode-descriptionForeground: {secondaryText};
  --vscode-errorForeground: {theme.DiffRemoved};
  --vscode-icon-foreground: {theme.Ink};
  --vscode-focusBorder: {theme.Accent};
  --vscode-textLink-foreground: {theme.Accent};
  --vscode-textLink-activeForeground: {activeAccent};
  --vscode-editor-background: {theme.Surface};
  --vscode-editor-foreground: {theme.Ink};
  --vscode-editorCursor-foreground: {theme.Accent};
  --vscode-sideBar-background: {under};
  --vscode-sideBar-foreground: {theme.Ink};
  --vscode-sideBarTitle-foreground: {theme.Ink};
  --vscode-panel-background: {under};
  --vscode-activityBar-background: {under};
  --vscode-activityBar-activeBorder: {theme.Accent};
  --vscode-activityBarBadge-background: {theme.Accent};
  --vscode-activityBarBadge-foreground: {buttonText};
  --vscode-badge-background: {softAccent};
  --vscode-badge-foreground: {theme.Ink};
  --vscode-input-background: {elevated2};
  --vscode-input-foreground: {theme.Ink};
  --vscode-input-placeholderForeground: {tertiaryText};
  --vscode-input-border: {border};
  --vscode-dropdown-background: {elevated2};
  --vscode-button-background: {theme.Accent};
  --vscode-button-foreground: {buttonText};
  --vscode-button-secondaryBackground: {elevated2};
  --vscode-button-secondaryForeground: {theme.Ink};
  --vscode-button-secondaryHoverBackground: {ColorUtil.Mix(elevated2, theme.Accent, dark ? 0.16 : 0.08)};
  --vscode-list-hoverBackground: {hover};
  --vscode-list-activeSelectionBackground: {softAccent};
  --vscode-list-activeSelectionForeground: {theme.Ink};
  --vscode-list-activeSelectionIconForeground: {theme.Accent};
  --vscode-list-focusOutline: {theme.Accent};
  --vscode-menu-background: {elevated2};
  --vscode-menu-border: {border};
  --vscode-menubar-selectionBackground: {hover};
  --vscode-menubar-selectionForeground: {theme.Ink};
  --vscode-scrollbarSlider-background: {ColorUtil.Alpha(theme.Ink, dark ? 0.22 : 0.16)};
  --vscode-scrollbarSlider-hoverBackground: {ColorUtil.Alpha(theme.Accent, dark ? 0.34 : 0.26)};
  --vscode-scrollbarSlider-activeBackground: {ColorUtil.Alpha(theme.Accent, dark ? 0.46 : 0.38)};
  --vscode-progressBar-background: {theme.Accent};
  --vscode-gitDecoration-addedResourceForeground: {theme.DiffAdded};
  --vscode-gitDecoration-deletedResourceForeground: {theme.DiffRemoved};
  --vscode-gitDecoration-modifiedResourceForeground: {theme.Accent};
  --vscode-terminal-background: {theme.Surface};
  --vscode-terminal-foreground: {theme.Ink};
}}

html[data-codex-window-type=""electron""].electron-opaque,
html[data-codex-window-type=""electron""].electron-opaque body {{
  background-color: {(theme.OpaqueWindows ? under : "transparent")} !important;
  background-image: none !important;
}}

html[data-codex-window-type=""electron""] .main-surface,
html[data-codex-window-type=""electron""] .browser-main-surface,
.main-surface,
.browser-main-surface {{
  background-color: {ColorUtil.Alpha(theme.Surface, dark ? 0.66 : 0.58)} !important;
  box-shadow: 0 0 0 1px {ColorUtil.Alpha(theme.Accent, dark ? 0.32 : 0.24)}, 0 18px 48px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.12)} !important;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel,
.app-shell-left-panel {{
  background: {ColorUtil.Alpha(ColorUtil.Mix(under, theme.Surface, dark ? 0.20 : 0.28), dark ? 0.88 : 0.84)} !important;
  border-right: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)} !important;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel button,
html[data-codex-window-type=""electron""] .app-shell-left-panel a {{
  color: {theme.Ink} !important;
  border-radius: 7px;
  transition: background-color 140ms ease, color 140ms ease, transform 140ms ease;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel button:hover,
html[data-codex-window-type=""electron""] .app-shell-left-panel a:hover {{
  color: {activeAccent} !important;
  background: {hover} !important;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel [aria-current=""page""],
html[data-codex-window-type=""electron""] .app-shell-left-panel [class~=""bg-token-list-hover-background""] {{
  color: {activeAccent} !important;
  background: {softAccent} !important;
  box-shadow: inset 0 0 0 1px {ColorUtil.Alpha(theme.Accent, dark ? 0.38 : 0.24)};
}}

{backgroundCss}

html[data-codex-window-type=""electron""] #codex-theme-home {{
  display: none;
  position: absolute !important;
  inset: 46px 0 138px;
  z-index: 6 !important;
  overflow-y: auto;
  padding: 24px 32px 28px;
  background: {theme.Surface};
}}

html[data-codex-window-type=""electron""] .codex-theme-home-active #codex-theme-home {{
  display: block;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-shell {{
  width: min(1480px, 100%);
  margin: 0 auto;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-hero {{
  min-height: 300px;
  display: flex;
  align-items: center;
  padding: 38px 42px;
  overflow: hidden;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.44 : 0.28)};
  border-radius: 8px;
  background-color: {theme.Surface};
  background-image: linear-gradient(90deg, {theme.Surface} 0%, {theme.Surface} 42%, {ColorUtil.Alpha(theme.Surface, 0.92)} 54%, {ColorUtil.Alpha(theme.Surface, 0.24)} 72%, transparent 100%), var(--codex-theme-background-image);
  background-size: 100% 100%, auto 100%;
  background-position: center, right center;
  background-repeat: no-repeat;
  box-shadow: 0 18px 44px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.10)};
}}

html[data-codex-window-type=""electron""] .codex-theme-home-copy {{
  width: min(560px, 48%);
  position: relative;
  z-index: 1;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-brand {{
  margin: 0 0 6px;
  color: {theme.Accent};
  font-size: 36px;
  line-height: 1.15;
  font-weight: 800;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-eyebrow {{
  margin: 0 0 20px;
  color: {secondaryText};
  font-size: 14px;
  font-weight: 600;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-title {{
  margin: 0 0 12px;
  color: {theme.Ink};
  font-size: 32px;
  line-height: 1.25;
  font-weight: 800;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-subtitle {{
  margin: 0;
  color: {secondaryText};
  font-size: 16px;
  line-height: 1.6;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-actions {{
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 14px;
  margin-top: 18px;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action {{
  min-height: 142px;
  display: grid;
  grid-template-columns: 48px minmax(0, 1fr);
  grid-template-rows: auto 1fr;
  column-gap: 12px;
  row-gap: 6px;
  padding: 18px;
  text-align: left;
  color: {theme.Ink};
  background: {elevated2};
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.34 : 0.22)};
  border-radius: 8px;
  box-shadow: 0 10px 26px {ColorUtil.Alpha(theme.Ink, dark ? 0.18 : 0.08)};
  transition: transform 140ms ease, border-color 140ms ease, box-shadow 140ms ease;
  min-width: 0;
  overflow: hidden;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action:hover {{
  transform: translateY(-2px);
  border-color: {ColorUtil.Alpha(theme.Accent, 0.72)};
  box-shadow: 0 14px 32px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.12)};
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action-icon {{
  grid-row: 1 / span 2;
  width: 46px;
  height: 46px;
  display: grid;
  place-items: center;
  color: {buttonText};
  background: {theme.Accent};
  border-radius: 50%;
  font-family: var(--font-mono-default);
  font-size: 17px;
  font-weight: 800;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action-title {{
  align-self: end;
  font-size: 15px;
  line-height: 1.35;
  font-weight: 700;
  min-width: 0;
  overflow-wrap: anywhere;
}}

html[data-codex-window-type=""electron""] .codex-theme-home-action-description {{
  color: {secondaryText};
  font-size: 12px;
  line-height: 1.5;
  min-width: 0;
  overflow-wrap: anywhere;
}}

@media (max-width: 1120px) {{
  html[data-codex-window-type=""electron""] #codex-theme-home {{ padding-inline: 22px; }}
  html[data-codex-window-type=""electron""] .codex-theme-home-hero {{ min-height: 280px; padding: 30px; }}
  html[data-codex-window-type=""electron""] .codex-theme-home-title {{ font-size: 28px; }}
}}

@media (max-width: 720px) {{
  html[data-codex-window-type=""electron""] .codex-theme-home-copy {{ width: 72%; }}
}}

html[data-codex-window-type=""electron""] .composer-surface-chrome {{
  background: {ColorUtil.Alpha(elevated2, dark ? 0.90 : 0.88)} !important;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.68 : 0.52)} !important;
  border-radius: 20px !important;
  backdrop-filter: blur(8px) saturate(1.06) !important;
  box-shadow: 0 0 0 1px {ColorUtil.Alpha(theme.Accent, dark ? 0.16 : 0.10)}, 0 12px 34px {ColorUtil.Alpha(theme.Ink, dark ? 0.32 : 0.16)} !important;
}}

html[data-codex-window-type=""electron""] .app-header-tint {{
  background: {ColorUtil.Alpha(under, dark ? 0.94 : 0.92)} !important;
  border-bottom: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.30 : 0.18)} !important;
  backdrop-filter: blur(8px) saturate(1.04) !important;
  box-shadow: 0 4px 18px {ColorUtil.Alpha(theme.Ink, dark ? 0.20 : 0.08)} !important;
}}

html[data-codex-window-type=""electron""] [data-content-search-unit-key$="":assistant""],
html[data-codex-window-type=""electron""] [data-message-author-role=""assistant""] {{
  margin-block: 4px 10px !important;
  padding: 14px 16px !important;
  background: {ColorUtil.Alpha(theme.Surface, dark ? 0.76 : 0.72)} !important;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.30 : 0.18)} !important;
  border-radius: 16px !important;
  backdrop-filter: none !important;
  box-shadow: 0 10px 28px {ColorUtil.Alpha(theme.Ink, dark ? 0.24 : 0.11)} !important;
}}

html[data-codex-window-type=""electron""] [data-user-message-bubble=""true""] {{
  background: {ColorUtil.Alpha(softAccent, dark ? 0.88 : 0.84)} !important;
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.42 : 0.30)} !important;
  box-shadow: 0 8px 24px {ColorUtil.Alpha(theme.Ink, dark ? 0.20 : 0.10)} !important;
  backdrop-filter: none !important;
}}

html[data-codex-window-type=""electron""] .app-shell-left-panel .absolute.bottom-0.z-20[class*=""inset-x-0""] {{
  background: linear-gradient(to bottom, {ColorUtil.Alpha(under, 0.18)}, {ColorUtil.Alpha(under, dark ? 0.96 : 0.92)} 38%) !important;
  border-top: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.28 : 0.18)} !important;
  backdrop-filter: none !important;
}}

html[data-codex-window-type=""electron""] [aria-label=""打开设置""] {{
  border: 1px solid {ColorUtil.Alpha(theme.Accent, dark ? 0.24 : 0.16)} !important;
  background: {ColorUtil.Alpha(softAccent, dark ? 0.38 : 0.48)} !important;
}}

html[data-codex-window-type=""electron""] button,
html[data-codex-window-type=""electron""] input,
html[data-codex-window-type=""electron""] textarea {{
  font-family: var(--vscode-font-family);
}}

html[data-codex-window-type=""electron""] ::selection {{
  background: {ColorUtil.Alpha(theme.Accent, dark ? 0.42 : 0.28)};
}}
";
        return baseCss + BuildLayoutCss(theme, dark, under, elevated2, softAccent, secondaryText, tertiaryText, buttonText);
    }

    private static string BuildLayoutCss(
        ThemeDefinition theme,
        bool dark,
        string under,
        string elevated2,
        string softAccent,
        string secondaryText,
        string tertiaryText,
        string buttonText)
    {
        var homeSurface = ColorUtil.Mix(theme.Surface, dark ? "#000000" : "#ffffff", dark ? 0.04 : 0.16);
        var cardSurface = ColorUtil.Alpha(elevated2, dark ? 0.92 : 0.94);
        var heroText = theme.CodeThemeId == "dilraba-star" ? "#ffffff" : theme.Ink;
        var heroSecondary = theme.CodeThemeId == "dilraba-star" ? "rgb(255 255 255 / 0.84)" : secondaryText;
        var heroShadow = theme.CodeThemeId is "kun-stage" or "dilraba-star"
            ? "0 2px 18px rgb(0 0 0 / 0.48)"
            : "0 2px 14px rgb(255 255 255 / 0.72)";
        var actionOne = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.DiffRemoved, dark ? 0.18 : 0.09), dark ? 0.94 : 0.96);
        var actionTwo = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.DiffAdded, dark ? 0.16 : 0.08), dark ? 0.94 : 0.96);
        var actionThree = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.Accent, dark ? 0.19 : 0.08), dark ? 0.94 : 0.96);
        var actionFour = ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, theme.Skill, dark ? 0.16 : 0.07), dark ? 0.94 : 0.96);
        var cardRadius = theme.CodeThemeId == "enfp-pop" ? 26 : theme.CodeThemeId == "kun-stage" ? 22 : 24;
        var heroRadius = theme.CodeThemeId == "enfp-pop" ? 28 : 30;

        return $$"""

/* High-fidelity theme shell, injected over the live Codex surfaces. */
html[data-codex-window-type="electron"] {
  --codex-theme-card-radius: {{cardRadius}}px;
  --codex-theme-hero-radius: {{heroRadius}}px;
}

/* Keep Codex's native layout controls above the theme home surface. */
html[data-codex-window-type="electron"] :where(button, [role="button"]):is(
  [aria-label*="sidebar" i],
  [aria-label*="side bar" i],
  [aria-label*="侧边栏"],
  [aria-label*="bottom panel" i],
  [aria-label*="toggle panel" i],
  [aria-label*="show panel" i],
  [aria-label*="hide panel" i],
  [aria-label*="底部面板"],
  [aria-label*="切换面板"],
  [aria-label*="显示面板"],
  [aria-label*="隐藏面板"],
  [title*="sidebar" i],
  [title*="side bar" i],
  [title*="侧边栏"],
  [title*="bottom panel" i],
  [title*="toggle panel" i],
  [title*="底部面板"],
  [data-testid*="sidebar-toggle" i],
  [data-testid*="panel-toggle" i]
) {
  display: inline-flex !important;
  visibility: visible !important;
  opacity: 1 !important;
  pointer-events: auto !important;
  color: {{theme.Ink}} !important;
  z-index: 12 !important;
}

html[data-codex-window-type="electron"] :where(button, [role="button"]):is(
  [aria-label*="sidebar" i],
  [aria-label*="side bar" i],
  [aria-label*="侧边栏"],
  [aria-label*="bottom panel" i],
  [aria-label*="底部面板"]
) :where(svg, img) {
  visibility: visible !important;
  opacity: 1 !important;
  color: currentColor !important;
}

html[data-codex-window-type="electron"] .codex-theme-masthead {
  display: flex;
  align-items: center;
  justify-content: space-between;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon {
  flex: 0 0 auto;
  display: grid;
  place-items: center;
  color: {{theme.Accent}};
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon[hidden] {
  display: none !important;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon svg,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon img {
  width: 100%;
  height: 100%;
  object-fit: contain;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon svg {
  stroke: currentColor;
}

html[data-codex-window-type="electron"] #codex-theme-home {
  inset: 44px 0 150px;
  padding: 16px 28px 30px;
  color: {{theme.Ink}};
  background: {{homeSurface}};
  font-family: {{theme.UiFont}};
  scrollbar-width: thin;
  scrollbar-color: {{ColorUtil.Alpha(theme.Accent, 0.46)}} transparent;
}

html[data-codex-window-type="electron"] .codex-theme-home-shell {
  width: min(1440px, 100%);
}

html[data-codex-window-type="electron"] .codex-theme-masthead {
  min-height: 68px;
  gap: 18px;
  padding: 4px 10px 14px;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-logo {
  width: auto;
  max-width: min(154px, 24%);
  height: 38px;
  display: block;
  object-fit: contain;
  object-position: left center;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-copy {
  min-width: 0;
  flex: 1;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-title {
  color: {{theme.Ink}};
  font-family: {{theme.DisplayFont}};
  font-size: 18px;
  line-height: 1.25;
  font-weight: 850;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-note {
  margin-top: 4px;
  color: {{secondaryText}};
  font-size: 12px;
}

html[data-codex-window-type="electron"] .codex-theme-masthead-badge {
  max-width: 38%;
  padding: 8px 13px;
  overflow: hidden;
  color: {{theme.Accent}};
  background: {{ColorUtil.Alpha(softAccent, dark ? 0.48 : 0.74)}};
  border: 1px dashed {{ColorUtil.Alpha(theme.Accent, 0.64)}};
  border-radius: 999px;
  font-size: 12px;
  font-weight: 800;
  text-overflow: ellipsis;
  white-space: nowrap;
}

html[data-codex-window-type="electron"] .codex-theme-home-hero {
  position: relative;
  min-height: 390px;
  height: clamp(390px, 48vh, 520px);
  display: block;
  padding: 0;
  overflow: hidden;
  isolation: isolate;
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.66 : 0.40)}};
  border-radius: var(--codex-theme-hero-radius);
  background-color: {{theme.Surface}};
  background-image: var(--codex-theme-background-image);
  background-size: cover;
  background-position: center 30%;
  background-repeat: no-repeat;
  box-shadow: 0 24px 58px {{ColorUtil.Alpha(theme.Ink, dark ? 0.34 : 0.15)}};
}

html[data-codex-window-type="electron"] .codex-theme-home-copy {
  position: absolute;
  inset: 46% auto auto 38px;
  z-index: 2;
  width: min(43%, 560px);
  transform: translateY(-14%);
  color: {{heroText}};
  text-shadow: {{heroShadow}};
}

html[data-codex-window-type="electron"] .codex-theme-pet {
  position: absolute;
  right: clamp(16px, 3vw, 42px);
  bottom: 18px;
  z-index: 2;
  width: min(var(--codex-theme-pet-size, 128px), 28%);
  height: min(var(--codex-theme-pet-size, 128px), 42%);
  pointer-events: none;
  user-select: none;
  filter: drop-shadow(0 12px 18px {{ColorUtil.Alpha(theme.Ink, dark ? 0.38 : 0.22)}});
}

html[data-codex-window-type="electron"] .codex-theme-pet-image {
  width: 100%;
  height: 100%;
  display: block;
  object-fit: contain;
  object-position: right bottom;
}

html[data-codex-window-type="electron"] .codex-theme-home-brand {
  display: none;
}

html[data-codex-window-type="electron"] .codex-theme-home-eyebrow {
  display: inline-flex;
  margin: 0 0 12px;
  padding: 6px 10px;
  color: {{heroText}};
  background: {{ColorUtil.Alpha(theme.Surface, dark ? 0.62 : 0.72)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.58)}};
  border-radius: 999px;
  backdrop-filter: blur(12px);
  font-size: 11px;
  letter-spacing: 0.08em;
}

html[data-codex-window-type="electron"] .codex-theme-home-title {
  margin: 0 0 10px;
  color: inherit;
  font-family: {{theme.DisplayFont}};
  font-size: clamp(28px, 2.8vw, 44px);
  line-height: 1.16;
  font-weight: 900;
  letter-spacing: -0.035em;
}

html[data-codex-window-type="electron"] .codex-theme-home-subtitle {
  max-width: 520px;
  color: {{heroSecondary}};
  font-size: clamp(14px, 1.15vw, 18px);
  line-height: 1.55;
  font-weight: 650;
}

html[data-codex-window-type="electron"] .codex-theme-home-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 7px;
  margin-top: 16px;
}

html[data-codex-window-type="electron"] .codex-theme-home-tag {
  padding: 5px 9px;
  color: {{heroText}};
  background: {{ColorUtil.Alpha(theme.Surface, dark ? 0.62 : 0.72)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.42)}};
  border-radius: 999px;
  backdrop-filter: blur(10px);
  font-size: 11px;
  font-weight: 750;
}

html[data-codex-window-type="electron"] .codex-theme-home-actions {
  grid-template-columns: repeat(4, minmax(0, 1fr));
  position: relative;
  z-index: 3;
  gap: 14px;
  margin: -104px 28px 0;
  transform: translateY(-36px);
}

html[data-codex-window-type="electron"] .codex-theme-home-action {
  min-height: 148px;
  display: grid;
  grid-template-columns: 1fr;
  grid-template-rows: 42px auto 1fr;
  justify-items: center;
  gap: 8px;
  padding: 12px 11px 11px;
  text-align: center;
  color: {{theme.Ink}};
  background: {{cardSurface}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, dark ? 0.54 : 0.34)}};
  border-radius: var(--codex-theme-card-radius);
  box-shadow: 0 16px 38px {{ColorUtil.Alpha(theme.Ink, dark ? 0.28 : 0.14)}};
  backdrop-filter: none;
}

html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(1) { background: {{actionOne}}; }
html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(2) { background: {{actionTwo}}; }
html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(3) { background: {{actionThree}}; }
html[data-codex-window-type="electron"] .codex-theme-home-action:nth-child(4) { background: {{actionFour}}; }

html[data-codex-window-type="electron"] .codex-theme-home-action:hover {
  transform: translateY(-5px);
  border-color: {{theme.Accent}};
  box-shadow: 0 22px 46px {{ColorUtil.Alpha(theme.Ink, dark ? 0.36 : 0.19)}};
}

html[data-codex-window-type="electron"] .codex-theme-home-action:focus-visible {
  outline: 3px solid {{ColorUtil.Alpha(theme.Accent, 0.44)}};
  outline-offset: 3px;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon {
  grid-row: auto;
  width: 40px;
  height: 40px;
  color: {{theme.Accent}};
  background: {{ColorUtil.Alpha(theme.Surface, dark ? 0.66 : 0.82)}};
  border: 1px solid {{ColorUtil.Alpha(theme.Accent, 0.44)}};
  border-radius: 50%;
  font-size: 0;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-icon svg,
html[data-codex-window-type="electron"] .codex-theme-home-action-icon img {
  width: 20px;
  height: 20px;
}

html[data-codex-theme-id="kun-stage"] .codex-theme-home-action-icon img {
  filter: invert(81%) sepia(25%) saturate(697%) hue-rotate(358deg) brightness(90%);
}

html[data-codex-theme-id="jackson-sage"] .codex-theme-home-action-icon img {
  filter: invert(57%) sepia(17%) saturate(825%) hue-rotate(39deg) brightness(93%);
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-action-icon img {
  filter: invert(59%) sepia(85%) saturate(626%) hue-rotate(121deg) brightness(88%);
}

html[data-codex-theme-id="dilraba-star"] .codex-theme-home-action-icon img {
  filter: invert(36%) sepia(93%) saturate(2494%) hue-rotate(247deg) brightness(94%);
}

html[data-codex-window-type="electron"] .codex-theme-home-action-title {
  align-self: auto;
  font-size: 15px;
  line-height: 1.35;
  font-weight: 850;
}

html[data-codex-window-type="electron"] .codex-theme-home-action-description {
  color: {{secondaryText}};
  font-size: 12px;
  line-height: 1.4;
}

html[data-codex-window-type="electron"] .composer-surface-chrome {
  min-height: 146px;
  padding: 12px !important;
  background: {{ColorUtil.Alpha(elevated2, dark ? 0.94 : 0.93)}} !important;
  border-color: {{ColorUtil.Alpha(theme.Accent, dark ? 0.76 : 0.58)}} !important;
  border-radius: 24px !important;
  box-shadow: 0 0 0 1px {{ColorUtil.Alpha(theme.Accent, 0.12)}}, 0 18px 42px {{ColorUtil.Alpha(theme.Ink, dark ? 0.34 : 0.18)}} !important;
}

html[data-codex-window-type="electron"] .composer-surface-chrome:focus-within {
  box-shadow: 0 0 0 3px {{ColorUtil.Alpha(theme.Accent, 0.24)}}, 0 22px 48px {{ColorUtil.Alpha(theme.Ink, dark ? 0.38 : 0.20)}} !important;
}

html[data-codex-window-type="electron"] .composer-surface-chrome .ProseMirror {
  min-height: 64px;
  color: {{theme.Ink}} !important;
  caret-color: {{theme.Accent}} !important;
  font-size: 16.5px;
  line-height: 1.6;
}

html[data-codex-window-type="electron"] [role="dialog"],
html[data-codex-window-type="electron"] [data-radix-popper-content-wrapper] > * {
  color: {{theme.Ink}} !important;
  background: {{ColorUtil.Alpha(elevated2, dark ? 0.97 : 0.96)}} !important;
  border-color: {{ColorUtil.Alpha(theme.Accent, dark ? 0.50 : 0.34)}} !important;
  border-radius: 18px !important;
  box-shadow: 0 24px 68px {{ColorUtil.Alpha(theme.Ink, dark ? 0.42 : 0.23)}} !important;
  backdrop-filter: blur(10px) saturate(1.06) !important;
}

html[data-codex-window-type="electron"] [data-content-search-unit-key$=":assistant"],
html[data-codex-window-type="electron"] [data-message-author-role="assistant"] {
  border-radius: 20px !important;
}

html[data-codex-window-type="electron"] [data-user-message-bubble="true"],
html[data-codex-window-type="electron"] [data-message-author-role="user"] {
  border-radius: 20px 20px 6px 20px !important;
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-hero {
  height: clamp(330px, 42vh, 450px);
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-actions {
  margin: 16px 18px 0;
}

html[data-codex-theme-id="enfp-pop"] .codex-theme-home-action {
  min-height: 148px;
  border-radius: 26px;
}

html[data-codex-theme-id="dilraba-star"] .codex-theme-home-copy {
  inset-block-start: 45%;
}

html[data-codex-window-type="electron"][data-codex-theme-id="kun-stage"] .codex-theme-home-action {
  background: {{ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, "#ffffff", 0.06), 0.94)}} !important;
}

html[data-codex-window-type="electron"][data-codex-theme-id="jackson-sage"] .codex-theme-home-action {
  background: {{ColorUtil.Alpha(elevated2, 0.96)}} !important;
}

html[data-codex-window-type="electron"][data-codex-theme-id="dilraba-star"] .codex-theme-home-action {
  background: {{ColorUtil.Alpha(ColorUtil.Mix(theme.Surface, "#ffffff", 0.36), 0.92)}} !important;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-compact .codex-theme-home-actions {
  gap: 12px;
  margin: -56px 18px 0;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-compact .codex-theme-home-action {
  min-height: 142px;
  padding-inline: 10px;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-compact .codex-theme-home-hero {
  min-height: 300px;
  height: clamp(300px, 38vh, 390px);
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-actions {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin-top: 14px;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-copy {
  width: 60%;
}

@media (max-width: 1280px) {
  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    gap: 12px;
    margin: -56px 18px 0;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-action {
    min-height: 142px;
    padding-inline: 10px;
  }
}

@media (max-width: 1120px) {
  html[data-codex-window-type="electron"] #codex-theme-home {
    padding-inline: 18px;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-copy {
    width: 48%;
    inset-inline-start: 26px;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    margin: -36px 16px 0;
  }
}

@media (max-width: 900px) {
  html[data-codex-window-type="electron"] .codex-theme-home-copy {
    width: 60%;
  }
}

@media (max-width: 680px) {
  html[data-codex-window-type="electron"] #codex-theme-home {
    inset-block-end: 138px;
  }

  html[data-codex-window-type="electron"] .codex-theme-masthead-badge,
  html[data-codex-window-type="electron"] .codex-theme-home-tags {
    display: none;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-hero {
    min-height: 330px;
    height: 42vh;
    background-position: 62% center;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-copy {
    inset-inline: 20px;
    width: auto;
  }

  html[data-codex-window-type="electron"] .codex-theme-pet {
    right: 12px;
    bottom: 10px;
    max-width: 92px;
    max-height: 92px;
    opacity: 0.82;
  }

  html[data-codex-window-type="electron"] .codex-theme-home-actions {
    grid-template-columns: 1fr;
  }
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-actions {
  grid-template-columns: repeat(2, minmax(0, 1fr));
  margin: 14px 8px 0;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-action {
  min-height: 132px;
}

html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-hero {
  min-height: 300px;
  height: clamp(300px, 38vh, 340px);
}

@media (max-width: 520px) {
  html[data-codex-window-type="electron"] #codex-theme-home.codex-theme-home-narrow .codex-theme-home-actions {
    grid-template-columns: 1fr;
  }
}

@media (prefers-reduced-motion: reduce) {
  html[data-codex-window-type="electron"] .codex-theme-home-action {
    transition: none !important;
  }
}
""";
    }

    private static string QuoteFont(string font) => font.Contains(',') || font.Contains('"') || font.Contains('\'') ? font : $"\"{font.Replace("\"", "\\\"")}\"";
    private static string CssEscape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace(")", "\\)", StringComparison.Ordinal);
    private static string CssNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

public static class JsBuilder
{
    public static string Build(JsonNode? copy, JsonNode? home, string themeId, string? logoUrl, string? petUrl = null) => BuildV2(copy, home, themeId, logoUrl, petUrl);

    private static string BuildV2(JsonNode? copy, JsonNode? home, string themeId, string? logoUrl, string? petUrl)
    {
        var configNode = new JsonObject
        {
            ["copy"] = copy?.DeepClone() ?? new JsonObject(),
            ["home"] = home?.DeepClone() ?? new JsonObject(),
            ["themeId"] = themeId,
            ["logoUrl"] = logoUrl,
            ["petUrl"] = petUrl,
        };
        var payload = configNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return $$"""
(() => {
  const config = {{payload}};
  const prior = globalThis.__codexThemeStore;
  const originalTitle = prior?.originalTitle || document.title;
  if (prior?.dispose) prior.dispose();
  else {
    prior?.observer?.disconnect();
    if (prior?.resizeHandler) window.removeEventListener("resize", prior.resizeHandler);
    if (prior?.resizeTimer) clearTimeout(prior.resizeTimer);
    prior?.main?.classList.remove("codex-theme-home-active");
    document.getElementById("codex-theme-home")?.remove();
  }

  const copyConfig = config.copy || {};
  const homeConfig = config.home || {};
  const configuredSidebarLabels = homeConfig.sidebarLabels || {};
  const sidebarLabels = {
    newTask: configuredSidebarLabels.newTask,
    scheduled: configuredSidebarLabels.scheduled,
    plugins: configuredSidebarLabels.plugins,
    settings: configuredSidebarLabels.settings,
  };
  const previousSidebarLabels = prior?.sidebarLabels || {};
  const themeId = config.themeId || "theme";
  const logoUrl = config.logoUrl || "";
  const petUrl = config.petUrl || "";
  const originals = prior?.originals || { text: new WeakMap(), attributes: new WeakMap() };
  const sidebarChanges = [];
  document.documentElement.dataset.codexThemeId = themeId;

  function normalizeText(value) {
    return String(value || "").replace(/\s+/g, " ").trim();
  }

  function replaceTextValue(value, replacements) {
    if (!value || !replacements) return value;
    const trimmed = value.trim();
    if (!trimmed) return value;
    const replacement = replacements[trimmed];
    return replacement ? value.replace(trimmed, replacement) : value;
  }

  function applyTextReplacement(node, replacements) {
    const current = node.nodeValue || "";
    let state = originals.text.get(node);
    if (!state || current !== state.applied) {
      state = { original: current, applied: current };
    }
    const next = replaceTextValue(state.original, replacements);
    state.applied = next;
    originals.text.set(node, state);
    if (current !== next) node.nodeValue = next;
  }

  function applyAttributeReplacement(node, attribute, replacements) {
    const current = node.getAttribute(attribute);
    if (current === null) return;
    let attributes = originals.attributes.get(node);
    if (!attributes) {
      attributes = new Map();
      originals.attributes.set(node, attributes);
    }
    let state = attributes.get(attribute);
    if (!state || current !== state.applied) {
      state = { original: current, applied: current };
    }
    const next = replaceTextValue(state.original, replacements);
    state.applied = next;
    attributes.set(attribute, state);
    if (current !== next) node.setAttribute(attribute, next);
  }

  function element(tag, className, text) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (text !== undefined && text !== null && text !== "") node.textContent = text;
    return node;
  }

  function isThemeNode(node) {
    return !!node?.closest?.("#codex-theme-home");
  }

  function nativeControls() {
    return Array.from(document.querySelectorAll("button, a, [role='button']"))
      .filter(node => !isThemeNode(node));
  }

  function findNativeControl(aliases) {
    const wanted = (aliases || []).map(normalizeText).filter(Boolean);
    if (!wanted.length) return null;
    for (const control of nativeControls()) {
      const haystacks = [
        normalizeText(control.textContent),
        normalizeText(control.getAttribute("aria-label")),
        normalizeText(control.getAttribute("title")),
      ].filter(Boolean);
      if (haystacks.some(haystack => wanted.some(alias => haystack === alias || haystack.includes(alias)))) {
        return control;
      }
    }
    return null;
  }

  function findNativeSidebar() {
    const shell = document.querySelector("aside.app-shell-left-panel, .app-shell-left-panel");
    if (shell) return shell;

    const labeled = Array.from(document.querySelectorAll("nav[aria-label]"))
      .find(node => {
        const label = normalizeText(node.getAttribute("aria-label"));
        return label === "\u5df2\u5b89\u6392\u4efb\u52a1\u6587\u4ef6\u5939" || label === "Scheduled tasks folder";
      });
    if (labeled) return labeled;

    return Array.from(document.querySelectorAll("nav"))
      .find(node => {
        if (isThemeNode(node)) return false;
        const rect = node.getBoundingClientRect();
        if (rect.left > 24 || rect.width < 180 || rect.width > 420 || rect.height < window.innerHeight * 0.45) return false;
        const text = normalizeText(node.textContent);
        return ["\u65b0\u5efa\u4efb\u52a1", "New Task", "\u65b0\u5efa\u70b9\u5b50", "\u9879\u76ee", "Projects"]
          .some(alias => text.includes(alias));
      }) || null;
  }

  function sidebarAliases(base, configured, previous) {
    return Array.from(new Set([...(base || []), configured, previous].map(normalizeText).filter(Boolean)));
  }

  function setSidebarLeafText(scope, aliases, value, predicate) {
    if (!scope || !value) return false;
    const wanted = sidebarAliases(aliases);
    const candidates = Array.from(scope.querySelectorAll("span, div"));
    const target = candidates.find(node => {
      if (node.childElementCount > 0 || !wanted.includes(normalizeText(node.textContent))) return false;
      return !predicate || predicate(node);
    });
    if (!target || target.textContent === value) return !!target;
    let change = sidebarChanges.find(item => item.node === target);
    if (!change) {
      change = { node: target, original: target.textContent || "", applied: target.textContent || "" };
      sidebarChanges.push(change);
    } else if (target.textContent !== change.applied) {
      change.original = target.textContent || "";
    }
    target.textContent = value;
    change.applied = value;
    return true;
  }

  function applyNativeSidebarLabels() {
    const sidebar = findNativeSidebar();
    if (!sidebar) return null;

    const definitions = {
      newTask: sidebarAliases(
        ["\u65b0\u5efa\u4efb\u52a1", "New Task", "New task", "\u65b0\u5efa\u70b9\u5b50", "\u65b0\u5efa\u7075\u611f", "\u65b0\u5efa\u624b\u8d26", "\u65b0\u5efa\u821e\u53f0", "New Spark", "Star Task", "New Note", "New Stage"],
        sidebarLabels.newTask,
        previousSidebarLabels.newTask
      ),
      scheduled: sidebarAliases(
        ["\u5df2\u5b89\u6392", "Scheduled", "\u7075\u611f\u6392\u671f"],
        sidebarLabels.scheduled,
        previousSidebarLabels.scheduled
      ),
      plugins: sidebarAliases(
        ["\u63d2\u4ef6", "\u6280\u80fd", "Skills", "Plugins", "\u6280\u80fd\u6e38\u4e50\u573a", "\u70ed\u5df4\u6280\u80fd", "\u5343\u73ba\u6280\u80fd", "\u821e\u53f0\u6280\u80fd", "Playground", "Star Skills", "Studio", "Setlist"],
        sidebarLabels.plugins,
        previousSidebarLabels.plugins
      ),
      settings: sidebarAliases(["\u8bbe\u7f6e", "Settings"], sidebarLabels.settings, previousSidebarLabels.settings),
    };

    setSidebarLeafText(sidebar, definitions.newTask, sidebarLabels.newTask);
    setSidebarLeafText(sidebar, definitions.scheduled, sidebarLabels.scheduled);
    setSidebarLeafText(sidebar, definitions.plugins, sidebarLabels.plugins);

    const settingsControl = Array.from(sidebar.querySelectorAll("button, a, [role='button']"))
      .find(node => {
        const aria = normalizeText(node.getAttribute("aria-label"));
        return aria === "\u6253\u5f00\u8bbe\u7f6e" || aria === "Open settings" || definitions.settings.includes(normalizeText(node.textContent));
      });
    setSidebarLeafText(settingsControl, definitions.settings, sidebarLabels.settings);

    sidebar.dataset.codexThemeSidebarLabels = themeId;
    return sidebar;
  }

  function sanitizeClone(node) {
    const clone = node.cloneNode(true);
    if (clone.removeAttribute) clone.removeAttribute("id");
    for (const child of clone.querySelectorAll?.("[id]") || []) child.removeAttribute("id");
    clone.setAttribute?.("aria-hidden", "true");
    clone.removeAttribute?.("aria-label");
    clone.removeAttribute?.("title");
    return clone;
  }

  function cloneNativeIcon(aliases, fallbackIndex) {
    const control = findNativeControl(aliases);
    const preferred = control?.querySelector("svg, img");
    if (preferred) return sanitizeClone(preferred);

    const pool = Array.from(document.querySelectorAll("button svg, a svg, [role='button'] svg, button img, a img"))
      .filter(node => !isThemeNode(node));
    if (!pool.length) return null;
    return sanitizeClone(pool[Math.abs(fallbackIndex || 0) % pool.length]);
  }

  function setIconHint(slot, aliases, index) {
    slot.dataset.iconAliases = (aliases || []).join("|");
    slot.dataset.iconIndex = String(index || 0);
    hydrateIcon(slot);
  }

  function hydrateIcon(slot) {
    if (!slot || slot.childElementCount) return;
    const aliases = (slot.dataset.iconAliases || "").split("|").filter(Boolean);
    const icon = cloneNativeIcon(aliases, Number(slot.dataset.iconIndex || 0));
    if (icon) {
      slot.hidden = false;
      slot.appendChild(icon);
    } else {
      slot.hidden = true;
    }
  }

  function hydrateThemeIcons() {
    for (const slot of document.querySelectorAll("#codex-theme-home [data-icon-index]")) {
      hydrateIcon(slot);
    }
  }

  function fillComposer(prompt) {
    const editor = document.querySelector(".ProseMirror");
    if (!editor) return;
    editor.focus();
    const selection = window.getSelection();
    const range = document.createRange();
    range.selectNodeContents(editor);
    selection?.removeAllRanges();
    selection?.addRange(range);
    if (!document.execCommand("insertText", false, prompt)) {
      editor.textContent = prompt;
      editor.dispatchEvent(new InputEvent("input", { bubbles: true, inputType: "insertText", data: prompt }));
    }
    editor.dispatchEvent(new Event("change", { bubbles: true }));
  }

  function applyThemeCopy() {
    document.documentElement.dataset.codexThemeId = themeId;
    if (copyConfig.title) document.title = copyConfig.title;

    const placeholderMap = copyConfig.replacePlaceholders || {};
    if (Object.keys(placeholderMap).length) {
      for (const input of document.querySelectorAll("input[placeholder], textarea[placeholder]")) {
        applyAttributeReplacement(input, "placeholder", placeholderMap);
      }
    }

    const editor = document.querySelector(".ProseMirror");
    if (editor && homeConfig.composerHint) {
      editor.setAttribute("aria-label", homeConfig.composerHint);
      editor.dataset.placeholder = homeConfig.composerHint;
    }
  }

  function createThemeHome() {
    const section = element("section", "codex-theme-home");
    section.id = "codex-theme-home";
    section.dataset.themeId = themeId;

    const shell = element("div", "codex-theme-home-shell");
    const masthead = element("header", "codex-theme-masthead");
    const mastheadCopy = element("div", "codex-theme-masthead-copy");
    mastheadCopy.append(
      element("div", "codex-theme-masthead-title", homeConfig.eyebrow || homeConfig.brand || "Codex"),
      element("div", "codex-theme-masthead-note", homeConfig.footerNote || homeConfig.subtitle || "")
    );
    masthead.append(
      mastheadCopy,
      element("div", "codex-theme-masthead-badge", homeConfig.badge || homeConfig.brand || themeId)
    );
    if (logoUrl) {
      const logo = element("img", "codex-theme-masthead-logo");
      logo.src = logoUrl;
      logo.alt = homeConfig.brand || "Codex";
      logo.decoding = "async";
      logo.draggable = false;
      masthead.prepend(logo);
    }

    const hero = element("div", "codex-theme-home-hero");
    const heroCopy = element("div", "codex-theme-home-copy");
    heroCopy.append(
      element("div", "codex-theme-home-brand", homeConfig.brand || "Codex"),
      element("div", "codex-theme-home-eyebrow", homeConfig.eyebrow || ""),
      element("h1", "codex-theme-home-title", homeConfig.title || "我们该构建什么？"),
      element("p", "codex-theme-home-subtitle", homeConfig.subtitle || "")
    );
    const tags = element("div", "codex-theme-home-tags");
    for (const tag of homeConfig.tags || []) tags.appendChild(element("span", "codex-theme-home-tag", tag));
    heroCopy.appendChild(tags);
    hero.appendChild(heroCopy);
    if (petUrl && homeConfig.pet) {
      const pet = element("div", "codex-theme-pet");
      const size = Math.max(48, Math.min(220, Number(homeConfig.pet.size) || 128));
      pet.style.setProperty("--codex-theme-pet-size", `${size}px`);
      const image = element("img", "codex-theme-pet-image");
      image.src = petUrl;
      image.alt = homeConfig.pet.alt || "";
      image.decoding = "async";
      image.draggable = false;
      pet.appendChild(image);
      hero.appendChild(pet);
    }

    const actions = element("div", "codex-theme-home-actions");
    for (const [index, action] of (homeConfig.quickActions || []).entries()) {
      const button = element("button", "codex-theme-home-action");
      button.type = "button";
      button.setAttribute("aria-label", action.title || "快速操作");
      const icon = element("span", "codex-theme-home-action-icon");
      setIconHint(icon, [action.title || "", action.description || ""], index + 6);
      button.append(
        icon,
        element("span", "codex-theme-home-action-title", action.title || "快速操作"),
        element("span", "codex-theme-home-action-description", action.description || "")
      );
      button.addEventListener("click", () => fillComposer(action.prompt || action.title || ""));
      actions.appendChild(button);
    }

    shell.append(masthead, hero, actions);
    section.appendChild(shell);
    return section;
  }

  function applyThemeHome() {
    const main = document.querySelector(".main-surface, .browser-main-surface");
    if (!main) return null;
    let home = document.getElementById("codex-theme-home");
    if (home?.dataset.themeId !== themeId) {
      home?.remove();
      home = null;
    }
    if (!home) home = createThemeHome();
    if (home.parentElement !== main) main.appendChild(home);

    const composer = document.querySelector(".composer-surface-chrome");
    const hasComposer = !!composer?.querySelector(".ProseMirror");
    const hasMessages = !!document.querySelector('[data-content-search-unit-key$=":user"], [data-content-search-unit-key$=":assistant"], [data-message-author-role]');
    const mainRect = main.getBoundingClientRect();
    home.classList.toggle("codex-theme-home-compact", mainRect.width <= 1180);
    home.classList.toggle("codex-theme-home-narrow", mainRect.width <= 760);
    if (hasComposer) {
      const composerRect = composer.getBoundingClientRect();
      const reserve = Math.min(280, Math.max(138, Math.ceil(mainRect.bottom - composerRect.top + 18)));
      home.style.bottom = `${reserve}px`;
    } else {
      home.style.removeProperty("bottom");
    }
    main.classList.toggle("codex-theme-home-active", hasComposer && !hasMessages);
    globalThis.__codexThemeStore.main = main;
    return home;
  }

  let queued = false;
  let frameId = 0;
  let disposed = false;
  let observer = null;
  let resizeHandler = null;
  let resizeTimer = 0;
  let applyCount = 0;
  const messageSelector = '[data-content-search-unit-key$=":user"], [data-content-search-unit-key$=":assistant"], [data-message-author-role]';
  const structureSelector = [
    ".main-surface",
    ".browser-main-surface",
    ".composer-surface-chrome",
    ".ProseMirror",
    ".app-shell-left-panel",
    "aside.app-shell-left-panel",
    "nav[aria-label]",
    "input[placeholder]",
    "textarea[placeholder]",
    messageSelector,
  ].join(",");

  function nodeContainsStructure(node) {
    if (node?.nodeType !== Node.ELEMENT_NODE || isThemeNode(node)) return false;
    return node.matches(structureSelector) || !!node.querySelector(structureSelector);
  }

  function mutationNeedsApply(mutation) {
    const target = mutation.target?.nodeType === Node.ELEMENT_NODE
      ? mutation.target
      : mutation.target?.parentElement;
    if (!target || isThemeNode(target)) return false;
    if (mutation.type === "attributes") return target.matches(structureSelector);
    if (target.closest(messageSelector) || target.closest(".ProseMirror")) return false;
    if (target.closest(".app-shell-left-panel, aside.app-shell-left-panel")) return true;
    return [...mutation.addedNodes, ...mutation.removedNodes].some(nodeContainsStructure);
  }

  function queueApply() {
    if (queued || disposed) return;
    queued = true;
    frameId = requestAnimationFrame(() => {
      frameId = 0;
      queued = false;
      if (disposed || globalThis.__codexThemeStore?.themeId !== themeId) return;
      applyCount += 1;
      applyThemeCopy();
      applyNativeSidebarLabels();
      applyThemeHome();
      hydrateThemeIcons();
    });
  }

  function dispose() {
    if (disposed) return;
    disposed = true;
    observer?.disconnect();
    if (frameId) cancelAnimationFrame(frameId);
    if (resizeTimer) clearTimeout(resizeTimer);
    if (resizeHandler) window.removeEventListener("resize", resizeHandler);
    globalThis.__codexThemeStore?.main?.classList.remove("codex-theme-home-active");
    document.getElementById("codex-theme-home")?.remove();
    document.documentElement.removeAttribute("data-codex-theme-id");
    for (const change of sidebarChanges) {
      if (change.node?.isConnected && change.node.textContent === change.applied) {
        change.node.textContent = change.original;
      }
    }
    if (copyConfig.title && document.title === copyConfig.title) document.title = originalTitle;
  }

  globalThis.__codexThemeStore = {
    observer: null,
    themeId,
    applyThemeCopy,
    applyNativeSidebarLabels,
    applyThemeHome,
    originals,
    sidebarLabels,
    sidebarChanges,
    originalTitle,
    dispose,
    main: null,
    resizeHandler: null,
    resizeTimer: 0,
    getApplyCount: () => applyCount,
  };

  applyThemeCopy();
  applyNativeSidebarLabels();
  applyThemeHome();
  hydrateThemeIcons();

  observer = new MutationObserver(mutations => {
    if (mutations.some(mutationNeedsApply)) queueApply();
  });
  observer.observe(document.documentElement, {
    subtree: true,
    childList: true,
    attributes: true,
    attributeFilter: ["placeholder", "data-content-search-unit-key", "data-message-author-role"],
  });
  globalThis.__codexThemeStore.observer = observer;
  resizeHandler = () => {
    if (resizeTimer) clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => {
      resizeTimer = 0;
      if (!disposed && globalThis.__codexThemeStore?.themeId === themeId) applyThemeHome();
      if (globalThis.__codexThemeStore) globalThis.__codexThemeStore.resizeTimer = 0;
    }, 120);
    globalThis.__codexThemeStore.resizeTimer = resizeTimer;
  };
  window.addEventListener("resize", resizeHandler, { passive: true });
  globalThis.__codexThemeStore.resizeHandler = resizeHandler;
})();
""";
    }
}

internal static class ColorUtil
{
    public static string Mix(string a, string b, double weight)
    {
        var ca = Parse(a);
        var cb = Parse(b);
        return ToHex(
            ca.R * (1 - weight) + cb.R * weight,
            ca.G * (1 - weight) + cb.G * weight,
            ca.B * (1 - weight) + cb.B * weight);
    }

    public static string Alpha(string hex, double opacity)
    {
        var c = Parse(hex);
        return string.Create(CultureInfo.InvariantCulture, $"rgb({c.R} {c.G} {c.B} / {Math.Clamp(opacity, 0, 1):0.000})");
    }

    public static double Luminance(string hex)
    {
        var c = Parse(hex);
        double Linear(int value)
        {
            var x = value / 255.0;
            return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linear(c.R) + 0.7152 * Linear(c.G) + 0.0722 * Linear(c.B);
    }

    public static string ReadableText(string hex) => Luminance(hex) > 0.55 ? "#141413" : "#ffffff";

    private static (int R, int G, int B) Parse(string hex)
    {
        if (!Regex.IsMatch(hex, "^#[0-9a-fA-F]{6}$")) throw new InvalidOperationException($"Invalid color: {hex}");
        return (
            Convert.ToInt32(hex.Substring(1, 2), 16),
            Convert.ToInt32(hex.Substring(3, 2), 16),
            Convert.ToInt32(hex.Substring(5, 2), 16));
    }

    private static string ToHex(double r, double g, double b)
    {
        static int Clamp(double value) => Math.Clamp((int)Math.Round(value), 0, 255);
        return $"#{Clamp(r):x2}{Clamp(g):x2}{Clamp(b):x2}";
    }
}
