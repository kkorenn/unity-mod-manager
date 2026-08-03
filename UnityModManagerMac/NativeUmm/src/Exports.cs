using System.Buffers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using dnlib.DotNet;

namespace NativeUmm;

/// Native entry points consumed by the Swift app via the bridging header.
/// One call in, one JSON string out. All reflection-free so it survives NativeAOT.
public static unsafe class Exports
{
    /// int -> char*  : umm_run(const char *requestJson)
    /// Request : { "action": "status|install|remove|restore|mods|installmod|logtail",
    ///             "gamePath": "...optional...",
    ///             "payloadDir": "...optional bundle Resources/Payload...",
    ///             "zipPath": "...for installmod...",
    ///             "forcePayload": true|false }
    /// Response is always JSON. Caller must umm_free the result.
    [UnmanagedCallersOnly(EntryPoint = "umm_run")]
    public static byte* Run(byte* requestUtf8)
    {
        string response;
        try
        {
            var request = Marshal.PtrToStringUTF8((nint)requestUtf8) ?? "{}";
            response = Handle(request);
        }
        catch (Exception ex)
        {
            response = ErrorJson(ex.Message);
        }

        return (byte*)Marshal.StringToCoTaskMemUTF8(response);
    }

    [UnmanagedCallersOnly(EntryPoint = "umm_free")]
    public static void Free(byte* pointer) => Marshal.FreeCoTaskMem((nint)pointer);

    private static string Handle(string requestJson)
    {
        string? action;
        string? gamePath;
        string? payloadDir;
        string? zipPath;
        string? modPath;
        var urls = new List<string>();
        bool forcePayload;

        using (var doc = JsonDocument.Parse(requestJson))
        {
            var root = doc.RootElement;
            action = root.TryGetProperty("action", out var a) ? a.GetString() : null;
            gamePath = root.TryGetProperty("gamePath", out var g) ? g.GetString() : null;
            payloadDir = root.TryGetProperty("payloadDir", out var p) ? p.GetString() : null;
            zipPath = root.TryGetProperty("zipPath", out var z) ? z.GetString() : null;
            modPath = root.TryGetProperty("path", out var pp) ? pp.GetString() : null;
            forcePayload = root.TryGetProperty("forcePayload", out var f) && f.ValueKind == JsonValueKind.True;
            if (root.TryGetProperty("urls", out var arr) && arr.ValueKind == JsonValueKind.Array)
                foreach (var item in arr.EnumerateArray())
                {
                    var u = item.GetString();
                    if (!string.IsNullOrWhiteSpace(u)) urls.Add(u);
                }
        }

        Log.Begin();

        var layout = GameLayout.Detect(string.IsNullOrWhiteSpace(gamePath) ? null : gamePath);
        if (layout is null)
            return BuildResponse(null, null, null, null, detected: false,
                error: "A Dance of Fire and Ice was not found. Select the game .app.");

        var installer = new Installer(layout);
        var bundledVersion = BundledVersion(payloadDir);

        switch (action)
        {
            case "status":
                break;
            case "install":
                installer.InstallAsync(payloadDir, forcePayload).GetAwaiter().GetResult();
                break;
            case "remove":
                installer.RemoveHook();
                break;
            case "restore":
                installer.RestoreOriginal();
                break;
            case "mods":
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
            case "installmod":
                if (string.IsNullOrWhiteSpace(zipPath))
                    return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: "No zip path provided.");
                ModManager.Install(layout, zipPath);
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
            case "installmods":
                foreach (var url in urls)
                {
                    try { ModManager.InstallFromUrlAsync(layout, url).GetAwaiter().GetResult(); }
                    catch (Exception ex) { Log.Fail($"Failed to install {url}: {ex.Message}"); }
                }
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
            case "uninstallmod":
                if (!string.IsNullOrWhiteSpace(modPath))
                    ModManager.Uninstall(layout, modPath);
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
            case "restoremod":
                if (!string.IsNullOrWhiteSpace(modPath))
                    ModManager.Restore(layout, modPath);
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
            case "removemod":
                if (!string.IsNullOrWhiteSpace(modPath))
                    ModManager.Remove(layout, modPath);
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
            case "logtail":
                return BuildLogTail(layout);
            default:
                return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: $"Unknown action: {action}");
        }

        return BuildResponse(layout, installer.ReadStatus(), bundledVersion, ModManager.List(layout), detected: true, error: null);
    }

    private static string BuildResponse(GameLayout? layout, InstallStatus? status, string? bundledVersion,
        List<ModInfo>? mods, bool detected, string? error)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteBoolean("ok", error is null);
            w.WriteBoolean("detected", detected);
            if (error is not null)
                w.WriteString("error", error);
            if (bundledVersion is not null)
                w.WriteString("bundledVersion", bundledVersion);

            w.WriteStartObject("status");
            if (layout is not null)
            {
                w.WriteString("gameRoot", layout.GameRoot);
                w.WriteString("appPath", layout.AppPath);
                w.WriteString("managedPath", layout.ManagedPath);
                w.WriteString("modsPath", layout.ModsPath);
                w.WriteString("processArch", RuntimeInformation.ProcessArchitecture.ToString());
                w.WriteString("gameArch", SafeArch(layout));
            }
            if (status is not null)
            {
                w.WriteBoolean("hookInstalled", status.HookInstalled);
                w.WriteBoolean("managerInstalled", status.ManagerInstalled);
                if (status.ManagerVersion is not null)
                    w.WriteString("managerVersion", status.ManagerVersion);
                w.WriteBoolean("hasBackup", status.HasOriginalBackup);
                w.WriteString("backupPath", status.OriginalBackupPath);
                if (status.Warning is not null)
                    w.WriteString("warning", status.Warning);
            }
            w.WriteEndObject();

            if (mods is not null)
            {
                w.WriteStartArray("mods");
                foreach (var mod in mods)
                {
                    w.WriteStartObject();
                    w.WriteString("id", mod.Id);
                    w.WriteString("name", mod.DisplayName);
                    w.WriteString("version", mod.Version);
                    if (mod.ManagerVersion is not null)
                        w.WriteString("managerVersion", mod.ManagerVersion);
                    if (mod.HomePage is not null)
                        w.WriteString("homePage", mod.HomePage);
                    w.WriteString("path", mod.Path);
                    w.WriteString("status", mod.Status);
                    w.WriteBoolean("installed", mod.Installed);
                    if (mod.Requirements.Count > 0)
                    {
                        w.WriteStartArray("requirements");
                        foreach (var req in mod.Requirements)
                        {
                            w.WriteStartObject();
                            w.WriteString("id", req.Id);
                            if (req.Version is not null)
                                w.WriteString("version", req.Version);
                            w.WriteString("state", req.State);
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            }

            WriteLog(w);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static string BuildLogTail(GameLayout layout)
    {
        var logPath = Path.Combine(layout.ManagerPath, "Log.txt");
        var text = File.Exists(logPath)
            ? string.Join("\n", File.ReadLines(logPath).TakeLast(400))
            : "No UnityModManager/Log.txt found.";

        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteBoolean("ok", true);
            w.WriteBoolean("detected", true);
            w.WriteString("logText", text);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void WriteLog(Utf8JsonWriter w)
    {
        w.WriteStartArray("log");
        foreach (var line in Log.Collect())
        {
            w.WriteStartObject();
            w.WriteString("level", line.Level);
            w.WriteString("message", line.Message);
            w.WriteEndObject();
        }
        w.WriteEndArray();
    }

    /// Version of the UMM payload bundled in the app (what install would write).
    private static string? BundledVersion(string? payloadDir)
    {
        if (string.IsNullOrWhiteSpace(payloadDir))
            return null;
        var dll = Path.Combine(payloadDir, "UnityModManager.dll");
        if (!File.Exists(dll))
            return null;
        try
        {
            using var module = ModuleDefMD.Load(File.ReadAllBytes(dll));
            return module.Assembly?.Version?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string SafeArch(GameLayout layout)
    {
        try { return layout.ReadExecutableArchitectures(); }
        catch { return "unknown"; }
    }

    private static string ErrorJson(string message)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer))
        {
            w.WriteStartObject();
            w.WriteBoolean("ok", false);
            w.WriteBoolean("detected", false);
            w.WriteString("error", message);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
