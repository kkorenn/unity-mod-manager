using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using TinyJson;
using UnityEngine;
using UnityEngine.UI;

namespace UnityModManagerNet
{
    public partial class UnityModManager
    {
        internal static class MacQolBuiltin
        {
            private const string Prefix = "[Mac QOL] ";
            private const string HarmonyId = "com.codex.macqol.builtin";
            private const string WorkshopUrl = "https://steamcommunity.com/app/977950/workshop/";
            private const string ModId = "MacQOL";
            private const string ConfigFileName = "MacQOL.config.json";

            private static readonly object patchLock = new object();

            [Serializable]
            private class MacQolConfig
            {
                public bool WorkshopFixEnabled = true;
                public bool FunctionKeyFixEnabled = true;
            }

            private static Harmony harmony;
            private static bool initialized;
            private static bool adofaiPatchesApplied;
            private static bool asyncHookBlocked;
            private static bool asyncHookBlockedLogged;
            private static bool asyncInputForcedLogged;
            private static bool openedFallbackUrl;
            private static Sprite fallbackSprite;
            private static MacQolConfig config = new MacQolConfig();
            private static readonly Regex asyncInputTrueRegex = new Regex("\"useAsynchronousInput\"\\s*:\\s*true", RegexOptions.Compiled);
            private static readonly Dictionary<string, KeyCode> keyLabelOverrides = new Dictionary<string, KeyCode>(StringComparer.OrdinalIgnoreCase)
            {
                { "Tab", KeyCode.Tab },
                { "39", KeyCode.Tab },
                { "1", KeyCode.F1 },
                { "2", KeyCode.F2 },
                { "3", KeyCode.F3 },
                { "4", KeyCode.F4 },
                { "5", KeyCode.F5 },
                { "6", KeyCode.F6 },
                { "7", KeyCode.F7 },
                { "8", KeyCode.F8 },
                { "9", KeyCode.F9 },
                { "10", KeyCode.F10 },
                { "11", KeyCode.F11 },
                { "12", KeyCode.F12 },
                { "13", KeyCode.F13 },
                { "14", KeyCode.F14 },
                { "15", KeyCode.F15 },
                { "16", KeyCode.F16 },
                { "17", KeyCode.F17 },
                { "18", KeyCode.F18 },
                { "19", KeyCode.F19 },
                { "20", KeyCode.F20 },
                { "21", KeyCode.F21 },
                { "22", KeyCode.F22 },
                { "23", KeyCode.F23 },
                { "24", KeyCode.F24 },
                { "LeftBrace", KeyCode.LeftBracket },
                { "RightBrace", KeyCode.RightBracket },
                { "BackSlash", KeyCode.Backslash },
                { "Apostrophe", KeyCode.Quote },
                { "LShift", KeyCode.LeftShift },
                { "RShift", KeyCode.RightShift },
                { "LControl", KeyCode.LeftControl },
                { "RControl", KeyCode.RightControl },
                { "LAlt", KeyCode.LeftAlt },
                { "RAlt", KeyCode.RightAlt },
                { "ArrowUp", KeyCode.UpArrow },
                { "ArrowDown", KeyCode.DownArrow },
                { "ArrowLeft", KeyCode.LeftArrow },
                { "ArrowRight", KeyCode.RightArrow },
                { "KeypadSlash", KeyCode.KeypadDivide },
                { "KeypadAsterisk", KeyCode.KeypadMultiply },
                { "KeypadDot", KeyCode.KeypadPeriod },
            };

            internal static void Initialize()
            {
                if (initialized)
                {
                    return;
                }

                initialized = true;

                if (!IsMacRuntime())
                {
                    return;
                }

                try
                {
                    LoadConfiguration();
                    harmony = new Harmony(HarmonyId);
                    PatchCoreMethods();
                    AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
                    TryPatchAdofaiTweaks();
                    Log("Built-in patches are active. " +
                        $"WorkshopFixEnabled={config.WorkshopFixEnabled}, " +
                        $"FunctionKeyFixEnabled={config.FunctionKeyFixEnabled}.");
                }
                catch (Exception ex)
                {
                    Error("Initialize failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static void PatchCoreMethods()
            {
                Patch("ADOStartup:LogMessageReceived", nameof(LogMessageReceivedPrefix), null, "Mac GPU warning suppressor");

                if (config.WorkshopFixEnabled)
                {
                    Patch("scnCLS:CreateFloors", null, nameof(CreateFloorsFinalizer), "workshop floor crash guard");
                    Patch("scnCLS:WorkshopLevelsPortal", null, nameof(WorkshopPortalFinalizer), "workshop browser fallback");
                }
                else
                {
                    Log("Workshop fixes are disabled by config.");
                }

                if (config.FunctionKeyFixEnabled)
                {
                    Patch("AsyncInputManager:StartHook", nameof(AsyncInputStartHookPrefix), nameof(AsyncInputStartHookFinalizer), "async input permission guard");
                    Patch("AsyncInputManager:ToggleHook", nameof(AsyncInputToggleHookPrefix), nameof(AsyncInputToggleHookFinalizer), "async input toggle guard");
                    Patch("Persistence:GetChosenAsynchronousInput", nameof(PersistenceGetChosenAsynchronousInputPrefix), null, "async input preference guard");
                    Patch("Persistence:SetChosenAsynchronousInput", nameof(PersistenceSetChosenAsynchronousInputPrefix), null, "async input persistence guard");
                    Patch("RDInputType_AsyncKeyboard:CheckKeyState", nameof(AsyncKeyboardCheckKeyStatePrefix), null, "async keyboard fallback mapping");

                    var keyLabelType = AccessTools.TypeByName("SkyHook.KeyLabel");
                    if (keyLabelType != null)
                    {
                        var keyArgs = new[] { keyLabelType, typeof(bool) };
                        Patch("AsyncInput:GetKey", keyArgs, nameof(AsyncInputGetKeyPrefix), null, "async get-key fallback mapping");
                        Patch("AsyncInput:GetKeyDown", keyArgs, nameof(AsyncInputGetKeyDownPrefix), null, "async get-keydown fallback mapping");
                        Patch("AsyncInput:GetKeyUp", keyArgs, nameof(AsyncInputGetKeyUpPrefix), null, "async get-keyup fallback mapping");
                    }
                }
                else
                {
                    Log("Function-key fallback fixes are disabled by config.");
                }
            }

            private static void Patch(string targetMethod, string prefixName, string finalizerName, string label)
            {
                Patch(targetMethod, null, prefixName, finalizerName, label);
            }

            private static void Patch(string targetMethod, Type[] argumentTypes, string prefixName, string finalizerName, string label)
            {
                try
                {
                    var method = argumentTypes != null
                        ? AccessTools.Method(targetMethod, argumentTypes)
                        : AccessTools.Method(targetMethod);
                    if (method == null)
                    {
                        Log("Skip patch (" + label + "): method not found " + targetMethod);
                        return;
                    }

                    HarmonyMethod prefix = null;
                    HarmonyMethod finalizer = null;

                    if (!string.IsNullOrEmpty(prefixName))
                    {
                        prefix = new HarmonyMethod(AccessTools.Method(typeof(MacQolBuiltin), prefixName));
                    }

                    if (!string.IsNullOrEmpty(finalizerName))
                    {
                        finalizer = new HarmonyMethod(AccessTools.Method(typeof(MacQolBuiltin), finalizerName));
                    }

                    harmony.Patch(method, prefix: prefix, finalizer: finalizer);
                    Log("Patched " + label + ".");
                }
                catch (Exception ex)
                {
                    Error("Patch failed (" + label + "): " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
            {
                try
                {
                    var name = args.LoadedAssembly?.GetName()?.Name ?? string.Empty;
                    if (name.Equals("AdofaiTweaks", StringComparison.OrdinalIgnoreCase))
                    {
                        TryPatchAdofaiTweaks();
                    }
                }
                catch (Exception ex)
                {
                    Error("Assembly load watcher failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static void TryPatchAdofaiTweaks()
            {
                lock (patchLock)
                {
                    if (adofaiPatchesApplied || harmony == null)
                    {
                        return;
                    }

                    var awake = AccessTools.Method("AdofaiTweaks.Tweaks.JudgmentVisuals.HitErrorMeter:Awake");
                    var generate = AccessTools.Method("AdofaiTweaks.Tweaks.JudgmentVisuals.HitErrorMeter:GenerateMeterPng");
                    if (awake == null || generate == null)
                    {
                        return;
                    }

                    harmony.Patch(
                        awake,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(MacQolBuiltin), nameof(HitErrorMeterAwakePrefix)))
                    );
                    harmony.Patch(
                        generate,
                        prefix: new HarmonyMethod(AccessTools.Method(typeof(MacQolBuiltin), nameof(HitErrorMeterGeneratePrefix)))
                    );
                    adofaiPatchesApplied = true;
                    Log("Patched AdofaiTweaks Judgment Visuals for macOS-safe startup.");
                }
            }

            private static void Log(string message)
            {
                Logger.Log(message, Prefix);
            }

            private static void Error(string message)
            {
                Logger.Error(message, Prefix + "[Error] ");
            }

            private static bool IsMacRuntime()
            {
                return Application.platform == RuntimePlatform.OSXPlayer
                    || Application.platform == RuntimePlatform.OSXEditor;
            }

            private static void LoadConfiguration()
            {
                var configPath = GetConfigPath();

                try
                {
                    var configDir = Path.GetDirectoryName(configPath);
                    if (!string.IsNullOrEmpty(configDir))
                    {
                        Directory.CreateDirectory(configDir);
                    }

                    if (File.Exists(configPath))
                    {
                        var loaded = JSONParser.FromJson<MacQolConfig>(File.ReadAllText(configPath));
                        config = loaded ?? new MacQolConfig();
                    }
                    else
                    {
                        config = new MacQolConfig();
                        File.WriteAllText(configPath, JSONWriter.ToJson(config));
                    }
                }
                catch (Exception ex)
                {
                    config = new MacQolConfig();
                    Error("Failed to load config, using defaults. " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static string GetConfigPath()
            {
                return Path.Combine(modsPath, ModId, ConfigFileName);
            }

            private static void ForceAsyncInputPreferenceOffOnDisk()
            {
                if (!IsMacRuntime() || !config.FunctionKeyFixEnabled || !asyncHookBlocked)
                {
                    return;
                }

                try
                {
                    var gameRoot = GetGameRootPath();
                    if (string.IsNullOrEmpty(gameRoot))
                    {
                        return;
                    }

                    var userDir = Path.Combine(gameRoot, "User");
                    var dataFiles = new[]
                    {
                        Path.Combine(userDir, "data.sav"),
                        Path.Combine(userDir, "data.sav.old"),
                    };

                    var changed = false;
                    foreach (var path in dataFiles)
                    {
                        if (!File.Exists(path))
                        {
                            continue;
                        }

                        var text = File.ReadAllText(path);
                        if (!asyncInputTrueRegex.IsMatch(text))
                        {
                            continue;
                        }

                        File.WriteAllText(path, asyncInputTrueRegex.Replace(text, "\"useAsynchronousInput\":false"));
                        changed = true;
                    }

                    if (changed)
                    {
                        Log("Forced useAsynchronousInput=false in User data files for macOS compatibility.");
                    }
                }
                catch (Exception ex)
                {
                    Error("Failed to force async input preference off on disk. " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static string GetGameRootPath()
            {
                try
                {
                    if (string.IsNullOrEmpty(modsPath))
                    {
                        return null;
                    }

                    var directory = Directory.GetParent(modsPath);
                    return directory?.FullName;
                }
                catch
                {
                    return null;
                }
            }

            private static bool IsSkyHookPermissionIssue(Exception ex)
            {
                while (ex != null)
                {
                    var typeName = ex.GetType().FullName ?? string.Empty;
                    var message = ex.Message ?? string.Empty;
                    if ((typeName.IndexOf("SkyHookException", StringComparison.OrdinalIgnoreCase) >= 0
                        || message.IndexOf("SkyHook", StringComparison.OrdinalIgnoreCase) >= 0)
                        && message.IndexOf("NOT_PERMITTED", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }

                    ex = ex.InnerException;
                }

                return false;
            }

            private static void BlockAsyncHookForSession(Exception reason)
            {
                asyncHookBlocked = true;
                if (asyncHookBlockedLogged)
                {
                    return;
                }

                asyncHookBlockedLogged = true;
                Log("Async input hook blocked for this session due to macOS permission error. Falling back to regular input. " +
                    "Reason: " + reason?.GetType().Name + ": " + reason?.Message);
                // Skip StopHook — the hook never started successfully, so StopHook throws TargetInvocationException.
                SafeInvokeStatic("AsyncInputManager:ResetHookMetadata");
                ForceAsyncInputPreferenceOffOnDisk();
            }

            private static void SafeInvokeStatic(string methodName)
            {
                try
                {
                    AccessTools.Method(methodName)?.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    Error("SafeInvokeStatic failed for " + methodName + ": " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static bool LogMessageReceivedPrefix(string logString)
            {
                if (!IsMacRuntime() || logString == null)
                {
                    return true;
                }

                // Suppress harmless Mac GPU warnings that cascade into TMP NullReferenceExceptions.
                // When ADOStartup.LogMessageReceived fires during asset bundle loading, it spawns a
                // toast UI containing TMP_Text whose Awake() calls TMP_Settings.fallbackFontAssets —
                // but TMP_Settings isn't initialized yet at that point, causing a NRE cascade.
                if (logString.StartsWith("RGBA Compressed BC7 UNorm format is not supported") ||
                    logString.StartsWith("Desired shader compiler platform") ||
                    logString.Contains("TextMeshPro/Mobile/Distance Field shader is not supported") ||
                    logString.Contains("TextMeshPro/Distance Field shader is not supported"))
                {
                    return false;
                }

                return true;
            }

            private static Exception CreateFloorsFinalizer(Exception __exception)
            {
                if (__exception == null)
                {
                    return null;
                }

                if (__exception is ArgumentNullException ane && string.Equals(ane.ParamName, "contents", StringComparison.Ordinal))
                {
                    Log("Suppressed workshop crash in scnCLS.CreateFloors (null contents).");
                    return null;
                }

                return __exception;
            }

            private static Exception WorkshopPortalFinalizer(Exception __exception)
            {
                if (__exception == null)
                {
                    return null;
                }

                if (!openedFallbackUrl)
                {
                    openedFallbackUrl = true;
                    Application.OpenURL(WorkshopUrl);
                    Log("Workshop portal failed in-game. Opened browser fallback: " + WorkshopUrl);
                }

                return null;
            }

            private static bool AsyncInputStartHookPrefix()
            {
                return !IsMacRuntime() || !asyncHookBlocked;
            }

            private static Exception AsyncInputStartHookFinalizer(Exception __exception)
            {
                if (!IsMacRuntime() || __exception == null)
                {
                    return __exception;
                }

                if (IsSkyHookPermissionIssue(__exception))
                {
                    BlockAsyncHookForSession(__exception);
                    return null;
                }

                return __exception;
            }

            private static bool AsyncInputToggleHookPrefix(bool active)
            {
                if (!IsMacRuntime())
                {
                    return true;
                }

                if (active && asyncHookBlocked)
                {
                    return false;
                }

                return true;
            }

            private static Exception AsyncInputToggleHookFinalizer(Exception __exception)
            {
                if (!IsMacRuntime() || __exception == null)
                {
                    return __exception;
                }

                if (IsSkyHookPermissionIssue(__exception))
                {
                    BlockAsyncHookForSession(__exception);
                    return null;
                }

                return __exception;
            }

            private static bool PersistenceGetChosenAsynchronousInputPrefix(ref bool __result)
            {
                if (!IsMacRuntime() || !config.FunctionKeyFixEnabled || !asyncHookBlocked)
                {
                    return true;
                }

                __result = false;
                if (!asyncInputForcedLogged)
                {
                    asyncInputForcedLogged = true;
                    Log("Forcing asynchronous input OFF on macOS for gameplay compatibility.");
                }

                return false;
            }

            private static void PersistenceSetChosenAsynchronousInputPrefix(ref bool enabled)
            {
                if (!IsMacRuntime() || !config.FunctionKeyFixEnabled || !asyncHookBlocked)
                {
                    return;
                }

                enabled = false;
            }

            private static bool AsyncInputGetKeyPrefix(object __0, ref bool __result)
            {
                return TryHandleAsyncKeyState(__0, 2, ref __result);
            }

            private static bool AsyncInputGetKeyDownPrefix(object __0, ref bool __result)
            {
                return TryHandleAsyncKeyState(__0, 0, ref __result);
            }

            private static bool AsyncInputGetKeyUpPrefix(object __0, ref bool __result)
            {
                return TryHandleAsyncKeyState(__0, 1, ref __result);
            }

            private static bool AsyncKeyboardCheckKeyStatePrefix(object __0, object __1, ref bool __result)
            {
                if (!IsMacRuntime())
                {
                    return true;
                }

                var state = ConvertState(__1);
                return TryHandleAsyncKeyState(__0, state, ref __result);
            }

            private static bool TryHandleAsyncKeyState(object key, int state, ref bool __result)
            {
                // Only intervene when the async hook is unavailable for this session.
                // Otherwise keep game/native async behavior intact.
                if (!IsMacRuntime() || !asyncHookBlocked || key == null)
                {
                    return true;
                }

                if (!TryMapKeyLabelToKeyCode(key, out var keyCode))
                {
                    return true;
                }

                switch (state)
                {
                    case 0: // WentDown
                        __result = Input.GetKeyDown(keyCode);
                        return false;
                    case 1: // WentUp
                        __result = Input.GetKeyUp(keyCode);
                        return false;
                    case 2: // IsDown
                        __result = Input.GetKey(keyCode);
                        return false;
                    case 3: // IsUp
                        __result = !Input.GetKey(keyCode);
                        return false;
                    default:
                        return true;
                }
            }

            private static int ConvertState(object state)
            {
                if (state == null)
                {
                    return 2;
                }

                try
                {
                    return Convert.ToInt32(state);
                }
                catch
                {
                    return 2;
                }
            }

            private static bool TryMapKeyLabelToKeyCode(object key, out KeyCode keyCode)
            {
                keyCode = KeyCode.None;

                if (TryMapViaSkyHookMapper(key, out keyCode))
                {
                    return true;
                }

                var keyName = key.ToString();
                if (string.IsNullOrEmpty(keyName))
                {
                    return false;
                }

                if (keyName.IndexOf("tab", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    keyCode = KeyCode.Tab;
                    return true;
                }

                if (keyLabelOverrides.TryGetValue(keyName, out keyCode))
                {
                    return true;
                }

                if (TryConvertToInt32(key, out var numeric))
                {
                    if (numeric >= 1 && numeric <= 24)
                    {
                        keyCode = (KeyCode)((int)KeyCode.F1 + (numeric - 1));
                        return true;
                    }

                    if (numeric == 39)
                    {
                        keyCode = KeyCode.Tab;
                        return true;
                    }
                }

                if (Enum.TryParse(keyName, true, out KeyCode parsed))
                {
                    keyCode = parsed;
                    return true;
                }

                return false;
            }

            private static bool TryMapViaSkyHookMapper(object key, out KeyCode keyCode)
            {
                keyCode = KeyCode.None;

                try
                {
                    var mapperType = AccessTools.TypeByName("SkyHook.AsyncKeyMapper");
                    if (mapperType == null)
                    {
                        return false;
                    }

                    var mapField = AccessTools.Field(mapperType, "AsyncKeyToUnityKeyMap");
                    if (mapField?.GetValue(null) is IDictionary map && map.Contains(key))
                    {
                        var value = map[key];
                        if (value is KeyCode mapped)
                        {
                            keyCode = mapped;
                            return true;
                        }

                        keyCode = (KeyCode)Convert.ToInt32(value);
                        return true;
                    }
                }
                catch
                {
                    // Ignore map lookup failures and continue with fallback mapping.
                }

                return false;
            }

            private static bool TryConvertToInt32(object value, out int result)
            {
                result = 0;
                if (value == null)
                {
                    return false;
                }

                try
                {
                    result = Convert.ToInt32(value);
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            private static bool HitErrorMeterAwakePrefix(object __instance)
            {
                if (!IsMacRuntime())
                {
                    return true;
                }

                BuildSafeHitErrorMeter(__instance);
                return false;
            }

            private static bool HitErrorMeterGeneratePrefix(object __instance)
            {
                if (!IsMacRuntime())
                {
                    return true;
                }

                if (__instance is Component component)
                {
                    var type = __instance.GetType();
                    var wrapper = AccessTools.Field(type, "wrapperObj")?.GetValue(__instance) as GameObject;
                    BuildSafeMeterVisuals(__instance, type, wrapper ?? component.gameObject);
                }

                return false;
            }

            private static void BuildSafeHitErrorMeter(object instance)
            {
                try
                {
                    if (!(instance is Component component))
                    {
                        return;
                    }

                    var type = instance.GetType();
                    var gameObject = component.gameObject;

                    SetStaticField(type, "_instance", instance);

                    var canvas = gameObject.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.sortingOrder = 10000;

                    var scaler = gameObject.AddComponent<CanvasScaler>();
                    SetField(instance, type, "scaler", scaler);

                    var wrapperObj = new GameObject("HitErrorMeterWrapper");
                    wrapperObj.transform.SetParent(component.transform, false);
                    wrapperObj.AddComponent<Canvas>();
                    SetField(instance, type, "wrapperObj", wrapperObj);

                    var wrapperRect = wrapperObj.GetComponent<RectTransform>();
                    SetField(instance, type, "wrapperRectTransform", wrapperRect);
                    wrapperRect.anchoredPosition = new Vector2(0f, -48f);
                    wrapperRect.sizeDelta = new Vector2(334f, 135f);

                    BuildSafeMeterVisuals(instance, type, wrapperObj);

                    var cachedTicks = new GameObject[60];
                    var cachedTweenIds = new string[60];
                    var tweenId = (string)(AccessTools.Field(type, "TWEEN_ID")?.GetValue(instance) ?? "macqol_meter");
                    var tickSprite = GetFallbackSprite();

                    for (var i = 0; i < cachedTicks.Length; i++)
                    {
                        var tickObj = new GameObject("HitErrorTick_" + i);
                        cachedTicks[i] = tickObj;
                        tickObj.transform.SetParent(wrapperObj.transform, false);
                        var tickImage = tickObj.AddComponent<Image>();
                        tickImage.sprite = tickSprite;
                        ConfigureRect(tickImage.rectTransform, 8f, 182f);
                        tickImage.color = Color.clear;
                        cachedTweenIds[i] = tweenId + "_tick_" + i;
                    }

                    SetField(instance, type, "cachedTicks", cachedTicks);
                    SetField(instance, type, "cachedTweenIds", cachedTweenIds);
                    AccessTools.Method(type, "UpdateLayout")?.Invoke(instance, null);
                    gameObject.SetActive(false);
                    Log("Applied safe Judgment Visuals meter initialization.");
                }
                catch (Exception ex)
                {
                    Error("BuildSafeHitErrorMeter failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static void BuildSafeMeterVisuals(object instance, Type type, GameObject wrapperObj)
            {
                try
                {
                    var currentHand = AccessTools.Field(type, "handImage")?.GetValue(instance) as Image;
                    if (currentHand != null)
                    {
                        return;
                    }

                    var sprite = GetFallbackSprite();

                    var meterObj = new GameObject("HitErrorMeterBase");
                    meterObj.transform.SetParent(wrapperObj.transform, false);
                    var meterImage = meterObj.AddComponent<Image>();
                    meterImage.sprite = sprite;
                    meterImage.color = new Color(1f, 1f, 1f, 0.35f);
                    ConfigureRect(meterImage.rectTransform, 400f, 200f);

                    var handObj = new GameObject("HitErrorMeterHand");
                    handObj.transform.SetParent(wrapperObj.transform, false);
                    var handImage = handObj.AddComponent<Image>();
                    handImage.sprite = sprite;
                    handImage.color = Color.white;
                    ConfigureRect(handImage.rectTransform, 30f, 140f);
                    SetField(instance, type, "handImage", handImage);
                }
                catch (Exception ex)
                {
                    Error("BuildSafeMeterVisuals failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            private static void ConfigureRect(RectTransform rectTransform, float width, float height)
            {
                var anchor = new Vector2(0.5f, 0f);
                rectTransform.anchorMin = anchor;
                rectTransform.anchorMax = anchor;
                rectTransform.pivot = anchor;
                rectTransform.anchoredPosition = Vector2.zero;
                rectTransform.sizeDelta = new Vector2(width, height);
            }

            private static void SetField(object instance, Type type, string fieldName, object value)
            {
                AccessTools.Field(type, fieldName)?.SetValue(instance, value);
            }

            private static void SetStaticField(Type type, string fieldName, object value)
            {
                AccessTools.Field(type, fieldName)?.SetValue(null, value);
            }

            private static Sprite GetFallbackSprite()
            {
                if (fallbackSprite != null)
                {
                    return fallbackSprite;
                }

                // UISprite.psd is unavailable in ADOFAI's Unity build and always logs a warning —
                // skip it and go straight to whiteTexture.
                try
                {
                    var texture = Texture2D.whiteTexture;
                    if (texture != null)
                    {
                        fallbackSprite = Sprite.Create(
                            texture,
                            new Rect(0f, 0f, texture.width, texture.height),
                            new Vector2(0.5f, 0.5f),
                            100f);
                    }
                }
                catch (Exception ex)
                {
                    Error("Fallback sprite creation failed: " + ex.GetType().Name + ": " + ex.Message);
                }

                return fallbackSprite;
            }
        }
    }
}
