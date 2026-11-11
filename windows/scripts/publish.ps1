# Builds Sorla for Windows into windows\artifacts\Sorla and, with -Install, installs it for the current user
# (no administrator rights): %LOCALAPPDATA%\Programs\Sorla plus a Start menu shortcut.
#
#   powershell -ExecutionPolicy Bypass -File windows\scripts\publish.ps1 -Install
#   powershell -ExecutionPolicy Bypass -File windows\scripts\publish.ps1 -Zip
#
# Works on a fresh PC: if the .NET 10 SDK is missing it is installed for the current user from Microsoft
# (dot.net/v1/dotnet-install.ps1, about 250 MB, no administrator rights).
#
# The app is self-contained by default (the .NET runtime is bundled, about 150 MB), so it runs on any 64-bit
# Windows 10/11 PC. -FrameworkDependent makes a 7 MB build that needs the .NET 10 Desktop Runtime installed.
param(
    [switch]$FrameworkDependent,
    [switch]$Zip,
    [switch]$Install,
    # Where to look for (and if needed install) the SDK; by default the per-user location, then PATH.
    [string]$DotnetDir = ''
)
$ErrorActionPreference = 'Stop'
$windows = Resolve-Path "$PSScriptRoot\.."
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Test-Sdk10([string]$exe) {
    if (-not $exe) { return $false }
    try { return [bool](& $exe --list-sdks 2>$null | Where-Object { $_ -match '^10\.' }) } catch { return $false }
}

$userDir = if ($DotnetDir) { $DotnetDir } else { Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet' }
$candidates = @(Join-Path $userDir 'dotnet.exe')
if (-not $DotnetDir) { $candidates += (Get-Command dotnet -ErrorAction SilentlyContinue).Source }
$dotnet = $candidates | Where-Object { $_ -and (Test-Path $_) -and (Test-Sdk10 $_) } | Select-Object -First 1

if (-not $dotnet) {
    Write-Host ".NET 10 SDK not found; installing it for this user in $userDir ..."
    $installer = Join-Path $env:TEMP 'dotnet-install.ps1'
    Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
    & $installer -Channel 10.0 -InstallDir $userDir -NoPath
    $dotnet = Join-Path $userDir 'dotnet.exe'
    if (-not (Test-Sdk10 $dotnet)) { throw ".NET 10 SDK installation failed" }
}
Write-Host "Using $dotnet"

$out = Join-Path $windows 'artifacts\Sorla'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
$sc = if ($FrameworkDependent) { 'false' } else { 'true' }
& $dotnet publish "$windows\src\Sorla" -c Release -r win-x64 --self-contained $sc -o $out -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
# Import libraries are only needed to link against onnxruntime, not to run it.
Get-ChildItem $out -Filter *.lib | Remove-Item
Write-Host "Published to $out"

if ($Zip) {
    $zipPath = Join-Path $windows 'artifacts\Sorla-win-x64.zip'
    Remove-Item $zipPath -ErrorAction SilentlyContinue
    Compress-Archive -Path "$out\*" -DestinationPath $zipPath
    Write-Host "Zipped to $zipPath"
}

if ($Install) {
    $target = Join-Path $env:LOCALAPPDATA 'Programs\Sorla'
    $running = Get-Process Sorla -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$target*" }
    if ($running) {
        & "$target\Sorla.exe" --quit
        $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
    }
    # Start from an empty folder, so files of an older build never mix with the new one.
    # Settings, the model and the log live in %LOCALAPPDATA%\Sorla and are not touched.
    if (Test-Path $target) { Get-ChildItem $target -Force | Remove-Item -Recurse -Force }
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item "$out\*" $target -Recurse -Force

    $shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sorla.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = "$target\Sorla.exe"
    $link.WorkingDirectory = $target
    $link.Description = 'Sorla - Swedish push-to-talk dictation'
    $link.Save()
    Write-Host "Installed to $target (Start menu: Sorla)"
    Start-Process "$target\Sorla.exe"
}
