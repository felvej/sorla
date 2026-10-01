# Makes Swedish test recordings with Windows' own sv-SE voice (Microsoft Bengt), so tests need no real speech.
# Output: %LOCALAPPDATA%\Sorla\testaudio\*.wav
$ErrorActionPreference = 'Stop'
$out = "$env:LOCALAPPDATA\Sorla\testaudio"
New-Item -ItemType Directory -Force $out | Out-Null

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Media.SpeechSynthesis.SpeechSynthesizer, Windows.Media.SpeechSynthesis, ContentType = WindowsRuntime]
$null = [Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType = WindowsRuntime]
$asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
    $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1'
})[0]
function Await($operation, $type) {
    $task = $asTask.MakeGenericMethod($type).Invoke($null, @($operation))
    $task.Wait()
    $task.Result
}

$synth = New-Object Windows.Media.SpeechSynthesis.SpeechSynthesizer
$voice = [Windows.Media.SpeechSynthesis.SpeechSynthesizer]::AllVoices | Where-Object { $_.Language -eq 'sv-SE' } | Select-Object -First 1
if (-not $voice) { throw "No Swedish voice installed. Add one under Settings > Time & language > Speech." }
$synth.Voice = $voice

$texts = [ordered]@{
    'hej'  = 'Hej, jag heter Bengt och jag testar Sorla på min dator.'
    'mote' = 'Mötet flyttas till torsdag klockan tre. Kan du skicka underlaget innan dess?'
}
foreach ($name in $texts.Keys) {
    $stream = Await ($synth.SynthesizeTextToStreamAsync($texts[$name])) ([Windows.Media.SpeechSynthesis.SpeechSynthesisStream])
    $reader = New-Object Windows.Storage.Streams.DataReader($stream.GetInputStreamAt(0))
    $size = [uint32]$stream.Size
    $null = Await ($reader.LoadAsync($size)) ([uint32])
    $bytes = New-Object byte[] $size
    $reader.ReadBytes($bytes)
    [IO.File]::WriteAllBytes("$out\$name.wav", $bytes)
    Write-Host "$out\$name.wav"
}
