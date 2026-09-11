using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;
using System.Windows.Input;
using H2CursorRouter.App;
using H2CursorRouter.App.Services;
using H2CursorRouter.Core.Configuration;
using H2CursorRouter.Core.Domain;
using H2CursorRouter.Core.Geometry;
using H2CursorRouter.Core.Validation;
using H2CursorRouter.H2;
using H2CursorRouter.Windows;

namespace H2CursorRouter.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private const int MaxVisibleLogEntries = 300;
    private readonly string _configPath;
    private readonly string _executablePath;
    private readonly LayoutEditingService _layoutEditingService = new();
    private readonly MonitorZoneMatcher _monitorZoneMatcher = new();
    private readonly ConfigurationCoordinator _configurationCoordinator;
    private readonly IStartupRegistrationService _startupRegistrationService;
    private readonly FileLogService _fileLogService;
    private readonly IH2DeviceClient _h2DeviceClient;
    private readonly IDisplayIdentificationService _displayIdentificationService;
    private readonly ITextInputDialogService _textInputDialogService;
    private readonly IConfirmationDialogService _confirmationDialogService;
    private readonly IMonitorTopologyService _monitorTopologyService;
    private readonly ICursorRoutingRuntime _routingRuntime;
    private readonly ProfileExecutionService _profileExecutionService;
    private readonly SafetySettings _safetySettings;
    private readonly SemaphoreSlim _profileExecutionLock = new(1, 1);
    private readonly SemaphoreSlim _configurationSaveLock = new(1, 1);
    private bool _startWithWindows;
    private CursorPoint? _selectedLayoutDraftStartPosition;
    private string _lastLoggedMonitorSignature = "";
    private MonitorSnapshot[] _lastLoggedMonitorSnapshot = [];
    private bool _suppressDisplayAliasUpdates;
    private bool _isUpdating;

    private UpdateViewModel? _updates;
    public UpdateViewModel? Updates { get => _updates; set => SetProperty(ref _updates, value); }

    public async Task PrepareForUpdateAsync()
    {
        _isUpdating = true;
        await _profileExecutionLock.WaitAsync();
        try
        {
            // Drain in-flight autosaves, then persist the final UI snapshot before exiting.
            await _configurationSaveLock.WaitAsync();
            try
            {
                var configuration = BuildConfiguration();
                var validation = _configurationCoordinator.Validate(configuration);
                ShowValidation(validation);
                if (!validation.IsValid)
                    throw new InvalidOperationException("Fix configuration validation errors before installing an update.");
                await _configurationCoordinator.SaveAsync(configuration, _configPath);
            }
            finally { _configurationSaveLock.Release(); }
            EmergencyUnlock();
        }
        finally { _profileExecutionLock.Release(); }
    }

    public void ResumeAfterUpdateFailure() => _isUpdating = false;

    public MainViewModel(
        AppConfiguration configuration,
        string configPath,
        string executablePath,
        IStartupRegistrationService startupRegistrationService,
        FileLogService fileLogService,
        IH2DeviceClient h2DeviceClient,
        IDisplayIdentificationService displayIdentificationService,
        ITextInputDialogService textInputDialogService,
        IConfirmationDialogService confirmationDialogService,
        IProfileDialogService profileDialogService,
        IDeviceDialogService deviceDialogService,
        IMonitorTopologyService monitorTopologyService,
        ICursorRoutingRuntime routingRuntime,
        CursorRoutingEngine routingEngine,
        AppConfigurationValidator configurationValidator)
    {
        _configPath = configPath;
        _executablePath = executablePath;
        _startupRegistrationService = startupRegistrationService;
        _fileLogService = fileLogService;
        _h2DeviceClient = h2DeviceClient;
        _displayIdentificationService = displayIdentificationService;
        _textInputDialogService = textInputDialogService;
        _confirmationDialogService = confirmationDialogService;
        _monitorTopologyService = monitorTopologyService;
        _routingRuntime = routingRuntime;
        _safetySettings = configuration.Safety;
        var configurationRowMapper = new ConfigurationRowMapper(_monitorZoneMatcher);
        _configurationCoordinator = new ConfigurationCoordinator(configurationRowMapper, configurationValidator);
        _profileExecutionService = new ProfileExecutionService(
            _h2DeviceClient,
            _routingRuntime,
            routingEngine,
            configurationValidator);

        var rows = _configurationCoordinator.ToRows(configuration);
        DisplayAliases = new ObservableCollection<DisplayAliasRow>(rows.DisplayAliases);
        foreach (var alias in DisplayAliases)
        {
            SubscribeDisplayAlias(alias);
        }

        DevicePresets = new DevicePresetViewModel(
            rows.Devices,
            rows.Presets,
            _h2DeviceClient,
            deviceDialogService,
            AddLog,
            AutoSaveConfiguration);
        LayoutEditor = new LayoutEditorViewModel(rows.Layouts, rows.Zones, rows.Portals);
        ProfileList = new ProfileListViewModel(
            rows.Profiles,
            profileDialogService,
            AddLog,
            AutoSaveConfiguration,
            () => HotkeysChanged?.Invoke(this, EventArgs.Empty));
        RuntimeLog = new RuntimeLogViewModel();
        ProfileList.SetFilter(FilterProfile);
        ApplyDisplayAliasesToZones(Zones);
        _startWithWindows = _startupRegistrationService.IsRegistered();

        SelectedDevice = Devices.FirstOrDefault();
        SelectedPreset = Presets.FirstOrDefault();
        SelectedLayout = Layouts.FirstOrDefault();
        SelectedProfile = Profiles.FirstOrDefault();

        AddDeviceCommand = new RelayCommand(AddDevice);
        RemoveDeviceCommand = new RelayCommand(RemoveSelectedDevice, () => SelectedDevice is not null);
        GetAllPresetsCommand = new AsyncRelayCommand(GetAllPresetsAsync, () => Devices.Count > 0);
        GetPresetsCommand = new AsyncRelayCommand(GetPresetsAsync, () => SelectedDevice is not null);
        DeleteLayoutCommand = new RelayCommand<LayoutRow>(DeleteLayout, IsLayoutPersisted);
        AddZoneCommand = new RelayCommand(AddZone, () => SelectedLayout is not null);
        RemoveZoneCommand = new RelayCommand(RemoveSelectedZone, () => SelectedZone is not null);
        AddDisplayToCanvasCommand = new RelayCommand(AddSelectedDisplayToCanvas, () => SelectedAvailableMonitor is not null);
        AddPortalCommand = new RelayCommand(AddPortal, () => SelectedLayout is not null);
        RemovePortalCommand = new RelayCommand(RemoveSelectedPortal, () => SelectedPortal is not null);
        CreateLayoutFromMonitorsCommand = new RelayCommand(CreateLayoutFromMonitors, () => Monitors.Count > 0);
        SaveLayoutAsNewCommand = new RelayCommand(SaveSelectedLayoutAsNew, () => SelectedLayout is not null);
        OverwriteSelectedLayoutCommand = new RelayCommand(OverwriteSelectedLayout, () => SelectedLayout is not null && IsLayoutPersisted(SelectedLayout));
        AddProfileCommand = new RelayCommand(AddProfile);
        RemoveProfileCommand = new RelayCommand(RemoveSelectedProfile, () => SelectedProfile is not null);
        GeneratePortalsCommand = new RelayCommand(GeneratePortalsFromVisualAdjacency, () => SelectedLayout is not null);
        ValidateConfigurationCommand = new RelayCommand(ValidateConfiguration);
        SaveConfigurationCommand = new AsyncRelayCommand(SaveConfigurationAsync);
        ExecuteSelectedProfileCommand = new AsyncRelayCommand(ExecuteSelectedProfileAsync, () => SelectedProfile is not null);
        ExecuteProfileCommand = new AsyncRelayCommand<ProfileRow>(ExecuteProfileFromCommandAsync, profile => profile is not null);
        EmergencyUnlockCommand = new RelayCommand(EmergencyUnlock);
        StopRoutingCommand = new RelayCommand(() => StopRouting(clearLayout: true));
        RefreshDiagnosticsCommand = new RelayCommand(() => RefreshDiagnostics(log: true));
        IdentifyDisplaysCommand = new AsyncRelayCommand(IdentifyDisplaysAsync, () => Monitors.Count > 0);
        DeleteDisplayAliasCommand = new RelayCommand<DisplayAliasRow>(DeleteDisplayAlias, alias => alias is not null);
        SubscribeChildViewModels();
        _routingRuntime.Log += (_, message) => Dispatch(() => AddLog(message));
        _monitorTopologyService.TopologyChanged += OnMonitorTopologyChanged;
        RefreshDiagnostics(log: false);
        RefreshProfileLayoutNames();
        RefreshDashboardProfiles();
        ValidateConfiguration();
        AddLog($"Application started with routing disabled. Config path: {_configPath}");
    }

    public event EventHandler? HotkeysChanged;

    private void SubscribeChildViewModels()
    {
        DevicePresets.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(DevicePresetViewModel.SelectedDevice):
                    OnPropertyChanged(nameof(SelectedDevice));
                    RaiseCommandStates();
                    break;
                case nameof(DevicePresetViewModel.SelectedPreset):
                    OnPropertyChanged(nameof(SelectedPreset));
                    RaiseCommandStates();
                    break;
                case nameof(DevicePresetViewModel.H2ConnectionStatus):
                    OnPropertyChanged(nameof(H2ConnectionStatus));
                    break;
                case nameof(DevicePresetViewModel.IsH2Online):
                    OnPropertyChanged(nameof(IsH2Online));
                    break;
                case nameof(DevicePresetViewModel.IsOnline):
                    OnPropertyChanged(nameof(IsOnline));
                    break;
            }
        };

        LayoutEditor.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(LayoutEditorViewModel.SelectedLayout):
                    OnPropertyChanged(nameof(SelectedLayout));
                    RaiseCommandStates();
                    break;
                case nameof(LayoutEditorViewModel.SelectedZone):
                    OnPropertyChanged(nameof(SelectedZone));
                    OnPropertyChanged(nameof(HasSelectedZone));
                    RaiseCommandStates();
                    break;
                case nameof(LayoutEditorViewModel.SelectedAvailableMonitor):
                    OnPropertyChanged(nameof(SelectedAvailableMonitor));
                    RaiseCommandStates();
                    break;
                case nameof(LayoutEditorViewModel.SelectedPortal):
                    OnPropertyChanged(nameof(SelectedPortal));
                    RaiseCommandStates();
                    break;
                case nameof(LayoutEditorViewModel.LayoutPreviewScale):
                    OnPropertyChanged(nameof(LayoutPreviewScale));
                    break;
                case nameof(LayoutEditorViewModel.DisplayPreviewCanvasWidth):
                    OnPropertyChanged(nameof(DisplayPreviewCanvasWidth));
                    break;
                case nameof(LayoutEditorViewModel.DisplayPreviewCanvasHeight):
                    OnPropertyChanged(nameof(DisplayPreviewCanvasHeight));
                    break;
            }
        };

        ProfileList.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ProfileListViewModel.SelectedProfile):
                    OnPropertyChanged(nameof(SelectedProfile));
                    RaiseCommandStates();
                    break;
                case nameof(ProfileListViewModel.ProfileFilter):
                    OnPropertyChanged(nameof(ProfileFilter));
                    break;
            }
        };

        RuntimeLog.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(RuntimeLogViewModel.RuntimeStatus):
                    OnPropertyChanged(nameof(RuntimeStatus));
                    break;
                case nameof(RuntimeLogViewModel.LastRoutingEvent):
                    OnPropertyChanged(nameof(LastRoutingEvent));
                    break;
            }
        };
    }

    public DevicePresetViewModel DevicePresets { get; }
    public LayoutEditorViewModel LayoutEditor { get; }
    public ProfileListViewModel ProfileList { get; }
    public RuntimeLogViewModel RuntimeLog { get; }

    public ObservableCollection<DisplayAliasRow> DisplayAliases { get; }
    public ObservableCollection<DeviceRow> Devices => DevicePresets.Devices;
    public ObservableCollection<PresetRow> Presets => DevicePresets.Presets;
    public ObservableCollection<LayoutRow> Layouts => LayoutEditor.Layouts;
    public ObservableCollection<ZoneRow> Zones => LayoutEditor.Zones;
    public ObservableCollection<PortalRow> Portals => LayoutEditor.Portals;
    public ObservableCollection<ProfileRow> Profiles => ProfileList.Profiles;
    public ObservableCollection<ProfileRow> DashboardProfiles => ProfileList.DashboardProfiles;
    public ICollectionView FilteredProfiles => ProfileList.FilteredProfiles;
    public ObservableCollection<MonitorRow> Monitors => LayoutEditor.Monitors;
    public ObservableCollection<ZoneRow> SelectedLayoutZones => LayoutEditor.SelectedLayoutZones;
    public ObservableCollection<PortalRow> SelectedLayoutPortals => LayoutEditor.SelectedLayoutPortals;
    public ObservableCollection<MonitorRow> AvailableLayoutDisplays => LayoutEditor.AvailableLayoutDisplays;
    public ObservableCollection<string> Logs => RuntimeLog.Logs;
    public ObservableCollection<string> ValidationErrors => RuntimeLog.ValidationErrors;

    public ICommand AddDeviceCommand { get; }
    public ICommand RemoveDeviceCommand { get; }
    public ICommand GetAllPresetsCommand { get; }
    public ICommand GetPresetsCommand { get; }
    public ICommand DeleteLayoutCommand { get; }
    public ICommand AddZoneCommand { get; }
    public ICommand RemoveZoneCommand { get; }
    public ICommand AddDisplayToCanvasCommand { get; }
    public ICommand AddPortalCommand { get; }
    public ICommand RemovePortalCommand { get; }
    public ICommand CreateLayoutFromMonitorsCommand { get; }
    public ICommand SaveLayoutAsNewCommand { get; }
    public ICommand OverwriteSelectedLayoutCommand { get; }
    public ICommand AddProfileCommand { get; }
    public ICommand RemoveProfileCommand { get; }
    public ICommand GeneratePortalsCommand { get; }
    public ICommand ValidateConfigurationCommand { get; }
    public ICommand SaveConfigurationCommand { get; }
    public ICommand ExecuteSelectedProfileCommand { get; }
    public ICommand ExecuteProfileCommand { get; }
    public ICommand EmergencyUnlockCommand { get; }
    public ICommand StopRoutingCommand { get; }
    public ICommand RefreshDiagnosticsCommand { get; }
    public ICommand IdentifyDisplaysCommand { get; }
    public ICommand DeleteDisplayAliasCommand { get; }
    public string AppVersion { get; } = CreateDisplayVersion();
    public string AppDescription { get; } = "Windows cursor routing and video processor preset control for multi-display workstations.";
    public string AppLicenseSummary { get; } = "Released as open-source software under the MIT License.";
    public string ConfigPath => _configPath;
    public string LogDirectory => _fileLogService.LogDirectory;

    private static string CreateDisplayVersion()
    {
        var informationalVersion = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        var version = string.IsNullOrWhiteSpace(informationalVersion)
            ? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            : informationalVersion;
        return (version ?? "unknown").Split('+', 2)[0];
    }

    public DeviceRow? SelectedDevice
    {
        get => DevicePresets.SelectedDevice;
        set
        {
            if (!ReferenceEquals(DevicePresets.SelectedDevice, value))
            {
                DevicePresets.SelectedDevice = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }
    }

    public PresetRow? SelectedPreset
    {
        get => DevicePresets.SelectedPreset;
        set
        {
            if (!ReferenceEquals(DevicePresets.SelectedPreset, value))
            {
                DevicePresets.SelectedPreset = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }
    }

    public LayoutRow? SelectedLayout
    {
        get => LayoutEditor.SelectedLayout;
        set
        {
            if (!ReferenceEquals(LayoutEditor.SelectedLayout, value))
            {
                LayoutEditor.SelectedLayout = value;
                OnPropertyChanged();
                RefreshSelectedLayoutCollections();
                RaiseCommandStates();
            }
        }
    }

    public ZoneRow? SelectedZone
    {
        get => LayoutEditor.SelectedZone;
        set
        {
            if (ReferenceEquals(LayoutEditor.SelectedZone, value))
            {
                return;
            }

            if (LayoutEditor.SelectedZone is not null)
            {
                LayoutEditor.SelectedZone.IsSelected = false;
            }

            LayoutEditor.SelectedZone = value;
            OnPropertyChanged();
            if (LayoutEditor.SelectedZone is not null)
            {
                LayoutEditor.SelectedZone.IsSelected = true;
            }

            OnPropertyChanged(nameof(HasSelectedZone));
            RaiseCommandStates();
        }
    }

    public bool HasSelectedZone => SelectedZone is not null;

    public MonitorRow? SelectedAvailableMonitor
    {
        get => LayoutEditor.SelectedAvailableMonitor;
        set
        {
            if (!ReferenceEquals(LayoutEditor.SelectedAvailableMonitor, value))
            {
                LayoutEditor.SelectedAvailableMonitor = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }
    }

    public PortalRow? SelectedPortal
    {
        get => LayoutEditor.SelectedPortal;
        set
        {
            if (!ReferenceEquals(LayoutEditor.SelectedPortal, value))
            {
                LayoutEditor.SelectedPortal = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }
    }

    public ProfileRow? SelectedProfile
    {
        get => ProfileList.SelectedProfile;
        set
        {
            if (!ReferenceEquals(ProfileList.SelectedProfile, value))
            {
                ProfileList.SelectedProfile = value;
                OnPropertyChanged();
                RaiseCommandStates();
            }
        }
    }

    public string RuntimeStatus
    {
        get => RuntimeLog.RuntimeStatus;
        private set
        {
            if (!string.Equals(RuntimeLog.RuntimeStatus, value, StringComparison.Ordinal))
            {
                RuntimeLog.RuntimeStatus = value;
                OnPropertyChanged();
            }
        }
    }

    public double DisplayPreviewCanvasWidth
    {
        get => LayoutEditor.DisplayPreviewCanvasWidth;
        private set
        {
            if (!LayoutEditor.DisplayPreviewCanvasWidth.Equals(value))
            {
                LayoutEditor.DisplayPreviewCanvasWidth = value;
                OnPropertyChanged();
            }
        }
    }

    public double DisplayPreviewCanvasHeight
    {
        get => LayoutEditor.DisplayPreviewCanvasHeight;
        private set
        {
            if (!LayoutEditor.DisplayPreviewCanvasHeight.Equals(value))
            {
                LayoutEditor.DisplayPreviewCanvasHeight = value;
                OnPropertyChanged();
            }
        }
    }

    public string ActiveLayoutId => _routingRuntime.ActiveLayoutId ?? "";
    public string ActiveLayoutName
    {
        get
        {
            var activeLayoutId = _routingRuntime.ActiveLayoutId;
            if (string.IsNullOrWhiteSpace(activeLayoutId))
            {
                return "None";
            }

            return Layouts.FirstOrDefault(layout => string.Equals(layout.Id, activeLayoutId, StringComparison.OrdinalIgnoreCase))?.Name
                   ?? activeLayoutId;
        }
    }

    public bool IsRoutingEnabled => _routingRuntime.IsRoutingEnabled;
    public string RoutingStateText => IsRoutingEnabled ? "Enabled" : "Disabled";
    public string H2ConnectionStatus
    {
        get => DevicePresets.H2ConnectionStatus;
        private set
        {
            if (!string.Equals(DevicePresets.H2ConnectionStatus, value, StringComparison.Ordinal))
            {
                DevicePresets.H2ConnectionStatus = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsH2Online));
                OnPropertyChanged(nameof(IsOnline));
            }
        }
    }

    public bool IsH2Online => H2ConnectionStatus.StartsWith("Online:", StringComparison.OrdinalIgnoreCase);
    public bool IsOnline => IsH2Online;

    public string LastRoutingEvent
    {
        get => RuntimeLog.LastRoutingEvent;
        private set
        {
            if (!string.Equals(RuntimeLog.LastRoutingEvent, value, StringComparison.Ordinal))
            {
                RuntimeLog.LastRoutingEvent = value;
                OnPropertyChanged();
            }
        }
    }

    public string ProfileFilter
    {
        get => ProfileList.ProfileFilter;
        set
        {
            if (!string.Equals(ProfileList.ProfileFilter, value, StringComparison.Ordinal))
            {
                ProfileList.ProfileFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public double LayoutPreviewScale
    {
        get => LayoutEditor.LayoutPreviewScale;
        set
        {
            var clamped = Math.Clamp(value, 0.08, 0.5);
            if (!LayoutEditor.LayoutPreviewScale.Equals(clamped))
            {
                LayoutEditor.LayoutPreviewScale = clamped;
                OnPropertyChanged();
            }
        }
    }

    public double LayoutPreviewCanvasWidth =>
        Math.Max(3840, SelectedLayoutZones.Count == 0 ? 3840 : SelectedLayoutZones.Max(zone => zone.VisualRight) + LayoutPreviewGridSize * 2);

    public double LayoutPreviewCanvasHeight =>
        Math.Max(2160, SelectedLayoutZones.Count == 0 ? 2160 : SelectedLayoutZones.Max(zone => zone.VisualBottom) + LayoutPreviewGridSize * 2);

    public double LayoutPreviewGridSize => LayoutEditingService.GridSize;

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                try
                {
                    _startupRegistrationService.SetRegistered(value, _executablePath, "--tray");
                    AddLog(value
                        ? "Registered app to start with Windows in tray mode."
                        : "Removed app from Windows startup.");
                }
                catch (Exception exception)
                {
                    AddLog($"Failed to update Windows startup registration: {exception.Message}");
                    _startWithWindows = _startupRegistrationService.IsRegistered();
                    OnPropertyChanged();
                }
            }
        }
    }

    public async Task ExecuteProfileByIndexAsync(int index)
    {
        if (index < 0 || index >= Profiles.Count)
        {
            return;
        }

        var profile = Profiles[index];
        SelectedProfile = profile;
        await ExecuteProfileAsync(profile);
    }

    public async Task ExecuteSelectedProfileAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }

        await ExecuteProfileAsync(SelectedProfile);
    }

    public async Task RefreshDashboardStatusAsync()
    {
        RefreshDiagnostics(log: false);
        await RefreshH2ConnectionStatusAsync();
    }

    public void RefreshDisplays() => RefreshDiagnostics(log: true);

    private void OnMonitorTopologyChanged(object? sender, EventArgs e) =>
        Dispatch(() =>
        {
            if (_isUpdating) return;
            RefreshDiagnostics(log: false);
            AddLog("Display topology changed. Display list refreshed.");
        });

    private async Task ExecuteProfileFromCommandAsync(ProfileRow? profile)
    {
        if (profile is null)
        {
            return;
        }

        SelectedProfile = profile;
        await ExecuteProfileAsync(profile);
    }

    private async Task ExecuteProfileAsync(ProfileRow profileRow)
    {
        if (_isUpdating) return;
        await _profileExecutionLock.WaitAsync();
        try
        {
            if (_isUpdating) return;
            var configuration = BuildConfiguration();
            var profile = profileRow.ToModel();
            await _profileExecutionService.ExecuteAsync(
                new ProfileExecutionRequest(configuration, profile),
                new ProfileExecutionCallbacks
                {
                    ShowValidation = ShowValidation,
                    SetH2ConnectionStatus = value => H2ConnectionStatus = value,
                    SetDeviceOnline = SetDeviceOnline,
                    SelectLayout = SelectLayout,
                    StopRouting = StopRouting,
                    AddLog = AddLog,
                    RefreshRuntimeState = RefreshRuntimeState
                });
        }
        finally
        {
            _profileExecutionLock.Release();
        }
    }

    public Task GetPresetsAsync() => DevicePresets.GetPresetsAsync();

    public Task GetAllPresetsAsync() => DevicePresets.GetAllPresetsAsync();

    public void EmergencyUnlock()
    {
        _routingRuntime.EmergencyUnlock();
        RefreshRuntimeState();
    }

    public void Shutdown()
    {
        _routingRuntime.EmergencyUnlock();
    }

    public void AddLog(string message)
    {
        if (IsHighFrequencyRoutingDiagnostic(message))
        {
            LastRoutingEvent = message;
            RefreshRuntimeState();
            return;
        }

        Logs.Insert(0, $"[{DateTime.Now:HH:mm:ss}] {message}");
        while (Logs.Count > MaxVisibleLogEntries)
        {
            Logs.RemoveAt(Logs.Count - 1);
        }

        _fileLogService.Append(message);
        RuntimeStatus = message;
        if (IsRoutingEvent(message))
        {
            LastRoutingEvent = message;
        }

        RefreshRuntimeState();
    }

    private void AddDevice() => DevicePresets.AddDevice(CreateUniqueDeviceId);

    private Task RefreshH2ConnectionStatusAsync() => DevicePresets.RefreshH2ConnectionStatusAsync();

    private void RemoveSelectedDevice() => DevicePresets.RemoveSelectedDevice();

    private void DeleteLayout(LayoutRow layout)
    {
        if (!IsLayoutPersisted(layout))
        {
            return;
        }

        var affectedProfiles = Profiles
            .Where(profile => string.Equals(profile.CursorLayoutId, layout.Id, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var h2BackedProfiles = affectedProfiles.Count(ProfileHasH2Preset);
        var layoutOnlyProfiles = affectedProfiles.Length - h2BackedProfiles;
        var profileMessage = affectedProfiles.Length == 0
            ? ""
            : $"{Environment.NewLine}{Environment.NewLine}Profiles using this layout: {affectedProfiles.Length}. H2 preset profiles will keep only the preset. Layout-only profiles removed: {layoutOnlyProfiles}.";

        if (!_confirmationDialogService.Confirm(
                "Delete layout",
                $"Delete layout '{layout.Name}'? This cannot be undone.{profileMessage}"))
        {
            return;
        }

        DeleteLayoutWithoutConfirmation(layout);
    }

    private void DeleteLayoutWithoutConfirmation(LayoutRow layout)
    {
        var layoutId = layout.Id;
        if (string.Equals(ActiveLayoutId, layoutId, StringComparison.OrdinalIgnoreCase))
        {
            StopRouting(clearLayout: true);
        }

        foreach (var zone in Zones.Where(zone => zone.LayoutId == layoutId).ToArray())
        {
            Zones.Remove(zone);
        }

        foreach (var portal in Portals.Where(portal => portal.LayoutId == layoutId).ToArray())
        {
            Portals.Remove(portal);
        }

        foreach (var profile in Profiles.Where(profile => string.Equals(profile.CursorLayoutId, layoutId, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            if (ProfileHasH2Preset(profile))
            {
                profile.CursorLayoutId = null;
                profile.StartX = null;
                profile.StartY = null;
            }
            else
            {
                Profiles.Remove(profile);
            }
        }

        Layouts.Remove(layout);
        SelectedLayout = Layouts.FirstOrDefault();
        FilteredProfiles.Refresh();
        RefreshProfileLayoutNames();
        RefreshDashboardProfiles();
        HotkeysChanged?.Invoke(this, EventArgs.Empty);
        AddLog($"Deleted layout '{layout.Name}'.");
        AutoSaveConfiguration("Auto-saved configuration after deleting layout.");
    }

    private void AddZone()
    {
        if (SelectedLayout is null)
        {
            return;
        }

        var index = Zones.Count(zone => zone.LayoutId == SelectedLayout.Id) + 1;
        var row = new ZoneRow
        {
            LayoutId = SelectedLayout.Id,
            Id = $"ZONE_{index}",
            DisplayName = $"Zone {index}",
            WindowsRight = 1920,
            WindowsBottom = 1080,
            VisualRight = 1920,
            VisualBottom = 1080,
            IsVisible = true
        };
        SelectedLayoutZones.Add(row);
        SelectedZone = row;
        NormalizeSelectedLayoutVisualOrigin();
        RefreshLayoutPreviewCanvasSize();
    }

    private void RemoveSelectedZone()
    {
        if (SelectedZone is null)
        {
            return;
        }

        foreach (var portal in SelectedLayoutPortals.Where(portal =>
                     portal.LayoutId == SelectedZone.LayoutId &&
                     (string.Equals(portal.FromZoneId, SelectedZone.Id, StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(portal.ToZoneId, SelectedZone.Id, StringComparison.OrdinalIgnoreCase))).ToArray())
        {
            SelectedLayoutPortals.Remove(portal);
        }

        SelectedLayoutZones.Remove(SelectedZone);
        SelectedZone = SelectedLayoutZones.FirstOrDefault();
        RefreshAvailableLayoutDisplays();
        RefreshLayoutPreviewCanvasSize();
    }

    private void AddSelectedDisplayToCanvas()
    {
        if (SelectedAvailableMonitor is null)
        {
            return;
        }

        EnsureTemporaryLayoutCanvas();
        if (SelectedLayout is null)
        {
            return;
        }

        var zone = _monitorZoneMatcher.CreateZoneFromMonitor(SelectedLayout.Id, SelectedAvailableMonitor);
        SelectedLayoutZones.Add(zone);
        SelectedZone = zone;
        SelectedAvailableMonitor = null;
        NormalizeSelectedLayoutVisualOrigin();
        RefreshAvailableLayoutDisplays();
        RefreshLayoutPreviewCanvasSize();
        AddLog($"Added display '{zone.DisplayLabel}' to layout canvas.");
    }

    private void EnsureTemporaryLayoutCanvas()
    {
        if (SelectedLayout is not null)
        {
            return;
        }

        SelectedLayout = new LayoutRow
        {
            Id = $"layout-draft-{DateTime.Now:HHmmss}",
            Name = GetNextLayoutName(),
            Displays = "No displays"
        };
    }

    private void AddPortal()
    {
        if (SelectedLayout is null)
        {
            return;
        }

        var zones = SelectedLayoutZones.Where(zone => zone.IsVisible).ToArray();
        if (zones.Length == 0)
        {
            AddLog("Cannot add a portal because the selected layout has no visible zones.");
            return;
        }

        var row = new PortalRow
        {
            LayoutId = SelectedLayout.Id,
            FromZoneId = zones.ElementAtOrDefault(0)?.Id ?? "",
            ToZoneId = zones.ElementAtOrDefault(1)?.Id ?? zones.ElementAtOrDefault(0)?.Id ?? ""
        };
        SelectedLayoutPortals.Add(row);
        SelectedPortal = row;
    }

    private void RemoveSelectedPortal()
    {
        if (SelectedPortal is null)
        {
            return;
        }

        SelectedLayoutPortals.Remove(SelectedPortal);
        SelectedPortal = SelectedLayoutPortals.FirstOrDefault();
    }

    private void CreateLayoutFromMonitors()
    {
        var layout = new LayoutRow
        {
            Id = $"layout-detected-{DateTime.Now:HHmmss}",
            Name = GetNextLayoutName(),
            Displays = "No displays"
        };
        SelectedLayout = layout;

        var ordered = Monitors.OrderBy(monitor => monitor.Left).ThenBy(monitor => monitor.Top).ToArray();
        foreach (var monitor in ordered)
        {
            var zone = _monitorZoneMatcher.CreateZoneFromMonitor(layout.Id, monitor);
            SelectedLayoutZones.Add(zone);
        }

        NormalizeSelectedLayoutVisualOrigin();
        SelectedZone = SelectedLayoutZones.FirstOrDefault();
        RefreshAvailableLayoutDisplays();
        RefreshLayoutPreviewCanvasSize();
        AddLog("Loaded detected Windows displays into a temporary layout canvas. Use Save As New Layout to add it to the layout list.");
    }

    private bool PrepareSelectedLayoutDraft()
    {
        if (SelectedLayout is null)
        {
            return false;
        }

        RefreshSelectedLayoutWindowsCoordinatesFromDetectedDisplays();
        var visibleZones = SelectedLayoutZones.Where(zone => zone.IsVisible).ToArray();
        if (visibleZones.Length == 0)
        {
            AddLog("Cannot apply canvas layout because no visible zones are selected.");
            return false;
        }

        NormalizeSelectedLayoutVisualOrigin();

        var firstVisible = SelectedLayoutZones
            .Where(zone => zone.IsVisible)
            .OrderBy(zone => zone.VisualTop)
            .ThenBy(zone => zone.VisualLeft)
            .First();
        _selectedLayoutDraftStartPosition = new CursorPoint(
            firstVisible.WindowsLeft + (firstVisible.WindowsRight - firstVisible.WindowsLeft) / 2,
            firstVisible.WindowsTop + (firstVisible.WindowsBottom - firstVisible.WindowsTop) / 2);
        GeneratePortalsFromVisualAdjacency();
        RefreshLayoutPreviewCanvasSize();
        return true;
    }

    private void SaveSelectedLayoutAsNew()
    {
        if (SelectedLayout is null)
        {
            return;
        }

        var name = _textInputDialogService.Prompt(
            "Save layout",
            "Enter a name for the new layout.",
            GetNextLayoutName());
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        if (!PrepareSelectedLayoutDraft())
        {
            return;
        }

        var newId = CreateUniqueLayoutId(name);
        var newLayout = new LayoutRow
        {
            Id = newId,
            Name = name.Trim(),
            Description = SelectedLayout.Description,
            DefaultStartX = _selectedLayoutDraftStartPosition?.X,
            DefaultStartY = _selectedLayoutDraftStartPosition?.Y,
            Displays = FormatLayoutDisplaysWithAliases(SelectedLayoutZones)
        };
        Layouts.Add(newLayout);

        foreach (var zone in SelectedLayoutZones)
        {
            var copy = ZoneRow.FromModel(newId, zone.ToModel());
            copy.LayoutId = newId;
            Zones.Add(copy);
        }

        foreach (var portal in SelectedLayoutPortals)
        {
            var copy = PortalRow.FromModel(newId, portal.ToModel());
            copy.LayoutId = newId;
            Portals.Add(copy);
        }

        SelectedLayout = newLayout;
        RefreshProfileLayoutNames();
        AddLog($"Saved new layout '{newLayout.Name}' with generated portals.");
        AutoSaveConfiguration("Auto-saved configuration after saving layout.");
    }

    private void OverwriteSelectedLayout()
    {
        if (SelectedLayout is null)
        {
            return;
        }

        if (!IsLayoutPersisted(SelectedLayout))
        {
            AddLog("Temporary canvas layouts cannot be overwritten. Use Save As New Layout to add it to the layout list.");
            return;
        }

        if (!PrepareSelectedLayoutDraft())
        {
            return;
        }

        foreach (var zone in Zones.Where(zone => string.Equals(zone.LayoutId, SelectedLayout.Id, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            Zones.Remove(zone);
        }

        foreach (var portal in Portals.Where(portal => string.Equals(portal.LayoutId, SelectedLayout.Id, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            Portals.Remove(portal);
        }

        foreach (var zone in SelectedLayoutZones)
        {
            var copy = ZoneRow.FromModel(SelectedLayout.Id, zone.ToModel());
            copy.LayoutId = SelectedLayout.Id;
            Zones.Add(copy);
        }

        foreach (var portal in SelectedLayoutPortals)
        {
            var copy = PortalRow.FromModel(SelectedLayout.Id, portal.ToModel());
            copy.LayoutId = SelectedLayout.Id;
            Portals.Add(copy);
        }

        SelectedLayout.DefaultStartX = _selectedLayoutDraftStartPosition?.X;
        SelectedLayout.DefaultStartY = _selectedLayoutDraftStartPosition?.Y;
        SelectedLayout.Displays = FormatLayoutDisplaysWithAliases(SelectedLayoutZones);
        OnPropertyChanged(nameof(SelectedLayout));
        RefreshProfileLayoutNames();
        AddLog($"Overwrote layout '{SelectedLayout.Name}' with generated portals.");
        AutoSaveConfiguration("Auto-saved configuration after overwriting layout.");
    }

    private void AddProfile()
        => ProfileList.AddProfile(CreateProfileEditContext());

    public void EditProfile(ProfileRow profile)
        => ProfileList.EditProfile(profile, CreateProfileEditContext());

    private void RemoveSelectedProfile()
        => ProfileList.RemoveSelectedProfile();

    private void DeleteDisplayAlias(DisplayAliasRow? alias)
    {
        if (alias is null)
        {
            return;
        }

        if (alias.IsConnected)
        {
            alias.Alias = "";
            return;
        }

        DisplayAliases.Remove(alias);
        ApplyDisplayAliasesToMonitors();
        ApplyDisplayAliasesToZones(Zones);
        ApplyDisplayAliasesToZones(SelectedLayoutZones);
        AutoSaveConfiguration("Auto-saved configuration after removing display alias.");
    }

    private void SubscribeDisplayAlias(DisplayAliasRow alias)
    {
        alias.PropertyChanged += DisplayAliasOnPropertyChanged;
    }

    private void DisplayAliasOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_suppressDisplayAliasUpdates ||
            sender is not DisplayAliasRow ||
            e.PropertyName is not nameof(DisplayAliasRow.Alias))
        {
            return;
        }

        ApplyDisplayAliasesToMonitors();
        ApplyDisplayAliasesToZones(Zones);
        ApplyDisplayAliasesToZones(SelectedLayoutZones);
        AutoSaveConfiguration("Auto-saved configuration after updating display alias.");
    }

    private void GeneratePortalsFromVisualAdjacency()
    {
        if (SelectedLayout is null)
        {
            return;
        }

        SelectedLayoutPortals.Clear();
        var generated = _layoutEditingService.GeneratePortalsFromVisualAdjacency(SelectedLayoutZones);

        foreach (var portal in generated)
        {
            SelectedLayoutPortals.Add(ToPortalRow(SelectedLayout.Id, portal));
        }

        AddLog($"Generated {generated.Count} draft portals for layout '{SelectedLayout.Name}' from visual adjacency.");
    }

    private static PortalRow ToPortalRow(string layoutId, GeneratedPortal portal) => new()
    {
        LayoutId = layoutId,
        FromZoneId = portal.FromZoneId,
        FromEdge = portal.FromEdge,
        FromStartRatio = portal.FromStartRatio,
        FromEndRatio = portal.FromEndRatio,
        ToZoneId = portal.ToZoneId,
        ToEdge = portal.ToEdge,
        ToStartRatio = portal.ToStartRatio,
        ToEndRatio = portal.ToEndRatio
    };

    private void ValidateConfiguration()
    {
        var validation = _configurationCoordinator.Validate(BuildConfiguration());
        ShowValidation(validation);
        if (validation.IsValid)
        {
            RuntimeStatus = "Configuration validation passed.";
            return;
        }

        AddLog($"Configuration validation failed with {validation.Errors.Count} issue(s).");
    }

    private async Task SaveConfigurationAsync()
    {
        await SaveConfigurationCoreAsync($"Saved configuration to {_configPath}. Hotkeys were refreshed.");
    }

    private void AutoSaveConfiguration(string successMessage)
    {
        _ = SaveConfigurationCoreAsync(null);
    }

    private async Task SaveConfigurationCoreAsync(string? successMessage)
    {
        if (_isUpdating) return;
        try
        {
            var configuration = BuildConfiguration();
            var validation = _configurationCoordinator.Validate(configuration);
            ShowValidation(validation);
            if (!validation.IsValid)
            {
                AddLog("Configuration save was skipped because validation failed.");
                return;
            }

            await _configurationSaveLock.WaitAsync();
            try
            {
                await _configurationCoordinator.SaveAsync(configuration, _configPath);
            }
            finally
            {
                _configurationSaveLock.Release();
            }

            HotkeysChanged?.Invoke(this, EventArgs.Empty);
            if (!string.IsNullOrWhiteSpace(successMessage))
            {
                AddLog(successMessage);
            }
        }
        catch (Exception exception)
        {
            AddLog($"Configuration save failed: {exception.Message}");
        }
    }

    private void StopRouting(bool clearLayout)
    {
        var wasRoutingEnabled = _routingRuntime.IsRoutingEnabled;
        _routingRuntime.StopRouting(clearLayout);
        if (wasRoutingEnabled)
        {
            AddLog("Routing stopped and cursor clipping released.");
        }

        RefreshRuntimeState();
    }

    private void SetDeviceOnline(string deviceId, bool isOnline)
        => DevicePresets.SetDeviceOnline(deviceId, isOnline);

    private void SelectLayout(string layoutId)
    {
        SelectedLayout = Layouts.FirstOrDefault(row => string.Equals(row.Id, layoutId, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshDiagnostics(bool log)
    {
        Monitors.Clear();
        var monitors = _monitorTopologyService.GetMonitors();
        foreach (var monitor in monitors)
        {
            Monitors.Add(MonitorRow.FromModel(monitor));
        }

        var displayAliasMetadataChanged = MergeDisplayAliasesWithDetectedMonitors();
        ApplyDisplayAliasesToMonitors();
        ApplyDisplayAliasesToZones(Zones);
        ApplyDisplayAliasesToZones(SelectedLayoutZones);
        RefreshDisplayPreview();
        if (log)
        {
            var monitorSignature = BuildMonitorSignature();
            if (!string.Equals(monitorSignature, _lastLoggedMonitorSignature, StringComparison.Ordinal))
            {
                var currentSnapshot = BuildMonitorSnapshot();
                _lastLoggedMonitorSignature = monitorSignature;
                AddLog($"Detected {Monitors.Count} active display(s): {monitorSignature}");
                foreach (var message in BuildPotentialDisplayRemapMessages(currentSnapshot))
                {
                    AddLog(message);
                }

                _lastLoggedMonitorSnapshot = currentSnapshot;
            }
            else
            {
                RuntimeStatus = $"Display list refreshed ({Monitors.Count} active display(s)).";
            }
        }
        RefreshAvailableLayoutDisplays();
        RaiseCommandStates();
        if (displayAliasMetadataChanged)
        {
            AutoSaveConfiguration("Auto-saved configuration after refreshing display aliases.");
        }
    }

    private async Task IdentifyDisplaysAsync()
    {
        RefreshDiagnostics(log: true);
        await _displayIdentificationService.IdentifyAsync(Monitors.ToArray(), TimeSpan.FromSeconds(3));
        AddLog("Displayed monitor identification overlays.");
    }

    private bool MergeDisplayAliasesWithDetectedMonitors()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var connectedKeys = Monitors
            .Select(monitor => MonitorZoneMatcher.NormalizeZoneId(monitor.DeviceName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var aliasByKey = DisplayAliases
            .GroupBy(alias => MonitorZoneMatcher.NormalizeZoneId(alias.DeviceName), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        _suppressDisplayAliasUpdates = true;
        try
        {
            foreach (var alias in DisplayAliases)
            {
                var isConnected = connectedKeys.Contains(MonitorZoneMatcher.NormalizeZoneId(alias.DeviceName));
                if (alias.IsConnected != isConnected)
                {
                    alias.IsConnected = isConnected;
                    changed = true;
                    if (isConnected)
                    {
                        alias.LastSeenAtUtc = now;
                    }
                }
            }

            foreach (var monitor in Monitors.OrderBy(monitor => monitor.Left).ThenBy(monitor => monitor.Top))
            {
                var key = MonitorZoneMatcher.NormalizeZoneId(monitor.DeviceName);
                if (!aliasByKey.TryGetValue(key, out var alias))
                {
                    alias = new DisplayAliasRow
                    {
                        DeviceName = monitor.DeviceName
                    };
                    SubscribeDisplayAlias(alias);
                    DisplayAliases.Add(alias);
                    aliasByKey[key] = alias;
                    changed = true;
                }

                if (!string.Equals(alias.DeviceName, monitor.DeviceName, StringComparison.Ordinal))
                {
                    alias.DeviceName = monitor.DeviceName;
                    changed = true;
                }

                if (!alias.IsConnected)
                {
                    alias.IsConnected = true;
                    changed = true;
                    alias.LastSeenAtUtc = now;
                }

                if (alias.LastSeenAtUtc is null)
                {
                    alias.LastSeenAtUtc = now;
                    changed = true;
                }
            }

            var ordered = DisplayAliases
                .OrderByDescending(alias => alias.IsConnected)
                .ThenBy(alias => alias.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var currentIndex = DisplayAliases.IndexOf(ordered[index]);
                if (currentIndex != index)
                {
                    DisplayAliases.Move(currentIndex, index);
                }
            }
        }
        finally
        {
            _suppressDisplayAliasUpdates = false;
        }

        return changed;
    }

    private void ApplyDisplayAliasesToMonitors()
    {
        foreach (var monitor in Monitors)
        {
            monitor.DisplayAlias = FindDisplayAlias(monitor.DeviceName);
        }
    }

    private void ApplyDisplayAliasesToZones(IEnumerable<ZoneRow> zones)
    {
        foreach (var zone in zones)
        {
            zone.DisplayAlias = FindDisplayAlias(zone.Id);
        }

        foreach (var layout in Layouts)
        {
            var layoutZones = Zones.Where(zone => string.Equals(zone.LayoutId, layout.Id, StringComparison.OrdinalIgnoreCase));
            layout.Displays = FormatLayoutDisplaysWithAliases(layoutZones);
        }
    }

    private string FindDisplayAlias(string deviceName)
    {
        var key = MonitorZoneMatcher.NormalizeZoneId(deviceName);
        return DisplayAliases.LastOrDefault(alias =>
            string.Equals(MonitorZoneMatcher.NormalizeZoneId(alias.DeviceName), key, StringComparison.OrdinalIgnoreCase))?.Alias.Trim() ?? "";
    }

    private string FormatLayoutDisplaysWithAliases(IEnumerable<ZoneRow> zones)
    {
        var labels = zones
            .Where(zone => zone.IsVisible)
            .Select(zone => string.IsNullOrWhiteSpace(zone.DisplayAlias)
                ? DisplayLabelFormatter.Format(zone.DisplayName, zone.Id)
                : zone.DisplayAlias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return labels.Length == 0 ? "No displays" : string.Join(", ", labels);
    }

    private void RefreshDisplayPreview()
    {
        if (Monitors.Count == 0)
        {
            DisplayPreviewCanvasWidth = 640;
            DisplayPreviewCanvasHeight = 320;
            return;
        }

        var minLeft = Monitors.Min(monitor => monitor.Left);
        var minTop = Monitors.Min(monitor => monitor.Top);
        var maxRight = Monitors.Max(monitor => monitor.Right);
        var maxBottom = Monitors.Max(monitor => monitor.Bottom);
        var virtualWidth = Math.Max(1, maxRight - minLeft);
        var virtualHeight = Math.Max(1, maxBottom - minTop);
        const double maxPreviewWidth = 1120;
        const double maxPreviewHeight = 360;
        const double padding = 20;
        var scale = Math.Min(maxPreviewWidth / virtualWidth, maxPreviewHeight / virtualHeight);

        foreach (var monitor in Monitors)
        {
            monitor.PreviewLeft = (monitor.Left - minLeft) * scale + padding;
            monitor.PreviewTop = (monitor.Top - minTop) * scale + padding;
            monitor.PreviewWidth = Math.Max(90, monitor.Width * scale);
            monitor.PreviewHeight = Math.Max(70, monitor.Height * scale);
        }

        DisplayPreviewCanvasWidth = virtualWidth * scale + padding * 2;
        DisplayPreviewCanvasHeight = virtualHeight * scale + padding * 2;
    }

    private AppConfiguration BuildConfiguration() =>
        _configurationCoordinator.BuildConfiguration(
            Devices,
            Layouts,
            Zones,
            Portals,
            Profiles,
            Presets,
            DisplayAliases,
            Monitors,
            _safetySettings);

    private ProfileEditContext CreateProfileEditContext() => new(
        Layouts.ToArray(),
        SelectedLayout is not null && IsLayoutPersisted(SelectedLayout) ? SelectedLayout.Id : null,
        Devices.ToArray(),
        Presets.ToArray(),
        GetNextProfileName,
        CreateUniqueProfileId,
        CalculateLayoutStart,
        RefreshProfileLayoutNames);

    private bool IsLayoutPersisted(LayoutRow? layout) =>
        layout is not null && Layouts.Any(existing => ReferenceEquals(existing, layout));

    private static bool ProfileHasH2Preset(ProfileRow profile) =>
        !string.IsNullOrWhiteSpace(profile.DeviceId) &&
        profile.ScreenId is not null &&
        profile.PresetId is not null;

    private void ShowValidation(ValidationResult validation)
    {
        ValidationErrors.Clear();
        foreach (var error in validation.Errors)
        {
            ValidationErrors.Add(error);
        }

        if (validation.IsValid)
        {
            ValidationErrors.Add("Configuration is valid.");
        }
    }

    private void RefreshRuntimeState()
    {
        OnPropertyChanged(nameof(ActiveLayoutId));
        OnPropertyChanged(nameof(ActiveLayoutName));
        OnPropertyChanged(nameof(IsRoutingEnabled));
        OnPropertyChanged(nameof(RoutingStateText));
    }

    private void LoadConfigurationIntoRows(AppConfiguration configuration)
    {
        var rows = _configurationCoordinator.ToRows(configuration);

        Devices.Clear();
        foreach (var device in rows.Devices)
        {
            Devices.Add(device);
        }

        Layouts.Clear();
        foreach (var layout in rows.Layouts)
        {
            Layouts.Add(layout);
        }

        Zones.Clear();
        foreach (var zone in rows.Zones)
        {
            Zones.Add(zone);
        }

        Portals.Clear();
        foreach (var portal in rows.Portals)
        {
            Portals.Add(portal);
        }

        Profiles.Clear();
        foreach (var profile in rows.Profiles)
        {
            Profiles.Add(profile);
        }

        Presets.Clear();
        foreach (var preset in rows.Presets)
        {
            Presets.Add(preset);
        }

        DisplayAliases.Clear();
        foreach (var alias in rows.DisplayAliases)
        {
            SubscribeDisplayAlias(alias);
            DisplayAliases.Add(alias);
        }

        MergeDisplayAliasesWithDetectedMonitors();
        ApplyDisplayAliasesToMonitors();
        ApplyDisplayAliasesToZones(Zones);
        FilteredProfiles.Refresh();
        RefreshProfileLayoutNames();
        RefreshDashboardProfiles();
        SelectedDevice = Devices.FirstOrDefault();
        DevicePresets.RefreshSelectedPreset(SelectedDevice?.Id);
        SelectedLayout = Layouts.FirstOrDefault();
        SelectedProfile = Profiles.FirstOrDefault();
    }

    private void RefreshDashboardProfiles()
        => ProfileList.RefreshDashboardProfiles();

    private void RefreshProfileLayoutNames()
    {
        var layoutNames = Layouts.ToDictionary(
            layout => layout.Id,
            layout => layout.Name,
            StringComparer.OrdinalIgnoreCase);
        foreach (var profile in Profiles)
        {
            profile.CursorLayoutName = !string.IsNullOrWhiteSpace(profile.CursorLayoutId) &&
                                       layoutNames.TryGetValue(profile.CursorLayoutId, out var layoutName)
                ? layoutName
                : null;
        }
    }

    public void MoveZoneVisual(ZoneRow zone, double deltaX, double deltaY)
    {
        _layoutEditingService.MoveZoneVisual(zone, SelectedLayoutZones, deltaX, deltaY);
        SelectedZone = zone;
        RefreshLayoutPreviewCanvasSize();
    }

    public void ResizeZoneVisual(ZoneRow zone, double deltaWidth, double deltaHeight)
    {
        _layoutEditingService.ResizeZoneVisual(zone, SelectedLayoutZones, deltaWidth, deltaHeight);
        SelectedZone = zone;
        RefreshLayoutPreviewCanvasSize();
    }

    public void CompleteZoneVisualEdit(ZoneRow zone)
    {
        _layoutEditingService.AttachZoneToNearest(zone, SelectedLayoutZones);
        NormalizeSelectedLayoutVisualOrigin();
        RefreshLayoutPreviewCanvasSize();
    }

    private void NormalizeSelectedLayoutVisualOrigin()
    {
        _layoutEditingService.NormalizeVisualOrigin(SelectedLayoutZones);
    }

    private void RaiseCommandStates()
    {
        foreach (var command in new ICommand[]
        {
            RemoveDeviceCommand,
            GetAllPresetsCommand,
            GetPresetsCommand,
            DeleteLayoutCommand,
            AddZoneCommand,
            RemoveZoneCommand,
            AddDisplayToCanvasCommand,
            AddPortalCommand,
            RemovePortalCommand,
            CreateLayoutFromMonitorsCommand,
            RemoveProfileCommand,
            GeneratePortalsCommand,
            SaveLayoutAsNewCommand,
            OverwriteSelectedLayoutCommand,
            ExecuteSelectedProfileCommand,
            ExecuteProfileCommand,
            IdentifyDisplaysCommand
        })
        {
            switch (command)
            {
                case RelayCommand relay:
                    relay.RaiseCanExecuteChanged();
                    break;
                case RelayCommand<LayoutRow> layoutRelay:
                    layoutRelay.RaiseCanExecuteChanged();
                    break;
                case AsyncRelayCommand async:
                    async.RaiseCanExecuteChanged();
                    break;
                case AsyncRelayCommand<ProfileRow> asyncProfile:
                    asyncProfile.RaiseCanExecuteChanged();
                    break;
            }
        }
    }

    private bool FilterProfile(object item)
    {
        if (item is not ProfileRow profile || string.IsNullOrWhiteSpace(ProfileFilter))
        {
            return true;
        }

        return Contains(profile.Id, ProfileFilter) ||
               Contains(profile.Name, ProfileFilter) ||
               Contains(profile.Hotkey, ProfileFilter) ||
               Contains(profile.DeviceId, ProfileFilter) ||
               Contains(profile.CursorLayoutId, ProfileFilter) ||
               Contains(profile.LayoutSummary, ProfileFilter) ||
               Contains(profile.PresetDisplayName, ProfileFilter);
    }

    private void RefreshSelectedLayoutCollections()
    {
        SelectedZone = null;
        _selectedLayoutDraftStartPosition = null;
        SelectedLayoutZones.Clear();
        SelectedLayoutPortals.Clear();
        if (SelectedLayout is null)
        {
            RefreshAvailableLayoutDisplays();
            return;
        }

        foreach (var zone in Zones.Where(zone => string.Equals(zone.LayoutId, SelectedLayout.Id, StringComparison.OrdinalIgnoreCase)))
        {
            var copy = ZoneRow.FromModel(SelectedLayout.Id, zone.ToModel());
            copy.LayoutId = SelectedLayout.Id;
            SelectedLayoutZones.Add(copy);
        }

        foreach (var portal in Portals.Where(portal => string.Equals(portal.LayoutId, SelectedLayout.Id, StringComparison.OrdinalIgnoreCase)))
        {
            var copy = PortalRow.FromModel(SelectedLayout.Id, portal.ToModel());
            copy.LayoutId = SelectedLayout.Id;
            SelectedLayoutPortals.Add(copy);
        }

        ApplyDisplayAliasesToZones(SelectedLayoutZones);
        SelectedZone = SelectedLayoutZones.FirstOrDefault();
        RefreshAvailableLayoutDisplays();
        RefreshLayoutPreviewCanvasSize();
    }

    private void RefreshAvailableLayoutDisplays()
    {
        var selectedIds = SelectedLayoutZones
            .Select(zone => MonitorZoneMatcher.NormalizeZoneId(zone.Id))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        AvailableLayoutDisplays.Clear();
        foreach (var monitor in Monitors.Where(monitor => !selectedIds.Contains(MonitorZoneMatcher.NormalizeZoneId(monitor.DeviceName))))
        {
            AvailableLayoutDisplays.Add(monitor);
        }

        if (SelectedAvailableMonitor is null ||
            !AvailableLayoutDisplays.Contains(SelectedAvailableMonitor))
        {
            SelectedAvailableMonitor = AvailableLayoutDisplays.FirstOrDefault();
        }
    }

    private string GetNextLayoutName()
    {
        var existing = Layouts
            .Select(layout => layout.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var name = $"layout{i}";
            if (!existing.Contains(name))
            {
                return name;
            }
        }
    }

    private string CreateUniqueLayoutId(string name)
    {
        var baseId = MonitorZoneMatcher.NormalizeZoneId(name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "layout";
        }

        var id = baseId.StartsWith("layout-", StringComparison.OrdinalIgnoreCase)
            ? baseId
            : $"layout-{baseId}";
        var existing = Layouts.Select(layout => layout.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(id))
        {
            return id;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{id}-{i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string GetNextProfileName()
    {
        var existing = Profiles
            .Select(profile => profile.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; ; i++)
        {
            var name = $"profile{i}";
            if (!existing.Contains(name))
            {
                return name;
            }
        }
    }

    private string CreateUniqueProfileId(string name)
    {
        var baseId = MonitorZoneMatcher.NormalizeZoneId(name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "profile";
        }

        var id = baseId.StartsWith("profile-", StringComparison.OrdinalIgnoreCase)
            ? baseId
            : $"profile-{baseId}";
        var existing = Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(id))
        {
            return id;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{id}-{i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private string CreateUniqueDeviceId(string name)
    {
        var baseId = MonitorZoneMatcher.NormalizeZoneId(name).ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(baseId))
        {
            baseId = "device";
        }

        var id = baseId.StartsWith("h2-", StringComparison.OrdinalIgnoreCase)
            ? baseId
            : $"h2-{baseId}";
        var existing = Devices.Select(device => device.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(id))
        {
            return id;
        }

        for (var i = 2; ; i++)
        {
            var candidate = $"{id}-{i}";
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private CursorPoint? CalculateLayoutStart(string? layoutId)
    {
        if (string.IsNullOrWhiteSpace(layoutId))
        {
            return null;
        }

        var visibleZone = Zones
            .Where(zone => string.Equals(zone.LayoutId, layoutId, StringComparison.OrdinalIgnoreCase) && zone.IsVisible)
            .OrderBy(zone => zone.WindowsTop)
            .ThenBy(zone => zone.WindowsLeft)
            .FirstOrDefault();
        return visibleZone is null
            ? null
            : new CursorPoint(
                visibleZone.WindowsLeft + (visibleZone.WindowsRight - visibleZone.WindowsLeft) / 2,
                visibleZone.WindowsTop + (visibleZone.WindowsBottom - visibleZone.WindowsTop) / 2);
    }

    private void RefreshLayoutPreviewCanvasSize()
    {
        OnPropertyChanged(nameof(LayoutPreviewCanvasWidth));
        OnPropertyChanged(nameof(LayoutPreviewCanvasHeight));
    }

    private int RefreshSelectedLayoutWindowsCoordinatesFromDetectedDisplays() =>
        _monitorZoneMatcher.RefreshWindowsCoordinatesFromDetectedDisplays(SelectedLayoutZones, Monitors);

    private static bool Contains(string? value, string filter) =>
        value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsRoutingEvent(string message) =>
        message.Contains("Portal mapped", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Target:", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Cursor entered hidden zone", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("outside every known zone", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Activated cursor layout", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Activating cursor layout", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Routing started", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Routing did not start", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Routing stopped", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Emergency unlock", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Monitor topology changed", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Cursor clipped", StringComparison.OrdinalIgnoreCase);

    private static bool IsHighFrequencyRoutingDiagnostic(string message) =>
        message.StartsWith("Portal move:", StringComparison.OrdinalIgnoreCase) ||
        message.StartsWith("Cursor revert:", StringComparison.OrdinalIgnoreCase);

    private string BuildMonitorSignature() =>
        Monitors.Count == 0
            ? "none"
            : string.Join("; ", Monitors.Select(FormatMonitorForDiagnostics));

    private MonitorSnapshot[] BuildMonitorSnapshot() =>
        Monitors
            .Select(monitor => new MonitorSnapshot(
                monitor.DeviceName,
                monitor.Left,
                monitor.Top,
                monitor.Right,
                monitor.Bottom))
            .ToArray();

    private IEnumerable<string> BuildPotentialDisplayRemapMessages(MonitorSnapshot[] currentSnapshot)
    {
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var previous in _lastLoggedMonitorSnapshot)
        {
            var current = currentSnapshot.FirstOrDefault(monitor =>
                monitor.Left == previous.Left &&
                monitor.Top == previous.Top &&
                monitor.Right == previous.Right &&
                monitor.Bottom == previous.Bottom);
            if (current is null ||
                string.Equals(current.DeviceName, previous.DeviceName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = $"runtime|{previous.BoundsText}|{previous.DeviceName}|{current.DeviceName}";
            if (emitted.Add(key))
            {
                yield return
                    $"Possible display remap detected since previous refresh: {previous.DeviceName} {previous.BoundsText} now appears as {current.DeviceName}. No layout changes were applied.";
            }
        }

        foreach (var zone in Zones.Where(zone => zone.IsVisible))
        {
            var current = currentSnapshot.FirstOrDefault(monitor =>
                monitor.Left == zone.WindowsLeft &&
                monitor.Top == zone.WindowsTop &&
                monitor.Right == zone.WindowsRight &&
                monitor.Bottom == zone.WindowsBottom);
            if (current is null ||
                string.Equals(
                    MonitorZoneMatcher.NormalizeZoneId(current.DeviceName),
                    MonitorZoneMatcher.NormalizeZoneId(zone.Id),
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var boundsText = $"{zone.WindowsLeft},{zone.WindowsTop} -> {zone.WindowsRight},{zone.WindowsBottom}";
            var key = $"saved|{boundsText}|{zone.Id}|{current.DeviceName}";
            if (emitted.Add(key))
            {
                yield return
                    $"Possible saved layout display remap detected: saved {zone.Id} {boundsText} now matches {current.DeviceName}. No layout changes were applied.";
            }
        }
    }

    private static string FormatMonitorForDiagnostics(MonitorRow monitor)
    {
        var identity = monitor.Identity;
        if (identity is null)
        {
            return $"{monitor.DeviceName} {monitor.BoundsText} diag=unavailable";
        }

        var friendlyName = string.IsNullOrWhiteSpace(identity.MonitorFriendlyName) ? "unknown" : identity.MonitorFriendlyName;
        var devicePath = string.IsNullOrWhiteSpace(identity.MonitorDevicePath) ? "unknown" : identity.MonitorDevicePath;
        return
            $"{monitor.DeviceName} {monitor.BoundsText} " +
            $"diag=source={identity.SourceAdapterId}/{identity.SourceId}, " +
            $"target={identity.TargetAdapterId}/{identity.TargetId}, " +
            $"output={identity.OutputTechnology}, " +
            $"connector={identity.ConnectorInstance}, " +
            $"edid={identity.EdidManufactureId:X4}:{identity.EdidProductCodeId:X4}, " +
            $"name=\"{friendlyName}\", " +
            $"path=\"{devicePath}\"";
    }

    private sealed record MonitorSnapshot(string DeviceName, int Left, int Top, int Right, int Bottom)
    {
        public string BoundsText => $"{Left},{Top} -> {Right},{Bottom}";
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
