using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using H2CursorRouter.App.ViewModels;
using H2CursorRouter.Core.Configuration;
using H2CursorRouter.Core.Geometry;
using H2CursorRouter.Core.Validation;
using H2CursorRouter.H2;
using H2CursorRouter.Windows;

namespace H2CursorRouter.App;

public partial class App : System.Windows.Application
{
    private const string AppDataFolderName = "vp-cursor-portal";

    private CursorRoutingRuntime? _runtime;
    private IMonitorTopologyService? _monitorTopology;
    private IHotkeyService? _hotkeyService;
    private SingleInstanceService? _singleInstance;
    private bool _activationRequested;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        _singleInstance = new SingleInstanceService(SingleInstanceService.GetInstanceName());
        if (!_singleInstance.IsPrimaryInstance)
        {
            var notified = await _singleInstance.NotifyPrimaryAsync(TimeSpan.FromSeconds(5));
            if (!notified)
            {
                System.Windows.MessageBox.Show(
                    "vp-cursor-portal is already running. Open it from the system tray.",
                    "vp-cursor-portal",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            Shutdown();
            return;
        }

        _singleInstance.StartListening(() =>
        {
            _ = Dispatcher.BeginInvoke(new Action(RestoreMainWindow));
        });

        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;

        var cursorService = new Win32CursorService();
        _monitorTopology = new Win32MonitorTopologyService();
        _monitorTopology.StartWatching(TimeSpan.FromSeconds(2));
        _hotkeyService = new Win32HotkeyService();
        _runtime = new CursorRoutingRuntime(cursorService, _monitorTopology, new CursorRoutingEngine());

        var (configuration, configPath, loadWarning) = await LoadConfigurationAsync();
        var executablePath = Environment.ProcessPath ?? Assembly.GetExecutingAssembly().Location;
        var fileLogService = new FileLogService(Path.Combine(GetUserDataDirectory(), "logs"));
        var viewModel = new MainViewModel(
            configuration,
            configPath,
            executablePath,
            new WindowsStartupRegistrationService(),
            fileLogService,
            new H2DeviceClient(),
            new DisplayIdentificationService(),
            new TextInputDialogService(),
            new ConfirmationDialogService(),
            new ProfileDialogService(),
            new DeviceDialogService(),
            _monitorTopology,
            _runtime,
            new CursorRoutingEngine(),
            new AppConfigurationValidator());

        var startInTray = e.Args.Any(arg => string.Equals(arg, "--tray", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow(viewModel, _hotkeyService, startInTray);
        if (!string.IsNullOrWhiteSpace(loadWarning))
        {
            viewModel.AddLog(loadWarning);
            System.Windows.MessageBox.Show(
                loadWarning,
                "Configuration recovery",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        window.Show();
        if (_activationRequested)
        {
            window.ShowFromTray();
        }
    }

    private void RestoreMainWindow()
    {
        _activationRequested = true;
        if (MainWindow is MainWindow window)
        {
            window.ShowFromTray();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _runtime?.Dispose();
            _hotkeyService?.Dispose();
            _monitorTopology?.Dispose();
        }
        finally
        {
            // Keep ownership until cursor and hotkey cleanup has finished.
            _singleInstance?.Dispose();
            base.OnExit(e);
        }
    }

    private static async Task<(AppConfiguration Configuration, string ConfigPath, string? Warning)> LoadConfigurationAsync()
    {
        var configService = new ConfigFileService();
        var baseDirectory = AppContext.BaseDirectory;
        var userDataDirectory = GetUserDataDirectory();
        var configPath = Path.Combine(userDataDirectory, "config.json");
        var sampleConfigPath = Path.Combine(baseDirectory, "config.sample.json");

        if (File.Exists(configPath))
        {
            try
            {
                return (await configService.LoadAsync(configPath), configPath, null);
            }
            catch (Exception exception)
            {
                var backupPath = MoveInvalidConfigAside(configPath);
                var warning =
                    $"Failed to load user config.json. Invalid config backup: {backupPath}. Error: {exception.Message}";
                var fallback = await LoadFallbackConfigurationAsync(configService, sampleConfigPath, configPath, warning);
                return fallback;
            }
        }

        return await LoadFallbackConfigurationAsync(configService, sampleConfigPath, configPath, null);
    }

    private static async Task<(AppConfiguration Configuration, string ConfigPath, string? Warning)> LoadFallbackConfigurationAsync(
        ConfigFileService configService,
        string sampleConfigPath,
        string configPath,
        string? priorWarning)
    {
        static string JoinWarning(string? priorWarning, string next) =>
            string.IsNullOrWhiteSpace(priorWarning) ? next : $"{priorWarning} {next}";
        static string? AddFallbackMessage(string? priorWarning, string next) =>
            string.IsNullOrWhiteSpace(priorWarning) ? null : $"{priorWarning} {next}";

        if (File.Exists(sampleConfigPath))
        {
            try
            {
                return (
                    await configService.LoadAsync(sampleConfigPath),
                    configPath,
                    AddFallbackMessage(priorWarning, "Loaded bundled empty configuration instead."));
            }
            catch (Exception exception)
            {
                return (
                    SampleConfiguration.Create(),
                    configPath,
                    JoinWarning(
                        priorWarning,
                        $"Failed to load bundled config.sample.json. Loaded built-in empty configuration instead. Error: {exception.Message}"));
            }
        }

        return (
            SampleConfiguration.Create(),
            configPath,
            string.IsNullOrWhiteSpace(priorWarning)
                ? null
                : JoinWarning(priorWarning, "Loaded built-in empty configuration instead."));
    }

    private static string MoveInvalidConfigAside(string path)
    {
        var backupPath = $"{path}.invalid-{DateTime.Now:yyyyMMddHHmmssfff}";
        try
        {
            File.Move(path, backupPath);
            return backupPath;
        }
        catch
        {
            return "backup failed";
        }
    }

    private static string GetUserDataDirectory()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var directory = Path.Combine(appData, AppDataFolderName);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
