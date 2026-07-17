using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class CdpThemeInjectorTests
{
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
}
