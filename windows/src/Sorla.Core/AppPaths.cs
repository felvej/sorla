namespace Sorla.Core;

public static class AppPaths
{
    // Overridable so tests and portable installs can keep everything in one folder.
    public static string LocalData =>
        Environment.GetEnvironmentVariable("SORLA_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sorla");

    public static string SettingsFile => Path.Combine(LocalData, "settings.json");
}
