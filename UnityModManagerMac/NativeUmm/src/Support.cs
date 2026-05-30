namespace NativeUmm;

/// In-memory log sink. Replaces the TUI's colored Console output; the Swift UI
/// renders whatever we collect here per call.
internal static class Log
{
    [ThreadStatic] private static List<LogLine>? _buffer;

    public static void Begin() => _buffer = new List<LogLine>();
    public static void Info(string message) => _buffer?.Add(new LogLine("info", message));
    public static void Warn(string message) => _buffer?.Add(new LogLine("warn", message));
    public static void Fail(string message) => _buffer?.Add(new LogLine("error", message));
    public static IReadOnlyList<LogLine> Collect() => _buffer ?? (IReadOnlyList<LogLine>)Array.Empty<LogLine>();
}

internal readonly record struct LogLine(string Level, string Message);

/// App data lives under ~/Library/Application Support/UnityModManagerMac:
/// removed-mod backups, download caches, etc. (Kept out of the game's Mods
/// folder so removed mods don't keep loading.)
internal static class AppData
{
    public static string Root => Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Library", "Application Support", "UnityModManagerMac"));

    public static string Cache => Ensure(Path.Combine(Root, "cache"));
    public static string RemovedMods => Ensure(Path.Combine(Root, "removed-mods"));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}

/// Constants describing the ADOFAI hook target + UMM payload. Lifted verbatim
/// from the original MacTuiInstaller so the produced hook is byte-identical.
internal static class Spec
{
    public const string OfficialPayloadUrl = "https://www.dropbox.com/s/wz8x8e4onjdfdbm/UnityModManager.zip?dl=1";

    public static readonly EntryPointInfo EntryPoint = new(
        "UnityEngine.CoreModule.dll",
        "UnityEngine.MonoBehaviour",
        ".cctor",
        InsertPlace.Before);

    public static readonly string[] PayloadFiles =
    [
        "UnityModManager.dll",
        "UnityModManager.xml",
        "0Harmony.dll",
        "dnlib.dll",
        "System.Xml.dll"
    ];
}

internal sealed record EntryPointInfo(string AssemblyName, string TypeName, string MethodName, InsertPlace Place)
{
    public string ToConfigString()
    {
        var method = MethodName switch
        {
            ".cctor" => "cctor",
            ".ctor" => "ctor",
            _ => MethodName
        };
        return $"[{AssemblyName}]{TypeName}.{method}:{Place}";
    }
}

internal enum InsertPlace
{
    Before,
    After
}

internal sealed class InstallStatus
{
    public bool HookInstalled { get; set; }
    public bool ManagerInstalled { get; set; }
    public string? ManagerVersion { get; set; }
    public bool HasOriginalBackup { get; set; }
    public string OriginalBackupPath { get; set; } = "";
    public string? Warning { get; set; }
}

internal sealed record Payload(Dictionary<string, string> Files, bool IsBundled = false);
