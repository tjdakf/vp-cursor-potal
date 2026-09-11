using Microsoft.Win32;
using System.Runtime.Versioning;

namespace H2CursorRouter.Updater;

public static class InstalledApplication
{
    public const string ExecutableName = "vp-cursor-portal.exe";
    public const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{FA47277E-8B64-4CE7-B26A-B45168613CC6}_is1";

    [SupportedOSPlatform("windows")]
    public static bool IsInstalledDirectory(string directory)
    {
        using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        using var key = machine.OpenSubKey(UninstallKey);
        var installed = key?.GetValue("InstallLocation") as string ?? key?.GetValue("Inno Setup: App Path") as string;
        return !string.IsNullOrWhiteSpace(installed) &&
            string.Equals(Path.GetFullPath(installed).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(directory, ExecutableName));
    }
}
