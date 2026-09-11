param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts\vp-cursor-portal-win-x64",
    [bool]$SelfContained = $true,
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"

Write-Host "Restoring solution..."
dotnet restore H2CursorRouter.sln

Write-Host "Building solution..."
dotnet build H2CursorRouter.sln --configuration $Configuration --no-restore

Write-Host "Running tests..."
dotnet test H2CursorRouter.sln --configuration $Configuration --no-build

Write-Host "Publishing WPF app..."
dotnet publish src\H2CursorRouter.App\H2CursorRouter.App.csproj `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained $SelfContained `
    --output $Output

Write-Host "Publishing standalone update helper..."
dotnet publish src\H2CursorRouter.Updater\H2CursorRouter.Updater.csproj `
    --configuration $Configuration --runtime $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true --output artifacts\update-helper
if ($LASTEXITCODE -ne 0) { throw "Update helper publish failed." }
Copy-Item artifacts\update-helper\vp-cursor-portal-updater.exe (Join-Path $Output "vp-cursor-portal-updater.exe") -Force

if ($BuildInstaller) {
    $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $iscc)) {
        throw "Inno Setup compiler was not found at '$iscc'. Install Inno Setup 6 or run without -BuildInstaller."
    }

    Write-Host "Building installer..."
    New-Item -ItemType Directory -Force "artifacts\installer" | Out-Null
    $publishDir = (Resolve-Path $Output).Path
    & $iscc "/DPublishDir=$publishDir" "installer\inno\vp-cursor-portal.iss"
}

Write-Host ""
Write-Host "Published to: $Output"
if ($BuildInstaller) {
    Write-Host "Installer: artifacts\installer\vp-cursor-portal-setup.exe"
}
Write-Host "Run vp-cursor-portal.exe on Windows."
