using System.IO;
using H2CursorRouter.App.Services;
using H2CursorRouter.Updater;

namespace H2CursorRouter.App.ViewModels;

public sealed class UpdateViewModel : ViewModelBase
{
    private readonly IUpdateClient _client;
    private readonly IUpdateInstaller _installer;
    private readonly Version _currentVersion;
    private readonly string _downloadRoot;
    private readonly Action<bool> _savePreference;
    private ReleaseUpdate? _release;
    private CancellationTokenSource? _operation;
    private bool _busy;
    private bool _preparing;
    private bool _checkOnStartup;
    private string _status = "Check for updates to see whether a new version is available.";
    private int _downloadProgress;

    public UpdateViewModel(IUpdateClient client, IUpdateInstaller installer, Version currentVersion,
        string downloadRoot, bool checkOnStartup, Action<bool> savePreference)
    {
        _client = client;
        _installer = installer;
        _currentVersion = currentVersion;
        _downloadRoot = downloadRoot;
        _checkOnStartup = checkOnStartup;
        _savePreference = savePreference;
        CheckCommand = new AsyncRelayCommand(CheckAsync, () => !_busy);
        InstallCommand = new AsyncRelayCommand(InstallAsync, () => !_busy && _release is not null && _installer.IsInstalled);
        CancelCommand = new RelayCommand(() => _operation?.Cancel(), () => _busy && !_preparing);
    }

    public AsyncRelayCommand CheckCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }
    public RelayCommand CancelCommand { get; }
    public string CurrentVersion => _currentVersion.ToString(3);
    public string LatestVersion => _release?.Version.ToString() ?? "—";
    public string ReleaseNotes => _release?.Notes ?? "";
    public string InstallationNotice => _installer.IsInstalled
        ? "Install Update closes this app after saving settings. Approve the Windows permission prompt; the new version opens with routing disabled."
        : "Portable or development copy: automatic installation is unavailable. Use the installer edition for in-app updates.";
    public bool CanUseApp => !_preparing;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public int DownloadProgress { get => _downloadProgress; private set => SetProperty(ref _downloadProgress, value); }
    public bool CheckOnStartup
    {
        get => _checkOnStartup;
        set
        {
            if (value == _checkOnStartup) return;
            try { _savePreference(value); SetProperty(ref _checkOnStartup, value); }
            catch (Exception exception) { Status = $"Could not save update preference: {exception.Message}"; OnPropertyChanged(); }
        }
    }

    public async Task CheckAsync()
    {
        if (_busy) return;
        SetBusy(true);
        _operation = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        SetRelease(null);
        Status = "Checking for updates…";
        try
        {
            SetRelease(await _client.CheckAsync(_currentVersion, _operation.Token));
            Status = _release is null ? "You are using the latest available version." : $"Version {_release.Version} is available.";
        }
        catch (OperationCanceledException) { Status = "Update check cancelled or timed out. You can keep using the app."; }
        catch (Exception exception) { Status = $"Could not check for updates: {exception.Message}"; }
        finally { _operation.Dispose(); _operation = null; SetBusy(false); }
    }

    public async Task InstallAsync()
    {
        if (_busy || _release is null || !_installer.IsInstalled) return;
        SetBusy(true);
        DownloadProgress = 0;
        _operation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            Status = "Downloading and verifying update…";
            var directory = Path.Combine(_downloadRoot, Guid.NewGuid().ToString("N"));
            var path = await _client.DownloadAsync(_release, directory, new Progress<int>(value => DownloadProgress = value), _operation.Token);
            _operation.Token.ThrowIfCancellationRequested();
            _preparing = true;
            OnPropertyChanged(nameof(CanUseApp));
            CancelCommand.RaiseCanExecuteChanged();
            Status = "Saving settings and preparing installation. The app will close; approve the Windows permission prompt to continue.";
            await _installer.InstallAsync(path, _release);
        }
        catch (OperationCanceledException) { Status = "Update download cancelled or timed out. The installed app was not changed."; }
        catch (Exception exception) { Status = $"Could not install update: {exception.Message}"; }
        finally
        {
            _preparing = false;
            OnPropertyChanged(nameof(CanUseApp));
            _operation.Dispose(); _operation = null;
            SetBusy(false);
        }
    }

    private void SetRelease(ReleaseUpdate? release)
    {
        _release = release;
        OnPropertyChanged(nameof(LatestVersion));
        OnPropertyChanged(nameof(ReleaseNotes));
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CheckCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
    }
}
