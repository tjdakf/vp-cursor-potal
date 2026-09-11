using System.Net;
using System.Security.Cryptography;
using H2CursorRouter.Updater;
using Xunit;

namespace H2CursorRouter.Updater.Tests;

public sealed class GitHubUpdateClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vpc-download-" + Guid.NewGuid().ToString("N"));
    private static readonly byte[] Content = [1, 2, 3, 4];
    private static ReleaseUpdate Update(string? hash = null, long size = 4) => new(new Version(0, 1, 10), "v0.1.10", "",
        new Uri($"https://github.com/{ReleaseUpdate.Repository}/releases/download/v0.1.10/{ReleaseUpdate.InstallerName}"),
        size, hash ?? Convert.ToHexString(SHA256.HashData(Content)));

    [Fact]
    public async Task DownloadsOnlyAfterSizeAndHashMatch()
    {
        using var client = CreateClient(Content);
        var path = await new GitHubUpdateClient(client).DownloadAsync(Update(), _directory, new Progress<int>(), default);
        Assert.Equal(Content, await File.ReadAllBytesAsync(path));
        Assert.False(File.Exists(path + ".partial"));
    }

    [Fact]
    public async Task RejectsHashMismatchAndRemovesPartialFile()
    {
        using var client = CreateClient(Content);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(client).DownloadAsync(Update(new string('0', 64)), _directory, new Progress<int>(), default));
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task RejectsTruncatedDownload()
    {
        using var client = CreateClient([1, 2]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(client).DownloadAsync(Update(), _directory, new Progress<int>(), default));
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task RejectsUnexpectedRedirect()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) {
            Content = new ByteArrayContent(Content), RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://example.com/setup.exe") }));
        await Assert.ThrowsAsync<InvalidDataException>(() => new GitHubUpdateClient(client).DownloadAsync(Update(), _directory, new Progress<int>(), default));
    }

    [Fact]
    public async Task CancellationDoesNotLeaveAnInstaller()
    {
        using var client = CreateClient(Content);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new GitHubUpdateClient(client).DownloadAsync(Update(), _directory, new Progress<int>(), cancellation.Token));
        Assert.Empty(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task RateLimitIsReportedAsFailure()
    {
        using var client = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<HttpRequestException>(() => new GitHubUpdateClient(client).CheckAsync(new Version(0, 1, 9), default));
    }

    private static HttpClient CreateClient(byte[] bytes) => new(new Handler(request => new HttpResponseMessage(HttpStatusCode.OK) {
        Content = new ByteArrayContent(bytes), RequestMessage = request }));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(response(request));
        }
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
