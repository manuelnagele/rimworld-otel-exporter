using System;
using HarmonyLib;
using Verse;

namespace RimWorldOtelExporter.HarmonyPatches
{
    /// <summary>Applies all Harmony patches when the mod loads.</summary>
    [StaticConstructorOnStartup]
    public static class HarmonyPatcher
    {
        static HarmonyPatcher()
        {
            var harmony = new Harmony("manuelnagele.rimworld.otelexporter");

            // Apply each patch class in isolation so one bad target can never abort the whole
            // batch (a single throw inside PatchAll() previously took down every event patch).
            int applied = 0, failed = 0;
            foreach (var type in typeof(HarmonyPatcher).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), true).Length == 0) continue;
                try
                {
                    new PatchClassProcessor(harmony, type).Patch();
                    applied++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error($"[OtelExporter] Failed to apply patch {type.Name}: {ex}");
                }
            }

            if (failed == 0)
                Log.Message($"[OtelExporter] Harmony patches applied ({applied} classes).");
            else
                Log.Warning($"[OtelExporter] Harmony patches applied with issues: {applied} ok, {failed} failed. Event telemetry may be partial.");
        }
    }
}
