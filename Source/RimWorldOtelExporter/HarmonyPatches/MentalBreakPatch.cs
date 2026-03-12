using System;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    [HarmonyPatch(typeof(MentalStateHandler), "TryStartMentalState")]
    public static class MentalBreakPatch
    {
        public static void Postfix(MentalStateHandler __instance, MentalStateDef stateDef, bool __result)
        {
            if (!__result) return;
            if (!OtelExporterMod.Settings.EnableEvents) return;

            try
            {
                var pawn = __instance.pawn;
                if (pawn?.Faction != Faction.OfPlayer) return;

                string name = pawn.LabelShort ?? pawn.ThingID;
                float mood = pawn.needs?.mood?.CurLevel ?? 0f;

                var attrs = new[]
                {
                    Attr("event_type", "mental_break"),
                    Attr("pawn.name", name),
                    Attr("mental_state.def", stateDef?.defName ?? "unknown"),
                    Attr("pawn.mood_at_break", mood),
                };

                LogBuffer.Enqueue(BuildLogRecord(
                    $"Mental break: {name} — {stateDef?.label ?? "unknown"} (mood: {mood:P0})",
                    SeverityNumber.Warn,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs));
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] MentalBreakPatch error: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
