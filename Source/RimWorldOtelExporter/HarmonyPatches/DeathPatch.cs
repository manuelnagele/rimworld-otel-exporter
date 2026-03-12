using System;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    [HarmonyPatch(typeof(Pawn), "Kill")]
    public static class DeathPatch
    {
        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            if (!OtelExporterMod.Settings.EnableEvents) return;

            try
            {
                // Only track player-faction pawns and named animals
                bool isColonist = __instance.Faction == Faction.OfPlayer && __instance.RaceProps.Humanlike;
                bool isNamedAnimal = __instance.Faction == Faction.OfPlayer
                    && !__instance.RaceProps.Humanlike
                    && __instance.Name != null;

                if (!isColonist && !isNamedAnimal) return;

                string name = __instance.LabelShort ?? __instance.ThingID;
                int age = __instance.ageTracker?.AgeBiologicalYears ?? 0;
                string cause = dinfo?.Def?.defName ?? "unknown";
                string killerFaction = dinfo?.Instigator?.Faction?.Name ?? "unknown";
                string weapon = (dinfo?.Weapon?.defName) ?? "unknown";

                string body = $"{name} died — {cause}" + (killerFaction != "unknown" ? $" ({killerFaction})" : "");

                var attrs = new[]
                {
                    Attr("event_type", "death"),
                    Attr("pawn.name", name),
                    Attr("pawn.age_years", (long)age),
                    Attr("death.cause", cause),
                    Attr("death.killer_faction", killerFaction),
                    Attr("death.weapon_def", weapon),
                    Attr("pawn.is_animal", isNamedAnimal),
                };

                LogBuffer.Enqueue(BuildLogRecord(
                    body,
                    SeverityNumber.Warn,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs));
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] DeathPatch error: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
