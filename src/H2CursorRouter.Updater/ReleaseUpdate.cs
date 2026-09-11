using System.Text.Json;
using System.Text.RegularExpressions;

namespace H2CursorRouter.Updater;

public sealed record ReleaseUpdate(Version Version, string Tag, string Notes, Uri DownloadUri, long Size, string Sha256)
{
    public const string Repository = "tjdakf/vp-cursor-portal";
    public const string InstallerName = "vp-cursor-portal-setup.exe";
    public const long MaximumInstallerSize = 512L * 1024 * 1024;

    public static ReleaseUpdate? Parse(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var release = document.RootElement;
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())
            return null;
        var tag = release.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?\d+\.\d+\.\d+$") || !Version.TryParse(tag.TrimStart('v'), out var version))
            throw new InvalidDataException("The latest release has an unsupported version number.");
        var current = new Version(currentVersion.Major, currentVersion.Minor, Math.Max(0, currentVersion.Build));
        if (version <= current) return null;
        var assets = release.GetProperty("assets").EnumerateArray()
            .Where(asset => asset.GetProperty("name").GetString() == InstallerName).ToArray();
        if (assets.Length != 1) throw new InvalidDataException("The new release installer is not ready yet. Try again later.");
        var asset = assets[0];
        var expectedUrl = $"https://github.com/{Repository}/releases/download/{tag}/{InstallerName}";
        if (asset.GetProperty("browser_download_url").GetString() != expectedUrl ||
            asset.GetProperty("state").GetString() != "uploaded")
            throw new InvalidDataException("The release installer address is invalid or not ready.");
        var digest = asset.TryGetProperty("digest", out var hash) ? hash.GetString() ?? "" : "";
        if (!Regex.IsMatch(digest, "^sha256:[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("The release has no valid SHA-256 checksum. Installation is unavailable.");
        var size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumInstallerSize) throw new InvalidDataException("The installer size is invalid.");
        return new(version, tag, release.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
            new Uri(expectedUrl), size, digest[7..]);
    }
}
