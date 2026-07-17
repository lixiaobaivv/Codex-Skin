using CodexThemeStore.Core;

namespace CodexThemeStore.Core.Tests;

public sealed class DreamSkinDownloadServiceTests
{
    private static readonly Uri OfficialPackage = new(
        "https://github.com/lixiaobaivv/Codex-Skin/releases/download/official-themes-v1/Codex-Skin-theme-kun-stage-1.0.0.dreamskin");

    [Theory]
    [InlineData("github", "github", "gh-proxy", "ghfast")]
    [InlineData("gh-proxy", "gh-proxy", "github", "ghfast")]
    [InlineData("ghfast", "ghfast", "github", "gh-proxy")]
    public void OfficialReleaseUsesPreferredSourceThenFallbacks(
        string preferred,
        string first,
        string second,
        string third)
    {
        var candidates = DreamSkinDownloadService.GetDownloadCandidates(OfficialPackage, preferred);

        Assert.Equal([first, second, third], candidates.Select(candidate => candidate.SourceId));
        Assert.Equal(OfficialPackage, candidates.Single(candidate => candidate.SourceId == "github").Uri);
        Assert.StartsWith("https://gh-proxy.com/https://github.com/", candidates.Single(candidate => candidate.SourceId == "gh-proxy").Uri.AbsoluteUri);
        Assert.StartsWith("https://ghfast.top/https://github.com/", candidates.Single(candidate => candidate.SourceId == "ghfast").Uri.AbsoluteUri);
    }

    [Fact]
    public void NonGitHubPackageDoesNotUseRepositoryMirrors()
    {
        var package = new Uri("https://downloads.example.com/themes/sample.dreamskin");

        var candidate = Assert.Single(DreamSkinDownloadService.GetDownloadCandidates(package, "ghfast"));

        Assert.Equal("github", candidate.SourceId);
        Assert.Equal(package, candidate.Uri);
    }
}
