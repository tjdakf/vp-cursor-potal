using System.IO;
using H2CursorRouter.App;
using H2CursorRouter.App.Services;
using H2CursorRouter.App.ViewModels;
using H2CursorRouter.Core.Configuration;
using H2CursorRouter.Core.Domain;
using H2CursorRouter.Core.Geometry;
using H2CursorRouter.Core.Profiles;
using H2CursorRouter.Core.Validation;
using H2CursorRouter.H2;
using H2CursorRouter.Windows;
using Xunit;

namespace H2CursorRouter.App.Tests;

public sealed class MainViewModelCommandTests
{
    [Fact]
    public async Task PrepareForUpdatePersistsLatestDeviceSettings()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        viewModel.Devices.Add(new DeviceRow { Id = "retained", Name = "Field device", Host = "127.0.0.1" });
        await viewModel.PrepareForUpdateAsync();
        var saved = await new ConfigFileService().LoadAsync(viewModel.ConfigPath);
        Assert.Equal("Field device", Assert.Single(saved.Devices).Name);
        Assert.Equal(SafetySettings.Default, saved.Safety);
    }

    [Fact]
    public async Task PrepareForUpdateRefusesInvalidSettingsWithoutOverwritingSavedConfig()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        await new ConfigFileService().SaveAsync(new AppConfiguration([], [], [], SafetySettings.Default), viewModel.ConfigPath);
        var original = await File.ReadAllTextAsync(viewModel.ConfigPath);
        viewModel.Devices.Add(new DeviceRow { Id = "invalid", Host = "" });
        await Assert.ThrowsAsync<InvalidOperationException>(viewModel.PrepareForUpdateAsync);
        Assert.Equal(original, await File.ReadAllTextAsync(viewModel.ConfigPath));
    }

    [Fact]
    public void FacadeCollectionsAreBackedByChildViewModels()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();

        Assert.Same(viewModel.DevicePresets.Devices, viewModel.Devices);
        Assert.Same(viewModel.DevicePresets.Presets, viewModel.Presets);
        Assert.Same(viewModel.LayoutEditor.Layouts, viewModel.Layouts);
        Assert.Same(viewModel.LayoutEditor.SelectedLayoutZones, viewModel.SelectedLayoutZones);
        Assert.Same(viewModel.ProfileList.Profiles, viewModel.Profiles);
        Assert.Same(viewModel.RuntimeLog.Logs, viewModel.Logs);
    }

    [Fact]
    public void SelectedLayoutRefreshesFacadeLayoutCollections()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        var layout = new LayoutRow { Id = "layout", Name = "Layout" };
        viewModel.Layouts.Add(layout);
        viewModel.Zones.Add(new ZoneRow
        {
            LayoutId = "layout",
            Id = "DISPLAY1",
            DisplayName = "Monitor 1",
            WindowsRight = 100,
            WindowsBottom = 100,
            VisualRight = 100,
            VisualBottom = 100,
            IsVisible = true
        });

        viewModel.SelectedLayout = layout;

        var selectedZone = Assert.Single(viewModel.SelectedLayoutZones);
        Assert.Same(selectedZone, viewModel.SelectedZone);
        Assert.True(selectedZone.IsSelected);
        Assert.True(viewModel.HasSelectedZone);
    }

    [Fact]
    public void SelectedZoneUpdatesSelectionStateThroughFacade()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        var first = new ZoneRow { Id = "DISPLAY1" };
        var second = new ZoneRow { Id = "DISPLAY2" };

        viewModel.SelectedZone = first;
        viewModel.SelectedZone = second;

        Assert.False(first.IsSelected);
        Assert.True(second.IsSelected);
        Assert.Same(second, viewModel.LayoutEditor.SelectedZone);
    }

    [Fact]
    public void ConstructorHandlesExistingProfilesWhenFilterIsAttached()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create(new AppConfiguration(
            [],
            [],
            [
                new ExecutionProfile(
                    "profile",
                    "Profile",
                    "Ctrl+Alt+1",
                    null,
                    null,
                    null,
                    500,
                    true)
            ],
            SafetySettings.Default));

        Assert.Single(viewModel.Profiles);
        viewModel.ProfileFilter = "profile";
        Assert.True(viewModel.FilteredProfiles.Cast<object>().Any());
    }

    [Fact]
    public void ChildViewModelPropertyChangesRelayToFacadeBindings()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        viewModel.RuntimeLog.RuntimeStatus = "Changed";
        viewModel.DevicePresets.H2ConnectionStatus = "Online: 192.168.0.11:6000";
        viewModel.LayoutEditor.SelectedZone = new ZoneRow { Id = "DISPLAY1" };

        Assert.Contains(nameof(MainViewModel.RuntimeStatus), changed);
        Assert.Contains(nameof(MainViewModel.H2ConnectionStatus), changed);
        Assert.Contains(nameof(MainViewModel.IsH2Online), changed);
        Assert.Contains(nameof(MainViewModel.SelectedZone), changed);
        Assert.Contains(nameof(MainViewModel.HasSelectedZone), changed);
    }

    [Fact]
    public void HighFrequencyRoutingDiagnosticsUpdateLastEventWithoutVisibleLogEntry()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        var initialLogCount = viewModel.Logs.Count;

        viewModel.AddLog("Portal move: Portal mapped 'DISPLAY1' Right 0-1 to 'DISPLAY3' Left. Target: 3840, 540.");

        Assert.Equal(initialLogCount, viewModel.Logs.Count);
        Assert.Equal("Portal move: Portal mapped 'DISPLAY1' Right 0-1 to 'DISPLAY3' Left. Target: 3840, 540.", viewModel.LastRoutingEvent);
    }

    [Fact]
    public void AddPortalUsesSelectedDraftLayoutZones()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        viewModel.SelectedLayout = new LayoutRow { Id = "draft", Name = "Draft" };
        viewModel.SelectedLayoutZones.Add(new ZoneRow
        {
            LayoutId = "draft",
            Id = "DISPLAY1",
            DisplayName = "Monitor 1",
            WindowsLeft = 0,
            WindowsTop = 0,
            WindowsRight = 100,
            WindowsBottom = 100,
            VisualLeft = 0,
            VisualTop = 0,
            VisualRight = 100,
            VisualBottom = 100,
            IsVisible = true
        });
        viewModel.SelectedLayoutZones.Add(new ZoneRow
        {
            LayoutId = "draft",
            Id = "DISPLAY2",
            DisplayName = "Monitor 2",
            WindowsLeft = 100,
            WindowsTop = 0,
            WindowsRight = 200,
            WindowsBottom = 100,
            VisualLeft = 100,
            VisualTop = 0,
            VisualRight = 200,
            VisualBottom = 100,
            IsVisible = true
        });

        viewModel.AddPortalCommand.Execute(null);

        var portal = Assert.Single(viewModel.SelectedLayoutPortals);
        Assert.Equal("draft", portal.LayoutId);
        Assert.Equal("DISPLAY1", portal.FromZoneId);
        Assert.Equal("DISPLAY2", portal.ToZoneId);
        Assert.Same(portal, viewModel.SelectedPortal);
    }

    [Fact]
    public void SaveLayoutAsNewDoesNotAttachSeparatedZonesBeforeGeneratingPortals()
    {
        using var fixture = new MainViewModelFixture();
        var viewModel = fixture.Create();
        viewModel.SelectedLayout = new LayoutRow { Id = "draft", Name = "Draft" };
        viewModel.SelectedLayoutZones.Add(CreateVisibleZone("draft", "DISPLAY1", 0, 0, 120, 120));
        viewModel.SelectedLayoutZones.Add(CreateVisibleZone("draft", "DISPLAY2", 120, 0, 240, 120));
        viewModel.SelectedLayoutZones.Add(CreateVisibleZone("draft", "DISPLAY3", 280, 0, 400, 120));

        viewModel.SaveLayoutAsNewCommand.Execute(null);

        Assert.DoesNotContain(viewModel.SelectedLayoutPortals, portal =>
            string.Equals(portal.FromZoneId, "DISPLAY3", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(portal.ToZoneId, "DISPLAY3", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, viewModel.SelectedLayoutPortals.Count);
    }

    [Fact]
    public void RefreshDisplaysKeepsSavedDisplayAlias()
    {
        using var fixture = new MainViewModelFixture();
        var topology = new MonitorTopologyStub([CreateMonitorInfo(@"\\.\DISPLAY1", 0, 0, 100, 100)]);
        var configuration = new AppConfiguration(
            [],
            [],
            [],
            SafetySettings.Default,
            DisplayAliases: [new DisplayAlias("DISPLAY1", "Main wall", DateTimeOffset.Parse("2026-05-24T00:00:00Z"))]);
        var viewModel = fixture.Create(configuration, topology);

        topology.Monitors = [CreateMonitorInfo(@"\\.\DISPLAY1", 100, 0, 200, 100)];
        viewModel.RefreshDisplays();

        var alias = Assert.Single(viewModel.DisplayAliases);
        var monitor = Assert.Single(viewModel.Monitors);
        Assert.Equal("Main wall", alias.Alias);
        Assert.True(alias.IsConnected);
        Assert.Equal("Main wall", monitor.DisplayLabel);
    }

    [Fact]
    public void RefreshDisplaysPopulatesMissingLastSeenForSavedAlias()
    {
        using var fixture = new MainViewModelFixture();
        var topology = new MonitorTopologyStub();
        var configuration = new AppConfiguration(
            [],
            [],
            [],
            SafetySettings.Default,
            DisplayAliases: [new DisplayAlias("DISPLAY1", "Main wall", null)]);
        var viewModel = fixture.Create(configuration, topology);

        topology.Monitors = [CreateMonitorInfo(@"\\.\DISPLAY1", 0, 0, 100, 100)];
        viewModel.RefreshDisplays();

        var alias = Assert.Single(viewModel.DisplayAliases);
        Assert.Equal("Main wall", alias.Alias);
        Assert.True(alias.IsConnected);
        Assert.NotNull(alias.LastSeenAtUtc);
    }

    [Fact]
    public void RefreshDisplaysUpdatesLastSeenWhenSavedAliasReconnects()
    {
        using var fixture = new MainViewModelFixture();
        var topology = new MonitorTopologyStub();
        var previousLastSeenAt = DateTimeOffset.Parse("2026-05-24T00:00:00Z");
        var configuration = new AppConfiguration(
            [],
            [],
            [],
            SafetySettings.Default,
            DisplayAliases: [new DisplayAlias("DISPLAY1", "Main wall", previousLastSeenAt)]);
        var viewModel = fixture.Create(configuration, topology);
        var disconnectedAlias = Assert.Single(viewModel.DisplayAliases);
        Assert.False(disconnectedAlias.IsConnected);

        topology.Monitors = [CreateMonitorInfo(@"\\.\DISPLAY1", 0, 0, 100, 100)];
        viewModel.RefreshDisplays();

        var reconnectedAlias = Assert.Single(viewModel.DisplayAliases);
        Assert.True(reconnectedAlias.IsConnected);
        Assert.NotNull(reconnectedAlias.LastSeenAtUtc);
        Assert.True(reconnectedAlias.LastSeenAtUtc > previousLastSeenAt);
    }

    [Fact]
    public void RefreshDisplaysCreatesAliaslessDisplayRowForSaving()
    {
        using var fixture = new MainViewModelFixture();
        var topology = new MonitorTopologyStub();
        var viewModel = fixture.Create(monitorTopologyService: topology);

        topology.Monitors = [CreateMonitorInfo(@"\\.\DISPLAY2", 0, 0, 100, 100)];
        viewModel.RefreshDisplays();

        var row = Assert.Single(viewModel.DisplayAliases);
        Assert.Equal("DISPLAY2", row.DeviceName);
        Assert.Equal("", row.Alias);
        Assert.True(row.IsConnected);
        Assert.NotNull(row.LastSeenAtUtc);

        var configuration = new ConfigurationRowMapper(new MonitorZoneMatcher())
            .BuildConfiguration([], [], [], [], [], [], viewModel.DisplayAliases, viewModel.Monitors, SafetySettings.Default);
        var savedAlias = Assert.Single(configuration.DisplayAliasEntries);
        Assert.Equal("DISPLAY2", savedAlias.DeviceName);
        Assert.Equal("", savedAlias.Alias);
        Assert.Equal(row.LastSeenAtUtc, savedAlias.LastSeenAtUtc);
    }

    [Fact]
    public void CreateLayoutFromMonitorsKeepsZoneIdOriginalAfterAlias()
    {
        using var fixture = new MainViewModelFixture();
        var topology = new MonitorTopologyStub([CreateMonitorInfo(@"\\.\DISPLAY1", 0, 0, 100, 100)]);
        var configuration = new AppConfiguration(
            [],
            [],
            [],
            SafetySettings.Default,
            DisplayAliases: [new DisplayAlias("DISPLAY1", "Main wall", null)]);
        var viewModel = fixture.Create(configuration, topology);

        viewModel.CreateLayoutFromMonitorsCommand.Execute(null);

        var zone = Assert.Single(viewModel.SelectedLayoutZones);
        Assert.Equal("DISPLAY1", zone.Id);
        Assert.Equal("DISPLAY1", zone.DisplayName);
        Assert.Equal("Main wall", zone.DisplayLabel);
    }

    [Fact]
    public void RefreshDisplaysLogsPossibleSavedLayoutRemapWithoutChangingLayout()
    {
        using var fixture = new MainViewModelFixture();
        var layout = new CursorLayout(
            "layout-1",
            "Wall",
            [new CursorZone(
                "DISPLAY6",
                "DISPLAY6",
                new IntRect(0, 0, 100, 100),
                new VisualRect(0, 0, 100, 100),
                IsVisible: true)],
            []);
        var configuration = new AppConfiguration(
            [],
            [layout],
            [],
            SafetySettings.Default);
        var topology = new MonitorTopologyStub([CreateMonitorInfo(@"\\.\DISPLAY8", 0, 0, 100, 100)]);
        var viewModel = fixture.Create(configuration, topology);

        viewModel.RefreshDisplays();

        Assert.Contains(viewModel.Logs, log =>
            log.Contains("Possible saved layout display remap detected", StringComparison.OrdinalIgnoreCase) &&
            log.Contains("saved DISPLAY6 0,0 -> 100,100 now matches DISPLAY8", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("DISPLAY6", Assert.Single(viewModel.Zones).Id);
    }

    private static ZoneRow CreateVisibleZone(string layoutId, string id, int left, int top, int right, int bottom) => new()
    {
        LayoutId = layoutId,
        Id = id,
        DisplayName = id,
        WindowsLeft = left,
        WindowsTop = top,
        WindowsRight = right,
        WindowsBottom = bottom,
        VisualLeft = left,
        VisualTop = top,
        VisualRight = right,
        VisualBottom = bottom,
        IsVisible = true
    };

    private static MonitorInfo CreateMonitorInfo(string deviceName, int left, int top, int right, int bottom) =>
        new(deviceName, new IntRect(left, top, right, bottom), IsPrimary: false);

    private sealed class MainViewModelFixture : IDisposable
    {
        private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"h2-app-tests-{Guid.NewGuid():N}");

        public MainViewModel Create(AppConfiguration? configuration = null, IMonitorTopologyService? monitorTopologyService = null) => new(
            configuration ?? new AppConfiguration([], [], [], SafetySettings.Default),
            Path.Combine(_tempDirectory, "config.json"),
            "app.exe",
            new StartupRegistrationStub(),
            new FileLogService(Path.Combine(_tempDirectory, "logs")),
            new H2DeviceClientStub(),
            new DisplayIdentificationStub(),
            new TextInputDialogStub(),
            new ConfirmationDialogStub(),
            new ProfileDialogStub(),
            new DeviceDialogStub(),
            monitorTopologyService ?? new MonitorTopologyStub(),
            new CursorRoutingRuntimeStub(),
            new CursorRoutingEngine(),
            new AppConfigurationValidator());

        public void Dispose()
        {
            if (Directory.Exists(_tempDirectory))
            {
                DeleteTempDirectoryWithRetry();
            }
        }

        private void DeleteTempDirectoryWithRetry()
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                try
                {
                    Directory.Delete(_tempDirectory, recursive: true);
                    return;
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(50);
                }
            }
        }
    }

    private sealed class StartupRegistrationStub : IStartupRegistrationService
    {
        public bool IsRegistered() => false;
        public void SetRegistered(bool enabled, string executablePath, string arguments)
        {
        }
    }

    private sealed class H2DeviceClientStub : IH2DeviceClient
    {
        public Task<H2CommandResult> LoadPresetAsync(H2DeviceConfig device, int screenId, int presetId, CancellationToken cancellationToken = default) =>
            Task.FromResult(H2CommandResult.Failure("not implemented"));

        public Task<H2CommandResult> GetPresetEnumAsync(H2DeviceConfig device, int param0 = 0, int param1 = 0, CancellationToken cancellationToken = default) =>
            Task.FromResult(H2CommandResult.Failure("not implemented"));
    }

    private sealed class DisplayIdentificationStub : IDisplayIdentificationService
    {
        public Task IdentifyAsync(IReadOnlyList<MonitorRow> monitors, TimeSpan duration) => Task.CompletedTask;
    }

    private sealed class TextInputDialogStub : ITextInputDialogService
    {
        public string? Prompt(string title, string message, string defaultValue) => defaultValue;
    }

    private sealed class ConfirmationDialogStub : IConfirmationDialogService
    {
        public bool Confirm(string title, string message) => true;
    }

    private sealed class ProfileDialogStub : IProfileDialogService
    {
        public ProfileDialogResult? Prompt(
            string title,
            string defaultName,
            string? selectedHotkey,
            IReadOnlyList<LayoutRow> layouts,
            string? selectedLayoutId,
            bool isCursorLayoutOnly,
            IReadOnlyList<DeviceRow> devices,
            IReadOnlyList<PresetRow> presets,
            string? selectedDeviceId,
            int? selectedScreenId,
            int? selectedPresetId,
            string? selectedPresetDisplayName) =>
            null;
    }

    private sealed class DeviceDialogStub : IDeviceDialogService
    {
        public DeviceDialogResult? Prompt(string defaultName, string defaultHost, int defaultPort) => null;
    }

    private sealed class MonitorTopologyStub : IMonitorTopologyService
    {
        public MonitorTopologyStub()
        {
        }

        public MonitorTopologyStub(IReadOnlyList<MonitorInfo> monitors)
        {
            Monitors = monitors;
        }

        public event EventHandler? TopologyChanged;
        public IReadOnlyList<MonitorInfo> Monitors { get; set; } = [];
        public IReadOnlyList<MonitorInfo> GetMonitors() => Monitors;
        public string GetTopologySignature() => "";
        public void StartWatching(TimeSpan interval) => TopologyChanged?.Invoke(this, EventArgs.Empty);
        public void Dispose()
        {
        }
    }

    private sealed class CursorRoutingRuntimeStub : ICursorRoutingRuntime
    {
        public event EventHandler<string>? Log;
        public bool IsRoutingEnabled => false;
        public string? ActiveLayoutId => null;
        public void ActivateLayout(CursorLayout layout, CursorPoint startPosition, TimeSpan pollInterval) =>
            Log?.Invoke(this, $"Activated cursor layout '{layout.Name}'.");

        public void StopRouting(bool clearLayout)
        {
        }

        public void EmergencyUnlock()
        {
        }

        public void Dispose()
        {
        }
    }
}
