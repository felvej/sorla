using System.Text.Json;
using System.Text.Json.Serialization;
using Sorla.Core.Dictation;
using Sorla.Core.Transcription;

namespace Sorla.Core;

// Right Ctrl is the default on Windows: Right Alt is AltGr on Swedish keyboards, needed for @, $ and {}.
public enum TriggerKey
{
    RightControl,
    RightAlt,
    RightShift,
    ScrollLock,
}

public static class TriggerKeys
{
    public const TriggerKey Default = TriggerKey.RightControl;

    // Virtual-key codes as the low-level keyboard hook reports them (left and right told apart).
    public static int VirtualKey(this TriggerKey key) => key switch
    {
        TriggerKey.RightAlt => 0xA5,
        TriggerKey.RightShift => 0xA1,
        TriggerKey.ScrollLock => 0x91,
        _ => 0xA3,
    };

    public static string DisplayName(this TriggerKey key) => key switch
    {
        TriggerKey.RightAlt => Strings.Get("key.rightAlt"),
        TriggerKey.RightShift => Strings.Get("key.rightShift"),
        TriggerKey.ScrollLock => "Scroll Lock",
        _ => Strings.Get("key.rightControl"),
    };
}

public sealed class Settings
{
    public TriggerKey TriggerKey { get; set; } = TriggerKeys.Default;
    public RecordingMode RecordingMode { get; set; } = RecordingMode.PushToTalk;
    public bool KeepClipboardContent { get; set; } = true;
    // Off, nothing is kept for Paste Last once a dictation has been delivered.
    public bool KeepLastTranscription { get; set; } = true;
    public bool PlaySounds { get; set; } = true;
    public bool HasCompletedOnboarding { get; set; }
    public ModelVariant ModelVariant { get; set; } = ModelVariant.Int8;
    // Empty means the default under %LOCALAPPDATA%\Sorla\models.
    public string ModelDirectory { get; set; } = "";

    [JsonIgnore]
    public string EffectiveModelDirectory => string.IsNullOrWhiteSpace(ModelDirectory) ? ModelFiles.DefaultDirectory : ModelDirectory;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), Json) ?? new Settings() : new Settings();
        }
        catch (JsonException)
        {
            // A broken file must not stop Sorla from starting; the defaults are written back on the next save.
            return new Settings();
        }
    }

    public void Save(string? path = null)
    {
        path ??= AppPaths.SettingsFile;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, Json));
        File.Move(temporary, path, overwrite: true);
    }
}
