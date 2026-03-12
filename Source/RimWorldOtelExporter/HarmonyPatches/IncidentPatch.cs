using System;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    [HarmonyPatch(typeof(IncidentWorker), "TryExecuteWorker")]
    public static class IncidentPatch
    {
        public static void Postfix(IncidentWorker __instance, IncidentParms parms, bool __result)
        {
            if (!__result) return;
            if (!OtelExporterMod.Settings.EnableEvents) return;

            try
            {
                IncidentDef iDef = __instance.def;
                string incidentDef = iDef?.defName ?? "unknown";
                string factionName = parms?.faction?.Name ?? "none";
                float points = parms?.points ?? 0f;

                bool isHostile = iDef?.category == IncidentCategoryDefOf.ThreatBig
                    || iDef?.category == IncidentCategoryDefOf.ThreatSmall;

                string body = isHostile
                    ? $"Hostile incident: {incidentDef} ({points:F0} pts) from {factionName}"
                    : $"Incident: {incidentDef}";

                var attrs = new[]
                {
                    Attr("event_type", "incident"),
                    Attr("incident.def", incidentDef),
                    Attr("incident.points", (double)points),
                    Attr("incident.faction_name", factionName),
                    Attr("incident.is_hostile", isHostile),
                };

                LogBuffer.Enqueue(BuildLogRecord(
                    body,
                    isHostile ? SeverityNumber.Warn : SeverityNumber.Info,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs));
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] IncidentPatch error: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
