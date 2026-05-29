using System;
using System.IO;
using UnityEngine;
using UnityModManagerNet;

namespace MacQOL
{
    [Serializable]
    public class Settings : UnityModManager.ModSettings
    {
        public bool WorkshopFixEnabled = true;
        public bool FunctionKeyFixEnabled = false;

        public override void Save(UnityModManager.ModEntry modEntry)
        {
            Save(this, modEntry);
        }

        public static Settings Load(UnityModManager.ModEntry modEntry)
        {
            return Load<Settings>(modEntry) ?? new Settings();
        }
    }

    public static class Main
    {
        private static Settings settings;
        private static UnityModManager.ModEntry mod;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;
            settings = Settings.Load(modEntry);
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            WriteBridgeConfig();
            modEntry.Logger.Log("Mac QOL config loaded.");
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Mac QOL Built-in Configuration", GUILayout.ExpandWidth(false));
            GUILayout.Space(5f);

            settings.WorkshopFixEnabled = GUILayout.Toggle(
                settings.WorkshopFixEnabled,
                "Enable Workshop crash/browser fixes",
                GUILayout.ExpandWidth(false));

            settings.FunctionKeyFixEnabled = GUILayout.Toggle(
                settings.FunctionKeyFixEnabled,
                "Enable Function-key gameplay fallback",
                GUILayout.ExpandWidth(false));

            GUILayout.Space(8f);
            GUILayout.Label("Save + restart game for changes to apply.", GUILayout.ExpandWidth(false));
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry)
        {
            settings.Save(modEntry);
            WriteBridgeConfig();
            modEntry.Logger.Log("Saved. Restart game to apply changes.");
        }

        private static void WriteBridgeConfig()
        {
            try
            {
                var path = Path.Combine(mod.Path, "MacQOL.config.json");
                var json = "{\n" +
                           "  \"WorkshopFixEnabled\": " + (settings.WorkshopFixEnabled ? "true" : "false") + ",\n" +
                           "  \"FunctionKeyFixEnabled\": " + (settings.FunctionKeyFixEnabled ? "true" : "false") + "\n" +
                           "}\n";
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                mod?.Logger?.Error("Failed to write MacQOL.config.json: " + ex.Message);
            }
        }
    }
}
