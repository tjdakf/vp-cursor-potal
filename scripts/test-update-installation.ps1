# Intended only for an isolated Windows CI runner. Installs the real build into a temporary directory.
$ErrorActionPreference = "Stop"
if ($env:GITHUB_ACTIONS -ne "true") { throw "Run this installer smoke test only on an isolated GitHub Actions runner." }
$testRoot = Join-Path $env:RUNNER_TEMP ("vp-update-smoke-" + [guid]::NewGuid().ToString("N"))
$installDir = Join-Path $testRoot "installed app"
$handoffDir = Join-Path $testRoot "handoff"
New-Item -ItemType Directory -Force $handoffDir | Out-Null
$setup = (Resolve-Path "artifacts\installer\vp-cursor-portal-setup.exe").Path
$first = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SP-', '/NORESTART', '/APPUPDATE', "/DIR=`"$installDir`"") -Wait -PassThru
if ($first.ExitCode -ne 0) { throw "Initial install failed: $($first.ExitCode)" }
$appExe = Join-Path $installDir "vp-cursor-portal.exe"
if (-not (Test-Path $appExe)) { throw "Installed app is missing." }
$configDir = Join-Path $env:APPDATA "vp-cursor-portal"
New-Item -ItemType Directory -Force $configDir | Out-Null
$configPath = Join-Path $configDir "config.json"
# Use valid field settings; startup may refresh display history, but these saved settings must survive.
$config = '{"devices":[{"id":"update-test","name":"Preserve device","host":"127.0.0.1","port":6000,"deviceId":0,"timeoutMs":100}],"cursorLayouts":[],"profiles":[],"safety":{"emergencyHotkey":"Ctrl+Alt+Shift+Esc","disableRoutingOnMonitorTopologyChange":true,"startWithRoutingDisabled":true}}'
[IO.File]::WriteAllText($configPath, $config)
$configHash = (Get-FileHash $configPath -Algorithm SHA256).Hash
$second = Start-Process -FilePath $setup -ArgumentList @('/VERYSILENT', '/SP-', '/NORESTART', '/APPUPDATE', "/DIR=`"$installDir`"") -Wait -PassThru
if ($second.ExitCode -ne 0) { throw "Upgrade install failed: $($second.ExitCode)" }
if ((Get-FileHash $configPath -Algorithm SHA256).Hash -ne $configHash) { throw 'Installer changed configuration bytes.' }
$installerCopy = Join-Path $handoffDir "vp-cursor-portal-setup.exe"
$helper = Join-Path $handoffDir "vp-cursor-portal-updater.exe"
Copy-Item $setup $installerCopy
Copy-Item (Join-Path $installDir "vp-cursor-portal-updater.exe") $helper
$parent = Start-Process powershell.exe -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 120' -PassThru -WindowStyle Hidden
$request = @{
    InstallerPath = $installerCopy
    InstallDirectory = $installDir
    ParentProcessId = $parent.Id
    ParentStartTimeUtcTicks = $parent.StartTime.ToUniversalTime().Ticks
    Sha256 = (Get-FileHash $installerCopy -Algorithm SHA256).Hash
}
$requestPath = Join-Path $handoffDir "request.json"
[IO.File]::WriteAllText($requestPath, ($request | ConvertTo-Json))
$updater = $null
try {
    $updater = Start-Process $helper -ArgumentList "`"$requestPath`"" -PassThru
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    while (-not (Test-Path (Join-Path $handoffDir 'ready'))) {
        if (Test-Path (Join-Path $handoffDir 'error.log')) { throw (Get-Content (Join-Path $handoffDir 'error.log') -Raw) }
        if ($updater.HasExited -or [DateTime]::UtcNow -gt $deadline) { throw 'Updater did not acknowledge the parent process.' }
        Start-Sleep -Milliseconds 100
    }
    # The helper must wait until its parent exits before it starts Setup.
    if ($updater.HasExited) { throw 'Updater exited before parent handoff.' }
    Stop-Process -Id $parent.Id
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while (-not $updater.HasExited) {
        if (Test-Path (Join-Path $handoffDir 'error.log')) { throw (Get-Content (Join-Path $handoffDir 'error.log') -Raw) }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'Update installation timed out.' }
        Start-Sleep -Milliseconds 200
    }
    $updater.WaitForExit()
    if ($updater.ExitCode -ne 0) { throw "Updater failed: $($updater.ExitCode)" }
    $saved = Get-Content $configPath -Raw | ConvertFrom-Json
    if ($saved.devices[0].id -ne 'update-test' -or $saved.devices[0].name -ne 'Preserve device' -or $saved.devices[0].host -ne '127.0.0.1') { throw 'Saved device settings did not survive update.' }
    $actualVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($appExe).ProductVersion
    if (-not $actualVersion.StartsWith($env:APP_VERSION)) { throw "Installed version mismatch: $actualVersion" }
    Write-Host "Installer update passed; configuration bytes preserved; helper successfully requested app relaunch. Version: $actualVersion"
    Write-Host 'This smoke test does not verify visible WPF interaction, UAC consent, or real cursor/H2 operation.'
}
finally {
    if (-not $parent.HasExited) { Stop-Process -Id $parent.Id -ErrorAction SilentlyContinue }
    if ($updater -and -not $updater.HasExited) { Stop-Process -Id $updater.Id -ErrorAction SilentlyContinue }
    Get-Process -Name 'vp-cursor-portal' -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe } | Stop-Process -ErrorAction SilentlyContinue
}
