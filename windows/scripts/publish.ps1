# Builds Sorla for Windows into windows\artifacts\Sorla and, with -Install, installs it for the current user
# (no administrator rights): %LOCALAPPDATA%\Programs\Sorla plus a Start menu shortcut.
#
#   powershell -ExecutionPolicy Bypass -File windows\scripts\publish.ps1 -Install
#   powershell -ExecutionPolicy Bypass -File windows\scripts\publish.ps1 -SelfContained -Zip
#
# -SelfContained bundles the .NET runtime (about 150 MB), for PCs without the .NET 10 Desktop Runtime.
param(
    [switch]$SelfContained,
    [switch]$Zip,
    [switch]$Install
)
$ErrorActionPreference = 'Stop'
$windows = Resolve-Path "$PSScriptRoot\.."
$dotnet = if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") { "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" } else { "dotnet" }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$out = Join-Path $windows 'artifacts\Sorla'
Remove-Item -Recurse -Force $out -ErrorAction SilentlyContinue
$sc = if ($SelfContained) { 'true' } else { 'false' }
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
