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
            harmony.PatchAll();
            Log.Message("[OtelExporter] Harmony patches applied.");
        }
    }
}
