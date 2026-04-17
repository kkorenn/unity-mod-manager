using System;
using HarmonyLib;
using UnityEngine;
using UnityModManagerNet;

namespace KorensTweaks
{
    public static class Main
    {
        private const string HarmonyId = "koren.korens_tweaks";

        private static Harmony harmony;
        private static UnityModManager.ModEntry mod;
        private static float rescanTimer;

        public static bool Load(UnityModManager.ModEntry modEntry)
        {
            mod = modEntry;
            modEntry.OnUpdate = OnUpdate;

            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(typeof(Main).Assembly);

            ApplyToAllPlanets();
            ApplyToAllFloors();
            mod.Logger.Log("koren's tweaks loaded.");
            return true;
        }

        private static void OnUpdate(UnityModManager.ModEntry modEntry, float deltaTime)
        {
            rescanTimer += deltaTime;
            if (rescanTimer < 1f)
            {
                return;
            }

            rescanTimer = 0f;
            ApplyToAllPlanets();
            ApplyToAllFloors();
        }

        private static void ApplyToAllPlanets()
        {
            try
            {
                var renderers = UnityEngine.Object.FindObjectsOfType<PlanetRenderer>();
                if (renderers == null || renderers.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < renderers.Length; i++)
                {
                    ApplyRendererTweaks(renderers[i]);
                }
            }
            catch (Exception ex)
            {
                mod?.Logger?.Error("Failed to apply planet tweaks: " + ex.Message);
            }
        }

        private static void ApplyRendererTweaks(PlanetRenderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            DisableCoreParticle(renderer.coreParticles);
        }

        private static void ApplyToAllFloors()
        {
            try
            {
                var floors = UnityEngine.Object.FindObjectsOfType<scrFloor>();
                if (floors == null || floors.Length == 0)
                {
                    return;
                }

                for (int i = 0; i < floors.Length; i++)
                {
                    ApplyFloorTweaks(floors[i]);
                }
            }
            catch (Exception ex)
            {
                mod?.Logger?.Error("Failed to apply floor glow tweaks: " + ex.Message);
            }
        }

        private static void ApplyFloorTweaks(scrFloor floor)
        {
            if (floor == null)
            {
                return;
            }

            try
            {
                floor.disableGlow = true;
                floor.glowMultiplier = 0f;
                floor.isChangingGlowMult = false;

                if (floor.topGlow != null)
                {
                    floor.topGlow.enabled = false;
                }

                if (floor.bottomGlow != null)
                {
                    floor.bottomGlow.enabled = false;
                }
            }
            catch (Exception ex)
            {
                mod?.Logger?.Error("Failed disabling floor glow: " + ex.Message);
            }
        }

        private static void DisableCoreParticle(ParticleSystem particleSystem)
        {
            if (particleSystem == null)
            {
                return;
            }

            try
            {
                var emission = particleSystem.emission;
                emission.enabled = false;

                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Clear(true);

                var particleRenderer = particleSystem.GetComponent<Renderer>();
                if (particleRenderer != null)
                {
                    particleRenderer.enabled = false;
                }
            }
            catch (Exception ex)
            {
                mod?.Logger?.Error("Failed disabling core particle: " + ex.Message);
            }
        }

        [HarmonyPatch(typeof(PlanetRenderer), "Awake")]
        private static class PlanetRendererAwakePatch
        {
            private static void Postfix(PlanetRenderer __instance)
            {
                ApplyRendererTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(PlanetRenderer), "Revive")]
        private static class PlanetRendererRevivePatch
        {
            private static void Postfix(PlanetRenderer __instance)
            {
                ApplyRendererTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(PlanetRenderer), "SetPlanetColor")]
        private static class PlanetRendererSetPlanetColorPatch
        {
            private static void Postfix(PlanetRenderer __instance)
            {
                ApplyRendererTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(PlanetRenderer), "SetColor")]
        private static class PlanetRendererSetColorPatch
        {
            private static void Postfix(PlanetRenderer __instance)
            {
                ApplyRendererTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(scrFloor), "Awake")]
        private static class ScrFloorAwakePatch
        {
            private static void Postfix(scrFloor __instance)
            {
                ApplyFloorTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(scrFloor), "Start")]
        private static class ScrFloorStartPatch
        {
            private static void Postfix(scrFloor __instance)
            {
                ApplyFloorTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(scrFloor), "SetTrackStyle")]
        private static class ScrFloorSetTrackStylePatch
        {
            private static void Postfix(scrFloor __instance)
            {
                ApplyFloorTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(scrFloor), "ResetToLevelStart")]
        private static class ScrFloorResetToLevelStartPatch
        {
            private static void Postfix(scrFloor __instance)
            {
                ApplyFloorTweaks(__instance);
            }
        }

        [HarmonyPatch(typeof(scrFloor), "LightUp")]
        private static class ScrFloorLightUpPatch
        {
            private static void Prefix(scrFloor __instance)
            {
                ApplyFloorTweaks(__instance);
            }

            private static void Postfix(scrFloor __instance)
            {
                ApplyFloorTweaks(__instance);
            }
        }
    }
}
