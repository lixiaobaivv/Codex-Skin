using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class CdpThemeInjectorTests
{
    [Fact]
    public async Task UnavailableEndpointIsTreatedAsNoInjectableTargets()
    {
        using var httpClient = new HttpClient(new RefusingHandler());
        var injector = new CdpThemeInjector(httpClient);

        Assert.False(await injector.IsReadyAsync());
        Assert.Equal(0, await injector.InjectAsync(new ThemeInjectionPayload("", "", "test"), TimeSpan.FromSeconds(1)));
        Assert.Equal(0, await injector.RemoveAsync(TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData("ws://127.0.0.1:9229/devtools/page/1")]
    [InlineData("ws://localhost:9229/devtools/page/1")]
    public void AllowsOnlyExpectedLoopbackTargets(string url)
    {
        Assert.True(CdpThemeInjector.IsAllowedDebuggerWebSocketUrl(url));
    }

    [Theory]
    [InlineData("ws://127.0.0.1:9230/devtools/page/1")]
    [InlineData("ws://192.168.1.20:9229/devtools/page/1")]
    [InlineData("wss://localhost:9229/devtools/page/1")]
    [InlineData("ws://user@localhost:9229/devtools/page/1")]
    [InlineData("ws://localhost:9229/devtools/page/1?token=unexpected")]
    [InlineData("ws://localhost:9229/arbitrary/path")]
    [InlineData("not-a-url")]
    public void RejectsExternalOrMalformedTargets(string url)
    {
        Assert.False(CdpThemeInjector.IsAllowedDebuggerWebSocketUrl(url));
    }

    private sealed class RefusingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("Connection refused"));
    }
}
