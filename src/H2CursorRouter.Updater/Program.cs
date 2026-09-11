using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace H2CursorRouter.Updater;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows() || args.Length != 1) return 1;
        UpdateRequest? request = null;
        var parentExited = false;
        var directory = Path.GetDirectoryName(Path.GetFullPath(args[0]))!;
        try
        {
            request = JsonSerializer.Deserialize<UpdateRequest>(await File.ReadAllTextAsync(args[0]))
                ?? throw new InvalidDataException("Missing update request.");
            request.Validate();
            if (!InstalledApplication.IsInstalledDirectory(request.InstallDirectory))
                throw new InvalidDataException("This directory is not the registered installation.");
            using var parent = Process.GetProcessById(request.ParentProcessId);
            if (parent.StartTime.ToUniversalTime().Ticks != request.ParentStartTimeUtcTicks)
                throw new InvalidDataException("The application process changed. Please try again.");
            // Readiness is signalled only after acquiring the correct parent process handle.
            await File.WriteAllTextAsync(Path.Combine(directory, "ready"), "ready");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await parent.WaitForExitAsync(timeout.Token);
            parentExited = true;
            await using (var input = new FileStream(request.InstallerPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var hash = await SHA256.HashDataAsync(input);
                if (!string.Equals(Convert.ToHexString(hash), request.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded installer changed after verification.");
                using var installer = Process.Start(request.CreateInstallerStartInfo())
                    ?? throw new InvalidOperationException("Could not start the installer.");
                await installer.WaitForExitAsync();
                if (installer.ExitCode == 3010)
                    throw new InvalidOperationException("Windows must be restarted to finish replacing files. Restart Windows before using the updated app.");
                if (installer.ExitCode != 0)
                    throw new InvalidOperationException($"Installation did not complete (code {installer.ExitCode}). See {Path.Combine(directory, "install.log")}.");
            }
            Restart(request);
            TryDelete(request.InstallerPath);
            TryDelete(args[0]);
            TryDelete(Path.Combine(directory, "ready"));
            return 0;
        }
        catch (Exception exception)
        {
            try { await File.WriteAllTextAsync(Path.Combine(directory, "error.log"), exception.ToString()); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            var message = exception is Win32Exception { NativeErrorCode: 1223 }
                ? "Update cancelled at the Windows permission prompt. The application will reopen."
                : $"Update could not complete. Your saved configuration has not been deleted.\n\n{exception.Message}";
            MessageBox(IntPtr.Zero, message, "vp-cursor-portal update", 0x10);
            if (parentExited && request is not null)
            {
                try { Restart(request); }
                catch (Exception restartError) { MessageBox(IntPtr.Zero, $"Please open vp-cursor-portal manually.\n\n{restartError.Message}", "vp-cursor-portal update", 0x10); }
            }
            return 1;
        }
    }

    private static void Restart(UpdateRequest request)
    {
        using var app = Process.Start(new ProcessStartInfo(
            Path.Combine(request.InstallDirectory, InstalledApplication.ExecutableName)) { UseShellExecute = true, WorkingDirectory = request.InstallDirectory })
            ?? throw new InvalidOperationException("Could not restart the application.");
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);
}
