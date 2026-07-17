using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class MacOsCodexAdapterTests
{
    [Fact]
    public void LaunchesTheAppPathAndForwardsLoopbackCdpArguments()
    {
        var installation = new CodexInstallation(
            "/Applications/Codex.app",
            "/Applications/Codex.app/Contents/MacOS/Codex",
            "Codex");

        var startInfo = MacOsCodexAdapter.CreateLaunchStartInfo(installation, enableCdp: true);

        Assert.Equal("/usr/bin/open", startInfo.FileName);
        Assert.Equal(
            ["-n", installation.AppPath, "--args", "--remote-debugging-address=127.0.0.1", "--remote-debugging-port=9229"],
            startInfo.ArgumentList);
        Assert.DoesNotContain("-a", startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardError);
    }

    [Fact]
    public void LaunchWithoutCdpDoesNotForwardChromiumArguments()
    {
        var installation = new CodexInstallation(
            "/Applications/Codex.app",
            "/Applications/Codex.app/Contents/MacOS/Codex",
            "Codex");

        var startInfo = MacOsCodexAdapter.CreateLaunchStartInfo(installation, enableCdp: false);

        Assert.Equal(["-n", installation.AppPath], startInfo.ArgumentList);
    }
}
