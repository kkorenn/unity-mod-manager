using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using HarmonyLib;

namespace UnityModManagerNet
{
    public class Injector
    {
        public static void Run(bool doorstop = false)
        {
            if (UnityModManager.initialized)
                return;

            if (DeferredStart.ShouldDefer() && DeferredStart.Schedule(doorstop))
                return;

            try
            {
                _Run(doorstop);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                UnityModManager.OpenUnityFileLog();
            }
        }

        private static bool startUiWithManager;

        private static class DeferredStart
        {
            private static readonly object Lock = new object();
            private static bool pending;
            private static bool pendingDoorstop;
            private static bool sceneHooked;
            private static bool beforeRenderHooked;
            private static bool postQueued;
            private static SynchronizationContext unityContext;

            internal static bool ShouldDefer()
            {
                try
                {
                    return Time.frameCount <= 0;
                }
                catch
                {
                    return false;
                }
            }

            internal static bool Schedule(bool doorstop)
            {
                lock (Lock)
                {
                    if (pending)
                        return true;

                    pending = true;
                    pendingDoorstop = doorstop;
                    unityContext = SynchronizationContext.Current;
                }

                Console.WriteLine("[Manager] Unity frame 0 detected. Deferring mod loading until Unity is ready.");

                var armed = false;

                try
                {
                    SceneManager.sceneLoaded += OnSceneLoaded;
                    sceneHooked = true;
                    armed = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[Manager] SceneManager defer hook failed: " + e.Message);
                }

                try
                {
                    Application.onBeforeRender += OnBeforeRender;
                    beforeRenderHooked = true;
                    armed = true;
                }
                catch (Exception e)
                {
                    Console.WriteLine("[Manager] onBeforeRender defer hook failed: " + e.Message);
                }

                if (unityContext != null)
                {
                    armed = true;
                    QueueContextCheck();
                }

                if (armed)
                    return true;

                lock (Lock)
                {
                    pending = false;
                }
                return false;
            }

            private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
            {
                TryStart("sceneLoaded");
            }

            private static void OnBeforeRender()
            {
                TryStart("onBeforeRender");
            }

            private static void QueueContextCheck()
            {
                lock (Lock)
                {
                    if (!pending || postQueued || unityContext == null)
                        return;

                    postQueued = true;
                }

                ThreadPool.QueueUserWorkItem(_ =>
                {
                    Thread.Sleep(50);
                    var context = unityContext;
                    if (context == null)
                    {
                        lock (Lock)
                        {
                            postQueued = false;
                        }
                        return;
                    }

                    context.Post(__ =>
                    {
                        lock (Lock)
                        {
                            postQueued = false;
                        }
                        TryStart("syncContext");
                    }, null);
                });
            }

            private static void TryStart(string source)
            {
                lock (Lock)
                {
                    if (!pending)
                        return;

                    if (unityContext == null)
                        unityContext = SynchronizationContext.Current;
                }

                if (!IsReady())
                {
                    QueueContextCheck();
                    return;
                }

                bool doorstop;
                lock (Lock)
                {
                    if (!pending)
                        return;

                    pending = false;
                    doorstop = pendingDoorstop;
                }

                Cleanup();
                Console.WriteLine($"[Manager] Deferred mod loading continues after {source} at frame {Time.frameCount}.");
                Run(doorstop);
            }

            private static bool IsReady()
            {
                try
                {
                    return Time.frameCount > 0;
                }
                catch
                {
                    return false;
                }
            }

            private static void Cleanup()
            {
                if (sceneHooked)
                {
                    SceneManager.sceneLoaded -= OnSceneLoaded;
                    sceneHooked = false;
                }

                if (beforeRenderHooked)
                {
                    Application.onBeforeRender -= OnBeforeRender;
                    beforeRenderHooked = false;
                }
            }
        }

        private static void _Run(bool doorstop)
        {
            Console.WriteLine();
            Console.WriteLine();
            UnityModManager.Logger.Log("Injection...");

            if (!UnityModManager.Initialize())
            {
                UnityModManager.Logger.Log($"Cancel start due to an error.");
                UnityModManager.OpenUnityFileLog();
                return;
            }

            Fixes.Apply();

            if (!string.IsNullOrEmpty(UnityModManager.Config.UIStartingPoint) && UnityModManager.Config.UIStartingPoint != UnityModManager.Config.StartingPoint)
            {
                if (TryGetEntryPoint(UnityModManager.Config.UIStartingPoint, out var @class, out var method, out var place))
                {
                    var usePrefix = (place == "before");
                    var harmony = new HarmonyLib.Harmony(nameof(UnityModManager));
                    var prefix = typeof(Injector).GetMethod(nameof(Prefix_Show), BindingFlags.Static | BindingFlags.NonPublic);
                    var postfix = typeof(Injector).GetMethod(nameof(Postfix_Show), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, usePrefix ? new HarmonyMethod(prefix) : null, !usePrefix ? new HarmonyMethod(postfix) : null);
                }
                else
                {
                    UnityModManager.OpenUnityFileLog();
                    return;
                }
            }
            else
            {
                startUiWithManager = true;
            }

            if (!string.IsNullOrEmpty(UnityModManager.Config.StartingPoint))
            {
                if (!doorstop && UnityModManager.Config.StartingPoint == UnityModManager.Config.EntryPoint)
                {
                    UnityModManager.Start();
                    if (startUiWithManager)
                    {
                        RunUI();
                    }
                }
                else
                {
                    if (TryGetEntryPoint(UnityModManager.Config.StartingPoint, out var @class, out var method, out var place))
                    {
                        var usePrefix = (place == "before");
                        var harmony = new HarmonyLib.Harmony(nameof(UnityModManager));
                        var prefix = typeof(Injector).GetMethod(nameof(Prefix_Start), BindingFlags.Static | BindingFlags.NonPublic);
                        var postfix = typeof(Injector).GetMethod(nameof(Postfix_Start), BindingFlags.Static | BindingFlags.NonPublic);
                        harmony.Patch(method, usePrefix ? new HarmonyMethod(prefix) : null, !usePrefix ? new HarmonyMethod(postfix) : null);
                        UnityModManager.Logger.Log("Injection successful.");
                    }
                    else
                    {
                        UnityModManager.Logger.Log("Injection canceled.");
                        UnityModManager.OpenUnityFileLog();
                        return;
                    }
                }
            }
            else
            {
                if (startUiWithManager)
                {
                    UnityModManager.Logger.Error($"Can't start UI. UIStartingPoint is not defined.");
                    UnityModManager.OpenUnityFileLog();
                    return;
                }
                UnityModManager.Start();
            }

            if (!string.IsNullOrEmpty(UnityModManager.Config.TextureReplacingPoint))
            {
                if (TryGetEntryPoint(UnityModManager.Config.TextureReplacingPoint, out var @class, out var method, out var place))
                {
                    var usePrefix = (place == "before");
                    var harmony = new HarmonyLib.Harmony(nameof(UnityModManager));
                    var prefix = typeof(Injector).GetMethod(nameof(Prefix_TextureReplacing), BindingFlags.Static | BindingFlags.NonPublic);
                    var postfix = typeof(Injector).GetMethod(nameof(Postfix_TextureReplacing), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, usePrefix ? new HarmonyMethod(prefix) : null, !usePrefix ? new HarmonyMethod(postfix) : null);
                }
                else
                {
                    UnityModManager.OpenUnityFileLog();
                }
            }

            if (!string.IsNullOrEmpty(UnityModManager.Config.SessionStartPoint))
            {
                if (TryGetEntryPoint(UnityModManager.Config.SessionStartPoint, out var @class, out var method, out var place))
                {
                    var usePrefix = (place == "before");
                    var harmony = new HarmonyLib.Harmony(nameof(UnityModManager));
                    var prefix = typeof(Injector).GetMethod(nameof(Prefix_SessionStart), BindingFlags.Static | BindingFlags.NonPublic);
                    var postfix = typeof(Injector).GetMethod(nameof(Postfix_SessionStart), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, usePrefix ? new HarmonyMethod(prefix) : null, !usePrefix ? new HarmonyMethod(postfix) : null);
                }
                else
                {
                    UnityModManager.Config.SessionStartPoint = null;
                    UnityModManager.OpenUnityFileLog();
                }
            }

            if (!string.IsNullOrEmpty(UnityModManager.Config.SessionStopPoint))
            {
                if (TryGetEntryPoint(UnityModManager.Config.SessionStopPoint, out var @class, out var method, out var place))
                {
                    var usePrefix = (place == "before");
                    var harmony = new HarmonyLib.Harmony(nameof(UnityModManager));
                    var prefix = typeof(Injector).GetMethod(nameof(Prefix_SessionStop), BindingFlags.Static | BindingFlags.NonPublic);
                    var postfix = typeof(Injector).GetMethod(nameof(Postfix_SessionStop), BindingFlags.Static | BindingFlags.NonPublic);
                    harmony.Patch(method, usePrefix ? new HarmonyMethod(prefix) : null, !usePrefix ? new HarmonyMethod(postfix) : null);
                }
                else
                {
                    UnityModManager.Config.SessionStopPoint = null;
                    UnityModManager.OpenUnityFileLog();
                }
            }
        }

        static void RunUI()
        {
            if (!UnityModManager.UI.Load())
            {
                UnityModManager.Logger.Error($"Can't load UI.");
                return;
            }
            if (!UnityModManager.UI.Instance)
            {
                UnityModManager.Logger.Error("UnityModManager.UI does not exist.");
                return;
            }
        }

        static void Prefix_Start()
        {
            UnityModManager.Start();
            if (startUiWithManager)
            {
                RunUI();
            }
        }

        static void Postfix_Start()
        {
            UnityModManager.Start();
            if (startUiWithManager)
            {
                RunUI();
            }
        }

        static void Prefix_Show()
        {
            if (!UnityModManager.UI.Load())
            {
                UnityModManager.Logger.Error($"Can't load UI.");
            }
            if (!UnityModManager.UI.Instance)
            {
                UnityModManager.Logger.Error("UnityModManager.UI does not exist.");
                return;
            }
            UnityModManager.UI.Instance.FirstLaunch();
        }

        static void Postfix_Show()
        {
            if (!UnityModManager.UI.Load())
            {
                UnityModManager.Logger.Error($"Can't load UI.");
            }
            if (!UnityModManager.UI.Instance)
            {
                UnityModManager.Logger.Error("UnityModManager.UI does not exist.");
                return;
            }
            UnityModManager.UI.Instance.FirstLaunch();
        }

        static void Prefix_TextureReplacing()
        {
            //UnityModManager.ApplySkins();
        }

        static void Postfix_TextureReplacing()
        {
            //UnityModManager.ApplySkins();
        }

        static void Prefix_SessionStart()
        {
            foreach (var mod in UnityModManager.modEntries)
            {
                if (mod.Active && mod.OnSessionStart != null)
                {
                    try
                    {
                        mod.OnSessionStart.Invoke(mod);
                    }
                    catch (Exception e)
                    {
                        mod.Logger.LogException("OnSessionStart", e);
                    }
                }
            }
        }

        static void Postfix_SessionStart()
        {
            Prefix_SessionStart();
        }

        static void Prefix_SessionStop()
        {
            foreach (var mod in UnityModManager.modEntries)
            {
                if (mod.Active && mod.OnSessionStop != null)
                {
                    try
                    {
                        mod.OnSessionStop.Invoke(mod);
                    }
                    catch (Exception e)
                    {
                        mod.Logger.LogException("OnSessionStop", e);
                    }
                }
            }
        }

        static void Postfix_SessionStop()
        {
            Prefix_SessionStop();
        }

        internal static bool TryGetEntryPoint(string str, out Type foundClass, out MethodInfo foundMethod, out string insertionPlace)
        {
            foundClass = null;
            foundMethod = null;
            insertionPlace = null;
            
            if (TryParseEntryPoint(str, out string assemblyName, out _, out _, out _))
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (assembly.ManifestModule.Name == assemblyName)
                    {
                        return TryGetEntryPoint(assembly, str, out foundClass, out foundMethod, out insertionPlace);
                    }
                }
                try
                {
                    var asm = Assembly.Load(assemblyName);
                    return TryGetEntryPoint(asm, str, out foundClass, out foundMethod, out insertionPlace);
                }
                catch (Exception e)
                {
                    UnityModManager.Logger.Error($"File '{assemblyName}' cant't be loaded.");
                    UnityModManager.Logger.LogException(e);
                }

                return false;
            }

            return false;
        }

        internal static bool TryGetEntryPoint(Assembly assembly, string str, out Type foundClass, out MethodInfo foundMethod, out string insertionPlace)
        {
            foundClass = null;
            foundMethod = null;

            if (!TryParseEntryPoint(str, out _, out var className, out var methodName, out insertionPlace))
            {
                return false;
            }

            foundClass = assembly.GetType(className);
            if (foundClass == null)
            {
                UnityModManager.Logger.Error($"Class '{className}' not found.");
                return false;
            }

            foundMethod = foundClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (foundMethod == null)
            {
                UnityModManager.Logger.Error($"Method '{methodName}' not found.");
                return false;
            }

            return true;
        }

        internal static bool TryParseEntryPoint(string str, out string assembly, out string @class, out string method, out string insertionPlace)
        {
            assembly = string.Empty;
            @class = string.Empty;
            method = string.Empty;
            insertionPlace = string.Empty;

            var regex = new Regex(@"(?:(?<=\[)(?'assembly'.+(?>\.dll))(?=\]))|(?:(?'class'[\w|\.]+)(?=\.))|(?:(?<=\.)(?'func'\w+))|(?:(?<=\:)(?'mod'\w+))", RegexOptions.IgnoreCase);
            var matches = regex.Matches(str);
            var groupNames = regex.GetGroupNames();

            if (matches.Count > 0)
            {
                foreach (Match match in matches)
                {
                    foreach (var group in groupNames)
                    {
                        if (match.Groups[group].Success)
                        {
                            switch (group)
                            {
                                case "assembly":
                                    assembly = match.Groups[group].Value;
                                    break;
                                case "class":
                                    @class = match.Groups[group].Value;
                                    break;
                                case "func":
                                    method = match.Groups[group].Value;
                                    if (method == "ctor")
                                        method = ".ctor";
                                    else if (method == "cctor")
                                        method = ".cctor";
                                    break;
                                case "mod":
                                    insertionPlace = match.Groups[group].Value.ToLower();
                                    break;
                            }
                        }
                    }
                }
            }

            var hasError = false;

            if (string.IsNullOrEmpty(assembly))
            {
                hasError = true;
                UnityModManager.Logger.Error("Assembly name not found.");
            }

            if (string.IsNullOrEmpty(@class))
            {
                hasError = true;
                UnityModManager.Logger.Error("Class name not found.");
            }

            if (string.IsNullOrEmpty(method))
            {
                hasError = true;
                UnityModManager.Logger.Error("Method name not found.");
            }

            if (hasError)
            {
                UnityModManager.Logger.Error($"Error parsing EntryPoint '{str}'.");
                return false;
            }

            return true;
        }
    }
}
