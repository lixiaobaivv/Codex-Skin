using System.Net.WebSockets;
using System.Text.Json;

namespace CodexThemeStore.Core;

public sealed class CdpThemeInjector
{
    public const int DebugPort = 9229;
    private const int MaxCdpMessageBytes = 4 * 1024 * 1024;
    private const string LiveStyleId = "codex-theme-store-live-style";
    private const string ActiveInjectionStorageKey = "__codexThemeStoreActiveInjection";
    private const string NewDocumentScriptStorageKey = "__codexThemeStoreNewDocumentScript";
    private const string InjectionIdProperty = "injectionId";
    private readonly HttpClient _http;

    public CdpThemeInjector(HttpClient? httpClient = null)
    {
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync($"http://127.0.0.1:{DebugPort}/json/version", cancellationToken);
            if (!response.IsSuccessStatusCode) return false;
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("webSocketDebuggerUrl", out var node) &&
                   node.ValueKind == JsonValueKind.String && IsAllowedDebuggerWebSocketUrl(node.GetString());
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task<int> InjectAsync(ThemeInjectionPayload payload, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var injectionId = Guid.NewGuid().ToString("N");
        var themeExpression = BuildThemeExpression(payload, injectionId);
        var activationExpression = $@"(() => {{
  try {{
    localStorage.setItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}, {JsonSerializer.Serialize(injectionId)});
    if (localStorage.getItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}) !== {JsonSerializer.Serialize(injectionId)}) return false;
  }} catch {{ return false; }}
  return {themeExpression};
}})()";
        var newDocumentExpression = $@"(() => {{
  if (window.top !== window) return;
  const applySavedTheme = () => {{
    try {{ if (localStorage.getItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}) !== {JsonSerializer.Serialize(injectionId)}) return; }} catch {{ return; }}
    {themeExpression};
  }};
  if (document.documentElement) applySavedTheme();
  else document.addEventListener('DOMContentLoaded', applySavedTheme, {{ once: true }});
}})()";
        var verificationExpression = $@"(() => {{
  let active = false;
  try {{ active = localStorage.getItem({JsonSerializer.Serialize(ActiveInjectionStorageKey)}) === {JsonSerializer.Serialize(injectionId)}; }} catch {{}}
  const style = document.getElementById({JsonSerializer.Serialize(LiveStyleId)});
  const store = globalThis.__codexThemeStore;
  const actualTheme = document.getElementById('codex-theme-home')?.dataset?.themeId || '';
  return active && style?.dataset.codexThemeStoreInjectionId === {JsonSerializer.Serialize(injectionId)} &&
    !!store && store[{JsonSerializer.Serialize(InjectionIdProperty)}] === {JsonSerializer.Serialize(injectionId)} &&
    actualTheme.length > 0 && ({JsonSerializer.Serialize(payload.ThemeId)} === null || actualTheme === {JsonSerializer.Serialize(payload.ThemeId)});
}})()";

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var targets = await GetCodexPageTargetsAsync(timeoutSource.Token);
        var successes = 0;
        foreach (var target in targets)
        {
            if (await InjectTargetAsync(target, activationExpression, newDocumentExpression, verificationExpression, timeoutSource.Token))
                successes++;
        }
        return successes;
    }

    public async Task<int> RemoveAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        const string cleanupExpression = @"(() => {
  try {
    localStorage.removeItem('__codexThemeStoreActiveInjection');
    localStorage.removeItem('__codexThemeStoreNewDocumentScript');
  } catch {}
  globalThis.__codexThemeStore?.dispose?.();
  globalThis.__codexThemeStore?.observer?.disconnect();
  globalThis.__codexThemeStore?.resizeHandler && window.removeEventListener('resize', globalThis.__codexThemeStore.resizeHandler);
  delete globalThis.__codexThemeStore;
  document.getElementById('codex-theme-store-live-style')?.remove();
  document.getElementById('codex-theme-home')?.remove();
  document.documentElement.removeAttribute('data-codex-theme-id');
  setTimeout(() => location.reload(), 0);
  return true;
})()";

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        var targets = await GetCodexPageTargetsAsync(timeoutSource.Token);
        var successes = 0;
        foreach (var target in targets)
        {
            if (await RemoveTargetAsync(target, cleanupExpression, timeoutSource.Token)) successes++;
        }
        return successes;
    }

    public static bool IsAllowedDebuggerWebSocketUrl(string? webSocketUrl)
    {
        if (string.IsNullOrWhiteSpace(webSocketUrl) || !Uri.TryCreate(webSocketUrl, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) || uri.Port != DebugPort || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.AbsolutePath, "^/devtools/(?:page|browser)/[A-Za-z0-9._-]{1,200}$"))
            return false;
        return uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildThemeExpression(ThemeInjectionPayload payload, string injectionId) => $@"(() => {{
  let style = document.getElementById({JsonSerializer.Serialize(LiveStyleId)});
  if (!style) {{
    style = document.createElement('style');
    style.id = {JsonSerializer.Serialize(LiveStyleId)};
    const root = document.head || document.documentElement;
    if (!root) return false;
    root.appendChild(style);
  }}
  style.dataset.codexThemeStoreInjectionId = {JsonSerializer.Serialize(injectionId)};
  style.textContent = {JsonSerializer.Serialize(payload.Css)};
  {payload.JavaScript}
  if (!globalThis.__codexThemeStore) return false;
  globalThis.__codexThemeStore[{JsonSerializer.Serialize(InjectionIdProperty)}] = {JsonSerializer.Serialize(injectionId)};
  return true;
}})()";

    private async Task<List<string>> GetCodexPageTargetsAsync(CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _http.GetAsync($"http://127.0.0.1:{DebugPort}/json/list", cancellationToken);
        }
        catch (HttpRequestException)
        {
            return [];
        }
        using (response)
        {
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var targets = new List<(int Priority, string Url)>();
            foreach (var target in document.RootElement.EnumerateArray())
            {
                if (!target.TryGetProperty("type", out var type) || type.GetString() != "page") continue;
                var priority = GetTargetPriority(target);
                if (priority < 0 || !target.TryGetProperty("webSocketDebuggerUrl", out var socketNode)) continue;
                var socketUrl = socketNode.GetString();
                if (IsAllowedDebuggerWebSocketUrl(socketUrl)) targets.Add((priority, socketUrl!));
            }
            return targets.OrderBy(item => item.Priority).ThenBy(item => item.Url, StringComparer.Ordinal)
                .Select(item => item.Url).Distinct(StringComparer.Ordinal).ToList();
        }
    }

    private static int GetTargetPriority(JsonElement target)
    {
        var url = target.TryGetProperty("url", out var urlNode) ? urlNode.GetString() ?? "" : "";
        var title = target.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? "" : "";
        var appRoot = url.StartsWith("app:///", StringComparison.OrdinalIgnoreCase);
        var codex = url.Contains("codex", StringComparison.OrdinalIgnoreCase) || title.Contains("codex", StringComparison.OrdinalIgnoreCase);
        if (appRoot && codex) return 0;
        if (appRoot) return 1;
        return codex ? 2 : -1;
    }

    private static async Task<bool> InjectTargetAsync(string url, string activation, string newDocument, string verification, CancellationToken token)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), token);
        if (!Successful(await SendAsync(socket, 1, "Page.enable", new { }, token))) return false;
        const string shellProbe = "!!document.querySelector('.main-surface, .browser-main-surface, main.main-surface') && !!document.querySelector('.app-shell-left-panel, aside.app-shell-left-panel') && (!!document.querySelector('.composer-surface-chrome') || !!document.querySelector('[role=main]'))";
        if (!IsTrue(await EvaluateAsync(socket, 99, shellProbe, token))) return false;

        var previous = await EvaluateAsync(socket, 2, "(() => { try { return localStorage.getItem('__codexThemeStoreNewDocumentScript') || ''; } catch { return ''; } })()", token);
        if (TryString(previous, out var previousId) && !string.IsNullOrWhiteSpace(previousId))
            await TryRemoveScriptAsync(socket, previousId, 3, token);

        var add = await SendAsync(socket, 4, "Page.addScriptToEvaluateOnNewDocument", new { source = newDocument }, token);
        if (!TryResult(add, out var addResult) || !addResult.TryGetProperty("identifier", out var identifierNode)) return false;
        var identifier = identifierNode.GetString();
        var persist = await EvaluateAsync(socket, 5, $"(() => {{ try {{ localStorage.setItem('__codexThemeStoreNewDocumentScript', {JsonSerializer.Serialize(identifier)}); return true; }} catch {{ return false; }} }})()", token);
        if (!IsTrue(persist))
        {
            await TryRemoveScriptAsync(socket, identifier, 6, token);
            return false;
        }
        if (!SuccessfulEvaluation(await EvaluateAsync(socket, 7, activation, token)))
        {
            await TryRemoveScriptAsync(socket, identifier, 8, token);
            return false;
        }
        for (var attempt = 0; attempt < 5; attempt++)
        {
            if (IsTrue(await EvaluateAsync(socket, 10 + attempt, verification, token))) return true;
            if (attempt < 4) await Task.Delay(50, token);
        }
        await TryRemoveScriptAsync(socket, identifier, 20, token);
        return false;
    }

    private static async Task<bool> RemoveTargetAsync(string url, string cleanup, CancellationToken token)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri(url), token);
        if (!Successful(await SendAsync(socket, 1, "Page.enable", new { }, token))) return false;
        var previous = await EvaluateAsync(socket, 2, "(() => { try { return localStorage.getItem('__codexThemeStoreNewDocumentScript') || ''; } catch { return ''; } })()", token);
        if (TryString(previous, out var identifier) && !string.IsNullOrWhiteSpace(identifier))
            await TryRemoveScriptAsync(socket, identifier, 3, token);
        return IsTrue(await EvaluateAsync(socket, 4, cleanup, token));
    }

    private static Task<JsonElement?> EvaluateAsync(ClientWebSocket socket, int id, string expression, CancellationToken token) =>
        SendAsync(socket, id, "Runtime.evaluate", new { expression, awaitPromise = true, returnByValue = true }, token);

    private static async Task TryRemoveScriptAsync(ClientWebSocket socket, string? identifier, int id, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(identifier)) return;
        try { await SendAsync(socket, id, "Page.removeScriptToEvaluateOnNewDocument", new { identifier }, token); }
        catch (WebSocketException) { }
    }

    private static async Task<JsonElement?> SendAsync(ClientWebSocket socket, int id, string method, object parameters, CancellationToken token)
    {
        var request = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params = parameters });
        await socket.SendAsync(request, WebSocketMessageType.Text, true, token);
        var buffer = new byte[64 * 1024];
        using var response = new MemoryStream();
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, token);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            if (result.MessageType != WebSocketMessageType.Text) continue;
            response.Write(buffer, 0, result.Count);
            if (response.Length > MaxCdpMessageBytes) throw new InvalidDataException("CDP 响应超过 4 MB 安全限制。");
            if (!result.EndOfMessage) continue;
            using var document = JsonDocument.Parse(response.ToArray());
            if (document.RootElement.TryGetProperty("id", out var node) && node.GetInt32() == id) return document.RootElement.Clone();
            response.SetLength(0);
        }
    }

    private static bool Successful(JsonElement? response) => response is { } value && !value.TryGetProperty("error", out _);
    private static bool TryResult(JsonElement? response, out JsonElement result)
    {
        result = default;
        return Successful(response) && response!.Value.TryGetProperty("result", out result);
    }
    private static bool SuccessfulEvaluation(JsonElement? response) => TryResult(response, out var result) && !result.TryGetProperty("exceptionDetails", out _) && result.TryGetProperty("result", out _);
    private static bool IsTrue(JsonElement? response) => SuccessfulEvaluation(response) && TryResult(response, out var result) && result.TryGetProperty("result", out var remote) && remote.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.True;
    private static bool TryString(JsonElement? response, out string? value)
    {
        value = null;
        if (!SuccessfulEvaluation(response) || !TryResult(response, out var result) || !result.TryGetProperty("result", out var remote) || !remote.TryGetProperty("value", out var node) || node.ValueKind != JsonValueKind.String) return false;
        value = node.GetString();
        return true;
    }
}
