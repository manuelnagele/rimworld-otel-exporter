using System;
using System.Collections.Generic;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    [HarmonyPatch(typeof(Pawn_RelationsTracker), "AddDirectRelation")]
    public static class RelationsPatch
    {
        private static readonly HashSet<string> SignificantRelations = new HashSet<string>
        {
            "Spouse", "Lover", "Bond", "Rival", "Fiance"
        };

        public static void Postfix(Pawn_RelationsTracker __instance, PawnRelationDef def, Pawn otherPawn)
        {
            if (!OtelExporterMod.Settings.EnableEvents) return;
            if (def == null || otherPawn == null) return;
            if (!SignificantRelations.Contains(def.defName)) return;

            try
            {
                var pawnA = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
                if (pawnA?.Faction != Faction.OfPlayer && otherPawn.Faction != Faction.OfPlayer) return;

                string nameA = pawnA?.LabelShort ?? "unknown";
                string nameB = otherPawn.LabelShort ?? "unknown";

                var attrs = new[]
                {
                    Attr("event_type", "relationship"),
                    Attr("pawn_a", nameA),
                    Attr("pawn_b", nameB),
                    Attr("relation_type", def.defName),
                };

                LogBuffer.Enqueue(BuildLogRecord(
                    $"Relationship: {nameA} and {nameB} — {def.label ?? def.defName}",
                    SeverityNumber.Info,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs));
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] RelationsPatch error: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
