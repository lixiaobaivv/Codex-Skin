using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

internal sealed class WindowsSingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly string _pipeName;
    private readonly bool _ownsMutex;
    private Task? _listener;

    private WindowsSingleInstance(Mutex mutex, bool ownsMutex, string pipeName)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
        _pipeName = pipeName;
    }

    public bool IsPrimary => _ownsMutex;

    public static WindowsSingleInstance Create()
    {
        var identity = $"{Environment.UserDomainName}\\{Environment.UserName}|{Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)}";
        var suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16];
        var mutex = new Mutex(true, $"Local\\Codex-Skin-{suffix}", out var createdNew);
        return new WindowsSingleInstance(mutex, createdNew, $"Codex-Skin-{suffix}");
    }

    public bool TryForward(string? value)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
            client.Connect(3000);
            using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            writer.WriteLine(value is null ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void StartListening(Control owner, Func<string?, Task> handler)
    {
        if (!IsPrimary || _listener is not null) return;
        _listener = Task.Run(async () =>
        {
            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_shutdown.Token);
                    using var reader = new StreamReader(server, Encoding.UTF8, false, 4096, leaveOpen: true);
                    var line = await reader.ReadLineAsync(_shutdown.Token);
                    if (line is null || line.Length > 16 * 1024) continue;
                    var value = line.Length == 0 ? null : Encoding.UTF8.GetString(Convert.FromBase64String(line));
                    if (value is { Length: > 8192 }) continue;
                    if (!owner.IsDisposed && owner.IsHandleCreated)
                        owner.BeginInvoke(new Action(() => _ = handler(value)));
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is IOException or FormatException)
                {
                    // Ignore malformed local activation and continue serving the primary window.
                }
            }
        });
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try { _listener?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException) { }
        _shutdown.Dispose();
        if (_ownsMutex)
        {
            try { _mutex.ReleaseMutex(); }
            catch (ApplicationException) { }
        }
        _mutex.Dispose();
    }
}
