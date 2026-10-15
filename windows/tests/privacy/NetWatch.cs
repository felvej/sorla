#:property TargetFramework=net10.0-windows
#:property AllowUnsafeBlocks=true
#:property PublishAot=false

// Privacy check: runs Sorla (CLI transcription, then the tray app doing one full dictation from code) and records
// every TCP connection and UDP port the Sorla process owns, polling Windows' connection tables every few ms.
// Usage: dotnet run NetWatch.cs -- <Sorla.exe> <speech.wav> <model dir>
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

var exe = args[0];
var wav = args[1];
var modelDir = args[2];
var data = Path.Combine(Path.GetTempPath(), "sorla-netwatch");
if (Directory.Exists(data)) Directory.Delete(data, true);
Directory.CreateDirectory(data);
File.WriteAllText(Path.Combine(data, "settings.json"),
    $$"""{ "HasCompletedOnboarding": true, "PlaySounds": false, "ModelDirectory": {{System.Text.Json.JsonSerializer.Serialize(modelDir)}} }""");

var seen = new SortedSet<string>();
long polls = 0;

void Watch(Process p, TimeSpan max, Func<bool>? stop = null)
{
    var clock = Stopwatch.StartNew();
    while (clock.Elapsed < max && !p.HasExited && !(stop?.Invoke() ?? false))
    {
        polls++;
        foreach (var e in Tables.Endpoints(p.Id)) seen.Add(e);
        Thread.Sleep(5);
    }
}

Process Run(string arguments)
{
    var info = new ProcessStartInfo(exe, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    info.Environment["SORLA_DATA_DIR"] = data;
    return Process.Start(info)!;
}

// 0. Positive control: a process that does go online must show up, or the watcher proves nothing.
var control = Process.Start(new ProcessStartInfo("powershell.exe",
    "-NoProfile -Command \"Invoke-WebRequest https://huggingface.co -UseBasicParsing -Method Head | Out-Null\"") { UseShellExecute = false })!;
Watch(control, TimeSpan.FromSeconds(30));
Console.WriteLine($"control (PowerShell to huggingface.co): {seen.Count} endpoints, e.g. {seen.FirstOrDefault(e => !e.Contains("127.0.0.1")) ?? "none"}");
seen.Clear();

// 1. Command-line transcription: model load and inference.
var cli = Run($"--transcribe \"{wav}\"");
Watch(cli, TimeSpan.FromSeconds(90));
cli.WaitForExit();
var cliText = cli.StandardOutput.ReadToEnd().Trim();
Console.WriteLine($"cli: exit {cli.ExitCode}, {cliText.Length} chars transcribed");

// 2. Tray app: launch, load, one dictation started and stopped from code, then idle.
var log = Path.Combine(data, "sorla.log");
var tray = Run($"--fake-mic \"{wav}\" --auto-dictate 7");
var idleUntil = DateTime.MaxValue;
Watch(tray, TimeSpan.FromSeconds(120), () =>
{
    if (idleUntil == DateTime.MaxValue && File.Exists(log) && ReadShared(log).Contains("test guard")) idleUntil = DateTime.Now.AddSeconds(30);
    return DateTime.Now > idleUntil;
});
var quit = Run("--quit");
quit.WaitForExit();
Watch(tray, TimeSpan.FromSeconds(10));

Console.WriteLine($"polls: {polls}");
Console.WriteLine($"endpoints owned by Sorla: {seen.Count}");
foreach (var e in seen) Console.WriteLine("  " + e);
Console.WriteLine("--- log");
Console.WriteLine(ReadShared(log).Trim());

static string ReadShared(string path)
{
    using var s = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    return new StreamReader(s).ReadToEnd();
}

static class Tables
{
    private const int AF_INET = 2, AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5, UDP_TABLE_OWNER_PID = 1;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int af, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedUdpTable(nint table, ref int size, bool order, int af, int tableClass, uint reserved);

    public static IEnumerable<string> Endpoints(int pid)
    {
        foreach (var af in new[] { AF_INET, AF_INET6 })
        {
            foreach (var row in Read((nint p, ref int s) => GetExtendedTcpTable(p, ref s, false, af, TCP_TABLE_OWNER_PID_ALL, 0), af == AF_INET ? 24 : 56))
            {
                if (OwnerTcp(row, af) != pid) continue;
                yield return af == AF_INET
                    ? $"TCP {Ip4(row, 4)}:{Port(row, 8)} -> {Ip4(row, 12)}:{Port(row, 16)}"
                    : $"TCP6 [{Ip6(row, 0)}]:{Port(row, 20)} -> [{Ip6(row, 24)}]:{Port(row, 44)}";
            }
            foreach (var row in Read((nint p, ref int s) => GetExtendedUdpTable(p, ref s, false, af, UDP_TABLE_OWNER_PID, 0), af == AF_INET ? 12 : 28))
            {
                var owner = af == AF_INET ? BitConverter.ToInt32(row, 8) : BitConverter.ToInt32(row, 24);
                if (owner != pid) continue;
                yield return af == AF_INET ? $"UDP {Ip4(row, 0)}:{Port(row, 4)}" : $"UDP6 [{Ip6(row, 0)}]:{Port(row, 20)}";
            }
        }
    }

    private static int OwnerTcp(byte[] row, int af) => af == AF_INET ? BitConverter.ToInt32(row, 20) : BitConverter.ToInt32(row, 52);
    private static string Ip4(byte[] r, int o) => new IPAddress(r.AsSpan(o, 4)).ToString();
    private static string Ip6(byte[] r, int o) => new IPAddress(r.AsSpan(o, 16)).ToString();
    private static int Port(byte[] r, int o) => (r[o] << 8) | r[o + 1];

    private delegate uint Getter(nint table, ref int size);

    private static List<byte[]> Read(Getter get, int rowSize)
    {
        var size = 0;
        get(0, ref size);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (get(buffer, ref size) != 0) return [];
            var count = Marshal.ReadInt32(buffer);
            var rows = new List<byte[]>(count);
            for (var i = 0; i < count; i++)
            {
                var row = new byte[rowSize];
                Marshal.Copy(buffer + 4 + i * rowSize, row, 0, rowSize);
                rows.Add(row);
            }
            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
