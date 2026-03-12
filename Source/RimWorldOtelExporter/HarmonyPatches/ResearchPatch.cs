using System;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    [HarmonyPatch(typeof(ResearchManager), "FinishProject")]
    public static class ResearchPatch
    {
        public static void Postfix(ResearchProjectDef proj)
        {
            if (!OtelExporterMod.Settings.EnableEvents) return;
            if (proj == null) return;

            try
            {
                var attrs = new[]
                {
                    Attr("event_type", "research"),
                    Attr("research.def", proj.defName),
                    Attr("research.label", proj.label ?? proj.defName),
                    Attr("research.cost_total", (double)proj.baseCost),
                };

                LogBuffer.Enqueue(BuildLogRecord(
                    $"Research complete: {proj.label} ({proj.baseCost:F0} pts)",
                    SeverityNumber.Info,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs));
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] ResearchPatch error: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
