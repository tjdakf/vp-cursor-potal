using System.Text.Json;
using H2CursorRouter.Updater;
using Xunit;

namespace H2CursorRouter.Updater.Tests;

public sealed class ReleaseUpdateTests
{
    internal static string Release(string tag = "v0.1.10", string? digest = null, string? url = null, bool prerelease = false, bool draft = false, string state = "uploaded", long size = 4) =>
        JsonSerializer.Serialize(new { tag_name = tag, prerelease, draft, body = "Change notes", assets = new[] { new {
            name = ReleaseUpdate.InstallerName, state, size,
            digest = digest ?? "sha256:" + new string('a', 64),
            browser_download_url = url ?? $"https://github.com/{ReleaseUpdate.Repository}/releases/download/{tag}/{ReleaseUpdate.InstallerName}"
        } } });

    [Fact]
    public void NumericComparisonTreatsTenAsNewerThanNine()
    {
        var update = ReleaseUpdate.Parse(Release(), new Version(0, 1, 9, 0));
        Assert.Equal(new Version(0, 1, 10), update!.Version);
        Assert.Equal("Change notes", update.Notes);
    }

    [Theory]
    [InlineData("v0.1.9")]
    [InlineData("v0.1.8")]
    public void DoesNotOfferEqualOrOlderRelease(string tag) => Assert.Null(ReleaseUpdate.Parse(Release(tag), new Version(0, 1, 9, 0)));

    [Fact]
    public void IgnoresDraftAndPrerelease()
    {
        Assert.Null(ReleaseUpdate.Parse(Release(draft: true), new Version(0, 1, 9)));
        Assert.Null(ReleaseUpdate.Parse(Release(prerelease: true), new Version(0, 1, 9)));
    }

    [Theory]
    [InlineData("https://example.com/setup.exe")]
    [InlineData("http://github.com/tjdakf/vp-cursor-portal/releases/download/v0.1.10/vp-cursor-portal-setup.exe")]
    [InlineData("https://github.com/other/repo/releases/download/v0.1.10/vp-cursor-portal-setup.exe")]
    public void RejectsUnexpectedDownloadAddress(string url) => Assert.Throws<InvalidDataException>(() => ReleaseUpdate.Parse(Release(url: url), new Version(0, 1, 9)));

    [Theory]
    [InlineData("")]
    [InlineData("sha256:broken")]
    public void RejectsMissingOrInvalidChecksum(string digest) => Assert.Throws<InvalidDataException>(() => ReleaseUpdate.Parse(Release(digest: digest), new Version(0, 1, 9)));

    [Theory]
    [InlineData(0)]
    [InlineData(536870913)]
    public void RejectsInvalidSize(long size) => Assert.Throws<InvalidDataException>(() => ReleaseUpdate.Parse(Release(size: size), new Version(0, 1, 9)));

    [Fact]
    public void RejectsIncompleteUpload() => Assert.Throws<InvalidDataException>(() => ReleaseUpdate.Parse(Release(state: "starter"), new Version(0, 1, 9)));

    [Fact]
    public void InstallerArgumentsKeepPathsTogetherAndDoNotForceCloseOrReboot()
    {
        var directory = Path.Combine(Path.GetTempPath(), "test install with spaces");
        var request = new UpdateRequest(Path.Combine(Path.GetTempPath(), ReleaseUpdate.InstallerName), directory, 123, 456, new string('a', 64));
        var info = request.CreateInstallerStartInfo();
        Assert.Contains("/DIR=" + directory, info.ArgumentList);
        Assert.Contains("/SILENT", info.ArgumentList);
        Assert.Contains("/NORESTART", info.ArgumentList);
        Assert.Contains("/NOCLOSEAPPLICATIONS", info.ArgumentList);
        Assert.Contains("/APPUPDATE", info.ArgumentList);
        Assert.DoesNotContain("/FORCECLOSEAPPLICATIONS", info.ArgumentList);
        Assert.NotEqual("runas", info.Verb);
    }
}
