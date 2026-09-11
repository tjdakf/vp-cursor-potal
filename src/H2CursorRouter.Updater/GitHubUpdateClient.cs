using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace H2CursorRouter.Updater;

public interface IUpdateClient
{
    Task<ReleaseUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken);
    Task<string> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int> progress, CancellationToken cancellationToken);
}

public sealed class GitHubUpdateClient(HttpClient client) : IUpdateClient
{
    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("vp-cursor-portal-updater/1.0");
        return request;
    }

    public async Task<ReleaseUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var request = Request(new Uri($"https://api.github.com/repos/{ReleaseUpdate.Repository}/releases/latest"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return ReleaseUpdate.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false), currentVersion);
    }

    public async Task<string> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, ReleaseUpdate.InstallerName + ".partial");
        var installerPath = Path.Combine(directory, ReleaseUpdate.InstallerName);
        try
        {
            using var request = Request(update.DownloadUri);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null || finalUri.Scheme != "https" ||
                finalUri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("The download was redirected to an unexpected location.");
            if (response.Content.Headers.ContentLength is long length && length != update.Size)
                throw new InvalidDataException("The installer download size does not match the release.");
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long received = 0;
            await using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                int count;
                while ((count = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > update.Size || received > ReleaseUpdate.MaximumInstallerSize)
                        throw new InvalidDataException("The installer download exceeds its expected size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                    progress.Report((int)(received * 100 / update.Size));
                }
            }
            if (received != update.Size || !string.Equals(Convert.ToHexString(hash.GetHashAndReset()), update.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer verification failed. No changes were made to the installed application.");
            File.Move(temporaryPath, installerPath);
            return installerPath;
        }
        catch
        {
            File.Delete(temporaryPath);
            throw;
        }
    }
}
