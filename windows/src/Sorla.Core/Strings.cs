using System.Globalization;

namespace Sorla.Core;

// Swedish and English, picked from the Windows display language (SORLA_LANG=sv|en overrides).
public static class Strings
{
    public static string Language { get; set; } = Detect();

    private static string Detect()
    {
        var forced = Environment.GetEnvironmentVariable("SORLA_LANG");
        if (forced is "sv" or "en") return forced;
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "sv" ? "sv" : "en";
    }

    public static string Get(string key) =>
        (Language == "sv" ? Swedish : English).TryGetValue(key, out var value) ? value
        : English.TryGetValue(key, out var fallback) ? fallback
        : key;

    public static string Format(string key, params object[] args) => string.Format(Get(key), args);

    internal static IReadOnlyDictionary<string, string> English { get; } = new Dictionary<string, string>
    {
        ["app.tagline"] = "Talk. Release. Done.",
        ["key.rightControl"] = "Right Ctrl",
        ["key.rightAlt"] = "Right Alt (AltGr)",
        ["key.rightShift"] = "Right Shift",
        ["mode.pushToTalk"] = "Push to talk",
        ["mode.toggle"] = "Toggle",

        ["status.ready"] = "Ready - hold {0} and speak",
        ["status.readyToggle"] = "Ready - press {0} to start and stop",
        ["status.loading"] = "Loading the speech model...",
        ["status.noModel"] = "The speech model is not installed",
        ["status.modelFailed"] = "The speech model could not be loaded",
        ["status.recording"] = "Listening...",
        ["status.transcribing"] = "Transcribing...",
        ["status.downloading"] = "Downloading the speech model ({0:P0})",

        ["menu.pasteLast"] = "Paste Last (Ctrl+Alt+V)",
        ["menu.startDictation"] = "Start Dictation",
        ["menu.stopDictation"] = "Stop Dictation",
        ["menu.settings"] = "Settings...",
        ["menu.welcome"] = "Getting Started...",
        ["menu.about"] = "About Sorla",
        ["menu.quit"] = "Quit Sorla",

        ["settings.title"] = "Sorla Settings",
        ["settings.trigger"] = "Dictation key",
        ["settings.mode"] = "Recording mode",
        ["settings.keepClipboard"] = "Put back what you had copied",
        ["settings.keepLast"] = "Keep last transcription for Paste Last (5 minutes)",
        ["settings.sounds"] = "Play start and stop sounds",
        ["settings.launchAtLogin"] = "Start Sorla when I sign in to Windows",
        ["settings.model"] = "Speech model",
        ["settings.modelInt8"] = "Pianissimo int8 (660 MB, most accurate, recommended)",
        ["settings.modelInt4"] = "Pianissimo int4 (515 MB, smaller, slower)",
        ["settings.openModelFolder"] = "Open model folder",
        ["settings.close"] = "Close",
        ["settings.altGrWarning"] = "Right Alt is AltGr on Swedish keyboards. AltGr+key (@, $, {) will cancel a dictation but still work.",

        ["welcome.title"] = "Welcome to Sorla",
        ["welcome.intro"] = "Sorla is push-to-talk dictation in Swedish. Hold the key, speak, let go - the text is pasted where your cursor is. Everything runs on this PC: no account, no cloud, no analytics.",
        ["welcome.step.model"] = "1. Speech model (Klang Pianissimo, about {0} MB, downloaded once from Hugging Face)",
        ["welcome.step.try"] = "2. Try it: click in the box below, hold {0} and say something in Swedish.",
        ["welcome.download"] = "Download model",
        ["welcome.downloading"] = "Downloading {0}... {1:P0}",
        ["welcome.verifying"] = "Checking the files...",
        ["welcome.installed"] = "Installed - Sorla works offline from now on.",
        ["welcome.loading"] = "Installed - loading the model...",
        ["welcome.failed"] = "Download failed: {0}",
        ["welcome.done"] = "Done",
        ["welcome.tryPlaceholder"] = "Your dictated text appears here.",
        ["welcome.mic"] = "Microphone: {0}",
        ["welcome.noMic"] = "No microphone found. Connect one and allow microphone access in Windows Settings > Privacy & security > Microphone (\"Let desktop apps access your microphone\").",

        ["issue.noMicrophone.title"] = "Sorla can't hear you",
        ["issue.noMicrophone.body"] = "The microphone could not be started. Check that one is connected, and that \"Let desktop apps access your microphone\" is on in Windows Settings > Privacy & security > Microphone.",
        ["issue.openPrivacy"] = "Open microphone settings",
        ["issue.silent.title"] = "Your microphone seems muted",
        ["issue.silent.body"] = "Sorla recorded only silence. Check that the microphone isn't muted (a key on the keyboard, or the headset) and that the right input is chosen in Windows sound settings.",
        ["issue.openSound"] = "Open sound settings",
        ["issue.modelMissing.title"] = "The speech model is missing",
        ["issue.modelMissing.body"] = "Sorla needs the Pianissimo speech model to transcribe. Download it now? It is about {0} MB and only needs to be downloaded once.",
        ["issue.modelLoading.title"] = "The speech model is still loading",
        ["issue.modelLoading.body"] = "Sorla is getting ready. Try again in a few seconds.",
        ["issue.modelFailed.title"] = "The speech model could not be loaded",
        ["issue.modelFailed.body"] = "{0}\n\nTry downloading the model again from Getting Started.",
        ["issue.transcription.title"] = "The transcription failed",
        ["issue.transcription.body"] = "{0}",
        ["issue.notNow"] = "Not now",
        ["issue.download"] = "Download",

        ["note.windowChanged"] = "You switched windows, so the text was put on the clipboard instead. Paste it with Ctrl+V.",
        ["note.limit"] = "Recording stopped at the five-minute limit",
        ["about.title"] = "About Sorla",
        ["about.body"] = "Sorla for Windows {0}\n\nPush-to-talk dictation in Swedish, running entirely on this PC.\n\nSpeech recognition: Klang Pianissimo by Klang AI AB (CC BY 4.0), fine-tuned from NVIDIA Parakeet TDT 0.6B v3 (CC BY 4.0). Runs with ONNX Runtime (MIT). Audio with NAudio (MIT).\n\nBased on Sorla for Mac by Anders Markstrom (Apache 2.0). This Windows port is independent and not made or endorsed by Klang AI AB or the original author.",
        ["tray.firstRun"] = "Sorla is running here. Hold {0} and speak.",
        ["settings.microphone"] = "Microphone",
        ["settings.microphoneDefault"] = "Windows default ({0})",
        ["settings.ready"] = "Keep microphone ready",
        ["settings.readyHelp"] = "Opening a microphone takes a moment: about 0.2 s for a built-in one, over a second for Bluetooth headphones. Kept ready, the next dictation starts at once. Meanwhile Windows shows the microphone as in use, Bluetooth headphones stay in headset mode (music sounds worse), and only the last 0.3 s is held in memory.",
        ["ready.off"] = "Off (open only while dictating)",
        ["ready.oneMinute"] = "1 minute after a dictation",
        ["ready.fiveMinutes"] = "5 minutes after a dictation",
        ["ready.always"] = "Always (no delay at all)",
        ["note.micTooLate"] = "The microphone was not ready yet. Wait for the tone before you speak.",
    };

    internal static IReadOnlyDictionary<string, string> Swedish { get; } = new Dictionary<string, string>
    {
        ["app.tagline"] = "Prata. Släpp. Klart.",
        ["key.rightControl"] = "Höger Ctrl",
        ["key.rightAlt"] = "Höger Alt (AltGr)",
        ["key.rightShift"] = "Höger Skift",
        ["mode.pushToTalk"] = "Håll för att prata",
        ["mode.toggle"] = "Tryck för att starta och stoppa",

        ["status.ready"] = "Redo - håll {0} och prata",
        ["status.readyToggle"] = "Redo - tryck {0} för att starta och stoppa",
        ["status.loading"] = "Läser in taligenkänningen...",
        ["status.noModel"] = "Taligenkänningen är inte installerad",
        ["status.modelFailed"] = "Taligenkänningen kunde inte läsas in",
        ["status.recording"] = "Lyssnar...",
        ["status.transcribing"] = "Skriver ut...",
        ["status.downloading"] = "Laddar ner taligenkänningen ({0:P0})",

        ["menu.pasteLast"] = "Klistra in senaste (Ctrl+Alt+V)",
        ["menu.startDictation"] = "Starta diktering",
        ["menu.stopDictation"] = "Stoppa diktering",
        ["menu.settings"] = "Inställningar...",
        ["menu.welcome"] = "Kom igång...",
        ["menu.about"] = "Om Sorla",
        ["menu.quit"] = "Avsluta Sorla",

        ["settings.title"] = "Inställningar för Sorla",
        ["settings.trigger"] = "Dikteringstangent",
        ["settings.mode"] = "Inspelningsläge",
        ["settings.keepClipboard"] = "Lägg tillbaka det du hade kopierat",
        ["settings.keepLast"] = "Spara senaste texten för Klistra in senaste (5 minuter)",
        ["settings.sounds"] = "Spela ljud vid start och stopp",
        ["settings.launchAtLogin"] = "Starta Sorla när jag loggar in i Windows",
        ["settings.model"] = "Taligenkänning",
        ["settings.modelInt8"] = "Pianissimo int8 (660 MB, bäst träffsäkerhet, rekommenderas)",
        ["settings.modelInt4"] = "Pianissimo int4 (515 MB, mindre, långsammare)",
        ["settings.openModelFolder"] = "Öppna modellmappen",
        ["settings.close"] = "Stäng",
        ["settings.altGrWarning"] = "Höger Alt är AltGr på svenska tangentbord. AltGr+tangent (@, $, {) avbryter en diktering men fungerar som vanligt.",

        ["welcome.title"] = "Välkommen till Sorla",
        ["welcome.intro"] = "Sorla är diktering på svenska. Håll tangenten, prata och släpp - texten klistras in där markören står. Allt körs på den här datorn: inget konto, inget moln, ingen analys.",
        ["welcome.step.model"] = "1. Taligenkänning (Klang Pianissimo, cirka {0} MB, laddas ner en gång från Hugging Face)",
        ["welcome.step.try"] = "2. Prova: klicka i rutan nedan, håll {0} och säg något.",
        ["welcome.download"] = "Ladda ner",
        ["welcome.downloading"] = "Laddar ner {0}... {1:P0}",
        ["welcome.verifying"] = "Kontrollerar filerna...",
        ["welcome.installed"] = "Installerad - Sorla fungerar nu utan internet.",
        ["welcome.loading"] = "Installerad - läser in...",
        ["welcome.failed"] = "Nedladdningen misslyckades: {0}",
        ["welcome.done"] = "Klar",
        ["welcome.tryPlaceholder"] = "Den dikterade texten hamnar här.",
        ["welcome.mic"] = "Mikrofon: {0}",
        ["welcome.noMic"] = "Ingen mikrofon hittades. Anslut en och tillåt mikrofonen i Windows Inställningar > Sekretess och säkerhet > Mikrofon (\"Låt skrivbordsappar komma åt mikrofonen\").",

        ["issue.noMicrophone.title"] = "Sorla hör dig inte",
        ["issue.noMicrophone.body"] = "Mikrofonen gick inte att starta. Kontrollera att en mikrofon är ansluten och att \"Låt skrivbordsappar komma åt mikrofonen\" är på i Windows Inställningar > Sekretess och säkerhet > Mikrofon.",
        ["issue.openPrivacy"] = "Öppna mikrofoninställningar",
        ["issue.silent.title"] = "Mikrofonen verkar vara avstängd",
        ["issue.silent.body"] = "Sorla spelade bara in tystnad. Kontrollera att mikrofonen inte är avstängd (en tangent på tangentbordet eller på headsetet) och att rätt ingång är vald i Windows ljudinställningar.",
        ["issue.openSound"] = "Öppna ljudinställningar",
        ["issue.modelMissing.title"] = "Taligenkänningen saknas",
        ["issue.modelMissing.body"] = "Sorla behöver taligenkänningen Pianissimo för att skriva ut tal. Ladda ner den nu? Den är cirka {0} MB och laddas bara ner en gång.",
        ["issue.modelLoading.title"] = "Taligenkänningen läses fortfarande in",
        ["issue.modelLoading.body"] = "Sorla gör sig redo. Försök igen om några sekunder.",
        ["issue.modelFailed.title"] = "Taligenkänningen kunde inte läsas in",
        ["issue.modelFailed.body"] = "{0}\n\nProva att ladda ner modellen igen under Kom igång.",
        ["issue.transcription.title"] = "Utskriften misslyckades",
        ["issue.transcription.body"] = "{0}",
        ["issue.notNow"] = "Inte nu",
        ["issue.download"] = "Ladda ner",

        ["note.windowChanged"] = "Du bytte fönster, så texten lades på urklipp i stället. Klistra in med Ctrl+V.",
        ["note.limit"] = "Inspelningen stoppades vid gränsen på fem minuter",
        ["about.title"] = "Om Sorla",
        ["about.body"] = "Sorla för Windows {0}\n\nDiktering på svenska som körs helt på den här datorn.\n\nTaligenkänning: Klang Pianissimo av Klang AI AB (CC BY 4.0), finjusterad från NVIDIA Parakeet TDT 0.6B v3 (CC BY 4.0). Körs med ONNX Runtime (MIT). Ljud via NAudio (MIT).\n\nBygger på Sorla för Mac av Anders Markström (Apache 2.0). Den här Windows-versionen är fristående och inte gjord eller godkänd av Klang AI AB eller ursprungsförfattaren.",
        ["tray.firstRun"] = "Sorla körs här. Håll {0} och prata.",
        ["settings.microphone"] = "Mikrofon",
        ["settings.microphoneDefault"] = "Windows standard ({0})",
        ["settings.ready"] = "Håll mikrofonen redo",
        ["settings.readyHelp"] = "Det tar en stund att öppna en mikrofon: cirka 0,2 s för den inbyggda, över en sekund för Bluetooth-hörlurar. Hålls den redo startar nästa diktering direkt. Under tiden visar Windows att mikrofonen används, Bluetooth-hörlurar stannar i headsetläge (musik låter sämre) och bara de senaste 0,3 s finns i minnet.",
        ["ready.off"] = "Av (öppen bara medan du dikterar)",
        ["ready.oneMinute"] = "1 minut efter en diktering",
        ["ready.fiveMinutes"] = "5 minuter efter en diktering",
        ["ready.always"] = "Alltid (ingen fördröjning alls)",
        ["note.micTooLate"] = "Mikrofonen hann inte starta. Vänta på tonen innan du pratar.",
    };
}
