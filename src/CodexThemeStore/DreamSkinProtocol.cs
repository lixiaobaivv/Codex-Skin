using System.Net;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record DreamSkinInstallRequest(
    Uri PackageUri,
    string Sha256,
    long Size,
    string? Id,
    string? Version);

internal static class DreamSkinProtocol
{
    public const int MaxUriBytes = 4096;
    public const long MaxPackageBytes = 20L * 1024 * 1024;

    private static readonly Regex HashPattern = new("^[a-f0-9]{64}$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex ThemeIdPattern = new("^[a-z0-9]+(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SemVerPattern = new(
        "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?(?:\\+[0-9A-Za-z-]+(?:\\.[0-9A-Za-z-]+)*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly HashSet<string> AllowedParameters = new(StringComparer.Ordinal)
    {
        "url", "sha256", "size", "id", "version",
    };

    public static DreamSkinInstallRequest Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || Encoding.UTF8.GetByteCount(input) > MaxUriBytes)
        {
            throw Error("DSI_URI_INVALID", "dreamskin URI 为空或超过 4096 字节。");
        }
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals("dreamskin", StringComparison.OrdinalIgnoreCase)
            || !uri.Host.Equals("install", StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0
            || (uri.AbsolutePath.Length != 0 && uri.AbsolutePath != "/"))
        {
            throw Error("DSI_URI_INVALID", "只接受 dreamskin://install 深链接。");
        }

        var queryIndex = input.IndexOf('?');
        if (queryIndex < 0 || queryIndex == input.Length - 1)
        {
            throw Error("DSI_URI_INVALID", "深链接缺少查询参数。");
        }
        var rawQuery = input[(queryIndex + 1)..];
        var hashIndex = rawQuery.IndexOf('#');
        if (hashIndex >= 0) throw Error("DSI_URI_INVALID", "深链接不得包含 fragment。");

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in rawQuery.Split('&', StringSplitOptions.None))
        {
            if (pair.Length == 0) throw Error("DSI_URI_INVALID", "深链接包含空查询参数。");
            var equalsIndex = pair.IndexOf('=');
            if (equalsIndex <= 0 || pair.IndexOf('=', equalsIndex + 1) >= 0)
            {
                throw Error("DSI_URI_INVALID", "查询参数必须恰好包含一个等号。");
            }

            var rawName = pair[..equalsIndex];
            var rawValue = pair[(equalsIndex + 1)..];
            var name = DecodePercentOnce(rawName);
            if (name == "url" && (rawValue.Contains(':') || rawValue.Contains('/')))
            {
                throw Error("DSI_URI_INVALID", "url 参数必须完整进行百分号编码。");
            }
            var value = DecodePercentOnce(rawValue);
            if (!AllowedParameters.Contains(name)) throw Error("DSI_URI_INVALID", $"未知查询参数：{name}");
            if (!values.TryAdd(name, value)) throw Error("DSI_URI_INVALID", $"重复查询参数：{name}");
        }

        foreach (var required in new[] { "url", "sha256", "size" })
        {
            if (!values.TryGetValue(required, out var value) || value.Length == 0)
            {
                throw Error("DSI_URI_INVALID", $"缺少查询参数：{required}");
            }
        }

        var packageUri = ValidatePackageUri(values["url"]);
        var sha256 = values["sha256"];
        if (!HashPattern.IsMatch(sha256)) throw Error("DSI_URI_INVALID", "sha256 必须是 64 位小写十六进制。");
        if (!long.TryParse(values["size"], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var size)
            || size is < 1 or > MaxPackageBytes)
        {
            throw Error("DSI_URI_INVALID", "size 必须是 1 到 20971520 的十进制整数。");
        }

        var id = values.GetValueOrDefault("id");
        if (id is not null && (id.Length is < 3 or > 128 || !ThemeIdPattern.IsMatch(id)))
        {
            throw Error("DSI_URI_INVALID", "id 提示格式无效。");
        }
        var version = values.GetValueOrDefault("version");
        if (version is not null && (version.Length > 64 || !SemVerPattern.IsMatch(version)))
        {
            throw Error("DSI_URI_INVALID", "version 提示不是有效 SemVer。");
        }

        return new DreamSkinInstallRequest(packageUri, sha256, size, id, version);
    }

    public static void RunSelfTest()
    {
        const string packageUrl = "https://github.com/lixiaobaivv/Codex-Skin/releases/download/sample-v1/codex-skin-sample-1.0.0.dreamskin";
        const string hash = "7a75fff8086fe6949ef9e37e82c161a8e015a1e00e02181938cd479e9ae41387";
        var valid = $"dreamskin://install?url={Uri.EscapeDataString(packageUrl)}&sha256={hash}&size=2041227&id=codex-skin.jackson-sage-sample&version=1.0.0";
        var parsed = Parse(valid);
        if (parsed.PackageUri.AbsoluteUri != packageUrl || parsed.Size != 2041227 || parsed.Id != "codex-skin.jackson-sage-sample")
        {
            throw new InvalidOperationException("DSI_SELF_TEST_FAILED: 有效深链接解析结果不一致。");
        }

        var invalid = new[]
        {
            valid + "&sha256=" + hash,
            valid + "&unknown=1",
            valid.Replace("dreamskin://install", "dreamskin://preview", StringComparison.Ordinal),
            valid.Replace("sha256=" + hash, "sha256=" + hash.ToUpperInvariant(), StringComparison.Ordinal),
            valid.Replace(Uri.EscapeDataString(packageUrl), packageUrl, StringComparison.Ordinal),
            valid.Replace(Uri.EscapeDataString(packageUrl), Uri.EscapeDataString("http://example.com/theme.dreamskin"), StringComparison.Ordinal),
            valid.Replace(Uri.EscapeDataString(packageUrl), Uri.EscapeDataString("https://127.0.0.1/theme.dreamskin"), StringComparison.Ordinal),
            valid.Replace(Uri.EscapeDataString(packageUrl), Uri.EscapeDataString("https://example.com:8443/theme.dreamskin"), StringComparison.Ordinal),
            valid + "#fragment",
        };
        foreach (var candidate in invalid)
        {
            try
            {
                _ = Parse(candidate);
                throw new InvalidOperationException("DSI_SELF_TEST_FAILED: 非法深链接被错误接受。");
            }
            catch (InvalidOperationException ex) when (ex.Message.StartsWith("DSI_", StringComparison.Ordinal)
                && !ex.Message.StartsWith("DSI_SELF_TEST_FAILED", StringComparison.Ordinal))
            {
                // Expected rejection.
            }
        }

        foreach (var blocked in new[] { "10.0.0.1", "169.254.1.1", "172.16.0.1", "192.168.1.1", "::1", "fc00::1", "fe80::1" })
        {
            if (IsPublicAddress(IPAddress.Parse(blocked)))
            {
                throw new InvalidOperationException($"DSI_SELF_TEST_FAILED: 保留地址被错误识别为公网：{blocked}");
            }
        }
    }

    public static Uri ValidatePackageUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.UserInfo.Length != 0
            || uri.Fragment.Length != 0
            || (!uri.IsDefaultPort && uri.Port != 443)
            || string.IsNullOrWhiteSpace(uri.Host))
        {
            throw Error("DSI_URI_INVALID", "包地址必须是无凭据、无 fragment、端口 443 的绝对 HTTPS URL。");
        }
        if (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
        {
            throw Error("DSI_NETWORK_BLOCKED", "包地址不能使用本机或本地域名。");
        }
        if (IPAddress.TryParse(uri.Host, out var address) && !IsPublicAddress(address))
        {
            throw Error("DSI_NETWORK_BLOCKED", "包地址不能使用私网、链路本地或保留 IP。");
        }
        return uri;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var first = bytes[0];
            var second = bytes[1];
            if (first == 0 || first == 10 || first == 127 || first >= 224) return false;
            if (first == 100 && second is >= 64 and <= 127) return false;
            if (first == 169 && second == 254) return false;
            if (first == 172 && second is >= 16 and <= 31) return false;
            if (first == 192 && second == 168) return false;
            if (first == 192 && second == 0) return false;
            if (first == 192 && second == 88 && bytes[2] == 99) return false;
            if (first == 192 && second == 0 && bytes[2] == 2) return false;
            if (first == 198 && second is 18 or 19) return false;
            if (first == 198 && second == 51 && bytes[2] == 100) return false;
            if (first == 203 && second == 0 && bytes[2] == 113) return false;
            return true;
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal) return false;
            if ((bytes[0] & 0xFE) == 0xFC) return false;
            if (bytes[0] == 0x20 && bytes[1] == 0x02) return false; // 6to4 embeds IPv4 targets.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x00 && bytes[3] == 0x00) return false; // Teredo.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8) return false;
            if (bytes.AsSpan(0, 12).SequenceEqual(new byte[] { 0x00, 0x64, 0xFF, 0x9B, 0, 0, 0, 0, 0, 0, 0, 0 })) return false;
            if (bytes.AsSpan(0, 12).SequenceEqual(new byte[12])) return false;
            return true;
        }

        return false;
    }

    private static string DecodePercentOnce(string value)
    {
        using var output = new MemoryStream(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character == '%')
            {
                if (index + 2 >= value.Length || !TryHex(value[index + 1], out var high) || !TryHex(value[index + 2], out var low))
                {
                    throw Error("DSI_URI_INVALID", "查询参数包含畸形百分号编码。");
                }
                output.WriteByte((byte)((high << 4) | low));
                index += 2;
                continue;
            }

            if (character > 0x7F) throw Error("DSI_URI_INVALID", "查询参数中的非 ASCII 字符必须使用 UTF-8 百分号编码。");
            output.WriteByte((byte)character);
        }

        try
        {
            return new UTF8Encoding(false, true).GetString(output.ToArray());
        }
        catch (DecoderFallbackException)
        {
            throw Error("DSI_URI_INVALID", "查询参数不是严格 UTF-8。");
        }
    }

    private static bool TryHex(char value, out int result)
    {
        if (value is >= '0' and <= '9') { result = value - '0'; return true; }
        if (value is >= 'a' and <= 'f') { result = value - 'a' + 10; return true; }
        if (value is >= 'A' and <= 'F') { result = value - 'A' + 10; return true; }
        result = 0;
        return false;
    }

    private static InvalidOperationException Error(string code, string message) => new($"{code}: {message}");
}
