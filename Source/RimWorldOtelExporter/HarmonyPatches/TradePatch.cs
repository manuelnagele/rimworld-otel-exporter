using System;
using HarmonyLib;
using OpenTelemetry.Proto.Logs.V1;
using RimWorld;
using RimWorldOtelExporter.Transport;
using Verse;
using static RimWorldOtelExporter.Transport.OtlpSerializer;

namespace RimWorldOtelExporter.HarmonyPatches
{
    // Real signature: SetupWith(ITrader newTrader, Pawn newPlayerNegotiator, bool giftMode).
    // Harmony binds postfix params BY NAME, so the param must be 'newTrader', not 'trader'.
    // Verified against RimWorld 1.6 Assembly-CSharp by reflection.
    [HarmonyPatch(typeof(TradeSession), "SetupWith")]
    public static class TradePatch
    {
        public static void Postfix(ITrader newTrader, bool giftMode)
        {
            if (!OtelExporterMod.Settings.EnableEvents) return;
            if (newTrader == null) return;
            if (giftMode) return; // gift-mode caravans aren't a real trade session

            try
            {
                string traderName = newTrader.TraderName ?? "unknown";
                string factionName = newTrader.Faction?.Name ?? "none";
                string kind = newTrader.TraderKind?.defName ?? "unknown";

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
