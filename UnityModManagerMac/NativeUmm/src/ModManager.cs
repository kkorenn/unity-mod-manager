using System.IO.Compression;
using System.Text.Json;

namespace NativeUmm;

internal sealed record ModInfo(
    string Id,
    string DisplayName,
    string Version,
    string? ManagerVersion,
    string? HomePage,
    string Status,
    string Path,
    bool Installed);

/// Lists and installs UMM mods under the game's Mods/ folder — mirrors the
/// "Mods" tab of the original Windows installer.
internal static class ModManager
{
    public static List<ModInfo> List(GameLayout layout)
    {
        var result = new List<ModInfo>();
        AddFrom(result, layout.ModsPath, installed: true);
        AddFrom(result, AppData.RemovedMods, installed: false);
        result.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
        return result;
    }

    private static void AddFrom(List<ModInfo> result, string root, bool installed)
    {
        if (!Directory.Exists(root))
            return;

        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            // Ignore leftover assembly/in-place backups from older builds.
            if (Path.GetFileName(dir).Contains("nativeumm_backup", StringComparison.OrdinalIgnoreCase))
                continue;

            var infoPath = FindInfoJson(dir);
            if (infoPath is null)
                continue;

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(infoPath));
                var element = doc.RootElement;

                var id = GetString(element, "Id");
                if (string.IsNullOrEmpty(id))
                    id = Path.GetFileName(dir);
                var name = GetString(element, "DisplayName");
                if (string.IsNullOrEmpty(name))
                    name = id;
                var version = GetString(element, "Version");
                var manager = GetString(element, "ManagerVersion");
                var home = GetString(element, "HomePage");

                result.Add(new ModInfo(id, name, version,
                    string.IsNullOrEmpty(manager) ? null : manager,
                    string.IsNullOrEmpty(home) ? null : home,
                    installed ? "OK" : "Uninstalled", dir, installed));
            }
            catch
            {
                var name = Path.GetFileName(dir);
                result.Add(new ModInfo(name, name, "", null, null,
                    installed ? "Invalid Info.json" : "Uninstalled", dir, installed));
            }
        }
    }

    /// Removes an installed mod by Id, moving its folder out of Mods/ into the
    /// app's removed-mods backup so the game no longer loads it.
    /// Uninstall: moves an installed mod out of Mods/ into removed-mods (reversible).
    public static void Uninstall(GameLayout layout, string modPath)
    {
        if (!IsInside(modPath, layout.ModsPath) || !Directory.Exists(modPath))
        {
            Log.Warn("Mod not found in Mods folder.");
            return;
        }

        var dest = Path.Combine(AppData.RemovedMods, $"{Path.GetFileName(modPath)}_{DateTime.Now:yyyyMMdd_HHmmss}");
        MoveDirectory(modPath, dest);
        Log.Info($"Uninstalled '{Path.GetFileName(modPath)}' (kept in removed-mods).");
    }

    /// Restore: moves a previously uninstalled mod back from removed-mods into Mods/.
    public static void Restore(GameLayout layout, string modPath)
    {
        if (!IsInside(modPath, AppData.RemovedMods) || !Directory.Exists(modPath))
        {
            Log.Warn("Removed mod not found.");
            return;
        }

        var id = ReadId(modPath) ?? Path.GetFileName(modPath);
        Directory.CreateDirectory(layout.ModsPath);
        var target = Path.Combine(layout.ModsPath, id);
        if (Directory.Exists(target))
        {
            Log.Warn($"'{id}' is already installed.");
            return;
        }

        MoveDirectory(modPath, target);
        Log.Info($"Reinstalled '{id}'.");
    }

    /// Remove: permanently deletes a mod's folder (installed or removed). Guarded
    /// so it can only delete inside Mods/ or removed-mods.
    public static void Remove(GameLayout layout, string modPath)
    {
        if ((!IsInside(modPath, layout.ModsPath) && !IsInside(modPath, AppData.RemovedMods)) || !Directory.Exists(modPath))
        {
            Log.Warn("Mod not found.");
            return;
        }

        Directory.Delete(modPath, recursive: true);
        Log.Info($"Permanently removed '{Path.GetFileName(modPath)}'.");
    }

    private static bool IsInside(string path, string root)
    {
        var full = Path.GetFullPath(path);
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return full.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static string? ReadId(string dir)
    {
        var info = FindInfoJson(dir);
        if (info is null)
            return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(info));
            var id = GetString(doc.RootElement, "Id");
            return string.IsNullOrEmpty(id) ? null : id;
        }
        catch
        {
            return null;
        }
    }

    /// Downloads a mod zip from a URL into the cache, then installs it.
    public static async Task InstallFromUrlAsync(GameLayout layout, string url)
    {
        var cacheDir = Path.Combine(AppData.Cache, "recommended");
        Directory.CreateDirectory(cacheDir);

        var name = Path.GetFileName(new Uri(url).AbsolutePath);
        if (string.IsNullOrEmpty(name)) name = "mod";
        if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) name += ".zip";
        var zipPath = Path.Combine(cacheDir, name);

        Log.Info($"Downloading {name}...");
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("adofai-umm-mac/1.0");
            var bytes = await client.GetByteArrayAsync(url);
            await File.WriteAllBytesAsync(zipPath, bytes);
        }

        Install(layout, zipPath);
    }

    public static void Install(GameLayout layout, string zipPath)
    {
        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Zip not found: " + zipPath);

        Directory.CreateDirectory(layout.ModsPath);
        using var zip = ZipFile.OpenRead(zipPath);

        var infoEntry = zip.Entries.FirstOrDefault(e =>
            EntryFileName(e).Equals("Info.json", StringComparison.OrdinalIgnoreCase) && !IsJunk(e));
        if (infoEntry is null)
        {
            var listing = string.Join(", ", zip.Entries
                .Select(e => e.FullName).Where(n => !string.IsNullOrEmpty(n)).Take(15));
            throw new InvalidOperationException(
                $"No Info.json found in zip. Zip contains: {listing}");
        }

        var infoDir = DirectoryOf(Normalize(infoEntry.FullName)); // "" | "ModName" | "Wrapper/ModName"

        // For root-level Info.json we wrap everything in a folder named after the
        // mod Id. Otherwise we strip any wrapper above the mod folder so it lands
        // at Mods/<ModName>/... regardless of how the zip was nested.
        var idFolder = "";
        var stripPrefix = "";
        if (infoDir.Length == 0)
        {
            using var reader = new StreamReader(infoEntry.Open());
            using var doc = JsonDocument.Parse(reader.ReadToEnd());
            idFolder = GetString(doc.RootElement, "Id");
            if (string.IsNullOrEmpty(idFolder))
                idFolder = Path.GetFileNameWithoutExtension(zipPath);
        }
        else
        {
            var slash = infoDir.LastIndexOf('/');
            stripPrefix = slash >= 0 ? infoDir[..(slash + 1)] : "";
        }

        var count = 0;
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || IsJunk(entry))
                continue;

            var rel = Normalize(entry.FullName);
            if (stripPrefix.Length > 0 && rel.StartsWith(stripPrefix, StringComparison.OrdinalIgnoreCase))
                rel = rel[stripPrefix.Length..];

            var baseDir = idFolder.Length > 0 ? Path.Combine(layout.ModsPath, idFolder) : layout.ModsPath;
            var dest = Path.Combine(baseDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            count++;
        }

        Log.Info($"Installed {count} file(s) from {Path.GetFileName(zipPath)}");
    }

    private static void MoveDirectory(string src, string dest)
    {
        try
        {
            Directory.Move(src, dest);
        }
        catch (IOException)
        {
            // Cross-volume (e.g. Steam library on another drive): copy then delete.
            CopyDirectory(src, dest);
            Directory.Delete(src, recursive: true);
        }
    }

    private static void CopyDirectory(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(dest, Path.GetRelativePath(src, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');

    private static string EntryFileName(ZipArchiveEntry entry)
    {
        var name = Normalize(entry.FullName).TrimEnd('/');
        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }

    private static string DirectoryOf(string normalizedFullName)
    {
        var slash = normalizedFullName.LastIndexOf('/');
        return slash >= 0 ? normalizedFullName[..slash] : "";
    }

    private static bool IsJunk(ZipArchiveEntry entry)
    {
        var full = Normalize(entry.FullName);
        var name = EntryFileName(entry);
        return full.StartsWith("__MACOSX/", StringComparison.Ordinal)
            || name.StartsWith("._", StringComparison.Ordinal)
            || name == ".DS_Store";
    }

    private static string? FindInfoJson(string dir)
    {
        var direct = Path.Combine(dir, "Info.json");
        if (File.Exists(direct))
            return direct;

        return Directory.EnumerateFiles(dir, "*.json", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f => Path.GetFileName(f).Equals("Info.json", StringComparison.OrdinalIgnoreCase));
    }

    private static string GetString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
}
