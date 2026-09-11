using System.IO;
using H2CursorRouter.App.Services;
using H2CursorRouter.App.ViewModels;
using H2CursorRouter.Updater;
using Xunit;

namespace H2CursorRouter.App.Tests;

public sealed class UpdateViewModelTests
{
    private static readonly ReleaseUpdate Release = new(new Version(0, 1, 10), "v0.1.10", "Fixed a bug",
        new Uri("https://github.com/tjdakf/vp-cursor-portal/releases/download/v0.1.10/vp-cursor-portal-setup.exe"), 4, new string('a', 64));

    [Fact]
    public async Task NewReleaseEnablesInstallAndDownloadPrecedesHandoff()
    {
        var client = new Client();
        var installer = new Installer();
        var vm = Create(client, installer);
        Assert.False(vm.InstallCommand.CanExecute(null));
        await vm.CheckAsync();
        Assert.Equal("0.1.10", vm.LatestVersion);
        Assert.True(vm.InstallCommand.CanExecute(null));
        installer.OnInstall = () => {
            Assert.True(client.Downloaded);
            Assert.False(vm.CanUseApp);
        };
        await vm.InstallAsync();
        Assert.True(installer.Started);
    }

    [Fact]
    public async Task BadDownloadLeavesAppUsableAndDoesNotStartInstaller()
    {
        var client = new Client { DownloadFailure = new InvalidDataException("checksum mismatch") };
        var installer = new Installer();
        var vm = Create(client, installer);
        await vm.CheckAsync();
        await vm.InstallAsync();
        Assert.False(installer.Started);
        Assert.True(vm.CanUseApp);
        Assert.Contains("checksum mismatch", vm.Status);
        Assert.True(vm.InstallCommand.CanExecute(null));
    }

    [Fact]
    public async Task PortableCopyCanCheckButCannotInstall()
    {
        var client = new Client();
        var installer = new Installer { IsInstalled = false };
        var vm = Create(client, installer);
        await vm.CheckAsync();
        Assert.Equal("0.1.10", vm.LatestVersion);
        Assert.False(vm.InstallCommand.CanExecute(null));
        await vm.InstallAsync();
        Assert.False(client.Downloaded);
    }

    [Fact]
    public async Task FailedRecheckClearsPreviouslyOfferedUpdate()
    {
        var client = new Client();
        var vm = Create(client, new Installer());
        await vm.CheckAsync();
        client.CheckFailure = new IOException("offline");
        await vm.CheckAsync();
        Assert.False(vm.InstallCommand.CanExecute(null));
        Assert.Contains("offline", vm.Status);
        Assert.True(vm.CanUseApp);
    }

    [Fact]
    public async Task PreparationFailureRestoresControls()
    {
        var vm = Create(new Client(), new Installer { OnInstall = () => throw new IOException("save failed") });
        await vm.CheckAsync();
        await vm.InstallAsync();
        Assert.True(vm.CanUseApp);
        Assert.Contains("save failed", vm.Status);
    }

    [Fact]
    public async Task DownloadCanBeCancelledWithoutStartingInstaller()
    {
        var client = new Client { WaitForCancellation = true };
        var installer = new Installer();
        var vm = Create(client, installer);
        await vm.CheckAsync();
        var install = vm.InstallAsync();
        Assert.True(vm.CancelCommand.CanExecute(null));
        Assert.False(vm.CheckCommand.CanExecute(null));
        vm.CancelCommand.Execute(null);
        await install;
        Assert.False(installer.Started);
        Assert.True(vm.CanUseApp);
        Assert.Contains("cancelled", vm.Status);
    }

    [Fact]
    public void StartupPreferenceIsSavedOnlyWhenChanged()
    {
        var changes = new List<bool>();
        var vm = new UpdateViewModel(new Client(), new Installer(), new Version(0, 1, 9), "unused", false, changes.Add);
        vm.CheckOnStartup = true;
        vm.CheckOnStartup = true;
        Assert.Equal(new[] { true }, changes);
    }

    private static UpdateViewModel Create(Client client, Installer installer) => new(client, installer, new Version(0, 1, 9), "unused", false, _ => { });
    private sealed class Client : IUpdateClient
    {
        public bool Downloaded;
        public Exception? DownloadFailure;
        public Exception? CheckFailure;
        public bool WaitForCancellation;
        public Task<ReleaseUpdate?> CheckAsync(Version currentVersion, CancellationToken cancellationToken) =>
            CheckFailure is null ? Task.FromResult<ReleaseUpdate?>(Release) : Task.FromException<ReleaseUpdate?>(CheckFailure);
        public async Task<string> DownloadAsync(ReleaseUpdate update, string directory, IProgress<int> progress, CancellationToken cancellationToken)
        {
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            if (DownloadFailure is not null) throw DownloadFailure;
            Downloaded = true;
            return "verified-installer.exe";
        }
    }
    private sealed class Installer : IUpdateInstaller
    {
        public bool IsInstalled { get; set; } = true;
        public bool Started;
        public Action? OnInstall;
        public Task InstallAsync(string path, ReleaseUpdate release) { OnInstall?.Invoke(); Started = true; return Task.CompletedTask; }
    }
}
