using Sorla.Core;

namespace Sorla;

// Timings and errors only, never what was said: dictated text stays out of the log.
public static class Log
{
    private static readonly object Gate = new();
    private static string Path => System.IO.Path.Combine(AppPaths.LocalData, "sorla.log");

    public static void Info(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LocalData);
                var info = new FileInfo(Path);
                if (info.Exists && info.Length > 512 * 1024) File.Move(Path, Path + ".old", overwrite: true);
                File.AppendAllText(Path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
        }
    }

    public static void Error(string message, Exception e) => Info($"ERROR {message}: {e.GetType().Name}: {e.Message}");
}
