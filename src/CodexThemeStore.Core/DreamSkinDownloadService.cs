using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace CodexThemeStore.Core;

public static class DreamSkinDownloadService
{
    private const int MaxRedirects = 3;

    public static DreamSkinImportResult DownloadAndImport(DreamSkinInstallRequest request)
    {
        return DownloadAndImportAsync(request, CancellationToken.None).GetAwaiter().GetResult();
    }

    public static async Task<DreamSkinImportResult> DownloadAndImportAsync(
        DreamSkinInstallRequest request,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));

        var downloadRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexThemeStore",
            "downloads",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(downloadRoot);
        var archivePath = Path.Combine(downloadRoot, "archive.dreamskin");

        try
        {
            using var handler = CreateHandler();
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodexThemeStore", "1.0"));

            var current = request.PackageUri;
            var redirects = 0;
            while (true)
            {
                current = DreamSkinProtocol.ValidatePackageUri(current.AbsoluteUri);
                using var message = new HttpRequestMessage(HttpMethod.Get, current);
                message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.codex-dream-skin+zip"));
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);

                if (IsRedirect(response.StatusCode))
                {
                    if (redirects >= MaxRedirects)
                    {
                        throw Error("DSI_REDIRECT_LIMIT", "主题包下载重定向超过 3 次。");
                    }
                    var location = response.Headers.Location
                        ?? throw Error("DSI_DOWNLOAD_FAILED", "下载重定向缺少 Location。");
                    current = DreamSkinProtocol.ValidatePackageUri(new Uri(current, location).AbsoluteUri);
                    redirects++;
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw Error("DSI_DOWNLOAD_FAILED", $"主题包服务器返回 HTTP {(int)response.StatusCode}。");
                }

                var contentLength = response.Content.Headers.ContentLength;
                if (contentLength is < 1 or > DreamSkinProtocol.MaxPackageBytes)
                {
                    throw Error("DSI_SIZE_LIMIT", "服务器声明的主题包大小无效或超过 20 MiB。");
                }
                if (contentLength.HasValue && contentLength.Value != request.Size)
                {
                    throw Error("DSI_SIZE_MISMATCH", "服务器 Content-Length 与深链接声明不一致。");
                }

                await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
                await using var output = new FileStream(
                    archivePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                while (true)
                {
                    var read = await input.ReadAsync(buffer, timeout.Token);
                    if (read == 0) break;
                    total += read;
                    if (total > DreamSkinProtocol.MaxPackageBytes || total > request.Size)
                    {
                        throw Error("DSI_SIZE_LIMIT", "主题包响应体超过声明大小或 20 MiB 限制。");
                    }
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                await output.FlushAsync(timeout.Token);
                output.Close();

                if (total != request.Size)
                {
                    throw Error("DSI_SIZE_MISMATCH", "下载完成后的字节数与深链接声明不一致。");
                }
                var actualSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!actualSha256.Equals(request.Sha256, StringComparison.Ordinal))
                {
                    throw Error("DSI_HASH_MISMATCH", "下载主题包的 SHA-256 与深链接声明不一致。");
                }

                return DreamSkinPackageInstaller.ImportLocal(
                    archivePath,
                    new DreamSkinExpectedPackage(request.Sha256, request.Size, request.Id, request.Version));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Error("DSI_DOWNLOAD_FAILED", "主题包下载超时。");
        }
        catch (HttpRequestException ex)
        {
            throw Error("DSI_DOWNLOAD_FAILED", $"主题包下载失败：{ex.HttpRequestError}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(downloadRoot)) Directory.Delete(downloadRoot, recursive: true);
            }
            catch (IOException)
            {
                // Do not replace the original import result with a cleanup error.
            }
            catch (UnauthorizedAccessException)
            {
                // The next launch may reclaim an inherited-ACL temp directory.
            }
        }
    }

    private static SocketsHttpHandler CreateHandler()
    {
        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.Zero,
            PooledConnectionIdleTimeout = TimeSpan.Zero,
            MaxConnectionsPerServer = 1,
            MaxResponseHeadersLength = 32,
            ConnectCallback = ConnectPublicAddressAsync,
        };
    }

    private static async ValueTask<Stream> ConnectPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken);
        var publicAddresses = addresses.Where(DreamSkinProtocol.IsPublicAddress).ToArray();
        if (publicAddresses.Length == 0)
        {
            throw Error("DSI_NETWORK_BLOCKED", "下载域名未解析到允许的公网地址。");
        }

        Exception? lastError = null;
        foreach (var address in publicAddresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                lastError = ex;
                if (ex is OperationCanceledException) throw;
            }
        }

        throw new HttpRequestException("No public download address was reachable.", lastError);
    }

    private static bool IsRedirect(HttpStatusCode statusCode)
    {
        return statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;
    }

    private static InvalidOperationException Error(string code, string message) => new($"{code}: {message}");
}
