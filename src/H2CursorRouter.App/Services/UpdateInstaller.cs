using System.Diagnostics;
using System.IO;
using System.Text.Json;
using H2CursorRouter.Updater;

namespace H2CursorRouter.App.Services;

public interface IUpdateInstaller
{
    bool IsInstalled { get; }
    Task InstallAsync(string installerPath, ReleaseUpdate release);
}

public sealed class UpdateInstaller(Func<Task> prepare, Action resume, Action exit) : IUpdateInstaller
{
    public bool IsInstalled => InstalledApplication.IsInstalledDirectory(AppContext.BaseDirectory);

    public async Task InstallAsync(string installerPath, ReleaseUpdate release)
    {
        if (!IsInstalled) throw new InvalidOperationException("In-app installation is available only for the installed version.");
        var helperSource = Path.Combine(AppContext.BaseDirectory, "vp-cursor-portal-updater.exe");
        if (!File.Exists(helperSource)) throw new FileNotFoundException("The update helper is missing. Please reinstall using the published installer.");
        var directory = Path.GetDirectoryName(installerPath)!;
        var helperPath = Path.Combine(directory, "vp-cursor-portal-updater.exe");
        File.Copy(helperSource, helperPath, overwrite: false);
        using var current = Process.GetCurrentProcess();
        var request = new UpdateRequest(installerPath, AppContext.BaseDirectory, Environment.ProcessId,
            current.StartTime.ToUniversalTime().Ticks, release.Sha256);
        var requestPath = Path.Combine(directory, "request.json");
        await File.WriteAllTextAsync(requestPath, JsonSerializer.Serialize(request));
        Process? helper = null;
        try
        {
            await prepare();
            var start = new ProcessStartInfo(helperPath) { UseShellExecute = false, WorkingDirectory = directory };
            start.ArgumentList.Add(requestPath);
            helper = Process.Start(start) ?? throw new InvalidOperationException("Could not start the update helper.");
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!File.Exists(Path.Combine(directory, "ready")))
            {
                if (helper.HasExited) throw new InvalidOperationException("The update helper could not prepare the installation.");
                if (DateTime.UtcNow >= deadline) throw new TimeoutException("The update helper did not respond. Please try again.");
                await Task.Delay(100);
            }
            exit();
        }
        catch
        {
            // This child cannot start Setup until this process exits, so stopping it here is safe.
            try
            {
                if (helper is { HasExited: false })
                {
                    helper.Kill();
                    await helper.WaitForExitAsync();
                }
            }
            catch (InvalidOperationException) { }
            finally { resume(); }
            throw;
        }
        finally { helper?.Dispose(); }
    }
}
