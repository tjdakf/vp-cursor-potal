using System.Diagnostics;
using System.Text.RegularExpressions;

namespace H2CursorRouter.Updater;

public sealed record UpdateRequest(string InstallerPath, string InstallDirectory, int ParentProcessId, long ParentStartTimeUtcTicks, string Sha256)
{
    public void Validate()
    {
        if (!Path.IsPathFullyQualified(InstallerPath) || !Path.IsPathFullyQualified(InstallDirectory) ||
            Path.GetFileName(InstallerPath) != ReleaseUpdate.InstallerName ||
            ParentProcessId <= 0 || ParentStartTimeUtcTicks <= 0 || !Regex.IsMatch(Sha256, "^[0-9a-fA-F]{64}$"))
            throw new InvalidDataException("The update request is invalid.");
    }

    public ProcessStartInfo CreateInstallerStartInfo()
    {
        Validate();
        // Inno's loader requests elevation itself. Do not use 'runas': the helper must stay unelevated for relaunch.
        var info = new ProcessStartInfo(InstallerPath) { UseShellExecute = true };
        foreach (var argument in new[] { "/SP-", "/SILENT", "/NORESTART", "/RESTARTEXITCODE=3010", "/NORESTARTAPPLICATIONS", "/NOCLOSEAPPLICATIONS", "/APPUPDATE", $"/DIR={InstallDirectory}", $"/LOG={Path.Combine(Path.GetDirectoryName(InstallerPath)!, "install.log")}" })
            info.ArgumentList.Add(argument);
        return info;
    }
}
