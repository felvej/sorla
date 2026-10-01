# End-to-end test of Sorla for Windows: the real tray app, keyboard hook, model, clipboard and Ctrl+V,
# with a WAV file standing in for the microphone. Takes about a minute and briefly takes keyboard focus.
#
#   powershell -ExecutionPolicy Bypass -File windows\tests\e2e\run-e2e.ps1
param(
    [string]$Audio = "$env:LOCALAPPDATA\Sorla\testaudio\mote.wav",
    [string]$Expected = "Mötet flyttas till torsdag klockan tre. Kan du skicka underlaget innan dess?"
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$windows = Resolve-Path "$here\..\.."
$dotnet = if (Test-Path "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe") { "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" } else { "dotnet" }
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

if (-not (Test-Path $Audio)) { & "$windows\scripts\make-test-audio.ps1" }

Write-Host "Building Sorla..."
& $dotnet build "$windows\src\Sorla" -c Release -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$exe = "$windows\src\Sorla\bin\Release\net10.0-windows\win-x64\Sorla.exe"

Write-Host "Building test target..."
$targetDir = Join-Path $env:TEMP 'sorla-e2e-target'
& $dotnet build "$here\TestTarget.cs" -c Release -o $targetDir -v quiet | Out-Null
if ($LASTEXITCODE -ne 0) { throw "test target build failed" }

# A separate data folder, so the test never touches your own settings, but the real model.
$data = Join-Path $env:TEMP 'sorla-e2e-data'
Remove-Item -Recurse -Force $data -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $data | Out-Null
@{ HasCompletedOnboarding = $true; ModelDirectory = "$env:LOCALAPPDATA\Sorla\models\pianissimo-sv-onnx"; PlaySounds = $false } |
    ConvertTo-Json | Set-Content -Encoding UTF8 "$data\settings.json"

& $exe --quit
Start-Sleep -Milliseconds 500
$env:SORLA_DATA_DIR = $data
$sorla = Start-Process $exe -ArgumentList '--fake-mic', "`"$Audio`"" -PassThru
Remove-Item Env:\SORLA_DATA_DIR

Write-Host "Waiting for the model to load..."
$log = "$data\sorla.log"
$deadline = (Get-Date).AddSeconds(120)
while (-not ((Test-Path $log) -and (Select-String -Path $log -Pattern 'model ready' -Quiet))) {
    if ((Get-Date) -gt $deadline) { throw "model did not load: $(Get-Content $log -Raw)" }
    if ($sorla.HasExited) { throw "Sorla exited: $(Get-Content $log -Raw)" }
    Start-Sleep -Milliseconds 500
}

Write-Host "Running the dictation scenarios..."
$result = Join-Path $env:TEMP 'sorla-e2e-result.json'
Remove-Item $result -ErrorAction SilentlyContinue
Start-Process "$targetDir\TestTarget.exe" -ArgumentList "`"$result`"" -Wait
& $exe --quit
$sorla.WaitForExit(10000) | Out-Null

$r = Get-Content $result -Raw -Encoding UTF8 | ConvertFrom-Json
$failures = @()
function Check($name, $ok, $detail) {
    if ($ok) { Write-Host "PASS $name" -ForegroundColor Green } else { Write-Host "FAIL $name - $detail" -ForegroundColor Red; $script:failures += $name }
}
Check 'target window had focus' ($r.focused -eq $true) $r.focused
Check 'dictation pasted the transcript' ($r.dictation -eq $Expected) "got '$($r.dictation)'"
Check 'clipboard was put back' ($r.clipboardAfter -eq 'SORLA-ORIGINAL-CLIPBOARD') "got '$($r.clipboardAfter)'"
Check 'Paste Last pasted it again' ($r.pasteLast -eq $Expected) "got '$($r.pasteLast)'"
Check 'Esc cancelled the dictation' ($r.escape -eq '') "got '$($r.escape)'"
Check 'Right Ctrl+A was not a dictation' ($r.shortcut -eq '') "got '$($r.shortcut)'"
$logText = Get-Content $log -Raw -Encoding UTF8
Check 'log holds no dictated text' (-not $logText.Contains('torsdag')) 'transcript found in log'
Check 'Sorla quit cleanly' ($sorla.HasExited) 'still running'
if ($r.error) { Write-Host $r.error }
Write-Host "focus during hold: $($r.focusDuringHold -join " | ")"
Write-Host ''
Write-Host ($logText.Trim())
if ($failures.Count -gt 0) { exit 1 }
