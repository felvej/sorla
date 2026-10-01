# Sorla for Windows

**Talk. Release. Done.** (Prata. Släpp. Klart.)

Push-to-talk Swedish dictation for Windows. Hold **Right Ctrl**, speak Swedish, let go. The text is pasted where your cursor is, in whatever app you are using.

This is a fork of [markstrom/sorla](https://github.com/markstrom/sorla), the Mac app, ported to Windows. Speech recognition runs entirely on your PC, on the CPU, with [Klang Pianissimo](https://huggingface.co/KlangAI/pianissimo-sv). No account, no cloud, no analytics. After the one-time model download Sorla works fully offline.

The original Mac app and its documentation are unchanged in this repository (`Sources/`, `Tests/`, [README.mac.md](README.mac.md)). The Windows app is in [`windows/`](windows).

## Features

- **Push to talk or toggle.** Hold the key while you speak, or press once to start and once more to stop.
- **Your key.** Right Ctrl by default. Right Alt (AltGr), Right Shift or Scroll Lock instead.
- **Shortcuts still work.** Right Ctrl+C, Ctrl+click and so on call the dictation off instead of recording. Esc cancels a dictation.
- **A small indicator** at the top of the screen with a live waveform while you speak. It never takes focus.
- **Paste Last** with Ctrl+Alt+V pastes the most recent text again. It is kept in memory for five minutes and forgotten at once when you lock the PC or it goes to sleep. Turn off *Keep last transcription* and nothing is kept.
- **Your clipboard is kept.** Whatever you had copied is put back after Sorla pastes. Dictated text is marked so it stays out of Win+V clipboard history and cloud clipboard sync.
- **Never pastes into the wrong window.** If you switch windows before the text is ready, it is put on the clipboard instead, with a note on the indicator.
- Soft start and stop sounds (the same ones as on the Mac), launch at sign-in, Swedish and English interface (follows the Windows display language).
- Recordings stop by themselves after five minutes, so a forgotten toggle never keeps the microphone open.

## Requirements

- Windows 10 or 11, 64-bit (x64)
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or use the self-contained build)
- About 700 MB free space for the speech model, about 1.5 GB RAM while running
- A microphone, with *Let desktop apps access your microphone* on in Settings > Privacy & security > Microphone

No administrator rights are needed for anything.

## Install

Build and install for your own user (puts Sorla in `%LOCALAPPDATA%\Programs\Sorla` and adds it to the Start menu):

```powershell
powershell -ExecutionPolicy Bypass -File windows\scripts\publish.ps1 -Install
```

On first start the welcome window downloads the model (Klang's own ONNX export, `KlangAI/pianissimo-sv-onnx`, int8, 660 MB) from Hugging Face. Each file is pinned to one revision and checked against its SHA-256 before it is used. Then hold Right Ctrl in the test box and speak.

To install the model without Sorla downloading it (for example on a PC without internet), put these files from [KlangAI/pianissimo-sv-onnx](https://huggingface.co/KlangAI/pianissimo-sv-onnx) in `%LOCALAPPDATA%\Sorla\models\pianissimo-sv-onnx`: `config.json`, `nemo128.onnx`, `vocab.txt`, `encoder-model.int8.onnx`, `decoder_joint-model.int8.onnx`.

## Privacy

Your voice and your text never leave your PC. Audio is held in memory only while it is transcribed and never written to disk. The log (`%LOCALAPPDATA%\Sorla\sorla.log`) holds timings and errors, never what you said.

Sorla uses the network for one thing only: downloading the speech model from Hugging Face, when you click Download. There is no update check, no telemetry and no other network traffic.

## Using it

| | |
|---|---|
| Hold **Right Ctrl**, speak, release | Dictate (push to talk) |
| **Esc** while recording | Cancel |
| **Ctrl+Alt+V** | Paste Last |
| Tray icon | Status, Start/Stop Dictation, Settings, Getting Started, Quit |

Starting Sorla again while it runs opens its window. `Sorla.exe --quit` quits it.

Pasting into an app that runs as administrator only works if Sorla runs as administrator too (a Windows rule). The text is still on the clipboard.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (a per-user install without admin works: `dotnet-install.ps1 -Channel 10.0`).

```powershell
dotnet build windows\Sorla.slnx -c Release      # build
dotnet test windows\Sorla.slnx -c Release       # unit tests, plus a real-model test if the model is installed
windows\scripts\publish.ps1 [-SelfContained] [-Zip] [-Install]
```

Command-line helpers, handy for checking a PC:

```powershell
Sorla.exe --transcribe file.wav [more.wav]   # transcribe WAV files with the installed model
Sorla.exe --mic-check                        # open the microphone for 2 s and report the level (nothing is kept)
```

### End-to-end test

`windows\tests\e2e\run-e2e.ps1` runs the real tray app with a WAV file in place of the microphone, drives it with injected key presses into a test window, and checks that the text is pasted, the clipboard is restored, Paste Last, Esc and the shortcut rule work, and that no dictated text reaches the log. It takes about a minute and needs the keyboard and focus: don't touch the PC while it runs. In this mode Sorla only ever pastes into the test window. Test speech is made with Windows' own Swedish voice by `windows\scripts\make-test-audio.ps1`.

### How it works

| Part | File |
|---|---|
| Model: preprocessor, FastConformer encoder and greedy TDT decoding on ONNX Runtime | `windows/src/Sorla.Core/Transcription/` |
| Push-to-talk gesture (port of the Mac state machine) | `windows/src/Sorla.Core/Dictation/PushToTalkGesture.cs` |
| Keyboard and mouse hook on its own thread | `windows/src/Sorla/Platform/InputHook.cs` |
| Microphone (16 kHz mono via the Windows mixer) | `windows/src/Sorla/Platform/AudioCapture.cs` |
| Clipboard and Ctrl+V | `windows/src/Sorla/Platform/ClipboardPaster.cs` |
| Recording, transcription and delivery in order | `windows/src/Sorla/DictationController.cs` |
| Indicator (layered, click-through, never focused) | `windows/src/Sorla/UI/RecordingIndicator.cs` |

## Built on Klang Pianissimo

Sorla understands Swedish thanks to **Klang Pianissimo**, a Swedish speech recognition model by **Klang AI AB**, released under [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Sorla for Windows runs Klang's ONNX export, [KlangAI/pianissimo-sv-onnx](https://huggingface.co/KlangAI/pianissimo-sv-onnx). Pianissimo is fine-tuned from [NVIDIA Parakeet TDT 0.6B v3](https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3) (CC BY 4.0).

> Sorla for Windows is independent. It is not made, endorsed or supported by Klang AI AB or by the author of Sorla for Mac.

## License

Apache License 2.0, see [LICENSE](LICENSE) and [NOTICE](NOTICE). Sorla for Mac is copyright 2026 Anders Markström. Third-party licenses are in [`Resources/Licenses`](Resources/Licenses) and are shipped with the app:

| Component | License |
|---|---|
| Klang Pianissimo (Klang AI AB), based on NVIDIA Parakeet TDT 0.6B v3 | [CC BY 4.0](Resources/Licenses/Pianissimo.txt) |
| ONNX Runtime (Microsoft) | [MIT](Resources/Licenses/OnnxRuntime/LICENSE) |
| NAudio | [MIT](Resources/Licenses/NAudio/LICENSE) |
