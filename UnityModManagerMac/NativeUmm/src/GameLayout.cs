using System.Diagnostics;

namespace NativeUmm;

internal sealed class GameLayout
{
    private GameLayout(string gameRoot, string appPath)
    {
        GameRoot = gameRoot;
        AppPath = appPath;
        ManagedPath = Path.Combine(appPath, "Contents", "Resources", "Data", "Managed");
        EntryAssemblyPath = Path.Combine(ManagedPath, Spec.EntryPoint.AssemblyName);
        ManagerPath = Path.Combine(ManagedPath, "UnityModManager");
        ModsPath = Path.Combine(gameRoot, "Mods");
        OriginalBackupPath = EntryAssemblyPath + ".original_";
    }

    public string GameRoot { get; }
    public string AppPath { get; }
    public string ManagedPath { get; }
    public string EntryAssemblyPath { get; }
    public string ManagerPath { get; }
    public string ModsPath { get; }
    public string OriginalBackupPath { get; }

    public static GameLayout? Detect(string? requestedPath)
    {
        foreach (var candidate in CandidatePaths(requestedPath))
        {
            var expanded = ExpandHome(candidate);
            var app = ResolveAppPath(expanded);
            if (app is null)
                continue;

            var root = Directory.GetParent(app)?.FullName;
            if (root is null)
                continue;

            var layout = new GameLayout(root, app);
            if (File.Exists(layout.EntryAssemblyPath) && File.Exists(Path.Combine(layout.ManagedPath, "Assembly-CSharp.dll")))
                return layout;
        }

        return null;
    }

    public string ReadExecutableArchitectures()
    {
        var executable = Path.Combine(AppPath, "Contents", "MacOS", "ADanceOfFireAndIce");
        if (!File.Exists(executable))
            return "missing";

        try
        {
            var psi = new ProcessStartInfo("/usr/bin/lipo", $"-archs {Quote(executable)}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            if (process is null)
                return "unknown";
            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(1000);
            return string.IsNullOrWhiteSpace(output) ? "unknown" : output;
        }
        catch
        {
            return "unknown";
        }
    }

    private static IEnumerable<string> CandidatePaths(string? requestedPath)
    {
        if (!string.IsNullOrWhiteSpace(requestedPath))
            yield return requestedPath;

        yield return "~/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice";
        yield return "~/Library/Application Support/com.valvesoftware.Steam/steamapps/common/A Dance of Fire and Ice";
        yield return "~/.steam/steam/steamapps/common/A Dance of Fire and Ice";

        var libraryFolders = ExpandHome("~/Library/Application Support/Steam/steamapps/libraryfolders.vdf");
        if (File.Exists(libraryFolders))
        {
            foreach (var line in File.ReadLines(libraryFolders))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"path\"", StringComparison.Ordinal))
                    continue;

                var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2)
                    yield return Path.Combine(parts[^1].Replace(@"\\", "/"), "steamapps", "common", "A Dance of Fire and Ice");
            }
        }
    }

    private static string? ResolveAppPath(string path)
    {
        if (path.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(path))
            return path;

        if (Directory.Exists(Path.Combine(path, "Contents", "Resources", "Data", "Managed")))
            return path;

        var direct = Path.Combine(path, "ADanceOfFireAndIce.app");
        if (Directory.Exists(direct))
            return direct;

        if (!Directory.Exists(path))
            return null;

        return Directory.EnumerateDirectories(path, "*.app", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(p => Path.GetFileName(p).Contains("Dance", StringComparison.OrdinalIgnoreCase) ||
                                 Path.GetFileName(p).Contains("ADOFAI", StringComparison.OrdinalIgnoreCase));
    }

    private static string ExpandHome(string path)
    {
        if (path == "~")
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (path.StartsWith("~/", StringComparison.Ordinal))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);

        return path;
    }

    private static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
}
