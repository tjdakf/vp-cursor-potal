using H2CursorRouter.Core.Configuration;
using H2CursorRouter.Core.Domain;
using Xunit;

namespace H2CursorRouter.Core.Tests;

public sealed class ConfigFileServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "vpc-config-" + Guid.NewGuid().ToString("N"));
    private static AppConfiguration Empty => new([], [], [], SafetySettings.Default);

    [Fact]
    public async Task CancelledSavePreservesExistingBytesAndLeavesNoTemporaryFile()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        var service = new ConfigFileService();
        await service.SaveAsync(Empty, path);
        var original = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAsync(Empty, path, cancellation.Token));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.EnumerateFiles(_directory));
    }

    [Fact]
    public async Task SuccessfulSaveReplacesConfigurationWithoutBackupFiles()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "config.json");
        var service = new ConfigFileService();
        await service.SaveAsync(Empty, path);
        var updated = new AppConfiguration([new H2DeviceConfig("device", "Saved", "127.0.0.1", 6000, 0, TimeSpan.FromSeconds(1))], [], [], SafetySettings.Default);
        await service.SaveAsync(updated, path);
        Assert.Equal("Saved", Assert.Single((await service.LoadAsync(path)).Devices).Name);
        Assert.Single(Directory.EnumerateFiles(_directory));
    }

    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
