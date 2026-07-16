using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using NSec.Cryptography;

internal sealed record DreamSkinImportResult(
    string Id,
    string Version,
    string DisplayName,
    string ManifestPath,
    string PackageSha256);

internal sealed record DreamSkinExpectedPackage(
    string Sha256,
    long Size,
    string? Id,
    string? Version);

internal static class DreamSkinPackageInstaller
{
    private const long MaxPackageBytes = 20L * 1024 * 1024;
    private const long MaxManifestBytes = 64L * 1024;
    private const long MaxBackgroundBytes = 16L * 1024 * 1024;
    private const long MaxPreviewBytes = 2L * 1024 * 1024;
    private const long MaxExpandedBytes = 20L * 1024 * 1024;
    private const string EngineVersion = "1.0.0";

    private static readonly Regex ThemeIdPattern = new(
        "^[a-z0-9]+(?:[.-][a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex SemVerPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*))?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HexColorPattern = new(
        "^#[0-9A-Fa-f]{6}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RgbaColorPattern = new(
        "^rgba\\((25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9]),[ ]*(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9]),[ ]*(25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9]),[ ]*(0|1|0?\\.[0-9]{1,3}|1\\.0{1,3})\\)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Public development fixture key. The corresponding private key is only
    // used by tools/dreamskin/build-sample.mjs and must never sign production packages.
    private static readonly IReadOnlyDictionary<string, byte[]> TrustedKeys =
        new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["codex-skin.sample.2026-01"] = DecodeBase64Url("kuf25VngYoeAC2TDJ2kPGRfKGJZvQhZrdVnQhGvQ3fM"),
        };

    private static readonly HashSet<string> RootProperties = new(StringComparer.Ordinal)
    {
        "$schema", "schemaVersion", "packageVersion", "id", "name", "version",
        "description", "author", "engineVersion", "platforms", "brandSubtitle",
        "tagline", "projectPrefix", "projectLabel", "statusText", "quote", "image",
        "colors", "assets", "signature",
    };

    private static readonly HashSet<string> RootRequired = new(StringComparer.Ordinal)
    {
        "schemaVersion", "packageVersion", "id", "name", "version", "description",
        "author", "engineVersion", "platforms", "image", "colors", "assets", "signature",
    };

    private static readonly HashSet<string> ColorProperties = new(StringComparer.Ordinal)
    {
        "background", "panel", "panelAlt", "accent", "accentAlt",
        "secondary", "highlight", "text", "muted", "line",
    };

    public static DreamSkinImportResult ImportLocal(string packagePath, DreamSkinExpectedPackage? expected = null)
    {
        var fullPackagePath = Path.GetFullPath(packagePath.Trim().Trim('"'));
        if (!File.Exists(fullPackagePath))
        {
            throw Error("DSI_DOWNLOAD_FAILED", "找不到 .dreamskin 文件。");
        }
        if (!Path.GetExtension(fullPackagePath).Equals(".dreamskin", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("DSI_PACKAGE_INVALID", "本地导入只接受 .dreamskin 文件。");
        }

        var packageInfo = new FileInfo(fullPackagePath);
        if (packageInfo.Length is < 1 or > MaxPackageBytes)
        {
            throw Error("DSI_SIZE_LIMIT", "主题包大小必须在 1 到 20 MiB 之间。");
        }

        var packageSha256 = HashFile(fullPackagePath);
        if (expected is not null)
        {
            if (packageInfo.Length != expected.Size)
            {
                throw Error("DSI_SIZE_MISMATCH", "下载文件大小与深链接声明不一致。");
            }
            if (!packageSha256.Equals(expected.Sha256, StringComparison.Ordinal))
            {
                throw Error("DSI_HASH_MISMATCH", "下载文件 SHA-256 与深链接声明不一致。");
            }
        }
        using var archive = OpenPackage(fullPackagePath);
        ValidateEntryList(archive);

        var manifestEntry = archive.GetEntry("theme.json")
            ?? throw Error("DSI_PACKAGE_INVALID", "主题包缺少 theme.json。");
        var manifestBytes = ReadEntry(manifestEntry, MaxManifestBytes);
        if (manifestBytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
        {
            throw Error("DSI_MANIFEST_INVALID", "theme.json 必须是无 BOM 的 UTF-8。");
        }

        string manifestText;
        try
        {
            manifestText = new UTF8Encoding(false, true).GetString(manifestBytes);
        }
        catch (DecoderFallbackException)
        {
            throw Error("DSI_MANIFEST_INVALID", "theme.json 不是严格 UTF-8。");
        }

        using var document = ParseManifest(manifestText);
        var manifest = ValidateManifest(document.RootElement);
        if (expected?.Id is not null && !manifest.Id.Equals(expected.Id, StringComparison.Ordinal))
        {
            throw Error("DSI_MANIFEST_INVALID", "下载包主题 ID 与深链接提示不一致。");
        }
        if (expected?.Version is not null && !manifest.Version.Equals(expected.Version, StringComparison.Ordinal))
        {
            throw Error("DSI_MANIFEST_INVALID", "下载包版本与深链接提示不一致。");
        }
        VerifySignature(document.RootElement, manifest.Signature);

        var expectedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "theme.json", manifest.Background.Path, manifest.Preview.Path,
        };
        if (!archive.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal).SetEquals(expectedNames))
        {
            throw Error("DSI_PACKAGE_INVALID", "ZIP 文件名与清单资源声明不一致。");
        }

        var libraryRoot = GetLibraryRoot();
        Directory.CreateDirectory(libraryRoot);
        var stagingRoot = Path.Combine(libraryRoot, $".staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingRoot);

        try
        {
            ExtractVerifiedEntry(archive, manifest.Background, stagingRoot, MaxBackgroundBytes, 40_000_000);
            ExtractVerifiedEntry(archive, manifest.Preview, stagingRoot, MaxPreviewBytes, 8_000_000);
            File.WriteAllBytes(Path.Combine(stagingRoot, "theme.json"), manifestBytes);

            var expandedBytes = manifestBytes.LongLength + manifest.Background.Bytes + manifest.Preview.Bytes;
            if (expandedBytes > MaxExpandedBytes)
            {
                throw Error("DSI_SIZE_LIMIT", "主题包解压后超过 20 MiB。");
            }

            var target = GetInstallPath(libraryRoot, manifest.Id, manifest.Version);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target))
            {
                if (!InstalledPackageMatches(target, manifest, manifestBytes))
                {
                    throw Error("DSI_THEME_ID_CONFLICT", "相同主题 ID 和版本已存在不同内容。");
                }

                return new DreamSkinImportResult(
                    manifest.Id,
                    manifest.Version,
                    manifest.Name,
                    Path.Combine(target, "theme.json"),
                    packageSha256);
            }

            try
            {
                Directory.Move(stagingRoot, target);
            }
            catch (IOException) when (Directory.Exists(target))
            {
                if (!InstalledPackageMatches(target, manifest, manifestBytes))
                {
                    throw Error("DSI_THEME_ID_CONFLICT", "并发安装产生了不同内容的同版本主题。");
                }
            }

            return new DreamSkinImportResult(
                manifest.Id,
                manifest.Version,
                manifest.Name,
                Path.Combine(target, "theme.json"),
                packageSha256);
        }
        finally
        {
            if (Directory.Exists(stagingRoot)) Directory.Delete(stagingRoot, true);
        }
    }

    private static JsonDocument ParseManifest(string manifestText)
    {
        try
        {
            var document = JsonDocument.Parse(manifestText, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32,
            });
            RejectDuplicateProperties(document.RootElement);
            return document;
        }
        catch (JsonException ex)
        {
            throw Error("DSI_MANIFEST_INVALID", $"theme.json 解析失败：{ex.Message}");
        }
    }

    private static DreamSkinManifest ValidateManifest(JsonElement root)
    {
        RequireObject(root, "theme.json");
        RequireClosedObject(root, RootProperties, RootRequired, "theme.json");

        if (RequiredInt(root, "schemaVersion") != 1 || RequiredInt(root, "packageVersion") != 1)
        {
            throw Error("DSI_MANIFEST_INVALID", "只支持 schemaVersion=1 和 packageVersion=1。");
        }
        if (root.TryGetProperty("$schema", out var schemaValue)
            && RequireString(schemaValue, "$schema") != "https://raw.githubusercontent.com/lixiaobaivv/Codex-Skin-Store/main/spec/theme-package.schema.json")
        {
            throw Error("DSI_MANIFEST_INVALID", "$schema 不是受支持的规范地址。");
        }

        var id = RequiredText(root, "id", 128);
        if (id.Length < 3 || !ThemeIdPattern.IsMatch(id))
        {
            throw Error("DSI_MANIFEST_INVALID", "主题 ID 格式无效。");
        }

        var name = RequiredText(root, "name", 80);
        _ = RequiredText(root, "description", 500);
        var version = RequiredSemVer(root, "version");

        var author = RequiredProperty(root, "author");
        RequireClosedObject(author, new HashSet<string>(StringComparer.Ordinal) { "name", "homepage" }, new HashSet<string>(StringComparer.Ordinal) { "name" }, "author");
        _ = RequiredText(author, "name", 80);
        if (author.TryGetProperty("homepage", out var homepage))
        {
            var value = RequireString(homepage, "author.homepage");
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                throw Error("DSI_MANIFEST_INVALID", "author.homepage 必须是 HTTPS URL。");
            }
        }

        var engine = RequiredProperty(root, "engineVersion");
        RequireClosedObject(engine, new HashSet<string>(StringComparer.Ordinal) { "min", "maxExclusive" }, new HashSet<string>(StringComparer.Ordinal) { "min", "maxExclusive" }, "engineVersion");
        var engineMin = RequiredSemVer(engine, "min");
        var engineMax = RequiredSemVer(engine, "maxExclusive");
        if (CompareSemVer(EngineVersion, engineMin) < 0 || CompareSemVer(EngineVersion, engineMax) >= 0)
        {
            throw Error("DSI_UNSUPPORTED_ENGINE", $"主题需要引擎版本 [{engineMin}, {engineMax})，当前为 {EngineVersion}。");
        }

        var platforms = RequiredProperty(root, "platforms");
        if (platforms.ValueKind != JsonValueKind.Array || platforms.GetArrayLength() is < 1 or > 2)
        {
            throw Error("DSI_MANIFEST_INVALID", "platforms 必须包含 1 到 2 个平台。");
        }
        var platformNames = platforms.EnumerateArray().Select(value => RequireString(value, "platforms[]")).ToList();
        if (platformNames.Distinct(StringComparer.Ordinal).Count() != platformNames.Count || platformNames.Any(value => value is not ("windows" or "macos")))
        {
            throw Error("DSI_MANIFEST_INVALID", "platforms 包含重复或未知平台。");
        }
        if (!platformNames.Contains("windows", StringComparer.Ordinal))
        {
            throw Error("DSI_UNSUPPORTED_PLATFORM", "该主题未声明支持 Windows。");
        }

        foreach (var optionalText in new[] { "brandSubtitle", "projectPrefix", "projectLabel", "statusText", "quote" })
        {
            if (root.TryGetProperty(optionalText, out var value)) ValidateText(RequireString(value, optionalText), 80, optionalText);
        }
        if (root.TryGetProperty("tagline", out var tagline)) ValidateText(RequireString(tagline, "tagline"), 160, "tagline");

        var colors = RequiredProperty(root, "colors");
        RequireClosedObject(colors, ColorProperties, ColorProperties, "colors");
        foreach (var colorName in ColorProperties)
        {
            var color = RequiredText(colors, colorName, 32);
            if (!HexColorPattern.IsMatch(color) && !RgbaColorPattern.IsMatch(color))
            {
                throw Error("DSI_MANIFEST_INVALID", $"colors.{colorName} 不是允许的颜色格式。");
            }
        }

        var assets = RequiredProperty(root, "assets");
        RequireClosedObject(assets, new HashSet<string>(StringComparer.Ordinal) { "background", "preview" }, new HashSet<string>(StringComparer.Ordinal) { "background", "preview" }, "assets");
        var background = ValidateAsset(RequiredProperty(assets, "background"), "background", MaxBackgroundBytes, 8192, 8192);
        var preview = ValidateAsset(RequiredProperty(assets, "preview"), "preview", MaxPreviewBytes, 2400, 2400);

        var image = RequiredText(root, "image", 32);
        if (!image.Equals(background.Path, StringComparison.Ordinal))
        {
            throw Error("DSI_MANIFEST_INVALID", "image 必须等于 assets.background.path。");
        }

        var signatureElement = RequiredProperty(root, "signature");
        RequireClosedObject(
            signatureElement,
            new HashSet<string>(StringComparer.Ordinal) { "algorithm", "canonicalization", "keyId", "signedAt", "value" },
            new HashSet<string>(StringComparer.Ordinal) { "algorithm", "canonicalization", "keyId", "signedAt", "value" },
            "signature");
        if (RequiredText(signatureElement, "algorithm", 16) != "Ed25519" || RequiredText(signatureElement, "canonicalization", 16) != "RFC8785")
        {
            throw Error("DSI_MANIFEST_INVALID", "签名算法必须是 Ed25519/RFC8785。");
        }
        var keyId = RequiredText(signatureElement, "keyId", 64);
        if (!Regex.IsMatch(keyId, "^[A-Za-z0-9._-]+$", RegexOptions.CultureInvariant))
        {
            throw Error("DSI_MANIFEST_INVALID", "signature.keyId 格式无效。");
        }
        var signedAt = RequiredText(signatureElement, "signedAt", 64);
        if (!DateTimeOffset.TryParse(signedAt, out _))
        {
            throw Error("DSI_MANIFEST_INVALID", "signature.signedAt 不是有效时间。");
        }
        var signatureValue = RequiredText(signatureElement, "value", 86);
        byte[] signatureBytes;
        try
        {
            signatureBytes = DecodeBase64Url(signatureValue);
        }
        catch (FormatException)
        {
            throw Error("DSI_SIGNATURE_INVALID", "签名不是有效的无填充 base64url。");
        }
        if (signatureValue.Length != 86 || signatureBytes.Length != 64)
        {
            throw Error("DSI_SIGNATURE_INVALID", "Ed25519 签名必须是 64 字节。");
        }

        return new DreamSkinManifest(id, name, version, background, preview, new DreamSkinSignature(keyId, signatureBytes));
    }

    private static DreamSkinAsset ValidateAsset(JsonElement element, string role, long maxBytes, int maxWidth, int maxHeight)
    {
        var properties = new HashSet<string>(StringComparer.Ordinal) { "path", "mediaType", "bytes", "width", "height", "sha256" };
        RequireClosedObject(element, properties, properties, $"assets.{role}");
        var path = RequiredText(element, "path", 32);
        var mediaType = RequiredText(element, "mediaType", 32);
        var bytes = RequiredInt64(element, "bytes");
        var width = RequiredInt(element, "width");
        var height = RequiredInt(element, "height");
        var sha256 = RequiredText(element, "sha256", 64);

        var allowed = role == "background"
            ? new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["image/png"] = ["background.png"],
                ["image/jpeg"] = ["background.jpg", "background.jpeg"],
                ["image/webp"] = ["background.webp"],
            }
            : new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["image/png"] = ["preview.png"],
                ["image/jpeg"] = ["preview.jpg", "preview.jpeg"],
                ["image/webp"] = ["preview.webp"],
            };

        if (!allowed.TryGetValue(mediaType, out var paths) || !paths.Contains(path, StringComparer.Ordinal))
        {
            throw Error("DSI_MANIFEST_INVALID", $"assets.{role} 的路径与媒体类型不匹配。");
        }
        if (bytes is < 1 || bytes > maxBytes || width is < 1 || width > maxWidth || height is < 1 || height > maxHeight)
        {
            throw Error("DSI_SIZE_LIMIT", $"assets.{role} 超出大小或尺寸限制。");
        }
        if (!Regex.IsMatch(sha256, "^[a-f0-9]{64}$", RegexOptions.CultureInvariant))
        {
            throw Error("DSI_MANIFEST_INVALID", $"assets.{role}.sha256 格式无效。");
        }
        return new DreamSkinAsset(path, mediaType, bytes, width, height, sha256);
    }

    private static void VerifySignature(JsonElement root, DreamSkinSignature signature)
    {
        if (!TrustedKeys.TryGetValue(signature.KeyId, out var publicKeyBytes))
        {
            throw Error("DSI_SIGNATURE_UNTRUSTED", $"签名密钥不受信任：{signature.KeyId}");
        }

        var canonical = CanonicalizeForSignature(root);
        try
        {
            var algorithm = SignatureAlgorithm.Ed25519;
            var publicKey = PublicKey.Import(algorithm, publicKeyBytes, KeyBlobFormat.RawPublicKey);
            if (!algorithm.Verify(publicKey, canonical, signature.Value))
            {
                throw Error("DSI_SIGNATURE_INVALID", "Ed25519 签名验证失败。");
            }
        }
        catch (CryptographicException)
        {
            throw Error("DSI_SIGNATURE_INVALID", "Ed25519 公钥或签名无效。");
        }
    }

    private static byte[] CanonicalizeForSignature(JsonElement root)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            Indented = false,
        }))
        {
            WriteCanonical(writer, root, isSignatureObject: false);
        }
        return stream.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element, bool isSignatureObject)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    if (isSignatureObject && property.NameEquals("value")) continue;
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value, property.NameEquals("signature"));
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray()) WriteCanonical(writer, item, false);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var integer))
                {
                    throw Error("DSI_MANIFEST_INVALID", "协议 v1 清单只允许整数数值字段。");
                }
                writer.WriteNumberValue(integer);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw Error("DSI_MANIFEST_INVALID", "清单包含无法规范化的 JSON 值。");
        }
    }

    private static void ValidateEntryList(ZipArchive archive)
    {
        if (archive.Entries.Count != 3)
        {
            throw Error("DSI_PACKAGE_INVALID", "主题包必须恰好包含三个根目录文件。");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;
            var unixMode = (entry.ExternalAttributes >> 16) & 0xF000;
            if (string.IsNullOrWhiteSpace(name) || name != entry.Name || name.Contains('/') || name.Contains('\\') || name.Contains(':') || name.Contains('\0'))
            {
                throw Error("DSI_ZIP_TRAVERSAL", "ZIP 只能包含根目录普通文件。");
            }
            if (unixMode == 0xA000 || entry.Name.EndsWith('/'))
            {
                throw Error("DSI_ZIP_TRAVERSAL", "主题包不允许链接或目录条目。");
            }
            if (!names.Add(name))
            {
                throw Error("DSI_PACKAGE_INVALID", "ZIP 包含大小写冲突或重复文件名。");
            }
        }
    }

    private static void ExtractVerifiedEntry(ZipArchive archive, DreamSkinAsset asset, string targetRoot, long limit, long maxPixels)
    {
        var entry = archive.GetEntry(asset.Path) ?? throw Error("DSI_PACKAGE_INVALID", $"缺少资源 {asset.Path}。");
        var destination = Path.Combine(targetRoot, asset.Path);
        try
        {
            using var input = entry.Open();
            using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            CopyWithLimit(input, output, limit);
        }
        catch (InvalidDataException)
        {
            throw Error("DSI_PACKAGE_INVALID", $"资源 {asset.Path} 的 ZIP 数据损坏。");
        }

        var info = new FileInfo(destination);
        if (info.Length != asset.Bytes || !HashFile(destination).Equals(asset.Sha256, StringComparison.Ordinal))
        {
            throw Error("DSI_HASH_MISMATCH", $"资源 {asset.Path} 的大小或 SHA-256 不匹配。");
        }

        ValidateImage(destination, asset, maxPixels);
    }

    private static void ValidateImage(string path, DreamSkinAsset asset, long maxPixels)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        Span<byte> header = stackalloc byte[12];
        if (input.Read(header) < header.Length) throw Error("DSI_ASSET_INVALID", $"图片 {asset.Path} 已截断。");
        input.Position = 0;

        var png = header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var jpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var webp = header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8);
        if ((asset.MediaType == "image/png" && !png) || (asset.MediaType == "image/jpeg" && !jpeg) || (asset.MediaType == "image/webp" && !webp))
        {
            throw Error("DSI_ASSET_INVALID", $"图片 {asset.Path} 的文件魔数与声明类型不符。");
        }

        if (webp)
        {
            throw Error("DSI_ASSET_INVALID", "Windows MVP 导入器暂未启用 WebP 完整解码；请使用 PNG 或 JPEG。");
        }

        ValidateStaticImageContainer(path, asset, png, jpeg);

        try
        {
            using var image = System.Drawing.Image.FromStream(input, useEmbeddedColorManagement: false, validateImageData: true);
            var width = image.Width;
            var height = image.Height;
            if (width != asset.Width || height != asset.Height || (long)width * height > maxPixels)
            {
                throw Error("DSI_ASSET_INVALID", $"图片 {asset.Path} 的像素尺寸与清单不一致或超过限制。");
            }
        }
        catch (ArgumentException)
        {
            throw Error("DSI_ASSET_INVALID", $"图片 {asset.Path} 无法完整解码。");
        }
    }

    private static void ValidateStaticImageContainer(string path, DreamSkinAsset asset, bool png, bool jpeg)
    {
        var bytes = File.ReadAllBytes(path);
        if (jpeg)
        {
            if (bytes.Length < 4 || bytes[^2] != 0xFF || bytes[^1] != 0xD9)
            {
                throw Error("DSI_ASSET_INVALID", $"JPEG {asset.Path} 缺少结束标记或包含尾随载荷。");
            }
            return;
        }
        if (!png) return;

        var offset = 8;
        var sawHeader = false;
        var sawEnd = false;
        while (offset + 12 <= bytes.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(offset, 4));
            if (length > int.MaxValue || offset + 12L + length > bytes.Length)
            {
                throw Error("DSI_ASSET_INVALID", $"PNG {asset.Path} 包含截断或超长区块。");
            }
            var type = Encoding.ASCII.GetString(bytes, offset + 4, 4);
            if (!sawHeader && type != "IHDR") throw Error("DSI_ASSET_INVALID", $"PNG {asset.Path} 的首个区块不是 IHDR。");
            if (type == "IHDR")
            {
                if (sawHeader || length != 13) throw Error("DSI_ASSET_INVALID", $"PNG {asset.Path} 的 IHDR 无效。");
                sawHeader = true;
            }
            if (type == "acTL") throw Error("DSI_ASSET_INVALID", $"PNG {asset.Path} 是不允许的动画 PNG。");
            offset += checked((int)length + 12);
            if (type == "IEND")
            {
                if (length != 0 || offset != bytes.Length) throw Error("DSI_ASSET_INVALID", $"PNG {asset.Path} 的 IEND 或尾随数据无效。");
                sawEnd = true;
                break;
            }
        }
        if (!sawHeader || !sawEnd) throw Error("DSI_ASSET_INVALID", $"PNG {asset.Path} 缺少必要区块。");
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry, long limit)
    {
        try
        {
            using var input = entry.Open();
            using var output = new MemoryStream();
            CopyWithLimit(input, output, limit);
            return output.ToArray();
        }
        catch (InvalidDataException)
        {
            throw Error("DSI_PACKAGE_INVALID", $"ZIP 条目 {entry.FullName} 已损坏。");
        }
    }

    private static ZipArchive OpenPackage(string path)
    {
        try
        {
            return ZipFile.OpenRead(path);
        }
        catch (InvalidDataException)
        {
            throw Error("DSI_PACKAGE_INVALID", "文件不是有效的 ZIP 主题包。");
        }
    }

    private static void CopyWithLimit(Stream input, Stream output, long limit)
    {
        var buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            var read = input.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            total += read;
            if (total > limit) throw Error("DSI_SIZE_LIMIT", "ZIP 条目解压后超过允许大小。");
            output.Write(buffer, 0, read);
        }
    }

    private static bool InstalledPackageMatches(string target, DreamSkinManifest manifest, byte[] manifestBytes)
    {
        var manifestPath = Path.Combine(target, "theme.json");
        var backgroundPath = Path.Combine(target, manifest.Background.Path);
        var previewPath = Path.Combine(target, manifest.Preview.Path);
        return File.Exists(manifestPath)
            && File.ReadAllBytes(manifestPath).AsSpan().SequenceEqual(manifestBytes)
            && File.Exists(backgroundPath)
            && HashFile(backgroundPath).Equals(manifest.Background.Sha256, StringComparison.Ordinal)
            && File.Exists(previewPath)
            && HashFile(previewPath).Equals(manifest.Preview.Sha256, StringComparison.Ordinal);
    }

    private static string GetLibraryRoot()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_THEME_LIBRARY_DIR");
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStore",
            "themes",
            "packages");
    }

    private static string GetInstallPath(string root, string id, string version)
    {
        var target = Path.GetFullPath(Path.Combine(root, id, version));
        var rootWithSeparator = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw Error("DSI_ZIP_TRAVERSAL", "主题安装路径越界。");
        }
        return target;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw Error("DSI_MANIFEST_INVALID", $"JSON 包含重复键：{property.Name}");
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
        }
    }

    private static void RequireClosedObject(JsonElement element, HashSet<string> allowed, HashSet<string> required, string path)
    {
        RequireObject(element, path);
        var names = element.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var unknown = names.Except(allowed, StringComparer.Ordinal).FirstOrDefault();
        if (unknown is not null) throw Error("DSI_MANIFEST_INVALID", $"{path} 包含未知字段：{unknown}");
        var missing = required.Except(names, StringComparer.Ordinal).FirstOrDefault();
        if (missing is not null) throw Error("DSI_MANIFEST_INVALID", $"{path} 缺少字段：{missing}");
    }

    private static void RequireObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Error("DSI_MANIFEST_INVALID", $"{path} 必须是对象。");
    }

    private static JsonElement RequiredProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) throw Error("DSI_MANIFEST_INVALID", $"缺少字段：{name}");
        return value;
    }

    private static string RequiredText(JsonElement element, string name, int maxLength)
    {
        var value = RequireString(RequiredProperty(element, name), name);
        ValidateText(value, maxLength, name);
        return value;
    }

    private static string RequireString(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.String) throw Error("DSI_MANIFEST_INVALID", $"{path} 必须是字符串。");
        return element.GetString()!;
    }

    private static void ValidateText(string value, int maxLength, string path)
    {
        if (string.IsNullOrEmpty(value) || value.Length > maxLength || !value.IsNormalized(NormalizationForm.FormC) || value.Any(IsUnsafeTextCharacter))
        {
            throw Error("DSI_MANIFEST_INVALID", $"{path} 包含无效、未规范化或超长文本。");
        }
    }

    private static bool IsUnsafeTextCharacter(char value)
    {
        return value is <= '\u001F' or >= '\u007F' and <= '\u009F'
            || value is '\u202A' or '\u202B' or '\u202D' or '\u202E' or '\u202C' or '\u2066' or '\u2067' or '\u2068' or '\u2069';
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        if (!RequiredProperty(element, name).TryGetInt32(out var value)) throw Error("DSI_MANIFEST_INVALID", $"{name} 必须是整数。");
        return value;
    }

    private static long RequiredInt64(JsonElement element, string name)
    {
        if (!RequiredProperty(element, name).TryGetInt64(out var value)) throw Error("DSI_MANIFEST_INVALID", $"{name} 必须是整数。");
        return value;
    }

    private static string RequiredSemVer(JsonElement element, string name)
    {
        var value = RequiredText(element, name, 64);
        if (!SemVerPattern.IsMatch(value)) throw Error("DSI_MANIFEST_INVALID", $"{name} 不是有效 SemVer。");
        return value;
    }

    private static int CompareSemVer(string left, string right)
    {
        var leftMatch = SemVerPattern.Match(left);
        var rightMatch = SemVerPattern.Match(right);
        for (var index = 1; index <= 3; index++)
        {
            var comparison = long.Parse(leftMatch.Groups[index].Value).CompareTo(long.Parse(rightMatch.Groups[index].Value));
            if (comparison != 0) return comparison;
        }
        var leftPre = leftMatch.Groups[4].Value;
        var rightPre = rightMatch.Groups[4].Value;
        if (leftPre.Length == 0 && rightPre.Length > 0) return 1;
        if (rightPre.Length == 0 && leftPre.Length > 0) return -1;
        return string.CompareOrdinal(leftPre, rightPre);
    }

    private static string HashFile(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
    }

    private static byte[] DecodeBase64Url(string value)
    {
        if (value.Contains('=')) throw new FormatException("Padding is not allowed.");
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 0 => "", 2 => "==", 3 => "=", _ => throw new FormatException("Invalid base64url length.") };
        return Convert.FromBase64String(padded);
    }

    private static InvalidOperationException Error(string code, string message) => new($"{code}: {message}");

    private sealed record DreamSkinManifest(
        string Id,
        string Name,
        string Version,
        DreamSkinAsset Background,
        DreamSkinAsset Preview,
        DreamSkinSignature Signature);

    private sealed record DreamSkinAsset(
        string Path,
        string MediaType,
        long Bytes,
        int Width,
        int Height,
        string Sha256);

    private sealed record DreamSkinSignature(string KeyId, byte[] Value);
}
