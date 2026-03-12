using System;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    [HarmonyPatch(typeof(TradeSession), "SetupWith")]
    public static class TradePatch
    {
        public static void Postfix(ITrader trader)
        {
            if (!OtelExporterMod.Settings.EnableEvents) return;
            if (trader == null) return;

            try
            {
                string traderName = trader.TraderName ?? "unknown";
                string factionName = trader.Faction?.Name ?? "none";
                string kind = trader.TraderKind?.defName ?? "unknown";

                var attrs = new[]
                {
                    Attr("event_type", "trade"),
                    Attr("trader.name", traderName),
                    Attr("trader.faction", factionName),
                    Attr("trader.kind", kind),
                };

                LogBuffer.Enqueue(BuildLogRecord(
                    $"Trade session: {traderName} ({kind}) from {factionName}",
                    SeverityNumber.Info,
                    DateTimeToNanos(DateTime.UtcNow),
                    attrs));
            }
            catch (Exception ex)
            {
                Log.Warning($"[OtelExporter] TradePatch error: {ex.Message}");
            }
        }

        private static long DateTimeToNanos(DateTime dt) =>
            (long)(dt - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds * 1_000_000L;
    }
}
